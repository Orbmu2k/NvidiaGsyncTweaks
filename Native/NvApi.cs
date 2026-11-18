using System.Runtime.InteropServices;
using System.Text;

namespace NvidiaGsyncTweaks.Native;

/// <summary>A physical NVIDIA GPU as reported by NVAPI.</summary>
internal sealed record NvGpu(
    int Index,
    string Name,
    uint BusId,        // PCI bus number
    uint SlotId,       // PCI device (slot) number
    uint DeviceId,     // PCI device id (low 16) + vendor id (high 16)  - NVAPI "deviceId"
    uint SubSystemId,  // PCI subsystem id (subdevice << 16 | subvendor)
    uint RevisionId)
{
    /// <summary>NvPhysicalGpuHandle; only valid until NVAPI is unloaded.</summary>
    public nint Handle { get; init; }
}

/// <summary>
/// Minimal NVAPI binding (AOT friendly: raw function pointers obtained through nvapi_QueryInterface, no marshalling stubs).
/// All interface ids are the stable, long-known NVAPI ids; every function is validated at runtime and a missing one just disables the feature.
/// </summary>
internal static unsafe class NvApi
{
    // nvapi_QueryInterface ids
    private const uint ID_Initialize = 0x0150E828;
    private const uint ID_Unload = 0xD22BDD7E;
    private const uint ID_GetErrorMessage = 0x6C2D048C;
    private const uint ID_EnumPhysicalGPUs = 0xE5AC921F;
    private const uint ID_GPU_GetFullName = 0xCEEE8E9F;
    private const uint ID_GPU_GetBusId = 0x1BE0B8E5;
    private const uint ID_GPU_GetBusSlotId = 0x2A0A350F;
    private const uint ID_GPU_GetPCIIdentifiers = 0x2DDFB66E;
    private const uint ID_SYS_GetDriverAndBranchVersion = 0x2926AAAD;

    // ids verified against the interface table of nvapi64_impl.dll 621.12 (0E24CEEE -> NvAPI_GetDisplayDriverRegistryPath, ...)
    private const uint ID_EnumNvidiaDisplayHandle = 0x9ABDD40D;
    private const uint ID_GetPhysicalGPUsFromDisplay = 0x34EF9506;
    private const uint ID_GetDisplayDriverRegistryPath = 0x0E24CEEE;

    private const int NVAPI_MAX_PHYSICAL_GPUS = 64;
    private const int NVAPI_SHORT_STRING_MAX = 64;
    private const int NVAPI_LONG_STRING_MAX = 256;

    private static nint s_query;
    private static bool s_initialized;
    private static delegate* unmanaged[Cdecl]<int, byte*, int> s_getErrorMessage;

    public static string? LastError { get; private set; }

