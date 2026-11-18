using NvidiaGsyncTweaks.Services;
using Xunit;

namespace NvidiaGsyncTweaks.Tests;

public class AppInfoTests
{
    [Theory]
    [InlineData(1, 0, 57, 0, "1.0.57")]
    [InlineData(2, 5, 1, 0, "2.5.1")]
    [InlineData(1, 0, 0, 0, "1.0.0")]
    public void Format_ShowsMajorMinorBuild(int major, int minor, int build, int rev, string expected) =>
        Assert.Equal(expected, AppInfo.Format(new Version(major, minor, build, rev)));

    [Fact]
    public void Format_HandlesMissingParts()
    {
        Assert.Equal("0.0.0", AppInfo.Format(null));
        Assert.Equal("1.2.0", AppInfo.Format(new Version(1, 2)));   // no build part
    }

    [Fact]
    public void BuildNumberZero_IsALocalBuild_ButPipelineBuildsAreNot()
    {
        Assert.True(AppInfo.IsLocal(new Version(1, 0, 0, 0)));
        Assert.True(AppInfo.IsLocal(new Version(1, 0)));
        Assert.True(AppInfo.IsLocal(null));
        Assert.False(AppInfo.IsLocal(new Version(1, 0, 1, 0)));
        Assert.False(AppInfo.IsLocal(new Version(1, 0, 57, 0)));
    }

    [Fact]
    public void Display_HasAVersionPrefix()
    {
        Assert.StartsWith("v", AppInfo.Display);
        Assert.Contains(AppInfo.Version, AppInfo.Display);
    }
}
