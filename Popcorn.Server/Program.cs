using FluentMigrator.Runner;
using Popcorn.Persistence;
using Popcorn.Server;
using Popcorn.Server.Handlers;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddPopcornServices();
builder.Services.AddPopcornDatabase();
builder.Services.AddSerilog();

var app = builder.Build();

var runner = app.Services.GetRequiredService<IMigrationRunner>();
runner.MigrateUp();

app.UseWebSockets();
app.Map("/ws", HandleWebSocket);

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
    // app.UseSwagger();
    // app.UseSwaggerUI();
//}

//app.UseHttpsRedirection();

try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}

return;

static async Task HandleWebSocket(HttpContext context, IServiceScopeFactory serviceScopeFactory)
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        Log.Information("WebSocket connection established");

        var bus = new WebSocketBus(webSocket);
        WebSocketHandlers.RegisterAll(bus, serviceScopeFactory);
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