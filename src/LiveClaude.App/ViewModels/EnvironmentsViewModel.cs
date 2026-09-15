using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using LiveClaude.Core.Claude;
using LiveClaude.Core.Model;

namespace LiveClaude.App.ViewModels;

public sealed class EnvironmentViewModel : ObservableObject
{
    private bool _isSelected;

    public EnvironmentViewModel(RemoteEnvironment environment, EnvironmentUsage usage, string? instanceName)
    {
        Environment = environment;
        Usage = usage;
        InstanceName = instanceName;
    }

    public RemoteEnvironment Environment { get; }

    public EnvironmentUsage Usage { get; }

    public string? InstanceName { get; }

    public string Id => Environment.Id;

    public string Name => Environment.Name;

    public string Directory => Environment.Directory ?? "—";

    public string Machine => Environment.MachineName ?? "—";

    public string Created => Environment.CreatedUtc?.ToLocalTime().ToString("dd MMM HH:mm") ?? "—";

    public string Branch => string.IsNullOrWhiteSpace(Environment.Branch) ? "" : $"branch {Environment.Branch}";

    public string UsageText => Usage switch
    {
        EnvironmentUsage.InUse => InstanceName is null ? "live" : $"live — {InstanceName}",
        EnvironmentUsage.Stale => "stale — no server for this directory",
        _ => Environment.IsBridge ? "not tracked by LiveClaude" : "not a Remote Control bridge"
    };

    public Brush UsageBrush => Usage switch
    {
        EnvironmentUsage.InUse => new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80)),
        EnvironmentUsage.Stale => new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)),
        _ => new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80))
    };

    /// <summary>A live environment can never be selected: deleting it would cut the running server loose.</summary>
    public bool CanSelect => Usage != EnvironmentUsage.InUse;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (CanSelect)
                SetProperty(ref _isSelected, value);
        }
    }
}

/// <summary>
/// Lists the bridge environments registered on the account and removes the dead ones. Each
/// <c>claude remote-control</c> process registers one and nothing cleans it up, so the session
/// picker ends up showing the same project several times over.
/// </summary>
public sealed class EnvironmentsViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<InstanceSnapshot>> _instances;
    private readonly Func<IReadOnlyList<SessionConfig>> _sessions;

    private string _statusMessage = "Not loaded yet.";
    private bool _busy;
    private bool _showUnrelated;

    public EnvironmentsViewModel(
        Func<IReadOnlyList<InstanceSnapshot>> instances,
        Func<IReadOnlyList<SessionConfig>> sessions)
    {
        _instances = instances;
        _sessions = sessions;

        RefreshCommand = new RelayCommand(_ => RefreshAsync());
        SelectStaleCommand = new RelayCommand(_ =>
        {
            foreach (var item in Items)
                item.IsSelected = item.Usage == EnvironmentUsage.Stale;
            UpdateCounts();
        });

        // The everyday case: one directory, several registrations, only the newest worth keeping.
        SelectDuplicatesCommand = new RelayCommand(_ =>
        {
            foreach (var item in Items)
                item.IsSelected = false;

            var duplicates = EnvironmentClassifier
                .FindDuplicates(Items.Select(i => i.Environment), _instances())
                .Select(e => e.Id)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var item in Items.Where(i => duplicates.Contains(i.Id)))
                item.IsSelected = true;

            UpdateCounts();
            StatusMessage = SelectedCount == 0
                ? "No duplicates: every directory has a single environment."
                : $"Selected {SelectedCount} duplicate(s); the newest (or live) one per directory is kept.";
        });
        ClearSelectionCommand = new RelayCommand(_ =>
        {
            foreach (var item in Items)
                item.IsSelected = false;
            UpdateCounts();
        });
        DeleteSelectedCommand = new RelayCommand(_ => DeleteSelectedAsync());
    }

    public ObservableCollection<EnvironmentViewModel> Items { get; } = new();

    public RelayCommand RefreshCommand { get; }
    public RelayCommand SelectStaleCommand { get; }
    public RelayCommand SelectDuplicatesCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
    public RelayCommand DeleteSelectedCommand { get; }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool Busy
    {
        get => _busy;
        private set => SetProperty(ref _busy, value);
    }

    /// <summary>
    /// Bridge environments are always listed — they are the ones that pile up. Cloud and default
    /// environments have nothing to do with Remote Control, so they stay hidden unless asked for.
    /// </summary>
    public bool ShowUnrelated
    {
        get => _showUnrelated;
        set
        {
            if (SetProperty(ref _showUnrelated, value))
                _ = RefreshAsync();
        }
    }

    public async Task RefreshAsync()
    {
        Busy = true;
        try
        {
            using var client = new EnvironmentsClient();
            var environments = await client.ListAsync();

            var instances = _instances();
            var sessions = _sessions();

            Items.Clear();

            // Group by directory so the duplicates of one project sit together, newest first.
            var ordered = environments
                .OrderBy(e => e.Directory ?? "￿", StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(e => e.CreatedUtc ?? DateTimeOffset.MinValue)
                .ToList();

            foreach (var environment in ordered)
            {
                var usage = EnvironmentClassifier.Classify(environment, instances, sessions);
                if (!environment.IsBridge && !ShowUnrelated)
                    continue;

                var instanceName = instances
                    .FirstOrDefault(i => string.Equals(i.EnvironmentId, environment.Id, StringComparison.Ordinal))?.Name;

                var item = new EnvironmentViewModel(environment, usage, instanceName);
                item.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(EnvironmentViewModel.IsSelected))
                        UpdateCounts();
                };

                Items.Add(item);
            }

            UpdateCounts();
            var hidden = ordered.Count - Items.Count;
            StatusMessage = hidden > 0
                ? $"{Items.Count} Remote Control environment(s); {hidden} other environment(s) hidden."
                : $"{Items.Count} environment(s).";
        }
        catch (EnvironmentsClient.EnvironmentsException ex)
        {
            Items.Clear();
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            Items.Clear();
            StatusMessage = $"Could not reach the environments API: {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task DeleteSelectedAsync()
    {
        var selected = Items.Where(i => i.IsSelected && i.CanSelect).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "Nothing selected.";
            return;
        }

        var names = string.Join(Environment.NewLine, selected.Take(12).Select(i => $"  • {i.Name}"));
        if (selected.Count > 12)
            names += $"{Environment.NewLine}  … and {selected.Count - 12} more";

        var confirm = MessageBox.Show(
            $"Permanently delete {selected.Count} environment(s) from your Claude account?{Environment.NewLine}{Environment.NewLine}{names}" +
            $"{Environment.NewLine}{Environment.NewLine}This cannot be undone. Servers running right now are never included.",
            "Delete environments",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        Busy = true;
        var deleted = 0;
        var failures = new List<string>();

        try
        {
            using var client = new EnvironmentsClient();
            foreach (var item in selected)
            {
                try
                {
                    await client.DeleteAsync(item.Id);
                    deleted++;
                }
                catch (Exception ex)
                {
                    failures.Add($"{item.Name}: {ex.Message}");
                }
            }
        }
        finally
        {
            Busy = false;
        }

        StatusMessage = failures.Count == 0
            ? $"Deleted {deleted} environment(s)."
            : $"Deleted {deleted}, {failures.Count} failed — {failures[0]}";

        await RefreshAsync();
    }

    public int StaleCount => Items.Count(i => i.Usage == EnvironmentUsage.Stale);

    public int SelectedCount => Items.Count(i => i.IsSelected);

    private void UpdateCounts()
    {
        OnPropertyChanged(nameof(StaleCount));
        OnPropertyChanged(nameof(SelectedCount));
    }
}
