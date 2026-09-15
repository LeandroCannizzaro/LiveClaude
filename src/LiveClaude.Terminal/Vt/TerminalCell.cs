namespace LiveClaude.Terminal.Vt;

[Flags]
public enum CellFlags : byte
{
    None = 0,
    Bold = 1,
    Dim = 2,
    Italic = 4,
    Underline = 8,
    Inverse = 16,
    Hidden = 32,
    Strike = 64
}

/// <summary>One character cell: glyph plus resolved colours. -1 means "use the default colour".</summary>
public struct TerminalCell
{
    public char Char;
    public int Foreground;
    public int Background;
    public CellFlags Flags;

    public static readonly TerminalCell Empty = new() { Char = ' ', Foreground = -1, Background = -1, Flags = CellFlags.None };

    public bool SameStyle(in TerminalCell other) =>
        Foreground == other.Foreground && Background == other.Background && Flags == other.Flags;
}
