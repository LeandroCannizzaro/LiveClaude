using Avalonia.Media;
using Avalonia.Media.Immutable;
using LiveClaude.Core.Model;

namespace LiveClaude.App.ViewModels;

public sealed class InstanceViewModel : ObservableObject
{
    private InstanceSnapshot _snapshot;
    private SessionConfig? _config;

    public InstanceViewModel(InstanceSnapshot snapshot, SessionConfig? config)
    {
        _snapshot = snapshot;
        _config = config;
    }

    public string Id => _snapshot.Id;

    public InstanceSnapshot Snapshot => _snapshot;

    public SessionConfig? Config => _config;

    public string Name => _snapshot.Name;

    public string Directory => _snapshot.Directory;

    public InstanceState State => _snapshot.State;

    public string StateText => _snapshot.State switch
    {
        InstanceState.Running => "Running",
        InstanceState.Starting => "Starting",
        InstanceState.NeedsAttention => "Needs attention",
        InstanceState.Backoff => _snapshot.NextRetryUtc is { } retry
            ? $"Restarting in {Math.Max(0, (retry - DateTimeOffset.UtcNow).TotalSeconds):F0}s"
            : "Restarting",
        InstanceState.Failed => "Failed",
        InstanceState.Disabled => "Disabled",
        _ => "Stopped"
    };

    public IBrush StateBrush => _snapshot.State switch
    {
        InstanceState.Running => new ImmutableSolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80)),
        InstanceState.Starting => new ImmutableSolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA)),
        InstanceState.NeedsAttention => new ImmutableSolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)),
        InstanceState.Backoff => new ImmutableSolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)),
        InstanceState.Failed => new ImmutableSolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71)),
        _ => new ImmutableSolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80))
    };

    public string ProductBadge =>
        string.IsNullOrWhiteSpace(_snapshot.ProductDisplayName)
            ? _snapshot.ProductId
            : _snapshot.ProductDisplayName;

    public string Details
    {
        get
        {
            var parts = new List<string>();

            parts.Add(ProductBadge);

            if (_snapshot.ProcessId is { } pid)
                parts.Add($"pid {pid}");

            if (_snapshot.Uptime is { } uptime)
                parts.Add($"up {Format(uptime)}");

            if (_snapshot.RestartCount > 0)
                parts.Add($"{_snapshot.RestartCount} restart(s)");

            if (_config is not null &&
                string.Equals(_config.ProductId, "claude", StringComparison.OrdinalIgnoreCase))
            {
                var spawn = _config.Spawn switch
                {
                    SpawnMode.Worktree => "worktree",
                    SpawnMode.Session => "session",
                    _ => "same-dir"
                };

                parts.Add($"{spawn} · capacity {_config.Capacity} · {_config.PermissionMode}");
            }
            else if (_config is not null &&
                     string.Equals(_config.ProductId, "cursor", StringComparison.OrdinalIgnoreCase))
            {
                var dirs = 1 + _config.Cursor.ExtraWorkerDirs.Count;
                parts.Add($"{dirs} worker-dir(s)");
            }

            return string.Join("   ·   ", parts);
        }
    }

    public string? SessionUrl => _snapshot.SessionUrl;

    public bool HasSessionUrl => !string.IsNullOrWhiteSpace(_snapshot.SessionUrl);

    public string? Message => _snapshot.AttentionReason ?? _snapshot.LastError ?? _snapshot.LastOutput;

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    public IReadOnlyList<WorktreeInfo> Worktrees => _snapshot.Worktrees;

    public int BranchCount => Worktrees
        .Select(w => w.Branch)
        .Where(b => !string.IsNullOrEmpty(b))
        .Distinct(StringComparer.Ordinal)
        .Count();

    /// <summary>A plain single-branch checkout carries nothing worth showing.</summary>
    public bool HasMultipleWorktrees => Worktrees.Count > 1;

    public string WorktreeSummary =>
        $"{Worktrees.Count} worktrees · {BranchCount} branch{(BranchCount == 1 ? "" : "es")}";

    public bool CanStart => _snapshot.State is InstanceState.Stopped or InstanceState.Failed or InstanceState.Disabled;

    public bool CanStop => _snapshot.State is not InstanceState.Stopped and not InstanceState.Disabled;

    public void Update(InstanceSnapshot snapshot, SessionConfig? config)
    {
        _snapshot = snapshot;
        _config = config;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Directory));
        OnPropertyChanged(nameof(ProductBadge));
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(StateBrush));
        OnPropertyChanged(nameof(Details));
        OnPropertyChanged(nameof(SessionUrl));
        OnPropertyChanged(nameof(HasSessionUrl));
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(HasMessage));
        OnPropertyChanged(nameof(Worktrees));
        OnPropertyChanged(nameof(BranchCount));
        OnPropertyChanged(nameof(HasMultipleWorktrees));
        OnPropertyChanged(nameof(WorktreeSummary));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(Snapshot));
        OnPropertyChanged(nameof(Config));
    }

    private static string Format(TimeSpan value) =>
        value.TotalDays >= 1 ? $"{value.Days}d {value.Hours}h"
        : value.TotalHours >= 1 ? $"{value.Hours}h {value.Minutes}m"
        : value.TotalMinutes >= 1 ? $"{value.Minutes}m {value.Seconds}s"
        : $"{value.Seconds}s";

    public override string ToString() => Name;
}
