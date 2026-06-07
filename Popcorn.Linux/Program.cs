using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using GdkPixbuf;
using Gtk;
using Microsoft.Extensions.Hosting;
using Popcorn.Server;
using Popcorn.Shared.Utils;
using Serilog;
using WebKit.Internal;

namespace Popcorn.Linux;

public abstract class Program
{
    private static CancellationTokenSource _globalAppCts;

    private static string? ServerUrl = null;
    
    public static async Task Main(string[] args)
    {
        _globalAppCts = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true; // Prevent default termination
            _globalAppCts.Cancel();
            Console.WriteLine("Shutting down...");
        };

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Console()
            .WriteTo.Debug()
            .WriteTo.File(Path.Combine(DirectoryUtils.DataFolder, $"logs{Path.DirectorySeparatorChar}Popcorn-.txt"), rollingInterval: RollingInterval.Day)
            .CreateLogger();

        PopcornServerHost? serverHost = null;

        try
        {
            ServerUrl = await ResolveServerUrlAsync(_globalAppCts.Token);
            if (string.IsNullOrWhiteSpace(ServerUrl))
            {
                serverHost = new PopcornServerHost(args);
                await serverHost.StartAsync(Log.Logger, _globalAppCts.Token);
                BindServerShutdownToApplication(serverHost);
                ServerUrl = NormalizeServerUrl(serverHost.ListeningUrl) ?? GetDefaultServerUrl();
            }

            Console.WriteLine($"Popcorn.Linux running. Press Ctrl+C to shut down.");
            Console.WriteLine($"Listening on: {ServerUrl}");

            var gtkExitedTcs = new TaskCompletionSource<bool>();
            var gtkThread = new Thread(() =>
            {
                try
                {
                    // Run the GTK window setup and event loop
                    RunWebWindow(_globalAppCts.Token, gtkExitedTcs);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"An error occurred in the GTK application thread: {ex.Message}");
                    Log.Error(ex, "Error in GTK application thread.");
                    gtkExitedTcs.TrySetException(ex);
                }
                finally
                {
                    gtkExitedTcs.TrySetResult(true); // Signal that GTK thread has completed its work
                    Console.WriteLine("GTK application thread finished.");
                }
            })
            {
                IsBackground = true, // Allows the process to exit if only this background thread remains
                Name = "GTK Thread"
            };
            // GTK (and underlying libraries like WebKitGTK which uses COM on Windows) often require STA.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                gtkThread.SetApartmentState(ApartmentState.STA);
            }
            gtkThread.Start();
            
            var shutdownTriggerTask = await Task.WhenAny(
                Task.Delay(Timeout.Infinite, _globalAppCts.Token)
                    .ContinueWith(_ => { }), // Suppress OperationCanceledException for Task.WhenAny
                gtkExitedTcs.Task
            );
            if (shutdownTriggerTask == gtkExitedTcs.Task)
            {
                Console.WriteLine("GTK application closed. Signalling overall application shutdown...");
                _globalAppCts.Cancel(); // Initiate server shutdown
            }
            
            // Give a small delay to allow any final shutdown logic to begin
            await Task.Delay(100);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Application shutdown initiated by cancellation.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An unhandled error occurred during application execution: {ex.Message}");
            Log.Fatal(ex, "Application terminated due to an unhandled exception.");
        }
        finally
        {
            if (serverHost is not null)
            {
                Console.WriteLine("Attempting graceful shutdown of server host...");
                await serverHost.DisposeAsync();
            }
        }
        Console.WriteLine("Popcorn.Linux application finished.");
    }
    
    private static Gtk.Application _gtkApplication;
    private static ApplicationWindow _window;
    private static WebKit.WebView _webView;
    
    /// <summary>
    /// Runs the GTK application components. This method should be called on the GTK thread.
    /// </summary>
    /// <param name="appCtsToken">The CancellationToken for overall application shutdown.</param>
    /// <param name="gtkExitedTcs">The TaskCompletionSource to signal when the GTK thread exits.</param>
    private static void RunWebWindow(CancellationToken appCtsToken, TaskCompletionSource<bool> gtkExitedTcs)
    {
        // Initialize WebKit. This must be called on the thread that will create GTK widgets.
        WebKit.Module.Initialize();

        _gtkApplication = global::Gtk.Application.New("tomiscout.popcorn", Gio.ApplicationFlags.FlagsNone);

        _gtkApplication.OnActivate += (sender, args) => OnActivate((Gio.Application)sender, args);
        _gtkApplication.OnShutdown += (sender, args) => OnShutdown((Gio.Application)sender, args, gtkExitedTcs);

        // Register a callback with the CancellationToken.
        appCtsToken.Register(() =>
        {
            // It's crucial to call GTK methods from the GTK main thread.
            // GLib.Functions.TimeoutAddFull schedules a callback to run on the GTK main loop.
            GLib.Functions.TimeoutAdd(0, 0, () =>
            {
                Console.WriteLine("Signaling GTK application to quit from cancellation token.");
                _gtkApplication?.Quit();
                return false;
            });
        });

        _gtkApplication.RunWithSynchronizationContext(null);
    }
    
    private static void OnActivate(Gio.Application app, EventArgs args)
    {
        _window = global::Gtk.ApplicationWindow.New((global::Gtk.Application)app);
        _window.Title = "Popcorn";
        _window.SetDefaultSize(1280, 720);

        var overlay = global::Gtk.Overlay.New();
        _window.SetChild(overlay);

        _webView = WebviewGtk.CreateWebView(ServerUrl);
        overlay.AddOverlay(_webView);
        
        _window.Show();
    }
    
    /// <summary>
    /// Called when the GTK application is shutting down.
    /// </summary>
    /// <param name="gtkExitedTcs">The TaskCompletionSource to signal when GTK components are disposed.</param>
    private static void OnShutdown(Gio.Application app, EventArgs args, TaskCompletionSource<bool> gtkExitedTcs)
    {
        Console.WriteLine("GTK application OnShutdown event triggered.");
        DisposeGtkComponents();
        // Signal that the GTK application has completely shut down and its resources are disposed.
        gtkExitedTcs.TrySetResult(true);
        // If GTK initiated the shutdown (e.g., window closed), ensure the overall application
        // cancellation is signaled to prompt the server to shut down as well.
        _globalAppCts?.Cancel();
    }
    
    /// <summary>
    /// Disposes the GTK UI components.
    /// </summary>
    private static void DisposeGtkComponents()
    {
        Console.WriteLine("Disposing GTK components...");
        _webView?.Dispose();
        _window?.Dispose();
        _gtkApplication?.Dispose();
        Console.WriteLine("GTK components disposed.");
    }

    private static void Dispose()
    {
        DisposeGtkComponents(); //TODO not right here
    }

    private static async Task<string?> ResolveServerUrlAsync(CancellationToken cancellationToken)
    {
        var configuredUrl = NormalizeServerUrl(Environment.GetEnvironmentVariable("POPCORN_SERVER_URL"));
        if (!string.IsNullOrWhiteSpace(configuredUrl))
        {
            Console.WriteLine($"Using configured Popcorn server: {configuredUrl}");
            return configuredUrl;
        }

        var defaultServerUrl = GetDefaultServerUrl();
        if (await CanConnectAsync(defaultServerUrl, cancellationToken))
        {
            Console.WriteLine($"Using existing Popcorn server: {defaultServerUrl}");
            return defaultServerUrl;
        }

        return null;
    }

    private static string GetDefaultServerUrl()
    {
        var portValue = Environment.GetEnvironmentVariable("POPCORN_PORT");
        var port = int.TryParse(portValue, out var parsedPort) && parsedPort > 0
            ? parsedPort
            : 5000;

        return $"http://localhost:{port}";
    }

    private static string? NormalizeServerUrl(string? serverUrl)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            return null;
        }

        return serverUrl.Trim().TrimEnd('/');
    }

    private static async Task<bool> CanConnectAsync(string serverUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host switch
        {
            "localhost" => "127.0.0.1",
            _ => uri.Host
        };

        using var client = new TcpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(750));

        try
        {
            await client.ConnectAsync(host, uri.Port, timeoutCts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void BindServerShutdownToApplication(PopcornServerHost serverHost)
    {
        var applicationLifetime = serverHost.Services.GetService(typeof(IHostApplicationLifetime)) as IHostApplicationLifetime;
        if (applicationLifetime is null)
        {
            return;
        }

        applicationLifetime.ApplicationStopping.Register(() =>
        {
            if (_globalAppCts.IsCancellationRequested)
            {
                return;
            }

            Console.WriteLine("Popcorn.Server shutdown detected. Closing Popcorn.Linux...");
            _globalAppCts.Cancel();
        });
    }
}
