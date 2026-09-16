using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using LiveClaude.Core.Claude;
using LiveClaude.Core.Config;
using LiveClaude.Core.Logging;
using LiveClaude.Core.Model;

namespace LiveClaude.App.ViewModels;

public sealed class EnvironmentViewModel : ObservableObject
{
    private bool _isSelected;
    private string? _lastError;

    public EnvironmentViewModel(RemoteEnvironment environment, EnvironmentClassification classification)
    {
        Environment = environment;
        Usage = classification.Usage;
        InstanceName = classification.OwnerName;
        Note = classification.ProtectionNote;
    }

    public RemoteEnvironment Environment { get; }

    public EnvironmentUsage Usage { get; }

    public string? InstanceName { get; }

    /// <summary>Why this row cannot be deleted, spelled out instead of just greying the box.</summary>
    public string? Note { get; }

    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    public string Id => Environment.Id;

    public string Name => Environment.Name;

    public string Directory => Environment.Directory ?? "—";

    public string Machine => Environment.MachineName ?? "—";

    public string Created => Environment.CreatedUtc?.ToLocalTime().ToString("dd MMM HH:mm") ?? "—";

    public string Branch => string.IsNullOrWhiteSpace(Environment.Branch) ? "" : $"branch {Environment.Branch}";

    public string UsageText => Usage switch
    {
        EnvironmentUsage.InUse => InstanceName is null ? "live — protected" : $"live — {InstanceName}",
        EnvironmentUsage.Stale => "stale — no server for this directory",
        _ => Environment.IsBridge ? "not tracked by LiveClaude" : "not a Remote Control bridge"
    };

