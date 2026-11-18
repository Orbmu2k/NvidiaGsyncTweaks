namespace NvidiaGsyncTweaks.Model;

/// <summary>
/// Decoded value of the driver setting <c>vrrFrameSplitBehavior</c> (nvlddmkm 621.12, functions D928E0 / D93097).
///
///   bits  0..7   "stickiness" in percent: the multiplier that is already in use has its distance to the ideal
///                frame time multiplied by this value / 100.  85 (0x55) = 15 % bonus (NVIDIA default).  100 = no bonus.
///   bit  16      lowest-fit mode: use the smallest multiplier whose frame time still fits the VRR window (1x as long as possible).
///   bit  17      disable the stickiness bonus entirely.
///   bit  31      set by NVIDIA in the default (0x80000055); not evaluated by the frame splitting code.
/// </summary>
internal readonly record struct FrameSplit(uint Raw)
{
    /// <summary>Name of the registry value in the adapter driver key.</summary>
    public const string ValueName = "vrrFrameSplitBehavior";
    public const uint DefaultRaw = 0x80000055;
    public const uint LowestFitBit = 0x10000;
    public const uint NoStickyBit = 0x20000;

    public int Stickiness => (int)(Raw & 0xFF);
    public bool LowestFit => (Raw & LowestFitBit) != 0;
    public bool NoSticky => (Raw & NoStickyBit) != 0;

    public static FrameSplit Make(bool lowestFit, int stickiness, bool noSticky) =>
        new(0x80000000u | (lowestFit ? LowestFitBit : 0) | (noSticky ? NoStickyBit : 0) | (uint)Math.Clamp(stickiness, 0, 255));

    public static FrameSplit Default => new(DefaultRaw);
    /// <summary>The fix from the community thread: 0x80010055.</summary>
    public static FrameSplit LowestMultiplier => new(0x80010055);
}

/// <summary>
/// Re-implementation of the multiplier selection of the NVIDIA driver, used for the live "what will my monitor do" preview.
/// Frame time T (ms) is split into n equal parts (n = 1,2,3 ...). Only n where T/n lies within [minFrame, maxFrame]
/// (1/maxRefresh .. 1/vrrMin) are possible. The default policy picks the n whose frame time is closest to the middle of that window,
/// giving the multiplier currently in use a bonus.
/// </summary>
internal static class LfcModel
{
    public static int Choose(double frameMs, int current, double minFrameMs, double maxFrameMs, FrameSplit mode)
    {
        double mid = minFrameMs + (maxFrameMs - minFrameMs) / 2.0;
        double best = double.MaxValue;
        int selected = 1;
        for (int n = 1; n < 64; n++)
        {
            double split = frameMs / n;
            if (split < minFrameMs) break;
            if (split > maxFrameMs) continue;

            double dist = Math.Abs(split - mid);
            if (n == current && !mode.NoSticky) dist = dist * mode.Stickiness / 100.0;
            if (dist < best) { best = dist; selected = n; }
            if (mode.LowestFit) { selected = n; break; }
        }
        return selected;
    }

    /// <summary>The refresh rate the panel runs at for a steady frame rate, given the multiplier currently active.</summary>
    public static (int Multiplier, double RefreshHz) Evaluate(double fps, int current, double maxHz, double vrrMinHz, FrameSplit mode)
    {
        double minFrame = 1000.0 / maxHz, maxFrame = 1000.0 / vrrMinHz;
        int n = Choose(1000.0 / fps, current, minFrame, maxFrame, mode);
        return (n, fps * n);
    }

    /// <summary>FPS below which frame doubling switches ON (coming from 1x) and above which it switches OFF again (coming from 2x).</summary>
    public static (double On, double Off) DoublingThresholds(double maxHz, double vrrMinHz, FrameSplit mode)
    {
        double on = double.NaN, off = double.NaN;
        for (double fps = maxHz; fps >= 10; fps -= 0.05)
            if (Evaluate(fps, 1, maxHz, vrrMinHz, mode).Multiplier != 1) { on = fps + 0.05; break; }
        for (double fps = 10; fps <= maxHz; fps += 0.05)
            if (Evaluate(fps, 2, maxHz, vrrMinHz, mode).Multiplier == 1) { off = fps; break; }
        return (on, off);
    }
}
