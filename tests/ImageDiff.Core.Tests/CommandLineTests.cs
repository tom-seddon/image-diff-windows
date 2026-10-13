using ImageDiff.Core;

namespace ImageDiff.Core.Tests;

public class CommandLineTests
{
    [Fact]
    public void NoArgumentsOpensEmpty()
    {
        var o = CommandLine.Parse([]);
        Assert.Null(o.LeftPath);
        Assert.Null(o.RightPath);
        Assert.Equal(ViewMode.Difference, o.Mode);
        Assert.Equal(0, o.Tolerance);
    }

    [Fact]
    public void ParsesPathsAndOptions()
    {
        var o = CommandLine.Parse(["a.png", "b.png", "--mode", "slider", "-t", "12", "-o", "out.png", "--show"]);
        Assert.Equal("a.png", o.LeftPath);
        Assert.Equal("b.png", o.RightPath);
        Assert.Equal(ViewMode.Slider, o.Mode);
        Assert.Equal(12, o.Tolerance);
        Assert.Equal("out.png", o.OutputPath);
        Assert.True(o.Show);
    }

    [Theory]
    [InlineData("diff", ViewMode.Difference)]
    [InlineData("SIDE", ViewMode.SideBySide)]
    [InlineData("side-by-side", ViewMode.SideBySide)]
    [InlineData("swipe", ViewMode.Slider)]
    public void ModeNames(string name, ViewMode expected) => Assert.Equal(expected, CommandLine.ParseMode(name));

    [Theory]
    [InlineData("--mode", "blink")]
    [InlineData("-t", "300")]
    [InlineData("-t", "abc")]
    [InlineData("--bogus")]
    [InlineData("a", "b", "c")]
    [InlineData("-o")]
    public void InvalidArgumentsThrow(params string[] args) =>
        Assert.Throws<CommandLineException>(() => CommandLine.Parse(args));

    [Fact]
    public void HelpFlags()
    {
        Assert.True(CommandLine.Parse(["/?"]).ShowHelp);
        Assert.True(CommandLine.Parse(["--help"]).ShowHelp);
    }

    [Fact]
    public void GuiArgumentsRoundTrip()
    {
        var original = CommandLine.Parse(["a.png", "b.png", "-m", "side", "-t", "7", "--show"]);
        var parsed = CommandLine.Parse(CommandLine.ToGuiArguments(original));
        Assert.Equal("a.png", parsed.LeftPath);
        Assert.Equal("b.png", parsed.RightPath);
        Assert.Equal(ViewMode.SideBySide, parsed.Mode);
        Assert.Equal(7, parsed.Tolerance);
        Assert.False(parsed.Show);
    }
}
