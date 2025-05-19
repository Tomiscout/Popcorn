using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection; // For IServerAddressesFeature
using Microsoft.AspNetCore.Hosting.Server.Features; // For IServerAddressesFeature
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.Logging; // For ILogger
using Serilog; // If you want to ensure Serilog is configured by default

namespace Popcorn.Server;

public class PopcornServerHost : IAsyncDisposable
{
    private IHost? _host;
    private readonly string[] _args;
    private readonly Action<IHostBuilder>? _configureHostAction;
    private ILogger<PopcornServerHost>? _logger;

    public string? ListeningUrl { get; private set; }

    public PopcornServerHost(string[]? args = null, Action<IHostBuilder>? configureHostAction = null)
    {
        _args = args ?? Array.Empty<string>();
        _configureHostAction = configureHostAction;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .CreateBootstrapLogger();

        var hostBuilder = Host.CreateDefaultBuilder(_args);

        hostBuilder.UseSerilog((context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.Console());

        hostBuilder.ConfigureWebHostDefaults(webBuilder =>
        {
            webBuilder.UseUrls("http://127.0.0.1:0"); // "0" means dynamic port
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
            ListeningUrl = address;
            _logger?.LogInformation("Popcorn.Server listening on: {ListeningUrl}", ListeningUrl);
        }
        else
        {
            _logger?.LogWarning("Could not determine listening URL for Popcorn.Server.");
            // Fallback or handle error if URL is critical before proceeding
            ListeningUrl = "http://localhost:5000"; // Example fallback, not ideal for dynamic
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_host != null)
        {
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
}