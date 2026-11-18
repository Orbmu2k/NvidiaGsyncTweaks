using NvidiaGsyncTweaks.Model;
using Xunit;

namespace NvidiaGsyncTweaks.Tests;

/// <summary>
/// The LFC model is a re-implementation of the driver's multiplier selection (nvlddmkm 621.12, D928E0).
/// These numbers are the ones published in the community analysis of the issue; the model has to reproduce them.
/// </summary>
public class LfcModelTests
{
    [Theory]
    [InlineData(120, 50.0, 52.8)]
    [InlineData(144, 52.5, 55.5)]
    [InlineData(240, 58.4, 61.6)]
    [InlineData(360, 61.8, 65.2)]
    public void DefaultBehaviour_DoublingThresholds_MatchPublishedTable(double maxHz, double on, double off)
    {
        var (o, f) = LfcModel.DoublingThresholds(maxHz, 48, FrameSplit.Default);
        Assert.InRange(o, on - 0.15, on + 0.15);
        Assert.InRange(f, off - 0.15, off + 0.15);
    }

    [Theory]
    [InlineData(60, 2, 120, 1, 60)]
    [InlineData(50, 2, 100, 1, 50)]
    [InlineData(47, 2, 94, 2, 94)]
    [InlineData(30, 3, 90, 2, 60)]
    [InlineData(40, 2, 80, 2, 80)]
    public void LowestMultiplier_At360Hz_MatchesPublishedTable(double fps, int defMult, double defHz, int lowMult, double lowHz)
    {
        var d = LfcModel.Evaluate(fps, 1, 360, 48, FrameSplit.Default);
        var l = LfcModel.Evaluate(fps, 1, 360, 48, FrameSplit.LowestMultiplier);
        Assert.Equal(defMult, d.Multiplier);
        Assert.Equal(defHz, d.RefreshHz, 3);
        Assert.Equal(lowMult, l.Multiplier);
        Assert.Equal(lowHz, l.RefreshHz, 3);
    }

    [Fact]
    public void Stickier_0x80000032_At360Hz_MatchesPublishedThresholds()
    {
        var (on, off) = LfcModel.DoublingThresholds(360, 48, new FrameSplit(0x80000032));
        Assert.InRange(on, 56.3, 56.7);
        Assert.InRange(off, 70.4, 70.8);
    }

    [Fact]
    public void LowestMultiplier_OnlyDoublesBelowVrrMinimum()
    {
        var (on, off) = LfcModel.DoublingThresholds(240, 48, FrameSplit.LowestMultiplier);
        Assert.InRange(on, 47.8, 48.2);
        Assert.InRange(off, 47.8, 48.2);   // no hysteresis in lowest-fit mode
    }

    [Fact]
    public void FrameSplit_DecodesTheDriverBits()
    {
        var d = FrameSplit.Default;
        Assert.Equal(85, d.Stickiness);
        Assert.False(d.LowestFit);
        Assert.False(d.NoSticky);

        var l = FrameSplit.LowestMultiplier;
        Assert.Equal(0x80010055u, l.Raw);
        Assert.True(l.LowestFit);

        var m = FrameSplit.Make(lowestFit: true, stickiness: 50, noSticky: true);
        Assert.Equal(0x80030032u, m.Raw);
        Assert.True(m.LowestFit && m.NoSticky);
        Assert.Equal(50, m.Stickiness);
    }

    [Fact]
    public void NoSticky_Bit_RemovesTheBonus()
    {
        // with 0x80020055 (bit 17) the multiplier in use gets no bonus any more -> thresholds ON == OFF
        var (on, off) = LfcModel.DoublingThresholds(240, 48, new FrameSplit(0x80020055));
        Assert.InRange(Math.Abs(on - off), 0, 0.2);
    }
}
