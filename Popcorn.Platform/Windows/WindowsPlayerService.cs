using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Popcorn.Platform.Windows;

public class WindowsPlayerService(ILogger<WindowsPlayerService> logger) : NativePlayerService(logger)
{
    protected override void LaunchPlayer(string argument)
    {
        var player = FindPlayer(Path.Combine("Player", "Popcorn.Player.Windows.exe"), "Popcorn.Player.Windows",
            "Popcorn.Player.Windows.exe");

        var startInfo = new ProcessStartInfo(player)
        {
            ArgumentList = { argument },
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(player)!,
        };
        using var process = Process.Start(startInfo);
    }
}
