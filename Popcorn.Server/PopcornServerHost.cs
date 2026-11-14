using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection; // For IServerAddressesFeature
using Microsoft.AspNetCore.Hosting.Server.Features; // For IServerAddressesFeature
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.Logging;
using Popcorn.Shared.Utils; // For ILogger
using Serilog; // If you want to ensure Serilog is configured by default

namespace Popcorn.Server;

public class PopcornServerHost : IAsyncDisposable
{
    private IHost? _host;
    private readonly string[] _args;
    private readonly Action<IHostBuilder>? _configureHostAction;
    private ILogger<PopcornServerHost>? _logger;

    public string? ListeningUrl { get; private set; }
    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Host not started.");

    public PopcornServerHost(string[]? args = null, Action<IHostBuilder>? configureHostAction = null)
    {
        _args = args ?? Array.Empty<string>();
        _configureHostAction = configureHostAction;
    }

    public async Task StartAsync(Serilog.ILogger logger, CancellationToken cancellationToken = default)
    {
        Log.Logger = logger;

        DirectoryUtils.EnsureApplicationFolder();

        var hostBuilder = Host.CreateDefaultBuilder(_args)
            .UseEnvironment(
                Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                ?? "Production");

        hostBuilder.UseSerilog(logger);

        // Stop the hosted services (web server, downloads, plugins, tray) side by side, and give up on them after 10 s
        // instead of the default 30 s: each one bounds its own work, this is the backstop.
        hostBuilder.ConfigureServices(services => services.Configure<HostOptions>(o =>
        {
            o.ServicesStopConcurrently = true;
            o.ShutdownTimeout = TimeSpan.FromSeconds(10);
        }));

        hostBuilder.ConfigureWebHostDefaults(webBuilder =>
        {
            var port = Environment.GetEnvironmentVariable("POPCORN_PORT") ?? "5000";
            webBuilder.UseUrls($"http://127.0.0.1:{port}");
            webBuilder.ConfigureKestrel(o =>
            {
                o.ListenAnyIP(int.Parse(port));
            });
            webBuilder.UseStartup<Startup>();
        });

        // Allow external configuration of the host builder
        _configureHostAction?.Invoke(hostBuilder);

        _host = hostBuilder.Build();
        _logger = _host.Services.GetService<ILogger<PopcornServerHost>>();

        _logger?.LogInformation("Starting Popcorn.Server host...");
        await _host.StartAsync(cancellationToken);
        _logger?.LogInformation("Popcorn.Server host started.");

        // Get the dynamically assigned URL
        var serverAddressesFeature = _host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        if (serverAddressesFeature?.Addresses.FirstOrDefault() is string address)
        {
            ListeningUrl = NormalizeListeningUrl(address);
            _logger?.LogInformation("Popcorn.Server listening on: {ListeningUrl}", ListeningUrl);
        }
        else
        {
            _logger?.LogWarning("Could not determine listening URL for Popcorn.Server.");
            // Fallback or handle error if URL is critical before proceeding
            ListeningUrl = "http://localhost:5000"; // Example fallback, not ideal for dynamic
        }
    }

    public async Task RunAsync(Serilog.ILogger logger, CancellationToken cancellationToken = default)
    {
        await StartAsync(logger, cancellationToken);

        try
        {
            if (!string.IsNullOrWhiteSpace(ListeningUrl))
            {
                Console.WriteLine($"Listening on: {ListeningUrl}");
            }

            // Stops the host itself once shutdown is requested.
            await _host!.WaitForShutdownAsync(cancellationToken);
            _stopped = true;
        }
        finally
        {
            await StopAsync(CancellationToken.None);
        }
    }

    private bool _stopped;

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_host != null && !_stopped)
        {
            _stopped = true;
            _logger?.LogInformation("Stopping Popcorn.Server host...");
            await _host.StopAsync(cancellationToken);
            _logger?.LogInformation("Popcorn.Server host stopped.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is IAsyncDisposable asyncDisposableHost)
        {
            await asyncDisposableHost.DisposeAsync();
        }
        else
        {
            _host?.Dispose();
        }
        _host = null;
        GC.SuppressFinalize(this);
    }

    private static string NormalizeListeningUrl(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return address;
        }

        var host = uri.Host switch
        {
            "0.0.0.0" => "localhost",
            "::" => "localhost",
            "[::]" => "localhost",
            "*" => "localhost",
            "+" => "localhost",
            _ => uri.Host
        };

        return new UriBuilder(uri)
        {
            Host = host
        }.Uri.ToString().TrimEnd('/');
    }
}
