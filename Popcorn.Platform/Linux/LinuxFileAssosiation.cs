using Microsoft.Extensions.Logging;
using Popcorn.Domain.Utils;
using Popcorn.Shared.Interfaces;

namespace Popcorn.Platform.Linux;

public class LinuxFileAssociation(ILogger<LinuxFileAssociation> logger, INativeProcessRunner processRunner)
    : INativeFileAssosiation
{
    private readonly ILogger<LinuxFileAssociation> _logger = logger;

    public string? GetExecCommandForExtention(string ext, string filePath, string args)
    {
        // Also its possible to get mime from here: grep 'mp4' /etc/mime.types or 'file' command

        string? desktopFileName;
        if (string.IsNullOrEmpty(ext))
        {
            desktopFileName = processRunner.ExecuteCommandRead($"xdg-mime query default {MimeUtils.GetMimeTypeForFileExtension(ext)}");
        }
        else
        {
            desktopFileName = processRunner.ExecuteCommandRead($"xdg-mime query default $(xdg-mime query filetype '{filePath}')");
        }
        if (desktopFileName == null) return null;

        var desktopPath = FindDesktopFile(desktopFileName);
        if (desktopPath == null) return null;

        var command = ExtractExecCommand(desktopPath);
        if (command == null) return null;

        if (desktopFileName.Contains("VLC.desktop"))
        {
            args += " --fullscreen --one-instance";
        }

        if (command.Contains("flatpak") && command.Contains("@@u") && command.Contains("@@"))
        {
            var index = command.IndexOf("@@u");
            command = command.Insert(index + 3, args);
        }
        else
        {
            command += args;
        }

        return command;
    }

    private static readonly string[] VideoMimeTypes = ["video/x-matroska", "video/mp4"];

    public string? GetDefaultVideoPlayerName()
    {
        foreach (var mime in VideoMimeTypes)
        {
            var desktopFileName = processRunner.ExecuteCommandRead($"xdg-mime query default {mime}");
            if (string.IsNullOrWhiteSpace(desktopFileName)) continue;

            var desktopPath = FindDesktopFile(desktopFileName);
            var name = desktopPath == null ? null : ExtractDesktopEntryValue(desktopPath, "Name");
            return name ?? Path.GetFileNameWithoutExtension(desktopFileName);
        }
        return null;
    }

    private static string? FindDesktopFile(string desktopFileName)
    {
        // Same lookup order as xdg-mime: $XDG_DATA_HOME first, then $XDG_DATA_DIRS (may be unset under systemd).
        var home = Environment.GetEnvironmentVariable("HOME") ?? "";
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(dataHome)) dataHome = Path.Join(home, ".local/share");

        var dataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
        if (string.IsNullOrWhiteSpace(dataDirs)) dataDirs = "/usr/local/share:/usr/share";

        var searchPaths = new[] { dataHome }
            .Concat(dataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries))
            .Concat([Path.Join(home, ".local/share/flatpak/exports/share"), "/var/lib/flatpak/exports/share"])
            .Distinct();

        foreach (var path in searchPaths)
        {
            var appPath = Path.Join(path, "applications", desktopFileName);
            if (File.Exists(appPath)) return appPath;
        }
        return null;
    }

    /// <summary>Reads an unlocalized key from the [Desktop Entry] group.</summary>
    private static string? ExtractDesktopEntryValue(string filePath, string key)
    {
        var inDesktopEntry = false;
        foreach (var line in File.ReadLines(filePath))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('['))
            {
                if (inDesktopEntry) break;
                inDesktopEntry = trimmed == "[Desktop Entry]";
                continue;
            }
            if (inDesktopEntry && trimmed.StartsWith(key + "="))
                return trimmed[(key.Length + 1)..].Trim();
        }
        return null;
    }

    private string? ExtractExecCommand(string filePath)
    {
        foreach (var line in File.ReadAllLines(filePath))
        {
            if (line.StartsWith("Exec="))
            {
                var execCommand = line.Substring("Exec=".Length);
                execCommand = execCommand.Replace("%u", "").Replace("%U", "").Replace("%f", "").Replace("%F", "");
                return execCommand.Trim();
            }
        }

        return null;
    }
}
