using System.Collections.ObjectModel;
using Avalonia.Media;
using LiveClaude.App.Services;
using LiveClaude.Core.Model;

namespace LiveClaude.App.ViewModels;

public sealed class ThemeTokenItem : ObservableObject
{
    private readonly Action<string, Color> _onChanged;
    private Color _color;

    public ThemeTokenItem(ThemeTokenDescriptor descriptor, Color initialColor, Action<string, Color> onChanged)
    {
        Descriptor = descriptor;
        _color = initialColor;
        _onChanged = onChanged;
    }

    public ThemeTokenDescriptor Descriptor { get; }

    public string ColorKey => Descriptor.ColorKey;

    public string Label => Descriptor.Label;

    public Color Color
    {
        get => _color;
        set
        {
            if (SetProperty(ref _color, value))
            {
                OnPropertyChanged(nameof(Hex));
                _onChanged(ColorKey, value);
            }
        }
    }

    public string Hex => $"#{_color.R:X2}{_color.G:X2}{_color.B:X2}";

    internal void SetColorSilent(Color color)
    {
        if (SetProperty(ref _color, color, nameof(Color)))
            OnPropertyChanged(nameof(Hex));
    }
}

public sealed class ThemeEditorViewModel : ObservableObject
{
    private string _selectedPresetName = ThemeService.Presets[0].Name;
    private Dictionary<string, string> _customColors = new(StringComparer.OrdinalIgnoreCase);
    private bool _suppressUpdates;

    public ThemeEditorViewModel()
    {
        PresetNames = ThemeService.Presets.Select(p => p.Name).ToList();
        ResetToPresetCommand = new RelayCommand(_ => ResetToPreset());

        foreach (var token in ThemeService.Tokens)
        {
            var initial = ParseOrDefault(ThemeService.Presets[0].Colors[token.ColorKey]);
            Tokens.Add(new ThemeTokenItem(token, initial, OnTokenColorChanged));
        }
    }

    public IReadOnlyList<string> PresetNames { get; }

    public ObservableCollection<ThemeTokenItem> Tokens { get; } = new();

    public RelayCommand ResetToPresetCommand { get; }

    public string SelectedPresetName
    {
        get => _selectedPresetName;
        set
        {
            if (!SetProperty(ref _selectedPresetName, value) || _suppressUpdates)
                return;

            _customColors.Clear();
            SyncTokenColorsFromSettings();
            ApplyLive();
        }
    }

    public void LoadFrom(ThemeSettings settings)
    {
        _suppressUpdates = true;
        try
        {
            _selectedPresetName = ThemeService.GetPreset(settings.PresetName).Name;
            _customColors = new Dictionary<string, string>(settings.CustomColors, StringComparer.OrdinalIgnoreCase);
            OnPropertyChanged(nameof(SelectedPresetName));
            SyncTokenColorsFromSettings();
        }
        finally
        {
            _suppressUpdates = false;
        }

        ApplyLive();
    }

    public ThemeSettings ToSettings() => new()
    {
        PresetName = SelectedPresetName,
        CustomColors = new Dictionary<string, string>(_customColors, StringComparer.OrdinalIgnoreCase)
    };

    public void ResetToPreset()
    {
        _customColors.Clear();
        SyncTokenColorsFromSettings();
        ApplyLive();
    }

    private void OnTokenColorChanged(string key, Color color)
    {
        if (_suppressUpdates)
            return;

        _customColors[key] = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        ApplyLive();
    }

    private void SyncTokenColorsFromSettings()
    {
        _suppressUpdates = true;
        try
        {
            var resolved = ThemeService.ResolveColors(ToSettings());
            foreach (var item in Tokens)
            {
                if (resolved.TryGetValue(item.ColorKey, out var hex))
                    item.SetColorSilent(ParseOrDefault(hex));
            }
        }
        finally
        {
            _suppressUpdates = false;
        }
    }

    private void ApplyLive() => ThemeService.Apply(ToSettings());

    private static Color ParseOrDefault(string hex) =>
        Color.TryParse(hex, out var c) ? c : Colors.Black;
}
