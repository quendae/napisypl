using NapisyPL.Shell;

namespace NapisyPL.Core.Tests;

public sealed class CommandLineOptionsTests
{
    [Fact]
    public void NoArguments_OpensTheMainWindow() =>
        Assert.Equal(StartupAction.OpenMainWindow, CommandLineOptions.Parse([]).Action);

    [Fact]
    public void ContextMenuSearch_KeepsThePathWithSpaces()
    {
        var options = CommandLineOptions.Parse(["--search", @"Z:\seriale_napisy\Maximum Pleasure Guaranteed\Season 1"]);

        Assert.Equal(StartupAction.Search, options.Action);
        Assert.Equal([@"Z:\seriale_napisy\Maximum Pleasure Guaranteed\Season 1"], options.Paths);
    }

    [Fact]
    public void FileDroppedOnTheExe_IsASearch()
    {
        var options = CommandLineOptions.Parse([@"C:\Filmy\film.mkv"]);

        Assert.Equal(StartupAction.Search, options.Action);
        Assert.Equal([@"C:\Filmy\film.mkv"], options.Paths);
    }

    [Fact]
    public void SearchWithoutPaths_OpensTheMainWindow() =>
        Assert.Equal(StartupAction.OpenMainWindow, CommandLineOptions.Parse(["--search", " "]).Action);

    [Theory]
    [InlineData("--register-shell", StartupAction.RegisterExplorerMenu)]
    [InlineData("--UNREGISTER-SHELL", StartupAction.UnregisterExplorerMenu)]
    [InlineData("--background", StartupAction.Background)]
    public void Switches_AreRecognised(string argument, StartupAction expected) =>
        Assert.Equal(expected, CommandLineOptions.Parse([argument]).Action);

    [Fact]
    public void ForwardedArguments_RoundTrip()
    {
        var original = CommandLineOptions.Parse(["--search", @"C:\a b\film.mkv", @"D:\seria"]);

        var forwarded = CommandLineOptions.Parse(original.ToArguments());

        Assert.Equal(original.Action, forwarded.Action);
        Assert.Equal(original.Paths, forwarded.Paths);
    }

    [Fact]
    public void PlainLaunch_ForwardsNothingAndStillOpensTheWindow() =>
        Assert.Equal(StartupAction.OpenMainWindow,
            CommandLineOptions.Parse(CommandLineOptions.Parse([]).ToArguments()).Action);
}
