namespace Popcorn.Shared.Interfaces;

/// <summary>
/// The server's tray icon (Open in browser / Open app / Exit). Linux: StatusNotifier over D-Bus (Popcorn.Linux.Tray),
/// Windows: notification area icon (Popcorn.Windows.Tray). Shown by the server's ServerTrayHostedService.
/// </summary>
public interface ITrayManager
{
    Task<bool> CanUseTray(CancellationToken cancellationToken = default);
    Task ShowTray(CancellationToken cancellationToken = default);
    Task HideTray(CancellationToken cancellationToken = default);
}
