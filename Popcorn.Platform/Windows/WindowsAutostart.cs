using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Popcorn.Shared.Interfaces;

namespace Popcorn.Platform.Windows;

/// <summary>
/// The server's HKCU Run value "Popcorn" (written by Popcorn.Installer / install_windows.ps1), switched on and off the
/// way Task Manager → Startup apps does it: through its StartupApproved state, so the entry stays listed there and
/// both places show the same thing. Popcorn.Installer's platform/windows.rs writes the same values.
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsAutostart(ILogger<WindowsAutostart> logger) : INativeAutostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "Popcorn";

    public bool IsSupported => true;

    public bool IsEnabled()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey);
        if (run?.GetValue(ValueName) is not string command || string.IsNullOrWhiteSpace(command))
            return false;

        // 12 bytes: first byte even (2) = enabled, odd (3) = disabled, then the FILETIME of the change. No value =
        // never switched in Task Manager = enabled.
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } state || (state[0] & 1) == 0;
    }

    public void SetEnabled(bool enabled)
    {
        using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            // An existing command stays: a dev build switching it must not take over the installed server's entry.
            if (run.GetValue(ValueName) is not string command || string.IsNullOrWhiteSpace(command))
            {
                var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown server executable");
                run.SetValue(ValueName, $"\"{exe}\"");
            }
        }

        var state = new byte[12];
        state[0] = enabled ? (byte)2 : (byte)3;
        if (!enabled)
            BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(state, 4);
        using (var approved = Registry.CurrentUser.CreateSubKey(ApprovedKey))
            approved.SetValue(ValueName, state, RegistryValueKind.Binary);

        logger.LogInformation("Autostart at sign-in {State}", enabled ? "on" : "off");
    }
}
