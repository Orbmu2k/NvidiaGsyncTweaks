using System.Globalization;
using System.Text;
using NvidiaGsyncTweaks.Model;

namespace NvidiaGsyncTweaks.Services;

/// <summary>Result codes of the elevated worker process (it reports through its exit code only - no files, no pipes).</summary>
internal enum ElevatedExit
{
    Ok = 0,
    InvalidArguments = 2,
    NotNvidiaKey = 3,
    RegistryFailed = 4,
    RestartFailed = 5,
    OkRebootRequired = 20,
}

/// <summary>
/// What the elevated worker is asked to do. Only the two things that need administrator rights are possible:
/// write DWORDs below one NVIDIA display-adapter driver key, and restart that adapter.
/// The worker re-validates everything (key shape, allow-listed value names, numeric values), so even a forged command line
/// cannot write anything else.
/// </summary>
internal sealed record ElevatedRequest(string KeyPath, IReadOnlyDictionary<string, uint?> Changes, bool RestartDriver)
{
    public const string Switch = "--elevated-apply";
    public const int MaxChanges = 256;

    private const string KeyPrefix = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\";

    /// <summary>SYSTEM\CurrentControlSet\Control\Class\{4d36e968-...}\0000 - nothing else is accepted.</summary>
    public static bool IsValidKeyPath(string? key) =>
        key is not null
        && key.Length == KeyPrefix.Length + 4
        && key.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase)
        && key.AsSpan(KeyPrefix.Length).ToString().All(char.IsAsciiDigit);

    public string ToArguments()
    {
        var sb = new StringBuilder(Switch);
        sb.Append(" --key \"").Append(KeyPath).Append('"');
        foreach (var (name, value) in Changes.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (value is { } v) sb.Append(" --set ").Append(name).Append('=').Append(v.ToString(CultureInfo.InvariantCulture));
            else sb.Append(" --delete ").Append(name);
        }
        if (RestartDriver) sb.Append(" --restart");
        return sb.ToString();
    }

    /// <summary>Parses and validates the command line of the worker (args[0] is <see cref="Switch"/>).</summary>
    public static bool TryParse(IReadOnlyList<string> args, out ElevatedRequest? request, out string error)
    {
        request = null;
        error = "";
        if (args.Count == 0 || !string.Equals(args[0], Switch, StringComparison.Ordinal)) { error = "not a worker command line"; return false; }

        var allowed = new HashSet<string>(Catalog.AllNames, StringComparer.OrdinalIgnoreCase);
        string? key = null;
        bool restart = false;
        var changes = new Dictionary<string, uint?>(StringComparer.OrdinalIgnoreCase);

        for (int i = 1; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--key" when i + 1 < args.Count:
                    key = args[++i];
                    break;
                case "--restart":
                    restart = true;
                    break;
                case "--set" when i + 1 < args.Count:
                {
                    var kv = args[++i];
                    int eq = kv.IndexOf('=');
                    if (eq <= 0 || !uint.TryParse(kv.AsSpan(eq + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                    { error = $"bad --set '{kv}'"; return false; }
                    var name = kv[..eq];
                    if (!allowed.Contains(name)) { error = $"value '{name}' is not managed by this app"; return false; }
                    changes[name] = value;
                    break;
                }
                case "--delete" when i + 1 < args.Count:
                {
                    var name = args[++i];
                    if (!allowed.Contains(name)) { error = $"value '{name}' is not managed by this app"; return false; }
                    changes[name] = null;
                    break;
                }
                default:
                    error = $"unknown argument '{args[i]}'";
                    return false;
            }
        }

        if (!IsValidKeyPath(key)) { error = "invalid driver key path"; return false; }
        if (changes.Count > MaxChanges) { error = "too many changes"; return false; }
        if (changes.Count == 0 && !restart) { error = "nothing to do"; return false; }

        request = new ElevatedRequest(key!, changes, restart);
        return true;
    }
}
