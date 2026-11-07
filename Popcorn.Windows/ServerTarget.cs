using System.Diagnostics;
using System.Net.Sockets;

namespace Popcorn.Windows;

/// <summary>Where Popcorn.Server is, and starting it when it isn't running. The app never hosts the server itself.</summary>
public sealed class ServerTarget
{
    private const int DefaultPort = 5000;

    /// <summary>Base URL without trailing slash, e.g. <c>http://localhost:5000</c>.</summary>
    public string Url { get; }

    /// <summary>Set via <c>POPCORN_SERVER_URL</c> (e.g. by the tray): nothing is started then, only waited for.</summary>
    public bool Explicit { get; }

    private ServerTarget(string url, bool isExplicit)
    {
        Url = url;
        Explicit = isExplicit;
    }

    public static ServerTarget FromEnvironment()
    {
        var url = Environment.GetEnvironmentVariable("POPCORN_SERVER_URL")?.Trim().TrimEnd('/');
        if (!string.IsNullOrEmpty(url))
        {
            return new ServerTarget(url, true);
        }

        var port = int.TryParse(Environment.GetEnvironmentVariable("POPCORN_PORT"), out var parsed) && parsed is > 0 and < 65536
            ? parsed
            : DefaultPort;
        return new ServerTarget($"http://localhost:{port}", false);
    }

    public string AppUrl => $"{Url}/index.html";

    /// <param name="path">A page of the web UI, like <c>/watch/{id}?tab=sources</c>.</param>
    public string PageUrl(string path) => $"{Url}{path}";

    /// <summary>TCP connect check against the server's host and port.</summary>
    public async Task<bool> IsReachableAsync()
    {
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host == "localhost" ? "127.0.0.1" : uri.Host;
        try
        {
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(750));
            await client.ConnectAsync(host, uri.Port, timeout.Token);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Starts Popcorn.Server.exe from this app's folder (the install/bundle folder). It outlives the window; a server
    /// that is already running makes the new one exit at once (single instance).
    /// </summary>
    public static Process StartServer()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "Popcorn.Server.exe");
        if (!File.Exists(exe))
        {
            throw new FileNotFoundException($"Popcorn.Server.exe not found in {AppContext.BaseDirectory}");
        }

        return Process.Start(new ProcessStartInfo(exe)
        {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("Popcorn.Server.exe did not start");
    }
}
