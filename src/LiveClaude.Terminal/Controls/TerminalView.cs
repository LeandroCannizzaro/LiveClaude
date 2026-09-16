using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using LiveClaude.Vt;

namespace LiveClaude.Terminal.Controls;

/// <summary>
/// A terminal surface: renders a <see cref="TerminalScreen"/> and turns keystrokes into the bytes a
/// pseudo terminal expects. Drawn directly onto the canvas — no browser, no native terminal control,
/// and the same code on Windows, Linux and macOS.
/// </summary>
public sealed class TerminalView : Control
{
    private readonly TerminalScreen _screen;
    private readonly VtParser _parser;
    private readonly DispatcherTimer _renderTimer;
    private readonly DispatcherTimer _blinkTimer;

    private Typeface _typeface;
    private Typeface _boldTypeface;
    private double _cellWidth = 8;
    private double _cellHeight = 16;
    private long _renderedRevision = -1;
    private bool _cursorOn = true;

    static TerminalView()
    {
        AffectsRender<TerminalView>(
            TerminalFontFamilyProperty,
            TerminalFontSizeProperty,
            DefaultForegroundProperty,
            DefaultBackgroundProperty);

        FocusableProperty.OverrideDefaultValue<TerminalView>(true);
        ClipToBoundsProperty.OverrideDefaultValue<TerminalView>(true);
    }

