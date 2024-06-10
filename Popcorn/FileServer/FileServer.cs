using EmbedIO;
using EmbedIO.Files;
using Popcorn.Shared.Utils;
using Serilog;

namespace Popcorn.FileServer;

public static class FileServer
{
    public static WebServer WebServer { get; private set; }

    private const int MinPort = 20210;
    private const int MaxPort = 20213;
    private static string ContentRoot = Path.Combine(DirUtils.DataFolder, "segmentFinder" + Path.DirectorySeparatorChar);

    private static (WebServer, int) StartServer(int port = MinPort)
    {
        Dispose();

        if (port < MinPort || port > MaxPort)
        {
            return (null, 0);
        }

        try
        {
            WebServer = new WebServer(o => o
                .WithUrlPrefix($"http://localhost:{port}/")
                .WithMode(HttpListenerMode.EmbedIO))
            .WithStaticFolder("/", ContentRoot, true, m => m
                .WithContentCaching(true));

            WebServer.Start();
        } catch (Exception ex)
        {
            Log.Warning(ex, "Failed to start file server on port {port}", port);
            return StartServer(port + 1);
        }
        
        return (WebServer, port);
    }

    public static void Dispose()
    {
        if (WebServer != null)
        {
            WebServer.Dispose();
            WebServer = null;
        }
    }

    public static int? EnsureServerRunning()
    {
        if (WebServer?.State == WebServerState.Listening)
        {
            return null;
        }

        Dispose();
        var (server, port) = StartServer();
        if (server != null)
        {
            return port;
        }
        return null;
    }
}