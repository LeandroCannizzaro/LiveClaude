using System.Text;

namespace LiveClaude.Core.Logging;

/// <summary>
/// Writes to a console and to a log at the same time.
///
/// The install and uninstall commands usually run elevated, in a process the user never sees: their
/// output went nowhere, so a failure inside them was invisible. Everything they print is now kept in
/// a file the app can read back.
/// </summary>
public sealed class TeeTextWriter : TextWriter
{
    private readonly TextWriter _console;
    private readonly RollingLogWriter _log;
    private readonly StringBuilder _pending = new();

    public TeeTextWriter(TextWriter console, RollingLogWriter log)
    {
        _console = console;
        _log = log;
    }

    public override Encoding Encoding => _console.Encoding;

    public override void Write(char value)
    {
        _console.Write(value);

        if (value == '\n')
            Flush();
        else if (value != '\r')
            _pending.Append(value);
    }

    public override void Write(string? value)
    {
        if (value is null)
            return;

        foreach (var c in value)
            Write(c);
    }

    public override void WriteLine(string? value)
    {
        Write(value);
        Write('\n');
    }

    public override void Flush()
    {
        _console.Flush();

        if (_pending.Length == 0)
            return;

        _log.Write(_pending.ToString());
        _pending.Clear();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Flush();

        base.Dispose(disposing);
    }
}
