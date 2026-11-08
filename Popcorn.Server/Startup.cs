using System.Net.WebSockets;
using System.Text.Json.Serialization;
using FluentMigrator.Runner;
using Popcorn.Persistence;
using Popcorn.Server.PluginHost;
using Popcorn.Server.Services.Disk;
using Serilog;

namespace Popcorn.Server;

public class Startup
{
    private const string LocalDevCorsPolicy = "LocalDev";
    
    public void ConfigureServices(IServiceCollection services)
    {
        services
            .AddControllers()
            .AddJsonOptions(opts =>
            {
                opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });
        services.AddPopcornServices();
        services.AddPopcornDatabase();
        services.AddSerilog();
        
        services.AddCors(options =>
        {
            options.AddPolicy(LocalDevCorsPolicy, builder =>
            {
                builder
                    .WithOrigins("*")
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });
    }
    
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (!env.IsDevelopment()) //For some reason it cant resolve IMigrationRunner when Development
        {
            var runner = app.ApplicationServices.GetRequiredService<IMigrationRunner>();
            runner.MigrateUp();
        }
        
        var scratchFilesService = app.ApplicationServices.GetRequiredService<ScratchFilesCleanupService>();
        scratchFilesService.InitialCleanup();
        
        // Dev: the Vite dev server (another origin) imports plugin UI modules from here
        if (env.IsDevelopment())
        {
            app.UseCors(LocalDevCorsPolicy);
        }

        var pluginManager = app.ApplicationServices.GetRequiredService<PluginManager>();
        pluginManager.LoadAll();
        app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(pluginManager.UnloadAll);
        app.UseStaticFiles(new StaticFileOptions
        {
            RequestPath = "/plugins",
            FileProvider = new PluginWebFileProvider(pluginManager),
        });

        var webPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web");
        var webFiles = Directory.Exists(webPath)
            ? new Microsoft.Extensions.FileProviders.PhysicalFileProvider(webPath)
            : null;
        if (webFiles != null)
        {
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = webFiles });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = webFiles });
        }
        else
        {
            Log.Error("Web bundle directory not found at {webPath}", webPath);
        }
        
        app.UseWebSockets();
        app.Map("/ws", wsApp =>
        {
            wsApp.Run(async context =>
            {
                var serviceScopeFactory = app.ApplicationServices.GetRequiredService<IServiceScopeFactory>();
                await HandleWebSocket(context, serviceScopeFactory);
            });
        });
        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
            // SPA fallback: client-side routes (/watch, /watch/123, ...) have no file on disk, so serve
            // index.html and let React Router handle them. Only matches paths without a file extension,
            // so missing assets still 404.
            if (webFiles != null)
            {
                endpoints.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = webFiles });
            }
        });
    }
    
    private static async Task HandleWebSocket(HttpContext context, IServiceScopeFactory serviceScopeFactory)
    {
        if (context.WebSockets.IsWebSocketRequest)
        {
            using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            Log.Information("WebSocket connection established");

            var bus = new WebSocketBus(webSocket, serviceScopeFactory);
            WebSocketServices.RegisterWebServices(bus, typeof(Startup).Assembly);

            // Plugin status messages change on their own (IPluginStatus): push the plugin list, a burst as one.
            var pluginManager = context.RequestServices.GetRequiredService<PluginManager>();
            var statusPending = 0;
            void OnPluginStatus()
            {
                if (Interlocked.Exchange(ref statusPending, 1) == 1) return;
                _ = Task.Run(async () =>
                {
                    await Task.Delay(200);
                    Interlocked.Exchange(ref statusPending, 0);
                    if (webSocket.State != WebSocketState.Open) return;
                    try
                    {
                        using var scope = serviceScopeFactory.CreateScope();
                        await ActivatorUtilities.CreateInstance<Services.PluginWebService>(scope.ServiceProvider, bus).SendPluginList();
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Could not send plugin status change");
                    }
                });
            }
            pluginManager.StatusChanged += OnPluginStatus;
            try
            {
                await bus.ListenAsync();
            }
            finally
            {
                pluginManager.StatusChanged -= OnPluginStatus;
            }

            if (webSocket.State == WebSocketState.Open)
            {
                Log.Information("Closing Websocket");
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            }
        }
        else
        {
            context.Response.StatusCode = 400;
        }
    }
}