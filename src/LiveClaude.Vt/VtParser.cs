using System.Text;

namespace LiveClaude.Vt;

/// <summary>
/// Feeds terminal output into a <see cref="TerminalScreen"/>. Covers the sequences a modern CLI
/// actually emits: cursor movement, erase, scroll regions, SGR colours (16/256/true colour),
/// the alternate screen buffer and cursor visibility. Anything else is consumed and ignored.
/// </summary>
public sealed class VtParser
{
    private enum State
    {
        Ground,
        Escape,
        Csi,
        Osc,
        OscEscape,
        Charset,
        DcsIgnore
    }

    private readonly TerminalScreen _screen;
    private readonly StringBuilder _parameters = new();
    private readonly StringBuilder _oscBuffer = new();
    private State _state = State.Ground;
    private char _csiPrefix;

    public VtParser(TerminalScreen screen) => _screen = screen;

    /// <summary>Raised when the host should answer the terminal (e.g. a device status report).</summary>
    public event Action<string>? Respond;

    /// <summary>Raised when an OSC window-title sequence arrives.</summary>
    public event Action<string>? TitleChanged;

    public bool ApplicationCursorKeys { get; private set; }

    public bool BracketedPaste { get; private set; }

    public void Write(string data)
    {
        foreach (var c in data)
            Consume(c);
    }

    private void Consume(char c)
    {
        switch (_state)
        {
            case State.Ground:
                Ground(c);
                break;

            case State.Escape:
                Escape(c);
                break;

            case State.Csi:
                Csi(c);
                break;

            case State.Osc:
                if (c == '\x07')
                {
                    FinishOsc();
                }
                else if (c == '\x1b')
                {
                    _state = State.OscEscape;
                }
                else
                {
                    _oscBuffer.Append(c);
                    if (_oscBuffer.Length > 4096)
                        FinishOsc();
                }

                break;

            case State.OscEscape:
                FinishOsc();
                if (c != '\\')
                    Consume(c);
                break;

            case State.Charset:
                _state = State.Ground;
                break;

            case State.DcsIgnore:
                if (c == '\x1b')
                    _state = State.OscEscape;
                break;
        }
    }

    private void Ground(char c)
    {
        switch (c)
        {
            case '\x1b':
                _state = State.Escape;
                break;
            case '\r':
                _screen.CarriageReturn();
                break;
            case '\n':
            case '\v':
            case '\f':
                _screen.LineFeed();
                break;
            case '\b':
                _screen.Backspace();
                break;
            case '\t':
                _screen.Tab();
                break;
            case '\x07':
                break; // bell
            case '\0':
                break;
            default:
                if (!char.IsControl(c))
                    _screen.Write(c);
                break;
        }
    }

    private void Escape(char c)
    {
        switch (c)
        {
            case '[':
                _parameters.Clear();
                _csiPrefix = '\0';
                _state = State.Csi;
                break;
            case ']':
                _oscBuffer.Clear();
                _state = State.Osc;
                break;
            case 'P':
            case 'X':
            case '^':
            case '_':
                _state = State.DcsIgnore;
                break;
            case '(':
            case ')':
            case '*':
            case '+':
                _state = State.Charset;
                break;
            case '7':
                _screen.SaveCursor();
                _state = State.Ground;
                break;
            case '8':
                _screen.RestoreCursor();
                _state = State.Ground;
                break;
            case 'M':
                _screen.ReverseLineFeed();
                _state = State.Ground;
                break;
            case 'D':
                _screen.LineFeed();
                _state = State.Ground;
                break;
            case 'c':
                _screen.Reset();
                _state = State.Ground;
                break;
            default:
                _state = State.Ground;
                break;
        }
    }

    private void Csi(char c)
    {
        if (c is >= '0' and <= '9' or ';' or ':')
        {
            _parameters.Append(c);
            return;
        }

        if (c is '?' or '<' or '=' or '>' or '!')
        {
            _csiPrefix = c;
            return;
        }

        if (c is ' ' or '$' or '"' or '\'' or '*')
            return; // intermediate byte, ignored

        var parameters = ParseParameters();
        Dispatch(c, parameters);
        _state = State.Ground;
        _parameters.Clear();
    }

