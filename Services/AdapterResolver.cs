using Microsoft.Win32;
using NvidiaGsyncTweaks.Native;

namespace NvidiaGsyncTweaks.Services;

/// <summary>An NVIDIA GPU together with the registry key the display driver (nvlddmkm) reads its hidden values from.</summary>
internal sealed record ResolvedGpu(
    NvGpu Gpu,
    DisplayAdapter? Adapter,
    string? KeyPath,              // HKLM-relative: SYSTEM\CurrentControlSet\Control\Class\{4d36e968-...}\0000
    string? NvApiKeyPath,         // as reported by NvAPI_GetDisplayDriverRegistryPath (Control\Video\{GUID}\0000, a registry link)
    string? RegistryDriverVersion,
    uint? NvApiDriverVersion,     // 62112 == 621.12
    string MatchMethod,
    bool KeyExists,
    bool VersionMatches,
    bool NvApiAgreesWithPnp)
{
    public string DisplayKeyPath => KeyPath is null ? "-" : @"HKEY_LOCAL_MACHINE\" + KeyPath;
    public string NvApiDriverText => NvApiDriverVersion is { } v ? $"{v / 100}.{v % 100:00}" : "?";
    public string Title => $"{Gpu.Name}  ·  PCI {Gpu.BusId:X2}:{Gpu.SlotId:X2}";
}

/// <summary>
/// Finds the correct driver registry key per GPU.
/// 1) NVAPI itself: NvAPI_GetDisplayDriverRegistryPath returns the key the driver was given (Control\Video\{GUID}\0000, a registry
///    link) - the link is resolved to the real Class key.
/// 2) Cross-check / fallback for GPUs without an attached display: NVAPI reports the PCI location (bus / slot) and PCI ids of each
///    physical GPU; these are matched against the PnP display adapters, whose driver key ("{4d36e968-...}\000N") is the same key.
/// Hard-coding "\0000" would be wrong on systems with several display adapters, an iGPU, or after driver re-installs.
/// </summary>
internal static class AdapterResolver
{
    public static List<ResolvedGpu> Resolve()
    {
        var result = new List<ResolvedGpu>();
        var gpus = NvApi.EnumerateGpus();
        var adapters = DisplayAdapters.Enumerate().Where(a => a.VendorId == 0x10DE).ToList();
        var nvapiPaths = NvApi.GetDisplayDriverRegistryPaths();
        uint? nvapiVer = NvApi.GetDriverVersion()?.Version;

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var gpu in gpus)
        {
            // ---- 1) NVAPI: registry path the driver reports for this GPU -------------------------------------
            string? nvapiKey = nvapiPaths.FirstOrDefault(p => p.Gpu == gpu.Handle).Path;
            string? nvapiTarget = null;
            if (!string.IsNullOrEmpty(nvapiKey))
            {
                nvapiKey = RegistryLink.Normalize(nvapiKey);
                nvapiTarget = RegistryLink.ResolveHklmRelative(nvapiKey) ?? nvapiKey; // not a link -> use as is
            }

            // ---- 2) PnP adapter via NVAPI PCI identity ------------------------------------------------------
            DisplayAdapter? match = adapters.FirstOrDefault(a => !used.Contains(a.InstanceId) && a.BusNumber == gpu.BusId && a.PciDevice == gpu.SlotId);
            string how = match != null ? "PCI bus/slot from NVAPI" : "";
            if (match == null)
            {
                uint dev = gpu.DeviceId >> 16;
                var cand = adapters.Where(a => !used.Contains(a.InstanceId) && a.DeviceId == dev &&
                                               (gpu.SubSystemId == 0 || a.SubSystemId == gpu.SubSystemId)).ToList();
                if (cand.Count == 1) { match = cand[0]; how = "PCI device/subsystem id from NVAPI"; }
            }
            if (match == null && adapters.Count == 1 && gpus.Count == 1) { match = adapters[0]; how = "single NVIDIA adapter"; }
            if (match != null) used.Add(match.InstanceId);

            string? pnpKey = match is { DriverKey.Length: > 0 } ? @"SYSTEM\CurrentControlSet\Control\Class\" + match.DriverKey : null;

            bool agree = nvapiTarget is not null && pnpKey is not null &&
                         string.Equals(nvapiTarget, pnpKey, StringComparison.OrdinalIgnoreCase);

            // NVAPI's answer wins; PnP is the fallback
            string? keyPath = nvapiTarget ?? pnpKey;
            string method = nvapiTarget is not null
                ? (agree ? "NvAPI_GetDisplayDriverRegistryPath (confirmed by PCI bus/slot)" : "NvAPI_GetDisplayDriverRegistryPath")
                : how.Length > 0 ? how : "not matched";

            string? regVer = null;
            bool exists = false, verOk = false;
            if (keyPath is not null)
            {
                try
                {
                    using var k = Registry.LocalMachine.OpenSubKey(keyPath, false);
                    exists = k != null;
                    regVer = k?.GetValue("DriverVersion") as string;
                }
                catch { /* keep defaults */ }
                verOk = nvapiVer is { } v && regVer is not null && ToNvidiaNumber(regVer) == v;
            }

            result.Add(new ResolvedGpu(gpu, match, keyPath, nvapiKey, regVer, nvapiVer, method, exists, verOk, agree));
        }
        return result;
    }

    /// <summary>"32.0.16.2112" -> 62112 (NVIDIA 621.12).</summary>
    public static uint? ToNvidiaNumber(string windowsDriverVersion)
    {
        var p = windowsDriverVersion.Split('.');
        if (p.Length != 4 || !uint.TryParse(p[2], out var a) || !uint.TryParse(p[3], out var b)) return null;
        return (a % 10) * 10000 + b;
    }
}
