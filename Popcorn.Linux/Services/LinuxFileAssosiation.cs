
using System.IO;
using Microsoft.Extensions.Logging;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Utils;

namespace Popcorn.Linux;

public class LinuxFileAssociation : INativeFileAssosiation
{
    private readonly ILogger<LinuxFileAssociation> _logger;
    private readonly INativeProcessRunner _processRunner;

    public LinuxFileAssociation(ILogger<LinuxFileAssociation> logger, INativeProcessRunner processRunner)
    {
        _logger = logger;
        _processRunner = processRunner;
    }

    public string? GetExecCommandForExtention(string ext, string filePath, string args)
    {
        // Also its possible to get mime from here: grep 'mp4' /etc/mime.types or 'file' command

        string? desktopFileName;
        if (string.IsNullOrEmpty(ext))
        {
            desktopFileName = _processRunner.ExecuteCommandRead($"xdg-mime query default {MimeUtils.GetMimeTypeForFileExtension(ext)}");
        }
        else
        {
            desktopFileName = _processRunner.ExecuteCommandRead($"xdg-mime query default $(xdg-mime query filetype '{filePath}')");
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

    private string? FindDesktopFile(string desktopFileName)
    {
        var envPaths = _processRunner.ExecuteCommandRead("printenv XDG_DATA_DIRS")?.Split(':');
        if (envPaths == null || envPaths.Length == 0) return null;

        foreach (var path in envPaths!)
        {
            var appPath = $"{path}/applications/{desktopFileName}";
            if (File.Exists(appPath)) return appPath;
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
