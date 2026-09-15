using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LiveClaude.App.Converters;

/// <summary>Shows an element only when a collection is empty (the "nothing here yet" card).</summary>
public sealed class EmptyCountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Shows spawn and permission modes the way the CLI spells them, so the picker and the command
/// preview read the same.
/// </summary>
public sealed class CliLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        LiveClaude.Core.Model.SpawnMode.SameDir => "same-dir",
        LiveClaude.Core.Model.SpawnMode.Worktree => "worktree",
        LiveClaude.Core.Model.SpawnMode.Session => "session",
        LiveClaude.Core.Model.PermissionMode.Default => "default",
        LiveClaude.Core.Model.PermissionMode.AcceptEdits => "acceptEdits",
        LiveClaude.Core.Model.PermissionMode.Auto => "auto",
        LiveClaude.Core.Model.PermissionMode.BypassPermissions => "bypassPermissions",
        LiveClaude.Core.Model.PermissionMode.DontAsk => "dontAsk",
        LiveClaude.Core.Model.PermissionMode.Plan => "plan",
        _ => value?.ToString() ?? ""
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Shows an element when a string has content.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
