using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Olbrasoft.SystemTray.Linux;
using Popcorn.Shared.Interfaces;

namespace Popcorn.Platform.Linux;

public class LinuxTrayManager : ITrayManager
{
    private const string AppExecutableName = "Popcorn.Linux";
    private const string TrayIconId = "popcorn";
    private const int DefaultIconSize = 22;
    private const string TrayTooltip = "Popcorn";

    private readonly ILogger<LinuxTrayManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    private TrayIconManager? _trayIconManager;
    private ITrayIcon? _trayIcon;
    private readonly string _iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tray.svg");

    public LinuxTrayManager(
        ILogger<LinuxTrayManager> logger,
        ILoggerFactory loggerFactory,
        IHostApplicationLifetime hostApplicationLifetime)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _hostApplicationLifetime = hostApplicationLifetime;
    }

    public virtual Task<bool> CanUseTray(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsLinux())
        {
            return Task.FromResult(false);
        }

        var hasDisplay = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        var hasSessionBus = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"));
        var hasRuntimeDir = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"));

        return Task.FromResult(hasDisplay && hasSessionBus && hasRuntimeDir);
    }

    public async Task ShowTray(CancellationToken cancellationToken = default)
    {
        if (!await CanUseTray(cancellationToken))
        {
            _logger.LogDebug("Skipping tray startup because environment checks failed.");
            return;
        }

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_trayIcon is not null)
            {
                _trayIcon.Show();
                return;
            }

            var iconRenderer = new IconRenderer(
                _loggerFactory.CreateLogger<IconRenderer>(),
                DefaultIconSize);

            _trayIconManager = new TrayIconManager(
                _loggerFactory.CreateLogger<TrayIconManager>(),
                _loggerFactory,
                iconRenderer);

            var menuHandler = CreateMenuHandler();
            
            _trayIcon = await _trayIconManager.CreateIconAsync(
                TrayIconId,
                EnsureTrayIconFile(),
                TrayTooltip,
                menuHandler,
                cancellationToken);
            // Activate (the click that doesn't open the menu) arrives on the DBus thread.
            _trayIcon.Clicked += (_, _) => _ = Task.Run(ToggleAppAsync);

            _logger.LogInformation("Tray icon started.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start tray icon.");
            await HideTrayCoreAsync(CancellationToken.None);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task HideTray(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Tray icon hidden.");
            await HideTrayCoreAsync(cancellationToken);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    protected virtual Uri GetServerUri()
    {
        var portValue = Environment.GetEnvironmentVariable("POPCORN_PORT");
        var port = int.TryParse(portValue, out var parsedPort) && parsedPort > 0
            ? parsedPort
            : 5000;

        return new Uri($"http://localhost:{port}");
    }

    //WTF?
    protected virtual string? ResolveAppExecutablePath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, AppExecutableName),
            Path.Combine(AppContext.BaseDirectory, $"{AppExecutableName}.exe"),
            Path.Combine(Directory.GetCurrentDirectory(), AppExecutableName),
            Path.Combine(Directory.GetCurrentDirectory(), $"{AppExecutableName}.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private async Task HideTrayCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            _trayIcon?.Hide();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to hide tray icon cleanly.");
        }

        (_trayIconManager as IDisposable)?.Dispose();

        _trayIcon = null;
        _trayIconManager = null;

        await Task.CompletedTask;
    }

    private ITrayMenuHandler CreateMenuHandler()
    {
        var items = new DbusMenuNode[]
        {
            new DbusMenuItem(
                1,
                "Open in browser",
                () => OpenBrowserAsync(CancellationToken.None)
            ),
            new DbusMenuItem(
                2,
                "Open app",
                () => OpenAppAsync(CancellationToken.None)
            ),
            new DbusSeparator(3),
            new DbusMenuItem(
                4,
                "Exit",
                () => ExitAsync(CancellationToken.None)
            ),
        };

        return new PopcornTrayMenuHandler(
            logger: _loggerFactory.CreateLogger<PopcornTrayMenuHandler>(),
            items: items
        );
    }

    private Task OpenBrowserAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = "xdg-open",
            Arguments = GetServerUri().ToString(),
            UseShellExecute = false
        };

        Process.Start(startInfo)?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Icon click: closes the desktop app if it runs, else starts it.</summary>
    private async Task ToggleAppAsync()
    {
        try
        {
            var running = FindAppProcesses();
            running.ForEach(static process => process.Dispose());
            if (running.Count > 0)
            {
                await CloseAppAsync();
            }
            else
            {
                await OpenAppAsync(CancellationToken.None);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Tray click: could not open/close {ExecutableName}.", AppExecutableName);
        }
    }

    private Task OpenAppAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // A running instance is not checked for: Popcorn.Linux is single-instance (CEF profile), a second launch
        // focuses the first window and exits.
        var executablePath = ResolveAppExecutablePath();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            _logger.LogWarning("Could not find {ExecutableName} next to server executable.", AppExecutableName);
            return Task.CompletedTask;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory,
            UseShellExecute = false
        };
        startInfo.Environment["POPCORN_SERVER_URL"] = GetServerUri().ToString();

        // Not tracked: the app may as well have been started from the desktop entry, so closing finds it by name.
        Process.Start(startInfo)?.Dispose();
        _logger.LogInformation("Started {ExecutableName} using existing server at {ServerUrl}.", AppExecutableName, GetServerUri());
        return Task.CompletedTask;
    }

    private async Task ExitAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await CloseAppAsync();
        _hostApplicationLifetime.StopApplication();
    }

    /// <summary>
    /// Closes every running desktop app (browser process; CEF's helpers exit with it): SIGTERM, which Chromium
    /// handles as a normal quit, then a kill after a bounded wait so the tray's Exit always reaches StopApplication.
    /// </summary>
    private async Task CloseAppAsync()
    {
        foreach (var process in FindAppProcesses())
        {
            using (process)
            {
                try
                {
                    if (Kill(process.Id, SigTerm) != 0)
                    {
                        process.Kill(entireProcessTree: true);
                    }

                    try
                    {
                        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
                    }
                    catch (TimeoutException)
                    {
                        process.Kill(entireProcessTree: true);
                    }

                    _logger.LogInformation("Closed {ExecutableName} (pid {Pid}).", AppExecutableName, process.Id);
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    _logger.LogWarning("Could not stop {ExecutableName}: {Message}", AppExecutableName, e.Message);
                }
            }
        }
    }

    /// <summary>Running desktop apps of this user: CEF helpers share the process name but carry <c>--type=</c>.</summary>
    private static List<Process> FindAppProcesses()
    {
        var result = new List<Process>();
        foreach (var process in Process.GetProcessesByName(AppExecutableName))
        {
            string cmdline;
            try
            {
                cmdline = File.ReadAllText($"/proc/{process.Id}/cmdline");
            }
            catch (IOException)
            {
                process.Dispose();
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                process.Dispose();
                continue;
            }

            if (cmdline.Split('\0').Any(static arg => arg.StartsWith("--type=", StringComparison.Ordinal)))
            {
                process.Dispose();
                continue;
            }

            result.Add(process);
        }

        return result;
    }

    private const int SigTerm = 15;

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int pid, int signal);

    private string EnsureTrayIconFile()
    {
        if (!string.IsNullOrWhiteSpace(_iconPath) && File.Exists(_iconPath))
        {
            return _iconPath;
        }

        throw new Exception("No tray icon found");
    }

    private sealed class SimpleTrayMenu : ITrayMenu
    {
        private readonly List<ITrayMenuItem> _items = new();

        public IReadOnlyList<ITrayMenuItem> Items => _items;

        public void AddItem(ITrayMenuItem item) => _items.Add(item);

        public void RemoveItem(ITrayMenuItem item) => _items.Remove(item);

        public void Clear() => _items.Clear();
    }

    private sealed class SimpleTrayMenuItem : ITrayMenuItem
    {
        public SimpleTrayMenuItem(string id, string text)
        {
            Id = id;
            Text = text;
            IsEnabled = true;
        }

        public string Id { get; }

        public string Text { get; set; }

        public bool IsEnabled { get; set; }

        public bool IsSeparator => false;

        public IReadOnlyList<ITrayMenuItem> SubItems => Array.Empty<ITrayMenuItem>();

        public event EventHandler? Clicked;

        public void RaiseClicked() => Clicked?.Invoke(this, EventArgs.Empty);
    }
}
