using System.IO;
using System.Text;
using LiveClaude.Core.Pty;
using LiveClaude.Terminal.Vt;
using Xunit;

namespace LiveClaude.Tests;

/// <summary>
/// Exercises the pipeline the embedded terminal relies on: pseudo console → bytes → VT parser → screen.
/// </summary>
public class PtyProcessTests
{
    [Fact]
    public void PseudoConsoleIsAvailableOnThisMachine() => Assert.True(PtyProcess.IsSupported);

    [Fact]
    public async Task AChildProcessRunsAndReportsItsExitCode()
    {
        using var pty = StartProbe();
        var exitCode = await pty.Exited.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(0, exitCode);
        Assert.True(pty.HasExited);
    }

    /// <summary>
    /// Windows gives a console-owning process's console to every child it starts and ignores the
    /// pseudo-console attribute, so output is only captured when the host is windowless. The test
    /// runner owns a console; LiveClaude's supervisor and app deliberately do not.
    /// </summary>
    [Fact]
    public async Task OutputIsCapturedExactlyWhenTheHostIsWindowless()
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

        if (PtyProcess.HasOwnConsole)
            Assert.DoesNotContain("LiveClaudeProbe", screen.GetText());
        else
            Assert.Contains("LiveClaudeProbe", screen.GetText());
    }

    private static PtyProcess StartProbe() => PtyProcess.Start(new PtyOptions
    {
        ExecutablePath = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
        Arguments = ["/c", "echo", "LiveClaudeProbe"],
        WorkingDirectory = Path.GetTempPath(),
        Columns = 80,
        Rows = 25
    });
}
