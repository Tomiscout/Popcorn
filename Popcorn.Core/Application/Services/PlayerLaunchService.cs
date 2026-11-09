using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Popcorn.Domain;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Persistence;

namespace Popcorn.Application.Services;

/// <summary>
/// Opens a media file with the user's ordered native player list (<see cref="SettingsModel.Players"/>): the first
/// enabled player that launches wins, failures fall through to the next one.
/// </summary>
public class PlayerLaunchService(
    SettingsRepository settingsRepository,
    IFileService fileService,
    INativeFileAssosiation fileAssosiation,
    ILogger<PlayerLaunchService> logger,
    IPlayerService? popcornPlayer = null) // not registered on every platform
{
    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <param name="playerId">Explicitly chosen player (context menu / play dropdown): only that one is tried,
    /// even if it is disabled in the list. Null = walk the enabled players in order.</param>
    /// <returns>The player that opened the file, null if none could.</returns>
    public async Task<PlayerOption?> OpenAsync(MediaFile file, string? playerId = null)
    {
        var settings = await settingsRepository.GetSettingsAsync();

        var candidates = playerId == null
            ? settings.Players.Where(p => p.Enabled)
            : settings.Players.Where(p => p.Id == playerId);

        foreach (var player in candidates)
        {
            try
            {
                if (await TryLaunchAsync(player, file))
                {
                    logger.LogInformation("Opened {file} with {type} player {name}", file.Path, player.Type, player.Name);
                    return player;
                }
            }
            catch (Exception e)
            {
                logger.LogWarning("{type} player {name} failed to open {file}: {message}", player.Type, player.Name, file.Path, e.Message);
            }
        }

        logger.LogError("No configured player could open {file}", file.Path);
        return null;
    }

    public string? GetDefaultPlayerName() => fileAssosiation.GetDefaultVideoPlayerName();

    private async Task<bool> TryLaunchAsync(PlayerOption player, MediaFile file)
    {
        switch (player.Type)
        {
            case PlayerOptionType.Popcorn:
                return popcornPlayer != null && await popcornPlayer.OpenFile(file.Id);

            case PlayerOptionType.Default:
                fileService.LaunchMediaFileDefault(file.Path); // throws when nothing is associated
                return true;

            case PlayerOptionType.Program:
                if (!File.Exists(player.Path))
                {
                    logger.LogWarning("Player program {path} does not exist", player.Path);
                    return false;
                }
                return await StartDetachedAsync(new ProcessStartInfo(player.Path)
                {
                    ArgumentList = { file.Path },
                    WorkingDirectory = Path.GetDirectoryName(player.Path),
                });

            case PlayerOptionType.Script:
                if (string.IsNullOrWhiteSpace(player.Command)) return false;
                return await StartDetachedAsync(BuildScriptStartInfo(player.Command, file.Path));

            default:
                return false;
        }
    }

    /// <summary>
    /// Replaces every <see cref="PlayerOption.ArgsPlaceholder"/> with the shell-quoted file path (appended at the end
    /// when the placeholder is missing) and runs the line through the platform shell.
    /// </summary>
    public static ProcessStartInfo BuildScriptStartInfo(string command, string filePath)
    {
        var quoted = IsWindows ? $"\"{filePath}\"" : $"'{filePath.Replace("'", "'\\''")}'";
        var line = command.Contains(PlayerOption.ArgsPlaceholder)
            ? command.Replace(PlayerOption.ArgsPlaceholder, quoted)
            : $"{command.TrimEnd()} {quoted}";

        return IsWindows
            // /s: strip only the outer quotes so quoted program paths inside the line keep working.
            ? new ProcessStartInfo("cmd.exe") { Arguments = $"/d /s /c \"{line}\"" }
            : new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", line } };
    }

    /// <summary>How long a freshly started player may take to fail before it is considered launched.</summary>
    private static readonly TimeSpan StartupFailureWindow = TimeSpan.FromSeconds(1);

    /// <returns>False if the process exits with an error right away (e.g. shell "command not found").</returns>
    private async Task<bool> StartDetachedAsync(ProcessStartInfo startInfo)
    {
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        logger.LogTrace("Starting player process: {file} {args}", startInfo.FileName,
            startInfo.ArgumentList.Count > 0 ? string.Join(' ', startInfo.ArgumentList) : startInfo.Arguments);

        // The player keeps running on its own after this returns; the runtime reaps it when it exits.
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Process did not start");

        using var timeout = new CancellationTokenSource(StartupFailureWindow);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            return true; // still running
        }

        if (process.ExitCode == 0) return true; // e.g. handed the file to an already running instance
        logger.LogWarning("Player process {file} exited with code {code}", startInfo.FileName, process.ExitCode);
        return false;
    }
}
