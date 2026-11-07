using System.Runtime.InteropServices;
using Serilog;
using Serilog.Events;

namespace Popcorn.Player.Windows.Native;

public sealed record PlayerNativeOptions
{
    public required string OverlayDir { get; init; }
    public required string OverlayPage { get; init; }
    /// <summary>JavaScript run before the overlay's own scripts.</summary>
    public string InjectScript { get; init; } = "";
    /// <summary>WebView2 profile folder.</summary>
    public required string UserDataDir { get; init; }
    public string Title { get; init; } = "Popcorn player";
    public int DevToolsPort { get; init; }
}

/// <summary>
/// popcorn_player.dll (Popcorn.Player.Windows.Native, Rust): Win32 window, mpv core with its video in that window,
/// WebView2 overlay. Counterpart of Popcorn.Player.Linux's PlayerNative.
/// Call order, on the main thread (STA): <see cref="Init"/> and mpv clients on <see cref="MpvCore"/> (any order) →
/// <see cref="Run"/> (blocks until the window closes) → dispose the clients → <see cref="Destroy"/>.
/// </summary>
public static unsafe partial class PlayerNative
{
    private const string Lib = "popcorn_player";
    private const int EventCloseRequested = 1;
    private const int EventFullscreenChanged = 2;
    private const int EventReady = 3;

    /// <summary>Window is closing (overlay gone, mpv still running). Raised on the <see cref="Run"/> thread.</summary>
    public static event Action? CloseRequested;
    /// <summary>Raised on the <see cref="Run"/> thread.</summary>
    public static event Action<bool>? FullscreenChanged;
    /// <summary>The overlay's WebView2 exists and loads the page. Raised once, on the <see cref="Run"/> thread.</summary>
    public static event Action? Ready;
    /// <summary>Full paths of files dropped on the window. Raised on the <see cref="Run"/> thread.</summary>
    public static event Action<string[]>? FilesDropped;

    /// <summary>The mpv core owned by the native layer (created on first access).</summary>
    public static nint MpvCore => pn_mpv_core() is var core && core != 0
        ? core
        : throw new InvalidOperationException("mpv core could not be created, see log");

    /// <summary>The main window (owner for dialogs).</summary>
    public static nint WindowHandle => pn_window_handle();

    /// <summary>Creates the (hidden) window. Must run on the thread that later calls <see cref="Run"/>.</summary>
    public static void Init(PlayerNativeOptions options)
    {
        var strings = new List<nint>();
        byte* Utf8(string value)
        {
            var ptr = Marshal.StringToCoTaskMemUTF8(value);
            strings.Add(ptr);
            return (byte*)ptr;
        }

        try
        {
            var config = new Config
            {
                OverlayDir = Utf8(options.OverlayDir),
                OverlayPage = Utf8(options.OverlayPage),
                InjectScript = Utf8(options.InjectScript),
                UserDataDir = Utf8(options.UserDataDir),
                Title = Utf8(options.Title),
                DevToolsPort = options.DevToolsPort,
            };
            var callbacks = new Callbacks
            {
                UserData = 0,
                Log = &OnLog,
                WindowEvent = &OnWindowEvent,
                FilesDropped = &OnFilesDropped,
            };
            var rc = pn_init(&config, callbacks);
            if (rc != 0) throw new InvalidOperationException($"Native player initialisation failed ({rc}), see log");
        }
        finally
        {
            foreach (var ptr in strings) Marshal.FreeCoTaskMem(ptr);
        }
    }

    public static int Run() => pn_run();

    public static void Quit() => pn_quit();
    public static void SetFullscreen(bool fullscreen) => pn_set_fullscreen(fullscreen ? 1 : 0);
    public static void SetTitle(string title) => pn_set_title(title);
    /// <summary>Removes the logo the window shows while the overlay starts. Any thread.</summary>
    public static void HideSplash() => pn_hide_splash();
    /// <summary>Terminates the mpv core (waits until all client handles are destroyed), then the window.</summary>
    public static void Destroy() => pn_destroy();

    [UnmanagedCallersOnly]
    private static void OnLog(nint userData, int level, byte* message, nuint length)
    {
        try
        {
            var text = System.Text.Encoding.UTF8.GetString(message, (int)length);
            var serilogLevel = level switch
            {
                0 => LogEventLevel.Debug,
                1 => LogEventLevel.Information,
                2 => LogEventLevel.Warning,
                _ => LogEventLevel.Error,
            };
            Log.Write(serilogLevel, "[native] {message}", text);
        }
        catch
        {
            // Never let an exception unwind into native code.
        }
    }

    [UnmanagedCallersOnly]
    private static void OnWindowEvent(nint userData, int windowEvent, int value)
    {
        try
        {
            switch (windowEvent)
            {
                case EventCloseRequested:
                    CloseRequested?.Invoke();
                    break;
                case EventFullscreenChanged:
                    FullscreenChanged?.Invoke(value != 0);
                    break;
                case EventReady:
                    Ready?.Invoke();
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Native window event {event} handler failed", windowEvent);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnFilesDropped(nint userData, byte* paths, nuint length)
    {
        try
        {
            var text = System.Text.Encoding.UTF8.GetString(paths, (int)length);
            FilesDropped?.Invoke(text.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Files dropped handler failed");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Config
    {
        public byte* OverlayDir;
        public byte* OverlayPage;
        public byte* InjectScript;
        public byte* UserDataDir;
        public byte* Title;
        public int DevToolsPort;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Callbacks
    {
        public nint UserData;
        public delegate* unmanaged<nint, int, byte*, nuint, void> Log;
        public delegate* unmanaged<nint, int, int, void> WindowEvent;
        public delegate* unmanaged<nint, byte*, nuint, void> FilesDropped;
    }

    [LibraryImport(Lib)]
    private static partial int pn_init(Config* config, Callbacks callbacks);

    [LibraryImport(Lib)]
    private static partial nint pn_mpv_core();

    [LibraryImport(Lib)]
    private static partial int pn_run();

    [LibraryImport(Lib)]
    private static partial void pn_quit();

    [LibraryImport(Lib)]
    private static partial void pn_set_fullscreen(int fullscreen);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial void pn_set_title(string title);

    [LibraryImport(Lib)]
    private static partial void pn_hide_splash();

    [LibraryImport(Lib)]
    private static partial nint pn_window_handle();

    [LibraryImport(Lib)]
    private static partial void pn_destroy();
}
