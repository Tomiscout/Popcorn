using System.Text.Json.Serialization;
using FluentMigrator.Runner;
using Popcorn.Persistence;
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
        
        var webPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web");
        if (Directory.Exists(webPath))
        {
            app.UseDefaultFiles(new DefaultFilesOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(webPath)
            });

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(webPath)
            });
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
        if (env.IsDevelopment())
        {
            app.UseCors(LocalDevCorsPolicy);
        }
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
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
            await bus.ListenAsync();

            if (!webSocket.CloseStatus.HasValue)
            {
                Log.Information("Closing Websocket");
                await webSocket.CloseAsync(webSocket.CloseStatus.Value, webSocket?.CloseStatusDescription, CancellationToken.None);
            }
        }
        else
        {
            context.Response.StatusCode = 400;
        }
    }
}