    public TerminalView()
    {
        _screen = new TerminalScreen(120, 30);
        _parser = new VtParser(_screen);
        _parser.Respond += text => Input?.Invoke(text);
        _parser.TitleChanged += title => TitleChanged?.Invoke(title);

        _typeface = default;
        _boldTypeface = default;
        UpdateTypeface();

        _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _renderTimer.Tick += (_, _) =>
        {
            if (_screen.Revision != _renderedRevision)
                InvalidateVisual();
        };
        _renderTimer.Start();

        _blinkTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(600)
        };
        _blinkTimer.Tick += (_, _) =>
        {
            _cursorOn = !_cursorOn;
            if (IsFocused)
                InvalidateVisual();
        };
        _blinkTimer.Start();
    }

    #region Properties

    /// <summary>
    /// A comma-separated list, because no one font is present everywhere: Cascadia on a modern
    /// Windows, DejaVu Sans Mono on most Linux desktops, Menlo on macOS. The last entry is the
    /// generic family, which every system resolves to something fixed-width.
    /// </summary>
    public static readonly StyledProperty<string> TerminalFontFamilyProperty =
        AvaloniaProperty.Register<TerminalView, string>(
            nameof(TerminalFontFamily),
            "Cascadia Mono, Consolas, DejaVu Sans Mono, Menlo, Liberation Mono, monospace");

    public static readonly StyledProperty<double> TerminalFontSizeProperty =
        AvaloniaProperty.Register<TerminalView, double>(nameof(TerminalFontSize), 13.0);

    public static readonly StyledProperty<Color> DefaultForegroundProperty =
        AvaloniaProperty.Register<TerminalView, Color>(nameof(DefaultForeground), Color.FromRgb(0xE6, 0xE6, 0xE6));

    public static readonly StyledProperty<Color> DefaultBackgroundProperty =
        AvaloniaProperty.Register<TerminalView, Color>(nameof(DefaultBackground), Color.FromRgb(0x0C, 0x0C, 0x0C));

    public string TerminalFontFamily
    {
        get => GetValue(TerminalFontFamilyProperty);
        set => SetValue(TerminalFontFamilyProperty, value);
    }

    public double TerminalFontSize
    {
        get => GetValue(TerminalFontSizeProperty);
        set => SetValue(TerminalFontSizeProperty, value);
    }

    public Color DefaultForeground
    {
        get => GetValue(DefaultForegroundProperty);
        set => SetValue(DefaultForegroundProperty, value);
    }

    public Color DefaultBackground
    {
        get => GetValue(DefaultBackgroundProperty);
        set => SetValue(DefaultBackgroundProperty, value);
    }

    public int Columns => _screen.Columns;

    public int Rows => _screen.Rows;

    /// <summary>Text the user typed, ready to be written to the pseudo terminal.</summary>
    public event Action<string>? Input;

    /// <summary>Raised when the grid size changes, so the host can resize the pseudo terminal.</summary>
    public event Action<int, int>? GridSizeChanged;

    public event Action<string>? TitleChanged;

    #endregion

    /// <summary>Feeds raw terminal output (escape sequences included) into the screen.</summary>
    public void Write(string data)
    {
        if (string.IsNullOrEmpty(data))
            return;

        _parser.Write(data);
    }

    /// <summary>Injects input as if the user had typed it (used for toolbar shortcuts such as Ctrl+C).</summary>
    public void SendInput(string text) => Input?.Invoke(text);

    public void Clear()
    {
        _screen.Reset();
        InvalidateVisual();
    }

    public string GetScreenText() => _screen.GetText();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TerminalFontFamilyProperty || change.Property == TerminalFontSizeProperty)
        {
            UpdateTypeface();
            UpdateGridSize(Bounds.Size);
        }
    }

    private void UpdateTypeface()
    {
        var family = FontFamily.Parse(TerminalFontFamily);
        _typeface = new Typeface(family, FontStyle.Normal, FontWeight.Normal, FontStretch.Normal);
        _boldTypeface = new Typeface(family, FontStyle.Normal, FontWeight.Bold, FontStretch.Normal);

        var probe = CreateText("M", _typeface, Brushes.White);
        _cellWidth = Math.Max(1, probe.WidthIncludingTrailingWhitespace);
        _cellHeight = Math.Max(1, Math.Ceiling(probe.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        UpdateGridSize(size);
        return size;
    }

    private void UpdateGridSize(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0)
            return;

        var columns = Math.Max(20, (int)(size.Width / _cellWidth));
        var rows = Math.Max(5, (int)(size.Height / _cellHeight));

        if (columns == _screen.Columns && rows == _screen.Rows)
            return;

        _screen.Resize(columns, rows);
        GridSizeChanged?.Invoke(columns, rows);
        InvalidateVisual();
    }

    public override void Render(DrawingContext dc)
    {
        _renderedRevision = _screen.Revision;

        dc.FillRectangle(new ImmutableSolidColorBrush(DefaultBackground), new Rect(Bounds.Size));

        for (var row = 0; row < _screen.Rows; row++)
            RenderRow(dc, row);

        RenderCursor(dc);
    }

    private void RenderRow(DrawingContext dc, int row)
    {
        var column = 0;
        while (column < _screen.Columns)
        {
            var start = column;
            var cell = _screen[row, column];
            var text = new System.Text.StringBuilder();

            // Cells are coalesced into runs of identical styling, so a full-width row costs one draw
            // rather than one per character.
            while (column < _screen.Columns && _screen[row, column].SameStyle(cell))
            {
                var current = _screen[row, column];
                text.Append(current.Char == '\0' ? ' ' : current.Char);
                column++;
            }

            var (foreground, background) = ResolveColours(cell);
            var rect = new Rect(start * _cellWidth, row * _cellHeight, (column - start) * _cellWidth, _cellHeight);

            if (background != DefaultBackground)
                dc.FillRectangle(new ImmutableSolidColorBrush(background), rect);

            var content = text.ToString();
            if (string.IsNullOrWhiteSpace(content) || cell.Flags.HasFlag(CellFlags.Hidden))
                continue;

            var brush = new ImmutableSolidColorBrush(foreground);
            var typeface = cell.Flags.HasFlag(CellFlags.Bold) ? _boldTypeface : _typeface;

            dc.DrawText(CreateText(content, typeface, brush), new Point(rect.X, rect.Y));

            if (cell.Flags.HasFlag(CellFlags.Underline))
            {
                var y = rect.Bottom - 1.5;
                dc.DrawLine(new ImmutablePen(brush, 1), new Point(rect.X, y), new Point(rect.Right, y));
            }
        }
    }

    private void RenderCursor(DrawingContext dc)
    {
        if (!_screen.CursorVisible || (!_cursorOn && IsFocused))
            return;

        var rect = new Rect(
            _screen.CursorColumn * _cellWidth,
            _screen.CursorRow * _cellHeight,
            _cellWidth,
            _cellHeight);

        var colour = DefaultForeground;
        var brush = new ImmutableSolidColorBrush(colour, IsFocused ? 0.85 : 0.35);

        if (!IsFocused)
        {
            // Hollow while unfocused: the block would otherwise read as "this is where your typing
            // goes" in a window that is not listening.
            dc.DrawRectangle(null, new ImmutablePen(brush, 1), rect);
            return;
        }

        dc.FillRectangle(brush, rect);

        var cell = _screen[_screen.CursorRow, _screen.CursorColumn];
        if (cell.Char is not ' ' and not '\0')
        {
            dc.DrawText(
                CreateText(cell.Char.ToString(), _typeface, new ImmutableSolidColorBrush(DefaultBackground)),
                new Point(rect.X, rect.Y));
        }
    }

    private (Color Foreground, Color Background) ResolveColours(in TerminalCell cell)
    {
        var foreground = cell.Foreground >= 0 ? FromRgb(cell.Foreground) : DefaultForeground;
        var background = cell.Background >= 0 ? FromRgb(cell.Background) : DefaultBackground;

        if (cell.Flags.HasFlag(CellFlags.Inverse))
            (foreground, background) = (background, foreground);

        if (cell.Flags.HasFlag(CellFlags.Dim))
            foreground = Color.FromArgb(0xB0, foreground.R, foreground.G, foreground.B);

        return (foreground, background);
    }

    private static Color FromRgb(int rgb) =>
        Color.FromRgb((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));

    private FormattedText CreateText(string text, Typeface typeface, IBrush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, TerminalFontSize, brush);

    #region Input

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        _cursorOn = true;
        InvalidateVisual();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        InvalidateVisual();
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);

        if (string.IsNullOrEmpty(e.Text))
            return;

        // Control characters come from OnKeyDown; here only real text.
        if (e.Text.Length == 1 && char.IsControl(e.Text[0]) && e.Text[0] is not '\t')
            return;

        Input?.Invoke(e.Text);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

        // On macOS the clipboard lives on Cmd, and Ctrl+C has to stay available as the interrupt —
        // which is the whole reason this control exists.
        var clipboardModifier = OperatingSystem.IsMacOS()
            ? e.KeyModifiers.HasFlag(KeyModifiers.Meta)
            : control && shift;

        if (clipboardModifier && e.Key == Key.C)
        {
            CopyScreen();
            e.Handled = true;
            return;
        }

        if (clipboardModifier && e.Key == Key.V)
        {
            Paste();
            e.Handled = true;
            return;
        }

        var sequence = Translate(e.Key, control, shift, alt);
        if (sequence is null)
            return;

        Input?.Invoke(sequence);
        e.Handled = true;
    }

    /// <summary>
    /// Pastes the clipboard. Newlines become carriage returns because that is what a terminal expects
    /// from a keyboard, and bracketed paste is honoured when the application asked for it — without
    /// it an editor treats a pasted block as a burst of typing and auto-indents every line.
    /// </summary>
    public void Paste() => _ = PasteAsync();

    private async Task PasteAsync()
    {
        if (Clipboard is not { } clipboard)
            return;

        try
        {
            var text = await clipboard.GetTextAsync();
            if (string.IsNullOrEmpty(text))
                return;

            text = text.Replace("\r\n", "\r").Replace('\n', '\r');
            Input?.Invoke(_parser.BracketedPaste ? $"\x1b[200~{text}\x1b[201~" : text);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            // clipboard busy or unavailable; nothing useful to do
        }
    }

    public void CopyScreen() => _ = CopyScreenAsync();

    private async Task CopyScreenAsync()
    {
        if (Clipboard is not { } clipboard)
            return;

        try
        {
            await clipboard.SetTextAsync(GetScreenText());
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            // clipboard busy or unavailable
        }
    }

    /// <summary>
    /// The clipboard belongs to the window, so there is none until the control is in a visual tree —
    /// which is why every use above checks first rather than assuming.
    /// </summary>
    private IClipboard? Clipboard => TopLevel.GetTopLevel(this)?.Clipboard;

    private string? Translate(Key key, bool control, bool shift, bool alt)
    {
        var cursorPrefix = _parser.ApplicationCursorKeys ? "\x1bO" : "\x1b[";

        var sequence = key switch
        {
            Key.Enter => "\r",
            Key.Back => "\x7f",
            Key.Tab => shift ? "\x1b[Z" : "\t",
            Key.Escape => "\x1b",
            Key.Up => cursorPrefix + "A",
            Key.Down => cursorPrefix + "B",
            Key.Right => cursorPrefix + "C",
            Key.Left => cursorPrefix + "D",
            Key.Home => "\x1b[H",
            Key.End => "\x1b[F",
            Key.PageUp => "\x1b[5~",
            Key.PageDown => "\x1b[6~",
            Key.Insert => "\x1b[2~",
            Key.Delete => "\x1b[3~",
            Key.F1 => "\x1bOP",
            Key.F2 => "\x1bOQ",
            Key.F3 => "\x1bOR",
            Key.F4 => "\x1bOS",
            Key.F5 => "\x1b[15~",
            Key.F6 => "\x1b[17~",
            Key.F7 => "\x1b[18~",
            Key.F8 => "\x1b[19~",
            Key.F9 => "\x1b[20~",
            Key.F10 => "\x1b[21~",
            Key.F11 => "\x1b[23~",
            Key.F12 => "\x1b[24~",
            _ => null
        };

        if (sequence is not null)
            return alt ? "\x1b" + sequence : sequence;

        if (control)
        {
            if (key is >= Key.A and <= Key.Z)
                return ((char)(key - Key.A + 1)).ToString();

            return key switch
            {
                Key.Space => "\0",
                Key.OemOpenBrackets => "\x1b",
                Key.OemBackslash => "\x1c",
                Key.OemCloseBrackets => "\x1d",
                _ => null
            };
        }

        return null;
    }

    #endregion
}
