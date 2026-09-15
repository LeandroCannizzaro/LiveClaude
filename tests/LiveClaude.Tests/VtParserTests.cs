using LiveClaude.Core.Hosting;
using LiveClaude.Terminal.Vt;
using Xunit;

namespace LiveClaude.Tests;

public class VtParserTests
{
    private static (TerminalScreen Screen, VtParser Parser) Create(int columns = 20, int rows = 5)
    {
        var screen = new TerminalScreen(columns, rows);
        return (screen, new VtParser(screen));
    }

    [Fact]
    public void PlainTextLandsOnTheScreen()
    {
        var (screen, parser) = Create();
        parser.Write("hello");

        Assert.Equal("hello", screen.GetText());
        Assert.Equal(5, screen.CursorColumn);
    }

    [Fact]
    public void CursorPositioningIsOneBased()
    {
        var (screen, parser) = Create();
        parser.Write("\u001b[3;5Hx");

        Assert.Equal(2, screen.CursorRow);
        Assert.Equal('x', screen[2, 4].Char);
    }

    [Fact]
    public void EraseInDisplayClearsEverything()
    {
        var (screen, parser) = Create();
        parser.Write("noise\u001b[2J");

        Assert.Equal("", screen.GetText());
    }

    [Fact]
    public void SgrSetsColoursAndAttributes()
    {
        var (screen, parser) = Create();
        parser.Write("\u001b[1;31mA\u001b[0mB");

        var first = screen[0, 0];
        Assert.True(first.Flags.HasFlag(CellFlags.Bold));
        Assert.Equal(VtColors.ToRgb(1), first.Foreground);

        var second = screen[0, 1];
        Assert.Equal(CellFlags.None, second.Flags);
        Assert.Equal(-1, second.Foreground);
    }

    [Fact]
    public void TrueColourIsParsed()
    {
        var (screen, parser) = Create();
        parser.Write("\u001b[38;2;10;20;30mX");

        Assert.Equal(VtColors.FromRgb(10, 20, 30), screen[0, 0].Foreground);
    }

    [Fact]
    public void LineWrapScrollsAtTheBottom()
    {
        var (screen, parser) = Create(columns: 4, rows: 2);
        parser.Write("aaaa\r\nbbbb\r\ncccc");

        Assert.Contains("cccc", screen.GetText());
        Assert.DoesNotContain("aaaa", screen.GetText());
    }

    [Fact]
    public void TheAlternateBufferIsIsolated()
    {
        var (screen, parser) = Create();
        parser.Write("main");
        parser.Write("\u001b[?1049h");

        Assert.Equal("", screen.GetText());

        parser.Write("\u001b[?1049l");
        Assert.Equal("main", screen.GetText());
    }

    [Fact]
    public void CursorVisibilityFollowsDecTcem()
    {
        var (screen, parser) = Create();

        parser.Write("\u001b[?25l");
        Assert.False(screen.CursorVisible);

        parser.Write("\u001b[?25h");
        Assert.True(screen.CursorVisible);
    }

    [Fact]
    public void OscTitleSequencesAreReported()
    {
        var (_, parser) = Create();
        string? title = null;
        parser.TitleChanged += value => title = value;

        parser.Write("\u001b]0;LiveClaude\u0007");

        Assert.Equal("LiveClaude", title);
    }
}

public class ScheduledTaskXmlTests
{
    [Fact]
    public void TheTaskRunsAtLogonAndAtBootAndRestartsOnFailure()
    {
        var xml = ScheduledTaskInstaller.BuildXml(@"C:\Program Files\LiveClaude\LiveClaude.Service.exe", runAtBoot: true);

        Assert.Contains("<LogonTrigger>", xml);
        Assert.Contains("<BootTrigger>", xml);
        Assert.Contains("<RestartOnFailure>", xml);
        Assert.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>", xml);
        Assert.Contains("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>", xml);
        Assert.Contains("--supervise", xml);
    }

    [Fact]
    public void BootTriggerCanBeLeftOut()
    {
        var xml = ScheduledTaskInstaller.BuildXml(@"C:\tools\LiveClaude.Service.exe", runAtBoot: false);
        Assert.DoesNotContain("<BootTrigger>", xml);
    }
}
