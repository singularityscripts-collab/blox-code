using Microsoft.Win32;

namespace BloxCode.Core;

public enum GpuVendor { Unknown, Intel, Amd, Nvidia }

/// <summary>A graphics adapter as the system reports it; VRAM is null when it can't be read.</summary>
public sealed partial record GpuInfo(string Name, GpuVendor Vendor, long? VramBytes)
{
    /// <summary>Shares the computer's memory instead of having its own: Intel's (bar Arc cards) and AMD's APUs.</summary>
    public bool IsIntegrated => Vendor switch
    {
        // Discrete Intel cards are Arc A/B (A750, B580); "Arc Graphics" alone is the built-in one.
        GpuVendor.Intel => !DiscreteArc().IsMatch(Name),
        // AMD's built-in chips are "Radeon Graphics" / "Radeon 780M Graphics" / "Vega 8 Graphics"; cards say RX.
        GpuVendor.Amd => VramBytes is < 1L << 30 || (!Name.Contains("RX", StringComparison.OrdinalIgnoreCase) && Name.Contains("Graphics", StringComparison.OrdinalIgnoreCase)),
        _ => false,
    };

    [System.Text.RegularExpressions.GeneratedRegex(@"Arc(\(TM\))?\s+[AB]\d", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex DiscreteArc();

    public string Describe() => VramBytes is { } vram && !IsIntegrated ? $"{Name}, {Math.Round(vram / (double)(1L << 30))} GB" : Name;
}

/// <summary>
/// Picks a graphics preset for this computer from its graphics card, so players don't have to
/// guess. Only a suggestion: nothing is applied until the player clicks it.
/// </summary>
public static class GraphicsAdvisor
{
    private static readonly Lazy<GpuInfo?> Detected = new(DetectSafely);

    public static GpuInfo? Gpu => Detected.Value;

    public static GraphicsPreset? Recommended => Gpu is { } gpu ? Recommend(gpu) : null;

    public static GraphicsPreset Recommend(GpuInfo gpu)
    {
        if (gpu.IsIntegrated)
        {
            // Older Intel HD/UHD chips struggle even at Low; Iris Xe and AMD's APUs manage Low.
            bool weak = gpu.Vendor == GpuVendor.Intel && (gpu.Name.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase)
                                                         || gpu.Name.Contains("UHD", StringComparison.OrdinalIgnoreCase));
            return weak ? GraphicsPreset.Lowest : GraphicsPreset.Low;
        }
        return gpu.VramBytes switch
        {
            null => gpu.Name.Contains("RTX", StringComparison.OrdinalIgnoreCase) ? GraphicsPreset.High : GraphicsPreset.Medium,
            < 2L << 30 => GraphicsPreset.Low,
            < 4L << 30 => GraphicsPreset.Medium,
            _ => GraphicsPreset.High,
        };
    }

    private static GpuInfo? DetectSafely()
    {
        try
        {
            var gpus = OperatingSystem.IsWindows() ? FromRegistry() : OperatingSystem.IsLinux() ? FromSysfs() : new List<GpuInfo>();
            // Laptops often have two: the dedicated card is the one games use.
            var best = gpus.OrderBy(g => g.IsIntegrated).ThenByDescending(g => g.VramBytes ?? 0).FirstOrDefault();
            if (best is not null)
                Logger.Info("GraphicsAdvisor::Detect", $"{best.Describe()} ({best.Vendor}{(best.IsIntegrated ? ", integrated" : "")}) of {gpus.Count}");
            return best;
        }
        catch (Exception ex)
        {
            Logger.Warn("GraphicsAdvisor::Detect", ex.Message);
            return null;
        }
    }

    private static GpuVendor VendorOf(string pciId) => pciId.ToLowerInvariant() switch
    {
        var id when id.Contains("8086") => GpuVendor.Intel,
        var id when id.Contains("1002") => GpuVendor.Amd,
        var id when id.Contains("10de") => GpuVendor.Nvidia,
        _ => GpuVendor.Unknown,
    };

    /// <summary>Windows: the display adapter class in the registry (no WMI needed).</summary>
    private static List<GpuInfo> FromRegistry()
    {
        var gpus = new List<GpuInfo>();
        if (!OperatingSystem.IsWindows())
            return gpus;

        using var display = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
        if (display is null)
            return gpus;

        foreach (string sub in display.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
        {
            using var key = display.OpenSubKey(sub);
            if (key?.GetValue("DriverDesc") is not string name || key.GetValue("MatchingDeviceId") is not string device)
                continue;
            // Virtual displays (emulators, remote desktop) aren't PCI devices.
            if (!device.StartsWith("pci\\", StringComparison.OrdinalIgnoreCase))
                continue;

            long? vram = key.GetValue("HardwareInformation.qwMemorySize") switch
            {
                long q when q > 0 => q,
                byte[] { Length: 8 } b => BitConverter.ToInt64(b),
                _ => key.GetValue("HardwareInformation.MemorySize") switch
                {
                    int i when i != 0 => (uint)i,
                    byte[] { Length: >= 4 } b => BitConverter.ToUInt32(b),
                    _ => null,
                },
            };
            gpus.Add(new GpuInfo(name.Trim(), VendorOf(device), vram));
        }
        return gpus;
    }

    /// <summary>Linux: PCI vendor from sysfs; AMD reports its VRAM there, NVIDIA its model in /proc.</summary>
    private static List<GpuInfo> FromSysfs()
    {
        var gpus = new List<GpuInfo>();
        const string drm = "/sys/class/drm";
        if (!Directory.Exists(drm))
            return gpus;

        foreach (string card in Directory.EnumerateDirectories(drm, "card*").Where(c => Path.GetFileName(c)[4..].All(char.IsDigit)))
        {
            string device = Path.Combine(card, "device");
            string vendorFile = Path.Combine(device, "vendor");
            if (!File.Exists(vendorFile))
                continue;
            var vendor = VendorOf(File.ReadAllText(vendorFile).Trim());
            long? vram = File.Exists(Path.Combine(device, "mem_info_vram_total")) && long.TryParse(File.ReadAllText(Path.Combine(device, "mem_info_vram_total")).Trim(), out long v) ? v : null;
            string name = vendor switch
            {
                GpuVendor.Nvidia => NvidiaModel() ?? "NVIDIA graphics",
                GpuVendor.Amd => "AMD Radeon graphics",
                GpuVendor.Intel => "Intel graphics",
                _ => "Graphics card",
            };
            gpus.Add(new GpuInfo(name, vendor, vram));
        }
        return gpus;
    }

    private static string? NvidiaModel()
    {
        const string root = "/proc/driver/nvidia/gpus";
        if (!Directory.Exists(root))
            return null;
        foreach (string gpu in Directory.EnumerateDirectories(root))
        {
            string info = Path.Combine(gpu, "information");
            if (File.Exists(info) && File.ReadLines(info).FirstOrDefault(l => l.StartsWith("Model:", StringComparison.Ordinal)) is { } line)
                return line["Model:".Length..].Trim();
        }
        return null;
    }
}