    public IBrush UsageBrush => Usage switch
    {
        EnvironmentUsage.InUse => new ImmutableSolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80)),
        EnvironmentUsage.Stale => new ImmutableSolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)),
        _ => new ImmutableSolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80))
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

    /// <summary>Why the last delete of this environment failed, shown on the row itself.</summary>
    public string? LastError
    {
        get => _lastError;
        set
        {
            if (SetProperty(ref _lastError, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(LastError);
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
    private readonly RollingLogWriter _log;
    private readonly Dictionary<string, string> _lastErrors = new(StringComparer.Ordinal);
    private readonly HashSet<string> _forceCandidates = new(StringComparer.Ordinal);

    private string _statusMessage = "Not loaded yet.";
    private string _failureSummary = "";
    private bool _busy;
    private bool _showUnrelated;

    public EnvironmentsViewModel(
        Func<IReadOnlyList<InstanceSnapshot>> instances,
        Func<IReadOnlyList<SessionConfig>> sessions)
    {
        _instances = instances;
        _sessions = sessions;

        ConfigStore.EnsureDirectories();
        _log = new RollingLogWriter(Path.Combine(ConfigStore.LogDirectory, "environments.log"), maxSizeMb: 4);

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

        DeleteSelectedCommand = new RelayCommand(_ => DeleteSelectedAsync(force: false));
        ForceDeleteCommand = new RelayCommand(_ => DeleteSelectedAsync(force: true), _ => CanForce);
        OpenLogCommand = new RelayCommand(_ => ShellViewModel.OpenUrl(_log.Path));
    }

    public ObservableCollection<EnvironmentViewModel> Items { get; } = new();

    public RelayCommand RefreshCommand { get; }
    public RelayCommand SelectStaleCommand { get; }
    public RelayCommand SelectDuplicatesCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
    public RelayCommand DeleteSelectedCommand { get; }
    public RelayCommand ForceDeleteCommand { get; }
    public RelayCommand OpenLogCommand { get; }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>What went wrong on the last delete, shown above the list rather than hidden in a log.</summary>
    public string FailureSummary
    {
        get => _failureSummary;
        private set
        {
            if (SetProperty(ref _failureSummary, value))
                OnPropertyChanged(nameof(HasFailures));
        }
    }

    public bool HasFailures => !string.IsNullOrWhiteSpace(FailureSummary);

    public bool Busy
    {
        get => _busy;
        private set => SetProperty(ref _busy, value);
    }

    /// <summary>True when at least one failure said the environment still has session records.</summary>
    public bool CanForce => _forceCandidates.Count > 0;

    public int ForceCandidateCount => _forceCandidates.Count;

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
            using var client = new EnvironmentsClient(log: Log);
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
                if (!environment.IsBridge && !ShowUnrelated)
                    continue;

                var classification = EnvironmentClassifier.Describe(environment, instances, sessions);
                var item = new EnvironmentViewModel(environment, classification);

                // Keep the explanation of a failed delete visible across a refresh.
                if (_lastErrors.TryGetValue(environment.Id, out var error))
                    item.LastError = error;

                item.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(EnvironmentViewModel.IsSelected))
                        UpdateCounts();
                };

                Items.Add(item);
            }

            // Environments that are gone no longer need their error kept around.
            foreach (var id in _lastErrors.Keys.Where(id => Items.All(i => i.Id != id)).ToList())
                _lastErrors.Remove(id);
            foreach (var id in _forceCandidates.Where(id => Items.All(i => i.Id != id)).ToList())
                _forceCandidates.Remove(id);

            UpdateCounts();
            var hidden = ordered.Count - Items.Count;
            StatusMessage = hidden > 0
                ? $"{Items.Count} Remote Control environment(s); {hidden} other environment(s) hidden."
                : $"{Items.Count} environment(s).";
        }
        catch (EnvironmentsApiException ex)
        {
            Items.Clear();
            StatusMessage = ex.Message;
            FailureSummary = Decorate(ex);
        }
        catch (Exception ex)
        {
            Items.Clear();
            StatusMessage = $"Could not reach the environments API: {ex.Message}";
            FailureSummary = StatusMessage;
            Log($"LIST failed: {ex}");
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task DeleteSelectedAsync(bool force)
    {
        var selected = force
            ? Items.Where(i => _forceCandidates.Contains(i.Id) && i.CanSelect).ToList()
            : Items.Where(i => i.IsSelected && i.CanSelect).ToList();

        if (selected.Count == 0)
        {
            StatusMessage = force ? "Nothing to force." : "Nothing selected.";
            return;
        }

        var names = string.Join(Environment.NewLine, selected.Take(12).Select(i => $"  • {i.Name}"));
        if (selected.Count > 12)
            names += $"{Environment.NewLine}  … and {selected.Count - 12} more";

        var question = force
            ? $"Force-delete {selected.Count} environment(s)?{Environment.NewLine}{Environment.NewLine}{names}" +
              $"{Environment.NewLine}{Environment.NewLine}They still have session records attached — servers that were killed rather than stopped. " +
              "Forcing deletes those sessions together with the environment. This cannot be undone."
            : $"Permanently delete {selected.Count} environment(s) from your Claude account?{Environment.NewLine}{Environment.NewLine}{names}" +
              $"{Environment.NewLine}{Environment.NewLine}This cannot be undone. Servers running right now are never included.";

        if (!await Dialogs.ConfirmAsync(question, force ? "Force delete" : "Delete environments"))
            return;

        Busy = true;
        var deleted = 0;
        var failures = new List<string>();

        Log($"--- delete run: {selected.Count} environment(s), force={force}");

        try
        {
            using var client = new EnvironmentsClient(log: Log);

            foreach (var item in selected)
            {
                try
                {
                    await client.DeleteAsync(item.Id, force);
                    deleted++;
                    _lastErrors.Remove(item.Id);
                    _forceCandidates.Remove(item.Id);
                    item.LastError = null;
                }
                catch (EnvironmentsApiException ex)
                {
                    var detail = Decorate(ex);
                    failures.Add($"{item.Name}: {detail}");
                    _lastErrors[item.Id] = detail;
                    item.LastError = detail;

                    if (ex.RequiresForce)
                        _forceCandidates.Add(item.Id);
                    else
                        _forceCandidates.Remove(item.Id);
                }
                catch (Exception ex)
                {
                    var detail = ex.Message;
                    failures.Add($"{item.Name}: {detail}");
                    _lastErrors[item.Id] = detail;
                    item.LastError = detail;
                    Log($"DELETE {item.Id} threw: {ex}");
                }
            }
        }
        finally
        {
            Busy = false;
        }

        Log($"--- delete run finished: {deleted} deleted, {failures.Count} failed.");

        var attempted = selected.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        await RefreshAsync();

        // A server that is still running registers again within seconds, which looks exactly like
        // "the delete did nothing". Say so instead of leaving the user guessing.
        var reappeared = Items.Where(i => attempted.Contains(i.Id)).ToList();

        if (deleted > 0 && reappeared.Count > 0)
        {
            var note = $"Deleted {deleted}, but {reappeared.Count} came back: a server is still running for that " +
                       "directory and registers again after every delete. Stop the session in the Dashboard first.";
            failures.Insert(0, note);
            Log($"[warning] {reappeared.Count} environment(s) re-registered right after deletion.");
        }

        FailureSummary = failures.Count == 0
            ? ""
            : string.Join(Environment.NewLine, failures);

        StatusMessage = failures.Count == 0
            ? $"Deleted {deleted} environment(s)."
            : $"Deleted {deleted}, {failures.Count} issue(s) — details below. Full log: {_log.Path}";

        UpdateCounts();
    }

    private static string Decorate(EnvironmentsApiException ex) =>
        ex.RequestId is null ? ex.Message : $"{ex.Message} (request-id {ex.RequestId})";

    private void Log(string line) => _log.Write(line);

    public int StaleCount => Items.Count(i => i.Usage == EnvironmentUsage.Stale);

    public int SelectedCount => Items.Count(i => i.IsSelected);

    private void UpdateCounts()
    {
        OnPropertyChanged(nameof(StaleCount));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(CanForce));
        OnPropertyChanged(nameof(ForceCandidateCount));
    }
}
