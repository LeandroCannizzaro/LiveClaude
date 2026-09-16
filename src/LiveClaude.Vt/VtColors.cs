namespace LiveClaude.Vt;

/// <summary>The xterm 256-colour palette, packed as 0xRRGGBB.</summary>
public static class VtColors
{
    private static readonly int[] Palette = BuildPalette();

    public static int ToRgb(int index) =>
        index is >= 0 and < 256 ? Palette[index] : -1;

    public static int FromRgb(int r, int g, int b) =>
        (Math.Clamp(r, 0, 255) << 16) | (Math.Clamp(g, 0, 255) << 8) | Math.Clamp(b, 0, 255);

    private static int[] BuildPalette()
    {
        var palette = new int[256];

        // 0-15: the standard ANSI colours, tuned to match the Windows Terminal "Campbell" scheme.
        int[] basic =
        [
            0x0C0C0C, 0xC50F1F, 0x13A10E, 0xC19C00, 0x0037DA, 0x881798, 0x3A96DD, 0xCCCCCC,
            0x767676, 0xE74856, 0x16C60C, 0xF9F1A5, 0x3B78FF, 0xB4009E, 0x61D6D6, 0xF2F2F2
        ];
        Array.Copy(basic, palette, basic.Length);

        // 16-231: 6x6x6 colour cube.
        int[] steps = [0, 95, 135, 175, 215, 255];
        var index = 16;
        foreach (var r in steps)
        foreach (var g in steps)
        foreach (var b in steps)
            palette[index++] = (r << 16) | (g << 8) | b;

        // 232-255: greyscale ramp.
        for (var i = 0; i < 24; i++)
        {
            var level = 8 + i * 10;
            palette[232 + i] = (level << 16) | (level << 8) | level;
        }

        return palette;
    }
}
