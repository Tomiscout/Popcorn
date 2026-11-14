using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Popcorn.Domain.Interfaces;

namespace Popcorn.Platform.Windows;

/// <summary>
/// Keeps the system and the display on while a video plays: a power request (PowerCreateRequest), which unlike
/// SetThreadExecutionState is not tied to the calling thread. Shows up in <c>powercfg /requests</c>.
/// </summary>
public sealed class WindowsSleepInhibitor(ILogger<WindowsSleepInhibitor> logger) : ISleepInhibitor
{
    private const int PowerRequestDisplayRequired = 0;
    private const int PowerRequestSystemRequired = 1;
    private const uint PowerRequestContextVersion = 0;
    private const uint PowerRequestContextSimpleString = 0x1;

    private readonly object _gate = new();
    private nint _request;
    private bool _active;
    private bool _disposed;

    public void SetInhibited(bool inhibited)
    {
        lock (_gate)
        {
            if (_disposed || inhibited == _active) return;
            try
            {
                if (_request == 0) _request = CreateRequest();
                if (inhibited)
                {
                    Check(PowerSetRequest(_request, PowerRequestSystemRequired), "PowerSetRequest");
                    Check(PowerSetRequest(_request, PowerRequestDisplayRequired), "PowerSetRequest");
                }
                else
                {
                    Check(PowerClearRequest(_request, PowerRequestDisplayRequired), "PowerClearRequest");
                    Check(PowerClearRequest(_request, PowerRequestSystemRequired), "PowerClearRequest");
                }
                logger.LogInformation(inhibited ? "Sleep inhibited" : "Sleep inhibition released");
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Could not {action} the sleep inhibition", inhibited ? "take" : "release");
            }
            _active = inhibited;
        }
    }

    private static nint CreateRequest()
    {
        var reason = Marshal.StringToHGlobalUni("Playing a video");
        try
        {
            var context = new ReasonContext
            {
                Version = PowerRequestContextVersion,
                Flags = PowerRequestContextSimpleString,
                SimpleReasonString = reason,
            };
            var handle = PowerCreateRequest(ref context);
            if (handle is 0 or -1) throw new InvalidOperationException($"PowerCreateRequest failed ({Marshal.GetLastPInvokeError()})");
            return handle;
        }
        finally
        {
            Marshal.FreeHGlobal(reason);
        }
    }

    private static void Check(bool ok, string what)
    {
        if (!ok) throw new InvalidOperationException($"{what} failed ({Marshal.GetLastPInvokeError()})");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            // Closing the handle clears its requests.
            if (_request != 0) CloseHandle(_request);
            _request = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ReasonContext
    {
        public uint Version;
        public uint Flags;
        public nint SimpleReasonString;
        // Rest of the union (its Detailed struct is larger than the string pointer).
        private nint _padding1;
        private nint _padding2;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint PowerCreateRequest(ref ReasonContext context);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerSetRequest(nint request, int requestType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerClearRequest(nint request, int requestType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
