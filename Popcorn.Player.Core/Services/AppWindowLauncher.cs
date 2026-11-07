using System.Diagnostics;
using System.Runtime.InteropServices;
using Serilog;

namespace Popcorn.Player.Core.Services;

/// <summary>
/// Shows a page of the main app from the native player: the desktop app (Linux: Popcorn.Linux next to the player,
/// Windows: Popcorn.Windows.exe one folder up, the player lives in <c>Player\</c>), a running one takes the page over
/// through its single-instance hand-over. If it isn't there: the browser.
/// </summary>
public class AppWindowLauncher
{
    /// <param name="path">Site-relative page, like <c>/watch/{id}?tab=sources</c>.</param>
    public void Open(string path)
    {
        if (!path.StartsWith('/') || path.StartsWith("//"))
        {
            Log.Warning("Not an app page: {Path}", path);
            return;
        }

        try
        {
            var desktopApp = DesktopAppPath();
            var startInfo = desktopApp is not null
                ? new ProcessStartInfo(desktopApp) { ArgumentList = { $"--open-path={path}" }, WorkingDirectory = Path.GetDirectoryName(desktopApp) }
                : BrowserStartInfo(path);
            using var process = Process.Start(startInfo);
        }
        catch (Exception e)
        {
            Log.Error(e, "Couldn't open the app at {Path}", path);
        }
    }

    private static string? DesktopAppPath()
    {
        var path = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Popcorn.Windows.exe"))
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? Path.Combine(AppContext.BaseDirectory, "Popcorn.Linux")
                : null;
        return path is not null && File.Exists(path) ? path : null;
    }

    private static ProcessStartInfo BrowserStartInfo(string path)
    {
        var url = $"http://localhost:{Environment.GetEnvironmentVariable("POPCORN_PORT") ?? "5000"}{path}";
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new ProcessStartInfo(url) { UseShellExecute = true }
            : new ProcessStartInfo("xdg-open") { ArgumentList = { url } };
    }
}
