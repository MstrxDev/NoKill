using NoKill.Core.Updates;

namespace NoKill.Core.Tests;

public class UpdateVersionTests
{
    [Theory]
    [InlineData("0.1.0", "v0.2.0", true)]
    [InlineData("0.1.0", "v0.1.1", true)]
    [InlineData("0.1.0", "v1.0.0", true)]
    [InlineData("0.1.0", "v0.1.0", false)]   // same version: no update
    [InlineData("0.2.0", "v0.1.0", false)]   // downgrade: never offered
    [InlineData("0.1.0.0", "v0.1.0", false)] // 4-part assembly version vs 3-part tag
    [InlineData("0.1.0", "0.2", true)]       // short tag
    [InlineData("0.1.0", "not-a-version", false)]
    [InlineData("garbage", "v0.2.0", false)] // unparseable current: fail closed
    [InlineData("0.1.0", "", false)]
    [InlineData("0.1.0", "v0.2.0-beta", true)] // prerelease suffix stripped
    public void IsNewer_ComparesConservatively(string current, string tag, bool expected)
    {
        Assert.Equal(expected, UpdateVersion.IsNewer(current, tag));
    }

    [Theory]
    [InlineData("v0.2.0", "0.2.0")]
    [InlineData("V1.0.0", "1.0.0")]
    [InlineData(" v0.2.0-rc1 ", "0.2.0")]
    [InlineData("0.2.0", "0.2.0")]
    public void Normalize_StripsTagDecorations(string tag, string expected)
    {
        Assert.Equal(expected, UpdateVersion.Normalize(tag));
    }
}
