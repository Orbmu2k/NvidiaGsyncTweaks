using System.Globalization;
using System.Text;
using Microsoft.Win32;

namespace NvidiaGsyncTweaks.Services;

/// <summary>Reads / writes the hidden REG_DWORD values in one adapter driver key. A missing value means "driver default".</summary>
internal sealed class RegistryStore
{
    public string KeyPath { get; }
    private readonly RegistryKey _root;

    /// <param name="hklmRelativeKeyPath">path below HKEY_LOCAL_MACHINE (or below <paramref name="root"/>, used by tests)</param>
    public RegistryStore(string hklmRelativeKeyPath, RegistryKey? root = null)
    {
        KeyPath = hklmRelativeKeyPath;
        _root = root ?? Registry.LocalMachine;
    }

    public static string BackupFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NvidiaGsyncTweaks", "Backups");

    /// <summary>Where <see cref="WriteRestoreFile"/> puts its .reg files (default: <see cref="BackupFolder"/>).</summary>
    public string BackupDirectory { get; init; } = BackupFolder;

    public Dictionary<string, uint?> Read(IEnumerable<string> names)
    {
        var map = new Dictionary<string, uint?>(StringComparer.OrdinalIgnoreCase);
        using var key = _root.OpenSubKey(KeyPath, false);
        foreach (var n in names)
            map[n] = key is null ? null : Convert(key.GetValue(n));
        return map;
    }

    private static uint? Convert(object? raw) => raw switch
    {
        int i => unchecked((uint)i),
        long l => unchecked((uint)l),
        byte[] { Length: 4 } b => BitConverter.ToUInt32(b, 0),
        _ => null,
    };

    /// <summary>Applies the changes: a null value deletes the registry value (= driver default).</summary>
    public void Apply(IReadOnlyDictionary<string, uint?> changes)
    {
        using var key = _root.OpenSubKey(KeyPath, true)
                        ?? throw new InvalidOperationException("Registry key not found or access denied: " + KeyPath);
        foreach (var (name, value) in changes)
        {
            if (value is null) key.DeleteValue(name, throwOnMissingValue: false);
            else key.SetValue(name, unchecked((int)value.Value), RegistryValueKind.DWord);
        }
    }

    /// <summary>
    /// Reads a restore point written by <see cref="WriteRestoreFile"/>: the HKLM-relative key and the values (null = delete).
    /// Only this app's own simple format is understood - never anything else is applied from a .reg file.
    /// </summary>
    public static (string Key, Dictionary<string, uint?> Values)? ParseRestoreFile(string file)
    {
        const string hklm = @"HKEY_LOCAL_MACHINE\";
        string? key = null;
        var values = new Dictionary<string, uint?>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadAllLines(file, Encoding.Unicode))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var path = line[1..^1];
                if (!path.StartsWith(hklm, StringComparison.OrdinalIgnoreCase)) return null;
                key = path[hklm.Length..];
            }
            else if (line.StartsWith('"'))
            {
                int close = line.IndexOf('"', 1);
                if (close < 0 || close + 1 >= line.Length || line[close + 1] != '=') continue;
                var name = line[1..close];
                var data = line[(close + 2)..];
                if (data == "-") values[name] = null;
                else if (data.StartsWith("dword:", StringComparison.OrdinalIgnoreCase) &&
                         uint.TryParse(data.AsSpan(6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
                    values[name] = v;
            }
        }
        return key is null ? null : (key, values);
    }

    /// <summary>Writes a .reg file which restores the CURRENT state of the given values (absent values are deleted again).</summary>
    public string WriteRestoreFile(IEnumerable<string> names, string label)
    {
        Directory.CreateDirectory(BackupDirectory);
        var current = Read(names);
        var sb = new StringBuilder();
        sb.AppendLine("Windows Registry Editor Version 5.00");
        sb.AppendLine();
        sb.AppendLine($"; Restore point created {DateTime.Now:yyyy-MM-dd HH:mm:ss} by NVIDIA G-SYNC Tweaks");
        sb.AppendLine($"[{_root.Name}\\{KeyPath}]");
        foreach (var (name, value) in current.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine(value is { } v ? $"\"{name}\"=dword:{v.ToString("x8", CultureInfo.InvariantCulture)}" : $"\"{name}\"=-");

        var safe = string.Concat(label.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        var file = Path.Combine(BackupDirectory, $"{DateTime.Now:yyyyMMdd-HHmmss}-{safe}.reg");
        File.WriteAllText(file, sb.ToString(), Encoding.Unicode);
        return file;
    }
}

