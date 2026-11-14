using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using LiveClaude.Core.Model;

namespace LiveClaude.App.Services;

public sealed record ThemeTokenDescriptor(string ColorKey, string BrushKey, string Label);

public sealed record ThemePreset(
    string Name,
    bool IsLight,
    IReadOnlyDictionary<string, string> Colors);

/// <summary>Manages predefined UI themes and custom per-token color overrides.</summary>
public static class ThemeService
{
    public static readonly IReadOnlyList<ThemeTokenDescriptor> Tokens =
    [
        new("BackgroundColor", "BackgroundBrush", "Window background"),
        new("SurfaceColor", "SurfaceBrush", "Card / panel surface"),
        new("SurfaceAltColor", "SurfaceAltBrush", "Alternate surface"),
        new("BorderColor", "BorderBrush", "Borders"),
        new("TextColor", "TextBrush", "Primary text"),
        new("MutedTextColor", "MutedTextBrush", "Muted / secondary text"),
        new("AccentColor", "AccentBrush", "Accent"),
        new("AccentHoverColor", "AccentHoverBrush", "Accent hover"),
        new("SuccessColor", "SuccessBrush", "Success"),
        new("WarningColor", "WarningBrush", "Warning"),
        new("DangerColor", "DangerBrush", "Danger"),
        new("TerminalBackgroundColor", "TerminalBackgroundBrush", "Terminal background")
    ];

    public static readonly IReadOnlyList<ThemePreset> Presets =
    [
        new(
            "Default Dark",
            IsLight: false,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["BackgroundColor"] = "#0E1013",
                ["SurfaceColor"] = "#161A20",
                ["SurfaceAltColor"] = "#1D222A",
                ["BorderColor"] = "#2A313B",
                ["TextColor"] = "#E7EAEE",
                ["MutedTextColor"] = "#9AA4B2",
                ["AccentColor"] = "#D97757",
                ["AccentHoverColor"] = "#E68B6D",
                ["SuccessColor"] = "#4ADE80",
                ["WarningColor"] = "#FBBF24",
                ["DangerColor"] = "#F87171",
                ["TerminalBackgroundColor"] = "#0C0C0C"
            }),
        new(
            "Light",
            IsLight: true,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["BackgroundColor"] = "#F3F4F6",
                ["SurfaceColor"] = "#FFFFFF",
                ["SurfaceAltColor"] = "#E5E7EB",
                ["BorderColor"] = "#D1D5DB",
                ["TextColor"] = "#111827",
                ["MutedTextColor"] = "#4B5563",
                ["AccentColor"] = "#C2410C",
                ["AccentHoverColor"] = "#EA580C",
                ["SuccessColor"] = "#15803D",
                ["WarningColor"] = "#B45309",
                ["DangerColor"] = "#B91C1C",
                ["TerminalBackgroundColor"] = "#111827"
            }),
        new(
            "Midnight Blue",
            IsLight: false,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["BackgroundColor"] = "#0B1120",
                ["SurfaceColor"] = "#111827",
                ["SurfaceAltColor"] = "#1E293B",
                ["BorderColor"] = "#334155",
                ["TextColor"] = "#F1F5F9",
                ["MutedTextColor"] = "#94A3B8",
                ["AccentColor"] = "#38BDF8",
                ["AccentHoverColor"] = "#7DD3FC",
                ["SuccessColor"] = "#34D399",
                ["WarningColor"] = "#FBBF24",
                ["DangerColor"] = "#FB7185",
                ["TerminalBackgroundColor"] = "#020617"
            }),
        new(
            "Forest Dark",
            IsLight: false,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["BackgroundColor"] = "#0B1310",
                ["SurfaceColor"] = "#121E19",
                ["SurfaceAltColor"] = "#192A23",
                ["BorderColor"] = "#284035",
                ["TextColor"] = "#E6F0EC",
                ["MutedTextColor"] = "#90ABA0",
                ["AccentColor"] = "#34D399",
                ["AccentHoverColor"] = "#6EE7B7",
                ["SuccessColor"] = "#4ADE80",
                ["WarningColor"] = "#FBBF24",
                ["DangerColor"] = "#F87171",
                ["TerminalBackgroundColor"] = "#070D0B"
            }),
        new(
            "High Contrast",
            IsLight: false,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["BackgroundColor"] = "#000000",
                ["SurfaceColor"] = "#0A0A0A",
                ["SurfaceAltColor"] = "#171717",
                ["BorderColor"] = "#737373",
                ["TextColor"] = "#FFFFFF",
                ["MutedTextColor"] = "#D4D4D4",
                ["AccentColor"] = "#FACC15",
                ["AccentHoverColor"] = "#FDE047",
                ["SuccessColor"] = "#22C55E",
                ["WarningColor"] = "#F59E0B",
                ["DangerColor"] = "#EF4444",
                ["TerminalBackgroundColor"] = "#000000"
            })
    ];

    public static ThemePreset GetPreset(string? name) =>
        Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? Presets[0];

    public static Dictionary<string, string> ResolveColors(ThemeSettings settings)
    {
        var preset = GetPreset(settings.PresetName);
        var resolved = new Dictionary<string, string>(preset.Colors, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in settings.CustomColors)
        {
            if (Color.TryParse(value, out _))
                resolved[key] = value;
        }

        return resolved;
    }

    public static void Apply(ThemeSettings settings)
    {
        var app = Application.Current;
        if (app is null)
            return;

        var preset = GetPreset(settings.PresetName);
        app.RequestedThemeVariant = preset.IsLight ? ThemeVariant.Light : ThemeVariant.Dark;

        var resolved = ResolveColors(settings);
        foreach (var token in Tokens)
        {
            if (!resolved.TryGetValue(token.ColorKey, out var hex) || !Color.TryParse(hex, out var color))
                continue;

            app.Resources[token.ColorKey] = color;
            app.Resources[token.BrushKey] = new SolidColorBrush(color);

            if (string.Equals(token.ColorKey, "AccentColor", StringComparison.OrdinalIgnoreCase))
            {
                app.Resources["SystemAccentColor"] = color;
                app.Resources["SystemAccentColorBrush"] = new SolidColorBrush(color);
            }
        }
    }
}
