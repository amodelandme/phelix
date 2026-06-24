using Phelix.Cli;

namespace Phelix.Cli.Tests;

public class CliRendererTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "1")]
    [InlineData(340, "340")]
    [InlineData(999, "999")]
    [InlineData(1_000, "1k")]
    [InlineData(1_200, "1.2k")]
    [InlineData(4_800, "4.8k")]
    [InlineData(5_000, "5k")]
    [InlineData(999_999, "1000k")]
    [InlineData(1_000_000, "1M")]
    [InlineData(1_300_000, "1.3M")]
    public void FormatTokenCount_AbbreviatesAndTrimsTrailingZero(int count, string expected)
    {
        Assert.Equal(expected, CliRenderer.FormatTokenCount(count));
    }

    [Fact]
    public void FormatTokenCount_NegativeClampsToZero()
    {
        Assert.Equal("0", CliRenderer.FormatTokenCount(-42));
    }
}