    private void Dispatch(char final, int[] p)
    {
        int At(int index, int fallback = 1) => p.Length > index && p[index] > 0 ? p[index] : fallback;

        switch (final)
        {
            case 'A': _screen.MoveCursorRelative(-At(0), 0); break;
            case 'B': _screen.MoveCursorRelative(At(0), 0); break;
            case 'C': _screen.MoveCursorRelative(0, At(0)); break;
            case 'D': _screen.MoveCursorRelative(0, -At(0)); break;
            case 'E': _screen.MoveCursor(_screen.CursorRow + At(0), 0); break;
            case 'F': _screen.MoveCursor(_screen.CursorRow - At(0), 0); break;
            case 'G': _screen.MoveCursor(_screen.CursorRow, At(0) - 1); break;
            case 'd': _screen.MoveCursor(At(0) - 1, _screen.CursorColumn); break;
            case 'H':
            case 'f': _screen.MoveCursor(At(0) - 1, At(1) - 1); break;
            case 'J': _screen.EraseInDisplay(At(0, 0)); break;
            case 'K': _screen.EraseInLine(At(0, 0)); break;
            case 'L': _screen.InsertLines(At(0)); break;
            case 'M': _screen.DeleteLines(At(0)); break;
            case 'P': _screen.DeleteCharacters(At(0)); break;
            case '@': _screen.InsertCharacters(At(0)); break;
            case 'X': _screen.EraseCharacters(At(0)); break;
            case 'S': _screen.ScrollUp(At(0)); break;
            case 'T': _screen.ScrollDown(At(0)); break;
            case 'r': _screen.SetScrollRegion(At(0) - 1, p.Length > 1 && p[1] > 0 ? p[1] - 1 : _screen.Rows - 1); break;
            case 's': _screen.SaveCursor(); break;
            case 'u': _screen.RestoreCursor(); break;
            case 'm': ApplySgr(p); break;
            case 'h': SetMode(p, true); break;
            case 'l': SetMode(p, false); break;
            case 'n':
                if (At(0, 0) == 6)
                    Respond?.Invoke($"\x1b[{_screen.CursorRow + 1};{_screen.CursorColumn + 1}R");
                break;
        }
    }

    private void SetMode(int[] parameters, bool enabled)
    {
        if (_csiPrefix != '?')
            return;

        foreach (var mode in parameters)
        {
            switch (mode)
            {
                case 1:
                    ApplicationCursorKeys = enabled;
                    break;
                case 25:
                    _screen.CursorVisible = enabled;
                    break;
                case 47:
                case 1047:
                case 1049:
                    _screen.UseAlternateBuffer(enabled);
                    break;
                case 2004:
                    BracketedPaste = enabled;
                    break;
            }
        }
    }

    private void ApplySgr(int[] parameters)
    {
        if (parameters.Length == 0)
            parameters = [0];

        for (var i = 0; i < parameters.Length; i++)
        {
            var code = parameters[i];
            switch (code)
            {
                case 0:
                    _screen.Pen = TerminalCell.Empty;
                    break;
                case 1: _screen.Pen.Flags |= CellFlags.Bold; break;
                case 2: _screen.Pen.Flags |= CellFlags.Dim; break;
                case 3: _screen.Pen.Flags |= CellFlags.Italic; break;
                case 4: _screen.Pen.Flags |= CellFlags.Underline; break;
                case 7: _screen.Pen.Flags |= CellFlags.Inverse; break;
                case 8: _screen.Pen.Flags |= CellFlags.Hidden; break;
                case 9: _screen.Pen.Flags |= CellFlags.Strike; break;
                case 21:
                case 22: _screen.Pen.Flags &= ~(CellFlags.Bold | CellFlags.Dim); break;
                case 23: _screen.Pen.Flags &= ~CellFlags.Italic; break;
                case 24: _screen.Pen.Flags &= ~CellFlags.Underline; break;
                case 27: _screen.Pen.Flags &= ~CellFlags.Inverse; break;
                case 28: _screen.Pen.Flags &= ~CellFlags.Hidden; break;
                case 29: _screen.Pen.Flags &= ~CellFlags.Strike; break;
                case 39: _screen.Pen.Foreground = -1; break;
                case 49: _screen.Pen.Background = -1; break;

                case 38:
                case 48:
                {
                    var (colour, consumed) = ReadExtendedColour(parameters, i);
                    if (consumed > 0)
                    {
                        if (code == 38)
                            _screen.Pen.Foreground = colour;
                        else
                            _screen.Pen.Background = colour;
                        i += consumed;
                    }

                    break;
                }

                default:
                    if (code is >= 30 and <= 37)
                        _screen.Pen.Foreground = VtColors.ToRgb(code - 30);
                    else if (code is >= 40 and <= 47)
                        _screen.Pen.Background = VtColors.ToRgb(code - 40);
                    else if (code is >= 90 and <= 97)
                        _screen.Pen.Foreground = VtColors.ToRgb(code - 90 + 8);
                    else if (code is >= 100 and <= 107)
                        _screen.Pen.Background = VtColors.ToRgb(code - 100 + 8);
                    break;
            }
        }
    }

    private static (int Colour, int Consumed) ReadExtendedColour(int[] parameters, int index)
    {
        if (parameters.Length <= index + 1)
            return (-1, 0);

        return parameters[index + 1] switch
        {
            5 when parameters.Length > index + 2 => (VtColors.ToRgb(parameters[index + 2]), 2),
            2 when parameters.Length > index + 4 => (
                VtColors.FromRgb(parameters[index + 2], parameters[index + 3], parameters[index + 4]), 4),
            _ => (-1, 0)
        };
    }

    private int[] ParseParameters()
    {
        if (_parameters.Length == 0)
            return [];

        var parts = _parameters.ToString().Split(';');
        var values = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            // Sub-parameters (38:2:r:g:b) collapse to their first component.
            var token = parts[i].Split(':')[0];
            values[i] = int.TryParse(token, out var value) ? value : 0;
        }

        return values;
    }

    private void FinishOsc()
    {
        var content = _oscBuffer.ToString();
        _oscBuffer.Clear();
        _state = State.Ground;

        var separator = content.IndexOf(';');
        if (separator > 0 && content[..separator] is "0" or "2")
            TitleChanged?.Invoke(content[(separator + 1)..]);
    }
}
