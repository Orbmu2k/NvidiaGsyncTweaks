using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace NvidiaGsyncTweaks.Native;

/// <summary>
/// The key NVAPI reports (Control\Video\{adapter GUID}\0000) is a registry symbolic link (REG_LINK) to the real driver key
/// (Control\Class\{4d36e968-...}\000N). This resolves the link so we can show - and verify - the real target.
/// </summary>
internal static unsafe partial class RegistryLink
{
    private const nint HKEY_LOCAL_MACHINE = unchecked((nint)(int)0x80000002);
    private const int REG_OPTION_OPEN_LINK = 0x8;
    private const int KEY_READ = 0x20019;

    [LibraryImport("advapi32.dll", EntryPoint = "RegOpenKeyExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegOpenKeyEx(nint hKey, string subKey, int options, int samDesired, out nint result);

    [LibraryImport("advapi32.dll", EntryPoint = "RegQueryValueExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegQueryValueEx(nint hKey, string valueName, nint reserved, out int type, byte* data, ref int dataLen);

    [LibraryImport("advapi32.dll")]
    private static partial int RegCloseKey(nint hKey);

    /// <summary>Returns the HKLM-relative link target (ControlSet00x replaced by CurrentControlSet), or null when the key is not a link.</summary>
    public static string? ResolveHklmRelative(string hklmRelativePath)
    {
        if (RegOpenKeyEx(HKEY_LOCAL_MACHINE, hklmRelativePath, REG_OPTION_OPEN_LINK, KEY_READ, out var h) != 0) return null;
        try
        {
            byte* buf = stackalloc byte[1024];
            int len = 1024;
            if (RegQueryValueEx(h, "SymbolicLinkValue", 0, out var type, buf, ref len) != 0 || type != 6 /* REG_LINK */) return null;
            var target = Encoding.Unicode.GetString(buf, len).TrimEnd('\0');
            return Normalize(target);
        }
        finally { RegCloseKey(h); }
    }

    /// <summary>"\REGISTRY\MACHINE\SYSTEM\ControlSet001\..." or "System\CurrentControlSet\..." -> "SYSTEM\CurrentControlSet\..."</summary>
    public static string Normalize(string path)
    {
        const string prefix = @"\REGISTRY\MACHINE\";
        if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) path = path[prefix.Length..];
        path = path.TrimStart('\\');
        return ControlSet().Replace(path, "CurrentControlSet");
    }

    [GeneratedRegex(@"ControlSet\d{3}", RegexOptions.IgnoreCase)]
    private static partial Regex ControlSet();
}
