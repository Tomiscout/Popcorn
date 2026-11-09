using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Popcorn.Platform.Linux;

public class LinuxPlayerService(ILogger<LinuxPlayerService> logger) : NativePlayerService(logger)
{
    protected override void LaunchPlayer(string argument)
    {
        var player = FindPlayer("Popcorn.Player.Linux", "Popcorn.Player.Linux", "Popcorn.Player.Linux");

        // Detached through sh (background job: a Ctrl+C in the server's terminal doesn't reach it). The paths are
        // positional parameters, never part of the command line.
        var startInfo = new ProcessStartInfo("/bin/sh")
        {
            ArgumentList = { "-c", "nohup \"$0\" \"$1\" > /dev/null 2>&1 &", player, argument },
            UseShellExecute = false,
        };
        using var process = Process.Start(startInfo);
    }
}
