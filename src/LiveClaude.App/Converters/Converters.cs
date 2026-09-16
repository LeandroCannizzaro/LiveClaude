using System.Globalization;
using Avalonia.Data.Converters;
using LiveClaude.Core.Model;

namespace LiveClaude.App.Converters;

/// <summary>
/// Shows an element only when a collection is empty (the "nothing here yet" card).
///
/// Most of what the WPF version needed converters for is gone: Avalonia binds visibility to a plain
/// bool through IsVisible, so BoolToVisibility, InverseBoolToVisibility and StringToVisibility have
/// no reason to exist any more — the views bind IsVisible directly.
/// </summary>
public sealed class EmptyCountConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count == 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True when a string has content. Used where a whole panel hangs off one optional line.</summary>
public sealed class HasTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !string.IsNullOrWhiteSpace(value as string);

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
        SpawnMode.SameDir => "same-dir",
        SpawnMode.Worktree => "worktree",
        SpawnMode.Session => "session",
        PermissionMode.Default => "default",
        PermissionMode.AcceptEdits => "acceptEdits",
        PermissionMode.Auto => "auto",
        PermissionMode.BypassPermissions => "bypassPermissions",
        PermissionMode.DontAsk => "dontAsk",
        PermissionMode.Plan => "plan",
        _ => value?.ToString() ?? ""
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
