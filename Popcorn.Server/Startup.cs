using FluentMigrator.Runner;
using Popcorn.Persistence;
using Serilog;

namespace Popcorn.Server;

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddPopcornServices();
        services.AddPopcornDatabase();
        services.AddSerilog();
    }
    
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        var runner = app.ApplicationServices.GetRequiredService<IMigrationRunner>();
        runner.MigrateUp();
        
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
    }
    
    private static async Task HandleWebSocket(HttpContext context, IServiceScopeFactory serviceScopeFactory)
    {
        if (context.WebSockets.IsWebSocketRequest)
        {
            using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            Log.Information("WebSocket connection established");

            var bus = new WebSocketBus(webSocket, serviceScopeFactory);
            WebSocketServices.RegisterWebServices(bus);
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