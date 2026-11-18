using NvidiaGsyncTweaks.Model;
using Xunit;

namespace NvidiaGsyncTweaks.Tests;

public class CatalogTests
{
    private static IEnumerable<TweakDef> All => Catalog.Pages.SelectMany(p => p.Groups).Concat(Catalog.LfcGroups).SelectMany(g => g.Tweaks);

    [Fact]
    public void Pages_HaveUniqueTags()
    {
        var tags = Catalog.Pages.Select(p => p.Tag).ToList();
        Assert.Equal(tags.Count, tags.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain("lfc", tags);       // reserved for the LFC page
        Assert.DoesNotContain("backups", tags);   // reserved for the backups page
    }

    [Fact]
    public void EveryRow_IsUniquePerNameAndBit()
    {
        var keys = All.Select(t => (t.Name.ToLowerInvariant(), t.BitMask)).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void BitRows_OfOneRegister_ShareTheRegisterDefault()
    {
        foreach (var g in All.Where(t => t.Kind == TweakKind.Bit).GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            Assert.Single(g.Select(t => t.Default).Distinct());
    }

    [Fact]
    public void Numbers_HaveSaneRanges()
    {
        foreach (var t in All.Where(t => t.Kind == TweakKind.Number))
        {
            Assert.True(t.Min <= t.Max, t.Name);
            Assert.InRange((double)t.Default, t.Min, t.Max);
            Assert.True(t.Scale > 0, t.Name);
        }
    }

    [Fact]
    public void Choices_ContainTheDriverDefault()
    {
        foreach (var t in All.Where(t => t.Kind == TweakKind.Choice))
        {
            Assert.NotNull(t.Choices);
            Assert.Contains(t.Choices!, c => c.Value == t.Default);
        }
    }

    [Fact]
    public void Toggles_DefaultToOneOfTheirTwoValues()
    {
        foreach (var t in All.Where(t => t.Kind == TweakKind.Toggle))
            Assert.True(t.Default == t.OnValue || t.Default == t.OffValue, t.Name);
    }

    [Fact]
    public void Bits_AreNotEmpty_AndInsideTheRegisterDefaultOrZero()
    {
        foreach (var t in All.Where(t => t.Kind == TweakKind.Bit))
            Assert.NotEqual(0u, t.BitMask);
    }

    [Fact]
    public void AllNames_IncludesTheLfcValue_AndHasNoDuplicates()
    {
        var names = Catalog.AllNames.ToList();
        Assert.Contains(FrameSplit.ValueName, names);
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EveryTweak_HasTitleAndDescription()
    {
        foreach (var t in All)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Title), t.Name);
            Assert.False(string.IsNullOrWhiteSpace(t.Description), t.Name);
        }
    }
}
