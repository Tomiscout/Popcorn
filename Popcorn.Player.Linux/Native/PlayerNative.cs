using System.Runtime.InteropServices;
using Serilog;
using Serilog.Events;

namespace Popcorn.Player.Linux.Native;

public sealed record PlayerNativeOptions
{
    public required string OverlayDir { get; init; }
    public required string OverlayPage { get; init; }
    /// <summary>JavaScript run before the overlay's own scripts.</summary>
    public string InjectScript { get; init; } = "";
    public required string CacheDir { get; init; }
    public required string CefLogFile { get; init; }
    public required string SubprocessPath { get; init; }
    public string Title { get; init; } = "Popcorn player";
    public string AppId { get; init; } = "popcorn";
    public int DevToolsPort { get; init; }
}

/// <summary>
/// libpopcorn_player.so (Popcorn.Player.Native, Rust): Wayland window, mpv core + video surface, CEF overlay.
/// Call order: <see cref="Init"/> and mpv clients on <see cref="MpvCore"/> (any order; the core is created on first
/// access) → <see cref="Run"/> (blocks until the
/// window closes) → dispose the clients → <see cref="Destroy"/>.
/// </summary>
public static unsafe partial class PlayerNative
{
    private const string Lib = "popcorn_player";
    private const int EventCloseRequested = 1;
    private const int EventFullscreenChanged = 2;
    private const int EventReady = 3;

    /// <summary>Window is closing (overlay is being torn down, mpv still running). Raised on the <see cref="Run"/> thread.</summary>
    public static event Action? CloseRequested;
    /// <summary>Raised on the <see cref="Run"/> thread.</summary>
    public static event Action<bool>? FullscreenChanged;
    /// <summary>
    /// CEF finished starting. Chromium resets the process signal handlers during start-up (the native layer puts .NET's
    /// back), so don't start child processes (Process.Start, FFProbe, ...) before this: their exit would never be
    /// noticed. Raised once, on the <see cref="Run"/> thread.
    /// </summary>
    public static event Action? Ready;

    /// <summary>The mpv core owned by the native layer (created on first access).</summary>
    public static nint MpvCore => pn_mpv_core() is var core && core != 0
        ? core
        : throw new InvalidOperationException("mpv core could not be created, see log");

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
                CacheDir = Utf8(options.CacheDir),
                CefLogFile = Utf8(options.CefLogFile),
                SubprocessPath = Utf8(options.SubprocessPath),
                Title = Utf8(options.Title),
                AppId = Utf8(options.AppId),
                DevToolsPort = options.DevToolsPort,
            };
            var callbacks = new Callbacks
            {
                UserData = 0,
                Log = &OnLog,
                WindowEvent = &OnWindowEvent,
            };
            var rc = pn_init(&config, callbacks);
            if (rc != 0) throw new InvalidOperationException($"Native player initialisation failed ({rc}), see log");
        }
        finally
        {
            foreach (var ptr in strings) Marshal.FreeCoTaskMem(ptr);
        }
    }

    /// <summary>Must run on the process main thread.</summary>
    public static int Run() => pn_run();

    public static void Quit() => pn_quit();
    public static void SetFullscreen(bool fullscreen) => pn_set_fullscreen(fullscreen ? 1 : 0);
    public static void SetTitle(string title) => pn_set_title(title);
    /// <summary>Terminates the mpv core; waits until all client handles are destroyed.</summary>
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

    [StructLayout(LayoutKind.Sequential)]
    private struct Config
    {
        public byte* OverlayDir;
        public byte* OverlayPage;
        public byte* InjectScript;
        public byte* CacheDir;
        public byte* CefLogFile;
        public byte* SubprocessPath;
        public byte* Title;
        public byte* AppId;
        public int DevToolsPort;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Callbacks
    {
        public nint UserData;
        public delegate* unmanaged<nint, int, byte*, nuint, void> Log;
        public delegate* unmanaged<nint, int, int, void> WindowEvent;
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
    private static partial void pn_destroy();
}
