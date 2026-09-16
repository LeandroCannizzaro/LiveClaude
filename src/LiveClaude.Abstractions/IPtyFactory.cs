namespace LiveClaude.Abstractions;

/// <summary>What to start in a pseudo terminal.</summary>
public sealed class PtyOptions
{
    public required string ExecutablePath { get; init; }

    /// <summary>
    /// The argument vector, never a command line. POSIX passes this straight to the child; only the
    /// Windows implementation has to quote it back into a single string, and that quoting is its own
    /// business (see <see cref="IProcessLauncher.FormatCommandLine"/> for the display form).
    /// </summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    public string? WorkingDirectory { get; init; }

    public IReadOnlyDictionary<string, string>? Environment { get; init; }

    public short Columns { get; init; } = 120;

    public short Rows { get; init; } = 30;
}

/// <summary>
/// A child process hosted in a real terminal — ConPTY on Windows, a pty pair on Linux and macOS.
/// Unlike redirected pipes this gives the CLI a terminal, so its full-screen prompts (workspace
/// trust, the Remote Control confirmation, <c>/login</c>) render and can be answered.
/// </summary>
public interface IPtyProcess : IDisposable
{
    int ProcessId { get; }

    /// <summary>Raw terminal output, escape sequences included (UTF-8).</summary>
    Stream Output { get; }

    Task<int> Exited { get; }

    bool HasExited { get; }

    /// <summary>Sends keystrokes (UTF-8).</summary>
    void Write(string text);

    /// <summary>
    /// Asks the child to stop the way Ctrl+C would. This is how a Remote Control server is meant to
    /// go down: it deregisters its bridge environment instead of leaving a dead entry behind, which
    /// is exactly what a hard <see cref="Kill"/> creates.
    /// </summary>
    void SendInterrupt();

    void Resize(short columns, short rows);

    void Kill();
}

public interface IPtyFactory
{
    /// <summary>False when the OS build has no pseudo-terminal support (Windows before 1809).</summary>
    bool IsSupported { get; }

    /// <summary>
    /// True when this process owns a console and that breaks terminal capture.
    ///
    /// On Windows it does: a console owner hands its console to every child it starts and the
    /// pseudo-console attribute is then ignored, so the child's output never reaches the pipes. That
    /// is why both hosts are windowless executables. POSIX has no such rule and always returns false.
    /// </summary>
    bool HostOwnsConsole { get; }

    IPtyProcess Start(PtyOptions options);
}
