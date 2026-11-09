using Microsoft.Win32;
using Serilog;

namespace Popcorn.Shared.Ffmpeg;

public enum GpuVendor
{
    Other,
    Nvidia,
    Amd,
    Intel,
}

/// <param name="RenderNode">Linux DRM render node (<c>/dev/dri/renderD128</c>): the VA-API device of this GPU.</param>
public sealed record GpuAdapter(GpuVendor Vendor, string Name, string? RenderNode = null);

/// <summary>
/// The GPUs in this machine, by PCI vendor id (what ffmpeg reports it was built with says nothing about the hardware:
/// <c>ffmpeg -hwaccels</c> lists qsv on a PC without an Intel GPU). Linux: <c>/sys/class/drm/renderD*</c>; Windows:
/// the display adapter class in the registry.
/// </summary>
public static class GpuAdapters
{
    private static readonly Lazy<IReadOnlyList<GpuAdapter>> Detected = new(Detect);

    public static IReadOnlyList<GpuAdapter> All => Detected.Value;

    public static bool Has(GpuVendor vendor) => All.Any(adapter => adapter.Vendor == vendor);

    public static GpuVendor VendorOf(string pciVendorId) => pciVendorId.ToLowerInvariant() switch
    {
        "10de" => GpuVendor.Nvidia,
        "1002" or "1022" => GpuVendor.Amd,
        "8086" => GpuVendor.Intel,
        _ => GpuVendor.Other,
    };

    private static IReadOnlyList<GpuAdapter> Detect()
    {
        try
        {
            var adapters = OperatingSystem.IsWindows() ? DetectWindows()
                : OperatingSystem.IsLinux() ? DetectLinux()
                : [];
            Log.Information("GPUs: {gpus}", adapters.Count == 0 ? "none found" : string.Join(", ", adapters));
            return adapters;
        }
        catch (Exception e)
        {
            Log.Warning(e, "GPU detection failed");
            return [];
        }
    }

    private static List<GpuAdapter> DetectLinux()
    {
        const string drm = "/sys/class/drm";
        if (!Directory.Exists(drm)) return [];

        var adapters = new List<GpuAdapter>();
        foreach (var node in Directory.GetDirectories(drm, "renderD*").Order())
        {
            var device = Path.Combine(node, "device");
            var vendorId = ReadHex(Path.Combine(device, "vendor"));
            if (vendorId is null) continue;
            var deviceId = ReadHex(Path.Combine(device, "device"));
            adapters.Add(new GpuAdapter(VendorOf(vendorId), $"{vendorId}:{deviceId}",
                Path.Combine("/dev/dri", Path.GetFileName(node))));
        }
        return adapters;
    }

    private static string? ReadHex(string path)
    {
        try
        {
            var value = File.ReadAllText(path).Trim();
            return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Display adapter device class: one numbered subkey per installed adapter driver.</summary>
    private const string DisplayClassKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static List<GpuAdapter> DetectWindows()
    {
        using var displayClass = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
        if (displayClass is null) return [];

        var adapters = new List<GpuAdapter>();
        foreach (var name in displayClass.GetSubKeyNames().Where(name => name.All(char.IsDigit)))
        {
            try
            {
                using var adapter = displayClass.OpenSubKey(name);
                // e.g. "pci\ven_10de&dev_2803"; virtual/remote display drivers have none
                if (adapter?.GetValue("MatchingDeviceId") is not string id) continue;
                var start = id.IndexOf("ven_", StringComparison.OrdinalIgnoreCase);
                if (start < 0 || id.Length < start + 8) continue;
                var description = adapter.GetValue("DriverDesc") as string ?? id;
                adapters.Add(new GpuAdapter(VendorOf(id.Substring(start + 4, 4)), description));
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException)
            {
                // "Properties" and the like are not readable for users; adapters are
            }
        }
        return adapters;
    }
}
