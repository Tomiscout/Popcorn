namespace Popcorn.Shared.Interfaces;

/// <summary>
/// Whether the server starts when the user signs in. Lives in the OS (Windows: the HKCU Run value, the same switch
/// as Task Manager → Startup apps), not in Popcorn's settings, so changes made there show up here.
/// </summary>
public interface INativeAutostart
{
    /// <summary>False = Popcorn cannot manage it on this platform (the Settings page hides the switch).</summary>
    bool IsSupported { get; }

    bool IsEnabled();

    void SetEnabled(bool enabled);
}

/// <summary>Platforms without an implementation (Windows: <c>WindowsAutostart</c>, Linux: <c>LinuxAutostart</c>).</summary>
public class UnsupportedAutostart : INativeAutostart
{
    public bool IsSupported => false;

    public bool IsEnabled() => false;

    public void SetEnabled(bool enabled) => throw new PlatformNotSupportedException("Autostart is not managed on this platform");
}
