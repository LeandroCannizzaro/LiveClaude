using System.IO;
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

    /// <summary>
    /// The boot trigger needs administrator rights, and UAC may be answered with another account —
    /// the task must still belong to the user whose session the servers run in.
    /// </summary>
    [Fact]
    public void TheTaskIsRegisteredForTheGivenUserNotTheElevatedOne()
    {
        var xml = ScheduledTaskInstaller.BuildXml(@"C:\tools\LiveClaude.Service.exe", runAtBoot: true, userName: @"CANLE\cl");

        Assert.Contains(@"<UserId>CANLE\cl</UserId>", xml);
        Assert.Contains("<LogonType>InteractiveToken</LogonType>", xml);
    }

    /// <summary>
    /// sc.exe parses its own command line: every option is "key= value" with the space after the
    /// equals sign, and the executable is quoted inside the quoted binPath value so a path with
    /// spaces survives. Passing the pairs through ProcessStartInfo.ArgumentList instead produced
    /// "key= value" as one quoted token, which sc.exe rejected with a usage error.
    /// </summary>
    [Fact]
    public void TheServiceCommandLineIsShapedTheWayScExeExpects()
    {
        var line = WindowsServiceInstaller.BuildCreateCommandLine(
            @"C:\Program Files\LiveClaude\LiveClaude.Service.exe",
            @"CANLE\cl",
            "secret pass");

        Assert.Contains(@"binPath= ""\""C:\Program Files\LiveClaude\LiveClaude.Service.exe\"" --service""", line);
        Assert.Contains("start= auto", line);
        Assert.Contains(@"obj= ""CANLE\cl""", line);
        Assert.Contains(@"password= ""secret pass""", line);
        Assert.DoesNotContain("\"binPath=", line);
    }

    [Fact]
    public void WithoutAnAccountNoCredentialsGoOnTheCommandLine()
    {
        var line = WindowsServiceInstaller.BuildCreateCommandLine(@"C:\tools\LiveClaude.Service.exe");

        Assert.DoesNotContain("obj=", line);
        Assert.DoesNotContain("password=", line);
    }

    [Fact]
    public void TheFailureActionsRestartTheServiceThreeTimes()
    {
        var line = WindowsServiceInstaller.BuildFailureCommandLine();

        Assert.Contains("reset= 86400", line);
        Assert.Contains("actions= restart/5000/restart/10000/restart/30000", line);
    }

    [Theory]
    [InlineData("[SC] StartService FAILED 1069:", "Log on as a service")]
    [InlineData("[SC] OpenSCManager FAILED 5: Access is denied.", "elevated")]
    [InlineData("[SC] CreateService FAILED 1073:", "already exists")]
    public void ServiceFailuresAreExplained(string output, string expected) =>
        Assert.Contains(expected, WindowsServiceInstaller.Explain(new ProcessResult(1, output, "")));

    /// <summary>
    /// A ClickOnce install ships LiveClaude.Service.exe without its runtime configuration, and .NET
    /// refuses to start such an executable. Registering it would leave a task that fails at every
    /// logon, so the desktop application has to host the supervisor there instead.
    /// </summary>
    [Fact]
    public void TheAppIsRegisteredWhenTheSupervisorCannotStart()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"liveclaude-clickonce-{Guid.NewGuid():n}");
        Directory.CreateDirectory(directory);

        try
        {
            // What ClickOnce actually deploys: both executables, but only the app's runtimeconfig.
            File.WriteAllText(Path.Combine(directory, "LiveClaude.Service.exe"), "");
            File.WriteAllText(Path.Combine(directory, "LiveClaude.exe"), "");
            File.WriteAllText(Path.Combine(directory, "LiveClaude.runtimeconfig.json"), "{}");

            var command = SupervisorLauncher.Resolve(directory, asService: false);

            Assert.Equal(Path.Combine(directory, "LiveClaude.exe"), command.ExecutablePath);
            Assert.Equal("--supervise", command.Arguments);
            Assert.Contains("ClickOnce", command.Note);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TheSupervisorIsRegisteredWhenItsRuntimeConfigurationIsThere()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"liveclaude-zip-{Guid.NewGuid():n}");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(Path.Combine(directory, "LiveClaude.Service.exe"), "");
            File.WriteAllText(Path.Combine(directory, "LiveClaude.Service.runtimeconfig.json"), "{}");
            File.WriteAllText(Path.Combine(directory, "LiveClaude.exe"), "");
            File.WriteAllText(Path.Combine(directory, "LiveClaude.runtimeconfig.json"), "{}");

            var command = SupervisorLauncher.Resolve(directory, asService: true);

            Assert.Equal(Path.Combine(directory, "LiveClaude.Service.exe"), command.ExecutablePath);
            Assert.Equal("--service", command.Arguments);
            Assert.Null(command.Note);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TheRegisteredArgumentsEndUpInTheTaskAndTheService()
    {
        var xml = ScheduledTaskInstaller.BuildXml(@"C:\app\LiveClaude.exe", runAtBoot: false, arguments: "--supervise");
        Assert.Contains("<Arguments>--supervise</Arguments>", xml);
        Assert.Contains(@"<Command>C:\app\LiveClaude.exe</Command>", xml);

        var line = WindowsServiceInstaller.BuildCreateCommandLine(@"C:\app\LiveClaude.exe", arguments: "--service");
        Assert.Contains(@"binPath= ""\""C:\app\LiveClaude.exe\"" --service""", line);
    }

    [Theory]
    [InlineData(@"C:\Users\cl\AppData\Local\Apps\2.0\ABC123\LiveClaude", true)]
    [InlineData(@"C:\Users\cl\AppData\Local\Microsoft\WinGet\Packages\Publisher.App\1.0", true)]
    [InlineData(@"C:\Program Files\LiveClaude", false)]
    [InlineData(@"C:\Users\cl\Programs\LiveClaude", false)]
    public void UpdatableInstallLocationsAreRecognised(string directory, bool expected) =>
        Assert.Equal(expected, SupervisorDeployment.IsVolatileLocation(directory));
}
