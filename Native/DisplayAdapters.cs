using System.Runtime.InteropServices;
using System.Text;

namespace NvidiaGsyncTweaks.Native;

/// <summary>A present display-class PnP device (graphics adapter) with the data needed to locate its driver registry key.</summary>
internal sealed record DisplayAdapter(
    string InstanceId,
    string Description,
    string HardwareId,
    string DriverKey,      // e.g. {4d36e968-e325-11ce-bfc1-08002be10318}\0000
    uint BusNumber,
    uint Address,          // (PCI device << 16) | PCI function
    uint VendorId,
    uint DeviceId,
    uint SubSystemId)
{
    public uint PciDevice => Address >> 16;
    public uint PciFunction => Address & 0xFFFF;
}

internal sealed record DriverRestartResult(bool Success, string Message, bool RebootRequired);

internal static unsafe partial class DisplayAdapters
{
    public const string DisplayClassGuid = "{4d36e968-e325-11ce-bfc1-08002be10318}";

    // ---- cfgmgr32 ----
    private const uint CM_GETIDLIST_FILTER_CLASS = 0x00000200;
    private const uint CM_GETIDLIST_FILTER_PRESENT = 0x00000100;
    private const uint CM_DRP_DEVICEDESC = 0x01;
    private const uint CM_DRP_HARDWAREID = 0x02;
    private const uint CM_DRP_DRIVER = 0x0A;
    private const uint CM_DRP_BUSNUMBER = 0x16;
    private const uint CM_DRP_ADDRESS = 0x1D;
    private const uint DN_STARTED = 0x00000008;
    private const uint DN_NEED_RESTART = 0x00000100;

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_ID_List_SizeW")]
    private static partial uint CM_Get_Device_ID_List_Size(uint* len, char* filter, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_ID_ListW")]
    private static partial uint CM_Get_Device_ID_List(char* filter, char* buffer, uint bufferLen, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Locate_DevNodeW")]
    private static partial uint CM_Locate_DevNode(uint* devInst, char* deviceId, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_Registry_PropertyW")]
    private static partial uint CM_Get_DevNode_Registry_Property(uint devInst, uint property, uint* regDataType, void* buffer, uint* length, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_Status")]
    private static partial uint CM_Get_DevNode_Status(uint* status, uint* problem, uint devInst, uint flags);

    // ---- setupapi ----
    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public nuint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_PROPCHANGE_PARAMS
    {
        public uint HeaderSize;        // sizeof(SP_CLASSINSTALL_HEADER) = 8
        public uint InstallFunction;   // DIF_PROPERTYCHANGE
        public uint StateChange;       // DICS_*
        public uint Scope;             // DICS_FLAG_GLOBAL
        public uint HwProfile;
    }

    private const uint DIF_PROPERTYCHANGE = 0x12;
    private const uint DICS_ENABLE = 1, DICS_DISABLE = 2, DICS_PROPCHANGE = 3;
    private const uint DICS_FLAG_GLOBAL = 1;

    [LibraryImport("setupapi.dll", SetLastError = true)]
    private static partial nint SetupDiCreateDeviceInfoList(Guid* classGuid, nint hwndParent);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiOpenDeviceInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiOpenDeviceInfo(nint set, char* instanceId, nint hwnd, uint flags, SP_DEVINFO_DATA* data);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiSetClassInstallParamsW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiSetClassInstallParams(nint set, SP_DEVINFO_DATA* data, void* parms, uint size);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiCallClassInstaller(uint installFunction, nint set, SP_DEVINFO_DATA* data);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiDestroyDeviceInfoList(nint set);

    // ------------------------------------------------------------------------------------------------
    /// <summary>All present display adapters (class {4d36e968-...}).</summary>
    public static List<DisplayAdapter> Enumerate()
    {
        var list = new List<DisplayAdapter>();
        fixed (char* filter = DisplayClassGuid)
        {
            uint len = 0;
            if (CM_Get_Device_ID_List_Size(&len, filter, CM_GETIDLIST_FILTER_CLASS | CM_GETIDLIST_FILTER_PRESENT) != 0 || len <= 1)
                return list;
            var buf = new char[len];
            fixed (char* b = buf)
            {
                if (CM_Get_Device_ID_List(filter, b, len, CM_GETIDLIST_FILTER_CLASS | CM_GETIDLIST_FILTER_PRESENT) != 0)
                    return list;
            }
            // multi-sz
            int start = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i] != '\0') continue;
                if (i > start)
                {
                    var id = new string(buf, start, i - start);
                    var a = Read(id);
                    if (a != null) list.Add(a);
                }
                start = i + 1;
            }
        }
        return list;
    }

    private static DisplayAdapter? Read(string instanceId)
    {
        uint devInst = 0;
        fixed (char* id = instanceId)
        {
            if (CM_Locate_DevNode(&devInst, id, 0) != 0) return null;
        }

        string desc = GetString(devInst, CM_DRP_DEVICEDESC) ?? "";
        string hwid = GetMultiSzFirst(devInst, CM_DRP_HARDWAREID) ?? "";
        string driver = GetString(devInst, CM_DRP_DRIVER) ?? "";
        uint bus = GetUInt(devInst, CM_DRP_BUSNUMBER) ?? uint.MaxValue;
        uint addr = GetUInt(devInst, CM_DRP_ADDRESS) ?? uint.MaxValue;

        ParsePci(hwid, out uint ven, out uint dev, out uint sub);
        return new DisplayAdapter(instanceId, desc, hwid, driver, bus, addr, ven, dev, sub);
    }

    // PCI\VEN_10DE&DEV_2684&SUBSYS_889A1043&REV_A1
    private static void ParsePci(string hwid, out uint ven, out uint dev, out uint sub)
    {
        ven = dev = sub = 0;
        uint Hex(string key)
        {
            int p = hwid.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (p < 0) return 0;
            p += key.Length;
            int e = p;
            while (e < hwid.Length && Uri.IsHexDigit(hwid[e])) e++;
            return uint.TryParse(hwid.AsSpan(p, e - p), System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0;
        }
        ven = Hex("VEN_");
        dev = Hex("DEV_");
        sub = Hex("SUBSYS_");
    }

    private static string? GetString(uint devInst, uint prop)
    {
        char* buf = stackalloc char[512];
        uint size = 512 * sizeof(char), type = 0;
        if (CM_Get_DevNode_Registry_Property(devInst, prop, &type, buf, &size, 0) != 0 || size < 2) return null;
        return new string(buf);
    }

    private static string? GetMultiSzFirst(uint devInst, uint prop) => GetString(devInst, prop); // first string only (NUL terminated)

    private static uint? GetUInt(uint devInst, uint prop)
    {
        uint value = 0, size = sizeof(uint), type = 0;
        return CM_Get_DevNode_Registry_Property(devInst, prop, &type, &value, &size, 0) == 0 ? value : null;
    }

    // ------------------------------------------------------------------------------------------------
    /// <summary>Is the device started and without a pending-restart/problem flag?</summary>
    public static (bool Started, bool NeedsRestart, uint Problem)? GetStatus(string instanceId)
    {
        uint devInst = 0;
        fixed (char* id = instanceId)
        {
            if (CM_Locate_DevNode(&devInst, id, 0) != 0) return null;
        }
        uint status = 0, problem = 0;
        if (CM_Get_DevNode_Status(&status, &problem, devInst, 0) != 0) return null;
        return ((status & DN_STARTED) != 0, (status & DN_NEED_RESTART) != 0, problem);
    }

    /// <summary>
    /// Restarts the display adapter (stop + start of the PnP device) so the NVIDIA kernel driver (nvlddmkm) re-reads its registry values.
    /// This equals "Restart device" (pnputil /restart-device). Needs administrator rights. The screens go black for a moment.
    /// </summary>
    public static DriverRestartResult Restart(string instanceId)
    {
        nint set = SetupDiCreateDeviceInfoList(null, 0);
        if (set == -1 || set == 0)
            return new(false, $"SetupDiCreateDeviceInfoList failed (error {Marshal.GetLastPInvokeError()}).", false);
        try
        {
            var data = new SP_DEVINFO_DATA { cbSize = (uint)sizeof(SP_DEVINFO_DATA) };
            fixed (char* id = instanceId)
            {
                if (!SetupDiOpenDeviceInfo(set, id, 0, 0, &data))
                    return new(false, $"Device not found ({Marshal.GetLastPInvokeError()}).", false);
            }

            // 1st try: single "restart" request (DICS_PROPCHANGE)
            var r = Change(set, &data, DICS_PROPCHANGE);
            if (!r.ok)
            {
                // 2nd try: explicit disable + enable
                var d = Change(set, &data, DICS_DISABLE);
                if (!d.ok) return new(false, $"Restart failed (error {r.err} / {d.err}). Run as administrator.", false);
                Thread.Sleep(800);
                var e = Change(set, &data, DICS_ENABLE);
                if (!e.ok) return new(false, $"Adapter was disabled but could not be re-enabled (error {e.err}). Enable it in Device Manager!", false);
            }

            // wait for the device to come back
            for (int i = 0; i < 40; i++)
            {
                var st = GetStatus(instanceId);
                if (st is { Started: true, Problem: 0 })
                    return new(true, "Display driver restarted.", st.Value.NeedsRestart);
                Thread.Sleep(250);
            }
            return new(false, "The adapter did not report 'started' within 10 s. Check Device Manager.", true);
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    /// <summary>Everything of <see cref="Restart"/> except the final installer call: validates struct layouts / device lookup without touching the device.</summary>
    public static string DryRun(string instanceId)
    {
        nint set = SetupDiCreateDeviceInfoList(null, 0);
        if (set == -1 || set == 0) return $"SetupDiCreateDeviceInfoList failed ({Marshal.GetLastPInvokeError()})";
        try
        {
            var data = new SP_DEVINFO_DATA { cbSize = (uint)sizeof(SP_DEVINFO_DATA) };
            fixed (char* id = instanceId)
            {
                if (!SetupDiOpenDeviceInfo(set, id, 0, 0, &data)) return $"SetupDiOpenDeviceInfo failed ({Marshal.GetLastPInvokeError()})";
            }
            var p = new SP_PROPCHANGE_PARAMS { HeaderSize = 8, InstallFunction = DIF_PROPERTYCHANGE, StateChange = DICS_PROPCHANGE, Scope = DICS_FLAG_GLOBAL };
            bool ok = SetupDiSetClassInstallParams(set, &data, &p, (uint)sizeof(SP_PROPCHANGE_PARAMS));
            return $"sizeof(SP_DEVINFO_DATA)={sizeof(SP_DEVINFO_DATA)} (expect 32), sizeof(SP_PROPCHANGE_PARAMS)={sizeof(SP_PROPCHANGE_PARAMS)} (expect 20), " +
                   $"devinst=0x{data.DevInst:X}, SetClassInstallParams={(ok ? "ok" : "failed " + Marshal.GetLastPInvokeError())}, status={GetStatus(instanceId)}";
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    private static (bool ok, int err) Change(nint set, SP_DEVINFO_DATA* data, uint state)
    {
        var p = new SP_PROPCHANGE_PARAMS
        {
            HeaderSize = 8,
            InstallFunction = DIF_PROPERTYCHANGE,
            StateChange = state,
            Scope = DICS_FLAG_GLOBAL,
            HwProfile = 0,
        };
        if (!SetupDiSetClassInstallParams(set, data, &p, (uint)sizeof(SP_PROPCHANGE_PARAMS)))
            return (false, Marshal.GetLastPInvokeError());
        if (!SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, set, data))
            return (false, Marshal.GetLastPInvokeError());
        return (true, 0);
    }
}