    /// <summary>Loads nvapi64.dll and calls NvAPI_Initialize. Returns false when no NVIDIA driver / NVAPI is available.</summary>
    public static bool Initialize()
    {
        if (s_initialized) return true;
        try
        {
            if (!NativeLibrary.TryLoad("nvapi64.dll", out var lib) ||
                !NativeLibrary.TryGetExport(lib, "nvapi_QueryInterface", out s_query))
            {
                LastError = "nvapi64.dll not found (NVIDIA display driver not installed?)";
                return false;
            }

            var init = (delegate* unmanaged[Cdecl]<int>)Query(ID_Initialize);
            if (init == null) { LastError = "NvAPI_Initialize not exported"; return false; }
            var st = init();
            if (st != 0) { LastError = $"NvAPI_Initialize failed: {Describe(st)}"; return false; }

            s_getErrorMessage = (delegate* unmanaged[Cdecl]<int, byte*, int>)Query(ID_GetErrorMessage);
            s_initialized = true;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    public static void Shutdown()
    {
        if (!s_initialized) return;
        try
        {
            var unload = (delegate* unmanaged[Cdecl]<int>)Query(ID_Unload);
            if (unload != null) unload();
        }
        catch { /* ignore */ }
        s_initialized = false;
    }

    private static void* Query(uint id) => s_query == 0 ? null : ((delegate* unmanaged[Cdecl]<uint, void*>)s_query)(id);

    private static string Describe(int status)
    {
        if (s_getErrorMessage != null)
        {
            byte* buf = stackalloc byte[NVAPI_SHORT_STRING_MAX];
            if (s_getErrorMessage(status, buf) == 0) return $"{ReadAnsi(buf)} ({status})";
        }
        return $"NVAPI status {status}";
    }

    private static string ReadAnsi(byte* p)
    {
        int n = 0;
        while (n < NVAPI_SHORT_STRING_MAX && p[n] != 0) n++;
        return Encoding.ASCII.GetString(p, n);
    }

    /// <summary>Enumerates all physical NVIDIA GPUs with their PCI location (used to find the correct driver registry key).</summary>
    public static List<NvGpu> EnumerateGpus()
    {
        var result = new List<NvGpu>();
        if (!Initialize()) return result;

        var enumGpus = (delegate* unmanaged[Cdecl]<nint*, uint*, int>)Query(ID_EnumPhysicalGPUs);
        var getName = (delegate* unmanaged[Cdecl]<nint, byte*, int>)Query(ID_GPU_GetFullName);
        var getBus = (delegate* unmanaged[Cdecl]<nint, uint*, int>)Query(ID_GPU_GetBusId);
        var getSlot = (delegate* unmanaged[Cdecl]<nint, uint*, int>)Query(ID_GPU_GetBusSlotId);
        var getPci = (delegate* unmanaged[Cdecl]<nint, uint*, uint*, uint*, uint*, int>)Query(ID_GPU_GetPCIIdentifiers);
        if (enumGpus == null) return result;

        nint* handles = stackalloc nint[NVAPI_MAX_PHYSICAL_GPUS];
        uint count = 0;
        if (enumGpus(handles, &count) != 0) return result;

        byte* nameBuf = stackalloc byte[NVAPI_SHORT_STRING_MAX];
        for (int i = 0; i < count; i++)
        {
            string name = "NVIDIA GPU";
            uint bus = uint.MaxValue, slot = uint.MaxValue, dev = 0, sub = 0, rev = 0, ext = 0;
            if (getName != null && getName(handles[i], nameBuf) == 0) name = ReadAnsi(nameBuf);
            if (getBus != null) getBus(handles[i], &bus);
            if (getSlot != null) getSlot(handles[i], &slot);
            if (getPci != null) getPci(handles[i], &dev, &sub, &rev, &ext);
            result.Add(new NvGpu(i, name, bus, slot, dev, sub, rev) { Handle = handles[i] });
        }
        return result;
    }

    /// <summary>
    /// NvAPI_GetDisplayDriverRegistryPath: for every NVIDIA display handle the registry path of the driver key that serves it
    /// (as the driver itself reports it), together with the physical GPU the display is attached to.
    /// </summary>
    public static List<(nint Gpu, string Path)> GetDisplayDriverRegistryPaths()
    {
        var result = new List<(nint, string)>();
        if (!Initialize()) return result;

        var enumDisp = (delegate* unmanaged[Cdecl]<uint, nint*, int>)Query(ID_EnumNvidiaDisplayHandle);
        var getPath = (delegate* unmanaged[Cdecl]<nint, byte*, int>)Query(ID_GetDisplayDriverRegistryPath);
        var gpusFromDisp = (delegate* unmanaged[Cdecl]<nint, nint*, uint*, int>)Query(ID_GetPhysicalGPUsFromDisplay);
        if (enumDisp == null || getPath == null || gpusFromDisp == null) return result;

        byte* buf = stackalloc byte[NVAPI_LONG_STRING_MAX];
        nint* gpuHandles = stackalloc nint[NVAPI_MAX_PHYSICAL_GPUS];
        for (uint i = 0; i < 64; i++)
        {
            nint disp = 0;
            if (enumDisp(i, &disp) != 0) break;

            for (int k = 0; k < NVAPI_LONG_STRING_MAX; k++) buf[k] = 0;
            if (getPath(disp, buf) != 0) continue;
            int n = 0;
            while (n < NVAPI_LONG_STRING_MAX && buf[n] != 0) n++;
            var path = Encoding.ASCII.GetString(buf, n);

            uint gc = 0;
            nint gpu = 0;
            if (gpusFromDisp(disp, gpuHandles, &gc) == 0 && gc > 0) gpu = gpuHandles[0];
            result.Add((gpu, path));
        }
        return result;
    }

    /// <summary>Driver version as NVIDIA marketing number, e.g. 62112 for 621.12, plus branch string (e.g. "r620_xx").</summary>
    public static (uint Version, string Branch)? GetDriverVersion()
    {
        if (!Initialize()) return null;
        var fn = (delegate* unmanaged[Cdecl]<uint*, byte*, int>)Query(ID_SYS_GetDriverAndBranchVersion);
        if (fn == null) return null;
        uint v = 0;
        byte* branch = stackalloc byte[NVAPI_SHORT_STRING_MAX];
        if (fn(&v, branch) != 0) return null;
        return (v, ReadAnsi(branch));
    }
}
