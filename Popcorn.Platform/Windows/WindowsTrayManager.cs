using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Popcorn.Shared.Interfaces;
using static Popcorn.Platform.Windows.Win32;

namespace Popcorn.Platform.Windows;

/// <summary>
/// Notification area icon of the server on Windows, same menu as the Linux tray: Open in browser / Open app / Exit
/// (+ Check for updates when Popcorn.Installer.exe is bundled).
/// Left click opens the app. Runs its own Win32 message loop on a background thread (hidden window that receives
/// the icon's messages); re-adds the icon when Explorer restarts (TaskbarCreated).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsTrayManager(ILogger<WindowsTrayManager> logger, IHostApplicationLifetime lifetime) : ITrayManager
{
    private const string AppExecutableName = "Popcorn.Windows.exe";
    private const string InstallerExecutableName = "Popcorn.Installer.exe";
    private const string WindowClass = "PopcornServerTray";
    private const string Tooltip = "Popcorn";
    private const uint TrayMessage = WM_APP + 1;
    private const uint IconId = 1;

    private const int MenuOpenBrowser = 1;
    private const int MenuOpenApp = 2;
    private const int MenuExit = 3;
    private const int MenuCheckUpdates = 4;

    private readonly object _lock = new();
    private Thread? _thread;
    private IntPtr _hwnd;
    private IntPtr _icon;
    private bool _ownsIcon;
    private uint _taskbarCreatedMessage;
    private WndProc? _wndProc; // referenced so the GC keeps the callback alive
    private Process? _launchedApp;

    public Task<bool> CanUseTray(CancellationToken cancellationToken = default) =>
        Task.FromResult(OperatingSystem.IsWindows() && Environment.UserInteractive);

    public Task ShowTray(CancellationToken cancellationToken = default)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            if (_thread is not null)
            {
                return Task.CompletedTask;
            }

            _thread = new Thread(() => RunMessageLoop(ready)) { IsBackground = true, Name = "Tray" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        return ready.Task.WaitAsync(cancellationToken);
    }

    public Task HideTray(CancellationToken cancellationToken = default)
    {
        Thread? thread;
        lock (_lock)
        {
            thread = _thread;
            _thread = null;
            if (_hwnd != IntPtr.Zero)
            {
                PostMessage(_hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
        }

        thread?.Join(TimeSpan.FromSeconds(2));
        return Task.CompletedTask;
    }

    private void RunMessageLoop(TaskCompletionSource ready)
    {
        var instance = GetModuleHandle(null);
        try
        {
            _wndProc = OnWindowMessage;
            var windowClass = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = instance,
                lpszClassName = WindowClass,
            };
            if (RegisterClassEx(ref windowClass) == 0)
            {
                throw new InvalidOperationException($"RegisterClassEx failed ({Marshal.GetLastWin32Error()})");
            }

            // A hidden top-level window rather than a message-only one: those don't get the TaskbarCreated broadcast.
            var hwnd = CreateWindowEx(0, WindowClass, Tooltip, 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
            {
                throw new InvalidOperationException($"CreateWindowEx failed ({Marshal.GetLastWin32Error()})");
            }

            lock (_lock)
            {
                _hwnd = hwnd;
            }

            _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
            LoadTrayIcon();
            AddIcon();
            logger.LogInformation("Tray icon started.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start tray icon.");
            Cleanup(instance);
            ready.TrySetResult();
            return;
        }

        ready.TrySetResult();

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        Cleanup(instance);
        logger.LogInformation("Tray icon hidden.");
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (msg == TrayMessage)
            {
                switch ((uint)lParam.ToInt64())
                {
                    case WM_LBUTTONUP:
                        OpenApp();
                        break;
                    case WM_RBUTTONUP:
                        ShowMenu(hwnd);
                        break;
                }
                return IntPtr.Zero;
            }

            if (msg == _taskbarCreatedMessage && msg != 0)
            {
                AddIcon();
                return IntPtr.Zero;
            }

            switch (msg)
            {
                case WM_CLOSE:
                    RemoveIcon();
                    DestroyWindow(hwnd);
                    return IntPtr.Zero;
                case WM_DESTROY:
                    lock (_lock)
                    {
                        _hwnd = IntPtr.Zero;
                    }
                    PostQuitMessage(0);
                    return IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            // Never let an exception unwind into user32.
            logger.LogError(ex, "Tray message {Message} failed.", msg);
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void ShowMenu(IntPtr hwnd)
    {
        var menu = CreatePopupMenu();
        try
        {
            AppendMenu(menu, MF_STRING, MenuOpenBrowser, "Open in browser");
            AppendMenu(menu, MF_STRING, MenuOpenApp, "Open app");
            if (File.Exists(InstallerPath))
            {
                AppendMenu(menu, MF_STRING, MenuCheckUpdates, "Check for updates");
            }
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING, MenuExit, "Exit");
            SetMenuDefaultItem(menu, MenuOpenApp, 0);

            GetCursorPos(out var point);
            // Without the foreground switch the menu doesn't close when clicking elsewhere (documented quirk).
            SetForegroundWindow(hwnd);
            var command = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_NONOTIFY, point.X, point.Y, hwnd, IntPtr.Zero);
            PostMessage(hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            switch (command)
            {
                case MenuOpenBrowser:
                    OpenBrowser();
                    break;
                case MenuOpenApp:
                    OpenApp();
                    break;
                case MenuCheckUpdates:
                    CheckForUpdates();
                    break;
                case MenuExit:
                    Exit();
                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private static Uri GetServerUri()
    {
        var port = int.TryParse(Environment.GetEnvironmentVariable("POPCORN_PORT"), out var parsed) && parsed > 0 ? parsed : 5000;
        return new Uri($"http://localhost:{port}");
    }

    private void OpenBrowser()
    {
        Process.Start(new ProcessStartInfo(GetServerUri().ToString()) { UseShellExecute = true })?.Dispose();
    }

    /// <summary>Starts the desktop app; a second start hands over to the running window, which comes to the front.</summary>
    private void OpenApp()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, AppExecutableName);
        if (!File.Exists(executable))
        {
            logger.LogWarning("{Executable} not found, opening the browser instead.", executable);
            OpenBrowser();
            return;
        }

        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
        };
        // Like the Linux tray: point the app at this server, so it never tries to start another one.
        startInfo.Environment["POPCORN_SERVER_URL"] = GetServerUri().ToString();

        var process = Process.Start(startInfo);
        if (process is null)
        {
            return;
        }

        if (_launchedApp is { HasExited: false })
        {
            process.Dispose(); // hands over to the running instance and exits
            return;
        }

        _launchedApp?.Dispose();
        _launchedApp = process;
    }

    private static string InstallerPath => Path.Combine(AppContext.BaseDirectory, InstallerExecutableName);

    /// <summary>Popcorn.Installer shows whether a newer release exists and installs it (stopping this server).</summary>
    private void CheckForUpdates()
    {
        try
        {
            Process.Start(new ProcessStartInfo(InstallerPath)
            {
                // Installs a newer release right away (no offer screen), restarts what ran.
                ArgumentList = { "--from-app" },
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
            })?.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not start {Installer}.", InstallerPath);
        }
    }

    private void Exit()
    {
        // Like the Linux tray: the app window it started goes with the server.
        try
        {
            if (_launchedApp is { HasExited: false })
            {
                _launchedApp.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not stop the app window.");
        }

        lifetime.StopApplication();
    }

    private void LoadTrayIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "popcorn.ico");
        _icon = File.Exists(path)
            ? LoadImage(IntPtr.Zero, path, IMAGE_ICON, GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON), LR_LOADFROMFILE)
            : IntPtr.Zero;
        _ownsIcon = _icon != IntPtr.Zero;
        if (!_ownsIcon)
        {
            logger.LogWarning("Tray icon {Path} missing, using the default application icon.", path);
            _icon = LoadIcon(IntPtr.Zero, IDI_APPLICATION);
        }
    }

    private NOTIFYICONDATA IconData() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = IconId,
        uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
        uCallbackMessage = TrayMessage,
        hIcon = _icon,
        szTip = Tooltip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private void AddIcon()
    {
        var data = IconData();
        if (!Shell_NotifyIcon(NIM_ADD, ref data))
        {
            logger.LogWarning("Shell_NotifyIcon(NIM_ADD) failed.");
        }
    }

    private void RemoveIcon()
    {
        var data = IconData();
        Shell_NotifyIcon(NIM_DELETE, ref data);
    }

    private void Cleanup(IntPtr instance)
    {
        lock (_lock)
        {
            if (_hwnd != IntPtr.Zero)
            {
                RemoveIcon();
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
        }

        if (_ownsIcon)
        {
            DestroyIcon(_icon);
        }
        _icon = IntPtr.Zero;
        UnregisterClass(WindowClass, instance);
    }
}
