namespace NvidiaGsyncTweaks.Model;

/// <summary>Toggle = on/off value, Choice = list, Number = numeric box, Bit = one flag inside a bitmask register.</summary>
internal enum TweakKind { Toggle, Choice, Number, Bit }

/// <summary>How much a setting can hurt: shown as a colour-coded badge.</summary>
internal enum TweakRisk { Safe, Caution, Experimental }

internal sealed record Choice(uint Value, string Label);

/// <summary>
/// One hidden REG_DWORD value of the NVIDIA display driver. A missing registry value always means "driver default".
/// <see cref="Default"/> is the value the driver uses when the value is absent (taken from the driver's own table, nvlddmkm 621.12).
/// </summary>
internal sealed record TweakDef(
    string Name,
    string Title,
    string Description,
    TweakKind Kind,
    uint Default,
    TweakRisk Risk = TweakRisk.Safe,
    IReadOnlyList<Choice>? Choices = null,
    double Min = 0,
    double Max = 0,
    double Step = 1,
    string Unit = "",
    uint OnValue = 1,
    uint OffValue = 0,
    string? Note = null,
    uint BitMask = 0,       // Kind.Bit: the flag(s) of the shared register this row switches
    double Scale = 1);      // Kind.Number: displayed = raw / Scale (e.g. 1000 for mHz -> Hz)

internal sealed record TweakGroup(string Title, string? Intro, IReadOnlyList<TweakDef> Tweaks);

internal sealed record TweakPage(string Tag, string Title, string Subtitle, string Icon, IReadOnlyList<TweakGroup> Groups);
