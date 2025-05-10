namespace Popcorn.Server.Handlers;

public static class WebSocketHandlers
{
    public static void RegisterAll(WebSocketBus bus, IServiceScopeFactory scopeFactory)
    {
        MediaItemHandlers.Register(bus, scopeFactory);
        MediaListHandlers.Register(bus, scopeFactory);
        FileHandlers.Register(bus, scopeFactory);
        LibraryHandlers.Register(bus, scopeFactory);
        SettingsHandlers.Register(bus, scopeFactory);
    }
}