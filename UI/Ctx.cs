using NvidiaGsyncTweaks.Model;
using NvidiaGsyncTweaks.Services;

namespace NvidiaGsyncTweaks.UI;

/// <summary>Immutable snapshot of the app state handed to the page builders (React style: state down, callbacks up).</summary>
internal sealed class Ctx
{
    public ResolvedGpu? Gpu { get; init; }
    public IReadOnlyDictionary<string, uint?> Current { get; init; } = new Dictionary<string, uint?>();
    public IReadOnlyDictionary<string, uint?> Edits { get; init; } = new Dictionary<string, uint?>();
    public bool Busy { get; init; }

    public double MaxHz { get; init; } = 240;
    public double VrrMinHz { get; init; } = 48;
    public Action<double> SetMaxHz { get; init; } = _ => { };
    public Action<double> SetVrrMinHz { get; init; } = _ => { };

    /// <summary>Sets a pending value (null = remove the registry value = driver default).</summary>
    public Action<string, uint?> Edit { get; init; } = (_, _) => { };

    /// <summary>Value as stored in the registry right now (null = absent).</summary>
    public uint? Stored(string name) => Current.TryGetValue(name, out var v) ? v : null;

    /// <summary>Value after pending edits (null = absent / driver default).</summary>
    public uint? Pending(string name) => Edits.TryGetValue(name, out var e) ? e : Stored(name);

    public uint Effective(string name, uint driverDefault) => Pending(name) ?? driverDefault;
    public uint EffectiveCurrent(string name, uint driverDefault) => Stored(name) ?? driverDefault;

    public bool IsPending(string name) => Edits.ContainsKey(name);
    public bool IsCustom(string name, uint driverDefault) => Pending(name) is { } v && v != driverDefault;

    public int PendingCount => Edits.Count;

    public void Set(TweakDef d, uint value) => Edit(d.Name, value == d.Default ? null : value);
}
