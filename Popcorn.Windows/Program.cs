using Popcorn.Shared.Logging;
using Serilog;

namespace Popcorn.Windows;

/// <summary>
/// Popcorn desktop app for Windows: a WebView2 window on Popcorn.Server's web UI. Finds the server via
/// <c>POPCORN_SERVER_URL</c> or <c>localhost:$POPCORN_PORT</c>; when it doesn't answer, starts Popcorn.Server.exe from
/// its own folder and shows a waiting page. <c>--open-path=/watch/...</c> opens a page of the app (the native player's
/// "Find file"); a running window takes it over.
/// </summary>
internal static class Program
{
    /// <summary>Same folder as Popcorn.Shared's DirectoryUtils.DataFolder (not referenced to keep the bundle's DLLs in step).</summary>
    public static readonly string DataFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Popcorn");

    [STAThread]
    private static int Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.DailyFile(Path.Combine(Program.DataFolder, "logs"), "Popcorn.Windows")
            .CreateLogger();

        try
        {
            var page = OpenPathArgument(args);
            using var instance = SingleInstance.Acquire(page);
            if (instance is null)
            {
                Log.Information("Already running, handed over to it");
                return 0;
            }

            ApplicationConfiguration.Initialize();
            var form = new MainForm(ServerTarget.FromEnvironment(), page);
            form.Load += (_, _) => instance.Listen(relaunchPage => form.BeginInvoke(() => form.OnRelaunch(relaunchPage)));
            Application.Run(form);
            return 0;
        }
        catch (Exception e)
        {
            Log.Fatal(e, "Popcorn.Windows failed");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    /// <returns>The site-relative page of <c>--open-path=</c>, or null.</returns>
    private static string? OpenPathArgument(string[] args)
    {
        const string prefix = "--open-path=";
        var path = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
        return IsAppPage(path) ? path : null;
    }

    public static bool IsAppPage(string? path) => path is not null && path.StartsWith('/') && !path.StartsWith("//");
}
