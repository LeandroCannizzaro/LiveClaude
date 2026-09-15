using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LiveClaude.Terminal.Vt;

namespace LiveClaude.Terminal.Controls;

/// <summary>
/// A terminal surface: renders a <see cref="TerminalScreen"/> and turns keystrokes into the bytes a
/// pseudo console expects. Pure WPF drawing, no browser and no native control.
/// </summary>
public sealed class TerminalView : FrameworkElement
{
    private readonly TerminalScreen _screen;
    private readonly VtParser _parser;
    private readonly DispatcherTimer _renderTimer;
    private readonly DispatcherTimer _blinkTimer;

    private Typeface _typeface = null!;
    private Typeface _boldTypeface = null!;
    private double _cellWidth = 8;
    private double _cellHeight = 16;
    private long _renderedRevision = -1;
    private bool _cursorOn = true;

    public TerminalView()
    {
        _screen = new TerminalScreen(120, 30);
        _parser = new VtParser(_screen);
        _parser.Respond += text => Input?.Invoke(text);
        _parser.TitleChanged += title => TitleChanged?.Invoke(title);

        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        SnapsToDevicePixels = true;

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

    public static readonly DependencyProperty TerminalFontFamilyProperty = DependencyProperty.Register(
        nameof(TerminalFontFamily), typeof(string), typeof(TerminalView),
        new FrameworkPropertyMetadata("Cascadia Mono, Consolas, Courier New", FrameworkPropertyMetadataOptions.AffectsRender, OnFontChanged));

    public static readonly DependencyProperty TerminalFontSizeProperty = DependencyProperty.Register(
        nameof(TerminalFontSize), typeof(double), typeof(TerminalView),
        new FrameworkPropertyMetadata(13.0, FrameworkPropertyMetadataOptions.AffectsRender, OnFontChanged));

    public static readonly DependencyProperty DefaultForegroundProperty = DependencyProperty.Register(
        nameof(DefaultForeground), typeof(Color), typeof(TerminalView),
        new FrameworkPropertyMetadata(Color.FromRgb(0xE6, 0xE6, 0xE6), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DefaultBackgroundProperty = DependencyProperty.Register(
        nameof(DefaultBackground), typeof(Color), typeof(TerminalView),
        new FrameworkPropertyMetadata(Color.FromRgb(0x0C, 0x0C, 0x0C), FrameworkPropertyMetadataOptions.AffectsRender));

    public string TerminalFontFamily
    {
        get => (string)GetValue(TerminalFontFamilyProperty);
        set => SetValue(TerminalFontFamilyProperty, value);
    }

    public double TerminalFontSize
    {
        get => (double)GetValue(TerminalFontSizeProperty);
        set => SetValue(TerminalFontSizeProperty, value);
    }

    public Color DefaultForeground
    {
        get => (Color)GetValue(DefaultForegroundProperty);
        set => SetValue(DefaultForegroundProperty, value);
    }

    public Color DefaultBackground
    {
        get => (Color)GetValue(DefaultBackgroundProperty);
        set => SetValue(DefaultBackgroundProperty, value);
    }

    public int Columns => _screen.Columns;

    public int Rows => _screen.Rows;

    /// <summary>Text the user typed, ready to be written to the pseudo console.</summary>
    public event Action<string>? Input;

    /// <summary>Raised when the grid size changes, so the host can resize the pseudo console.</summary>
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

    private static void OnFontChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TerminalView view)
        {
            view.UpdateTypeface();
            view.UpdateGridSize(view.RenderSize);
        }
    }

    private void UpdateTypeface()
    {
        var family = new FontFamily(TerminalFontFamily);
        _typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        _boldTypeface = new Typeface(family, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        var probe = CreateText("M", _typeface, Brushes.White);
        _cellWidth = Math.Max(1, probe.WidthIncludingTrailingWhitespace);
        _cellHeight = Math.Max(1, Math.Ceiling(probe.Height));
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        UpdateGridSize(info.NewSize);
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

    protected override void OnRender(DrawingContext dc)
    {
        _renderedRevision = _screen.Revision;

        var defaultBackground = new SolidColorBrush(DefaultBackground);
        defaultBackground.Freeze();
        dc.DrawRectangle(defaultBackground, null, new Rect(RenderSize));

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

            while (column < _screen.Columns && _screen[row, column].SameStyle(cell))
            {
                var current = _screen[row, column];
                text.Append(current.Char == '\0' ? ' ' : current.Char);
                column++;
            }

            var (foreground, background) = ResolveColours(cell);
            var rect = new Rect(start * _cellWidth, row * _cellHeight, (column - start) * _cellWidth, _cellHeight);

            if (background != DefaultBackground)
            {
                var brush = new SolidColorBrush(background);
                brush.Freeze();
                dc.DrawRectangle(brush, null, rect);
            }

            var content = text.ToString();
            if (!string.IsNullOrWhiteSpace(content) && !cell.Flags.HasFlag(CellFlags.Hidden))
            {
                var brush = new SolidColorBrush(foreground);
                brush.Freeze();

                var typeface = cell.Flags.HasFlag(CellFlags.Bold) ? _boldTypeface : _typeface;
                var formatted = CreateText(content, typeface, brush);
                dc.DrawText(formatted, new Point(rect.X, rect.Y));

                if (cell.Flags.HasFlag(CellFlags.Underline))
                {
                    var y = rect.Bottom - 1.5;
                    var pen = new Pen(brush, 1);
                    pen.Freeze();
                    dc.DrawLine(pen, new Point(rect.X, y), new Point(rect.Right, y));
                }
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

        var brush = new SolidColorBrush(DefaultForeground) { Opacity = IsFocused ? 0.85 : 0.35 };
        brush.Freeze();

        if (IsFocused)
        {
            dc.DrawRectangle(brush, null, rect);

            var cell = _screen[_screen.CursorRow, _screen.CursorColumn];
            if (cell.Char is not ' ' and not '\0')
            {
                var textBrush = new SolidColorBrush(DefaultBackground);
                textBrush.Freeze();
                dc.DrawText(CreateText(cell.Char.ToString(), _typeface, textBrush), new Point(rect.X, rect.Y));
            }
        }
        else
        {
            var pen = new Pen(brush, 1);
            pen.Freeze();
            dc.DrawRectangle(null, pen, rect);
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

    private FormattedText CreateText(string text, Typeface typeface, Brush brush) =>
        new(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            TerminalFontSize,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    #region Input

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
    }

    protected override void OnGotFocus(RoutedEventArgs e)
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

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);

        if (string.IsNullOrEmpty(e.Text))
            return;

        // Control characters are produced by OnPreviewKeyDown; here only real text.
        if (e.Text.Length == 1 && char.IsControl(e.Text[0]) && e.Text[0] is not '\t')
            return;

        Input?.Invoke(e.Text);
        e.Handled = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        var control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;

        // Ctrl+Shift+C / Ctrl+Shift+V follow the Windows Terminal convention.
        if (control && shift && e.Key == Key.C)
        {
            CopyScreen();
            e.Handled = true;
            return;
        }

        if (control && shift && e.Key == Key.V)
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

    public void Paste()
    {
        if (!Clipboard.ContainsText())
            return;

        var text = Clipboard.GetText().Replace("\r\n", "\r").Replace('\n', '\r');
        Input?.Invoke(_parser.BracketedPaste ? $"\x1b[200~{text}\x1b[201~" : text);
    }

    public void CopyScreen()
    {
        try
        {
            Clipboard.SetText(GetScreenText());
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // clipboard busy; nothing useful to do
        }
    }

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
