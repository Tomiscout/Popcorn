namespace Popcorn.Linux.Tray.Interfaces;

public interface ITrayManager
{
    Task<bool> CanUseTray(CancellationToken cancellationToken = default);
    Task ShowTray(CancellationToken cancellationToken = default);
    Task HideTray(CancellationToken cancellationToken = default);
}
