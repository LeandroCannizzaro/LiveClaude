using System.Text;
using LiveClaude.Abstractions;
using LiveClaude.Vt;
using Xunit;

namespace LiveClaude.Tests.Platform;

/// <summary>
/// Exercises the pipeline the embedded terminal relies on, whichever pseudo terminal the platform
/// provides: pty → bytes → VT parser → screen.
///
/// The same assertions run on all three operating systems. Only the probe command differs, which is
/// exactly the amount of platform knowledge a test at this level should carry.
/// </summary>
public class PtyTests
{
    [Fact]
    public void APseudoTerminalIsAvailableOnThisMachine() =>
        Assert.True(PlatformLoader.Current.Pty.IsSupported);

    [Fact]
    public async Task AChildProcessRunsAndReportsItsExitCode()
    {
        using var pty = StartProbe();
        var exitCode = await pty.Exited.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(0, exitCode);
        Assert.True(pty.HasExited);
    }

    /// <summary>
    /// Output reaches the parser — unless the host owns a console, which only happens on Windows:
    /// there a console owner hands its console to every child and the pseudo-console attribute is
    /// ignored, so nothing is captured. The test runner owns one; LiveClaude's supervisor and app
    /// deliberately do not. On POSIX the pty slave is the child's controlling terminal either way,
    /// so capture always works and the assertion has no exception to make.
    /// </summary>
    [Fact]
    public async Task OutputIsCapturedUnlessTheHostOwnsAConsole()
    {
        var screen = new TerminalScreen(80, 25);
        var parser = new VtParser(screen);

        using var pty = StartProbe();

        var reader = Task.Run(async () =>
        {
            var buffer = new byte[4096];
            var decoder = Encoding.UTF8.GetDecoder();
            var chars = new char[8192];

            while (true)
            {
                var read = await pty.Output.ReadAsync(buffer.AsMemory());
                if (read <= 0)
                    break;

                var count = decoder.GetChars(buffer, 0, read, chars, 0);
                parser.Write(new string(chars, 0, count));
            }
        });

        await pty.Exited.WaitAsync(TimeSpan.FromSeconds(20));
        await Task.WhenAny(reader, Task.Delay(TimeSpan.FromSeconds(3)));

        if (PlatformLoader.Current.Pty.HostOwnsConsole)
            Assert.DoesNotContain("LiveClaudeProbe", screen.GetText());
        else
            Assert.Contains("LiveClaudeProbe", screen.GetText());
    }

    /// <summary>
    /// A resize must not disturb a running child. The window size reaches the pty through very
    /// different calls — ResizePseudoConsole and ioctl(TIOCSWINSZ) — and getting it wrong on POSIX
    /// takes the whole process down with a signal rather than returning an error.
    /// </summary>
    [Fact]
    public async Task ResizingAliveProcessIsHarmless()
    {
        using var pty = StartSleeper();

        pty.Resize(100, 40);
        pty.Resize(60, 20);

        Assert.False(pty.HasExited);

        pty.Kill();
        await pty.Exited.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Interrupting is what lets a Remote Control server deregister its bridge environment instead of
    /// leaving a dead entry in the session picker. On POSIX this only works because the child was
    /// started in its own session with the pty slave as its controlling terminal — without that the
    /// 0x03 byte is just a byte, and the process never sees SIGINT.
    /// </summary>
    [PlatformFact("linux", "macos")]
    public async Task InterruptStopsTheChild()
    {
        using var pty = StartSleeper();

        pty.SendInterrupt();

        var exited = await Task.WhenAny(pty.Exited, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(pty.Exited, exited);
    }

    private static IPtyProcess StartProbe() => PlatformLoader.Current.Pty.Start(new PtyOptions
    {
        ExecutablePath = Shell,
        Arguments = PlatformFactAttribute.Current == "windows"
            ? ["/c", "echo", "LiveClaudeProbe"]
            : ["-c", "echo LiveClaudeProbe"],
        WorkingDirectory = Path.GetTempPath(),
        Columns = 80,
        Rows = 25
    });

    private static IPtyProcess StartSleeper() => PlatformLoader.Current.Pty.Start(new PtyOptions
    {
        ExecutablePath = Shell,
        Arguments = PlatformFactAttribute.Current == "windows"
            ? ["/c", "timeout", "/t", "30"]
            : ["-c", "sleep 30"],
        WorkingDirectory = Path.GetTempPath(),
        Columns = 80,
        Rows = 25
    });

    private static string Shell => PlatformFactAttribute.Current == "windows"
        ? Path.Combine(Environment.SystemDirectory, "cmd.exe")
        : "/bin/sh";
}
