using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Popcorn.Player.Core.Player;

/// <summary>
/// Minimal libmpv client (P/Invoke, no wrapper library) on a client handle of an existing mpv core.
/// The core belongs to whoever created it (the players' native layer, which also owns the video output); this class
/// only owns its own handle and event loop. See mpv/client.h for the API.
/// </summary>
public sealed partial class LibMpvClient : IMpvClient
{
    private const string Lib = "libmpv.so.2";

    private const int FormatFlag = 3;
    private const int FormatDouble = 5;
    private const int EventShutdown = 1;
    private const int EventLogMessage = 2;
    private const int EventFileLoaded = 8;
    private const int EventPropertyChange = 22;

    private readonly ILogger<LibMpvClient> _logger;
    private readonly object _lock = new();
    private readonly Thread _eventThread;
    private nint _handle;
    private volatile bool _disposing;
    private ulong _nextObserveId = 1;

    public event EventHandler<(string Name, double Value)>? DoublePropertyChanged;
    public event EventHandler<(string Name, bool Value)>? FlagPropertyChanged;
    public event EventHandler? FileLoaded;
    public event EventHandler<string>? LogMessage;

    static LibMpvClient()
    {
        // Windows: mpv's MinGW build (MPV/build_windows.sh) is named libmpv-2.dll.
        NativeLibrary.SetDllImportResolver(typeof(LibMpvClient).Assembly, (name, assembly, searchPath) =>
            name == Lib && OperatingSystem.IsWindows() ? NativeLibrary.Load("libmpv-2.dll", assembly, searchPath) : 0);
    }

    /// <param name="core">mpv_handle of an initialised core (e.g. <c>pn_mpv_core()</c>).</param>
    public LibMpvClient(nint core, ILogger<LibMpvClient> logger)
    {
        _logger = logger;
        if (core == 0) throw new ArgumentException("mpv core handle is null", nameof(core));
        _handle = mpv_create_client(core, "popcorn");
        if (_handle == 0) throw new InvalidOperationException("mpv_create_client failed");
        mpv_request_log_messages(_handle, "warn");

        _eventThread = new Thread(EventLoop) { Name = "mpv-events", IsBackground = true };
        _eventThread.Start();
    }

    public void Command(params string[] args)
    {
        var ptrs = new nint[args.Length + 1];
        try
        {
            for (var i = 0; i < args.Length; i++) ptrs[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            lock (_lock)
            {
                if (_handle == 0) return;
                Check(mpv_command(_handle, ptrs), $"command {string.Join(' ', args)}");
            }
        }
        finally
        {
            foreach (var p in ptrs) if (p != 0) Marshal.FreeCoTaskMem(p);
        }
    }

    public void SetProperty(string name, string value)
    {
        lock (_lock)
        {
            if (_handle == 0) return;
            Check(mpv_set_property_string(_handle, name, value), $"set {name}={value}");
        }
    }

    public double? GetDouble(string name)
    {
        lock (_lock)
        {
            if (_handle == 0) return null;
            return mpv_get_property(_handle, name, FormatDouble, out var value) >= 0 ? value : null;
        }
    }

    public string? GetString(string name)
    {
        lock (_lock)
        {
            if (_handle == 0) return null;
            var ptr = mpv_get_property_string(_handle, name);
            if (ptr == 0) return null;
            try { return Marshal.PtrToStringUTF8(ptr); }
            finally { mpv_free(ptr); }
        }
    }

    public void ObserveDouble(string name)
    {
        lock (_lock)
        {
            if (_handle == 0) return;
            Check(mpv_observe_property(_handle, _nextObserveId++, name, FormatDouble), $"observe {name}");
        }
    }

    public void ObserveFlag(string name)
    {
        lock (_lock)
        {
            if (_handle == 0) return;
            Check(mpv_observe_property(_handle, _nextObserveId++, name, FormatFlag), $"observe {name}");
        }
    }

    private void Check(int rc, string what)
    {
        if (rc < 0) throw new InvalidOperationException($"mpv {what}: {Marshal.PtrToStringUTF8(mpv_error_string(rc))}");
    }

    private void EventLoop()
    {
        while (true)
        {
            var ev = mpv_wait_event(_handle, -1);
            if (_disposing) break;
            var id = Marshal.ReadInt32(ev);
            var data = Marshal.ReadIntPtr(ev, 16);
            try
            {
                switch (id)
                {
                    case EventShutdown:
                        _logger.LogInformation("mpv core shutting down");
                        Destroy();
                        return;
                    case EventFileLoaded:
                        FileLoaded?.Invoke(this, EventArgs.Empty);
                        break;
                    case EventPropertyChange when data != 0:
                        // mpv_event_property { const char *name; mpv_format format; void *data; }
                        var name = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(data)) ?? "";
                        var format = Marshal.ReadInt32(data, 8);
                        var valuePtr = Marshal.ReadIntPtr(data, 16);
                        if (format == FormatDouble && valuePtr != 0)
                        {
                            var value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(valuePtr));
                            DoublePropertyChanged?.Invoke(this, (name, value));
                        }
                        else if (format == FormatFlag && valuePtr != 0)
                        {
                            FlagPropertyChanged?.Invoke(this, (name, Marshal.ReadInt32(valuePtr) != 0));
                        }
                        break;
                    case EventLogMessage when data != 0:
                        // mpv_event_log_message { const char *prefix; const char *level; const char *text; ... }
                        var prefix = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(data));
                        var text = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(data, 16))?.TrimEnd();
                        LogMessage?.Invoke(this, $"[{prefix}] {text}");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "mpv event handler failed (event {id})", id);
            }
        }
        Destroy();
    }

    private void Destroy()
    {
        lock (_lock)
        {
            if (_handle == 0) return;
            mpv_destroy(_handle);
            _handle = 0;
        }
    }

    public void Dispose()
    {
        nint handle;
        lock (_lock) handle = _handle;
        if (handle == 0) return;
        _disposing = true;
        mpv_wakeup(handle);
        if (Thread.CurrentThread != _eventThread) _eventThread.Join(TimeSpan.FromSeconds(2));
        Destroy();
    }

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint mpv_create_client(nint ctx, string name);

    [LibraryImport(Lib)]
    private static partial void mpv_destroy(nint ctx);

    [LibraryImport(Lib)]
    private static partial void mpv_wakeup(nint ctx);

    [LibraryImport(Lib)]
    private static partial int mpv_command(nint ctx, nint[] args);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int mpv_set_property_string(nint ctx, string name, string data);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int mpv_get_property(nint ctx, string name, int format, out double data);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint mpv_get_property_string(nint ctx, string name);

    [LibraryImport(Lib)]
    private static partial void mpv_free(nint data);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int mpv_observe_property(nint ctx, ulong replyUserdata, string name, int format);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int mpv_request_log_messages(nint ctx, string minLevel);

    [LibraryImport(Lib)]
    private static partial nint mpv_wait_event(nint ctx, double timeout);

    [LibraryImport(Lib)]
    private static partial nint mpv_error_string(int error);
}
