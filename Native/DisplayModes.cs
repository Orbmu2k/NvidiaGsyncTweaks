using System.Runtime.InteropServices;

namespace NvidiaGsyncTweaks.Native;

/// <summary>Detects the refresh rates of the primary display (used to pre-fill the LFC preview).</summary>
internal static unsafe partial class DisplayModes
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DEVMODEW
    {
        public fixed char dmDeviceName[32];
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        public fixed char dmFormName[32];
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    private const uint ENUM_CURRENT_SETTINGS = 0xFFFFFFFF;

    [LibraryImport("user32.dll", EntryPoint = "EnumDisplaySettingsW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDisplaySettings(char* deviceName, uint modeNum, DEVMODEW* devMode);

    /// <summary>Current and highest refresh rate (at the current resolution) of the primary display.</summary>
    public static (int CurrentHz, int MaxHz)? Primary()
    {
        DEVMODEW cur = default;
        cur.dmSize = (ushort)sizeof(DEVMODEW);
        if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, &cur)) return null;
        uint w = cur.dmPelsWidth, h = cur.dmPelsHeight;
        int max = (int)cur.dmDisplayFrequency;

        for (uint i = 0; i < 4096; i++)
        {
            DEVMODEW m = default;
            m.dmSize = (ushort)sizeof(DEVMODEW);
            if (!EnumDisplaySettings(null, i, &m)) break;
            if (m.dmPelsWidth == w && m.dmPelsHeight == h && m.dmDisplayFrequency > max)
                max = (int)m.dmDisplayFrequency;
        }
        return ((int)cur.dmDisplayFrequency, max);
    }
}
