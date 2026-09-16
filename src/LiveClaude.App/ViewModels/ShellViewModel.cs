using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using LiveClaude.Core.Claude;
using LiveClaude.Abstractions;
using LiveClaude.Core.Config;
using LiveClaude.Core.Ipc;
using LiveClaude.Core.Model;
using LiveClaude.Core.Supervision;

namespace LiveClaude.App.ViewModels;

/// <summary>
/// The application's single view model: owns the connection to the supervisor (service, scheduled
/// task or in-process), the instance list, the session editor and the settings.
/// </summary>
public sealed class ShellViewModel : ObservableObject, IAsyncDisposable
{
    private readonly Dispatcher _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _ticker;

    private ISupervisorApi? _api;
    private Supervisor? _localSupervisor;
    private AppConfig _config = new();
    private InstanceViewModel? _selectedInstance;
    private string _connectionText = "Connecting…";
    private string _claudeInfo = "";
    private bool _isConnected;
    private int _selectedTabIndex;

    public ShellViewModel()
    {
        Editor = new SessionEditorViewModel();
        Editor.LoadNew();
        Hosting = new HostingViewModel();
        Environments = new EnvironmentsViewModel(
            () => Instances.Select(i => i.Snapshot).ToList(),
            () => _config.Sessions);

        RefreshCommand = new RelayCommand(_ => RefreshAsync());
        ReconnectCommand = new RelayCommand(_ => ConnectAsync(force: true));
        StartCommand = new RelayCommand(p => WithInstance(p, id => _api!.StartInstanceAsync(id)));
        StopCommand = new RelayCommand(p => WithInstance(p, id => _api!.StopInstanceAsync(id)));
        RestartCommand = new RelayCommand(p => WithInstance(p, id => _api!.RestartInstanceAsync(id)));
        NewSessionCommand = new RelayCommand(_ =>
        {
            Editor.LoadNew();
            SelectedTabIndex = 1;
        });
        EditSessionCommand = new RelayCommand(p =>
        {
            if (p is InstanceViewModel vm && vm.Config is not null)
                Editor.LoadFrom(vm.Config);
            SelectedTabIndex = 1;
        });
        SaveSessionCommand = new RelayCommand(_ => SaveSessionAsync());
        DeleteSessionCommand = new RelayCommand(_ => DeleteSessionAsync());
        OpenSessionUrlCommand = new RelayCommand(p =>
        {
            if (p is string url && !string.IsNullOrWhiteSpace(url))
                OpenUrl(url);
        });
        OpenLogFolderCommand = new RelayCommand(_ => OpenUrl(ConfigStore.LogDirectory));
        OpenConfigCommand = new RelayCommand(_ => OpenUrl(new ConfigStore().Path));
        SaveSettingsCommand = new RelayCommand(_ => SaveSettingsAsync());

        _ticker = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _ticker.Tick += (_, _) =>
        {
            foreach (var instance in Instances)
                instance.Update(instance.Snapshot, instance.Config);
        };
        _ticker.Start();
    }

    public ObservableCollection<InstanceViewModel> Instances { get; } = new();

    public SessionEditorViewModel Editor { get; }

    public HostingViewModel Hosting { get; }

    public EnvironmentsViewModel Environments { get; }

    /// <summary>Shown in the footer; clicking it opens the About window.</summary>
    public string AppVersion => $"v{Views.AppInfo.Version}";

    public ISupervisorApi? Api => _api;

    public AppConfig Config => _config;

    public InstanceViewModel? SelectedInstance
    {
        get => _selectedInstance;
        set
        {
            if (SetProperty(ref _selectedInstance, value) && value?.Config is not null)
                Editor.LoadFrom(value.Config);
        }
    }

    public string ConnectionText
    {
        get => _connectionText;
        private set => SetProperty(ref _connectionText, value);
    }

    public string ClaudeInfo
    {
        get => _claudeInfo;
        private set => SetProperty(ref _claudeInfo, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set => SetProperty(ref _isConnected, value);
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    #region Settings surface

    public string ClaudePath
    {
        get => _config.ClaudePath ?? "";
        set
        {
            _config.ClaudePath = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            OnPropertyChanged();
        }
    }

    public int BackoffInitialSeconds
    {
        get => _config.Backoff.InitialSeconds;
        set { _config.Backoff.InitialSeconds = Math.Max(1, value); OnPropertyChanged(); }
    }

    public int BackoffMaxSeconds
    {
        get => _config.Backoff.MaxSeconds;
        set { _config.Backoff.MaxSeconds = Math.Max(1, value); OnPropertyChanged(); }
    }

    public int BackoffMaxRestarts
    {
        get => _config.Backoff.MaxRestarts;
        set { _config.Backoff.MaxRestarts = Math.Max(0, value); OnPropertyChanged(); }
    }

    public int LogMaxSizeMb
    {
        get => _config.LogMaxSizeMb;
        set { _config.LogMaxSizeMb = Math.Clamp(value, 1, 512); OnPropertyChanged(); }
    }

    public int GracefulStopSeconds
    {
        get => _config.GracefulStopSeconds;
        set { _config.GracefulStopSeconds = Math.Clamp(value, 1, 120); OnPropertyChanged(); }
    }

    public bool TrackEnvironments
    {
        get => _config.TrackEnvironments;
        set { _config.TrackEnvironments = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<ClaudeInstall> DetectedInstalls { get; private set; } = [];

    #endregion

    #region Commands

    public RelayCommand RefreshCommand { get; }
    public RelayCommand ReconnectCommand { get; }
    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand RestartCommand { get; }
    public RelayCommand NewSessionCommand { get; }
    public RelayCommand EditSessionCommand { get; }
    public RelayCommand SaveSessionCommand { get; }
    public RelayCommand DeleteSessionCommand { get; }
    public RelayCommand OpenSessionUrlCommand { get; }
    public RelayCommand OpenLogFolderCommand { get; }
    public RelayCommand OpenConfigCommand { get; }
    public RelayCommand SaveSettingsCommand { get; }

    #endregion

    /// <summary>Connects to a running supervisor, falling back to an in-process one.</summary>
    public async Task ConnectAsync(bool force = false)
    {
        if (_api is not null && force)
        {
            await _api.DisposeAsync();
            _api = null;

            if (_localSupervisor is not null)
            {
                await _localSupervisor.DisposeAsync();
                _localSupervisor = null;
            }
        }

        if (_api is not null)
            return;

        ConfigStore.EnsureDirectories();

        var client = new IpcSupervisorClient();
        if (await client.TryConnectAsync(TimeSpan.FromSeconds(2)))
        {
            _api = client;
            client.StartAutoReconnect(TimeSpan.FromSeconds(10));
        }
        else
        {
            await client.DisposeAsync();

            _localSupervisor = new Supervisor(new ConfigStore(), host: "in-process (app)");
            await _localSupervisor.StartAsync();
            _api = new LocalSupervisorApi(_localSupervisor);
        }

        _api.StatusChanged += OnStatusChanged;
        _api.ConnectionChanged += connected => _dispatcher.BeginInvoke(() =>
        {
            IsConnected = connected;
            if (!connected)
                ConnectionText = "Supervisor disconnected — reconnecting…";
        });

        IsConnected = true;
        await RefreshAsync();
        await Hosting.RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (_api is null)
            return;

        _config = await _api.GetConfigAsync();
        DetectedInstalls = ClaudeLocator.FindAll(_config.ClaudePath);

        var status = await _api.GetStatusAsync();
        OnStatusChanged(status);

        OnPropertyChanged(nameof(ClaudePath));
        OnPropertyChanged(nameof(BackoffInitialSeconds));
        OnPropertyChanged(nameof(BackoffMaxSeconds));
        OnPropertyChanged(nameof(BackoffMaxRestarts));
        OnPropertyChanged(nameof(LogMaxSizeMb));
        OnPropertyChanged(nameof(GracefulStopSeconds));
        OnPropertyChanged(nameof(TrackEnvironments));
        OnPropertyChanged(nameof(DetectedInstalls));
    }

    private void OnStatusChanged(SupervisorStatus status) => _dispatcher.BeginInvoke(() =>
    {
        ConnectionText = status.Host switch
        {
            "service" => "Supervised by the Windows service",
            "task" => "Supervised by the scheduled task",
            _ => "Supervised by this app (no service or task running)"
        };

        // Lets the hosting card say "running" even when Windows will not let us read the task back.
        Hosting.ConnectedSupervisorHost = status.Host;

        ClaudeInfo = status.ClaudePath is null
            ? "Claude CLI not found — set the path in Settings"
            : $"{status.ClaudePath}  ({status.ClaudeVersion ?? "version unknown"})";

        var byId = _config.Sessions.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var snapshot in status.Instances)
        {
            byId.TryGetValue(snapshot.Id, out var config);
            var existing = Instances.FirstOrDefault(i => i.Id == snapshot.Id);

            if (existing is null)
                Instances.Add(new InstanceViewModel(snapshot, config));
            else
                existing.Update(snapshot, config);
        }

        foreach (var stale in Instances.Where(i => status.Instances.All(s => s.Id != i.Id)).ToList())
            Instances.Remove(stale);

        if (SelectedInstance is null)
            SelectedInstance = Instances.FirstOrDefault();
    });

    private async Task SaveSessionAsync()
    {
        if (_api is null)
            return;

        var session = Editor.ToConfig();
        var error = session.Validate();
        if (error is not null)
        {
            MessageBox.Show(error, "Session not saved", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await _api.UpsertSessionAsync(session);
        Editor.LoadFrom(session);
        await RefreshAsync();
    }

    private async Task DeleteSessionAsync()
    {
        if (_api is null || Editor.IsNew)
            return;

        var confirm = MessageBox.Show(
            $"Remove '{Editor.Name}' from LiveClaude?\n\nThe Remote Control server stops; nothing is deleted on disk.",
            "Remove session",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
            return;

        await _api.DeleteSessionAsync(Editor.Id);
        Editor.LoadNew();
        await RefreshAsync();
    }

    private async Task SaveSettingsAsync()
    {
        if (_api is null)
            return;

        await _api.SaveSettingsAsync(_config);
        await RefreshAsync();
    }

    private async Task WithInstance(object? parameter, Func<string, Task> action)
    {
        var id = parameter as string ?? (parameter as InstanceViewModel)?.Id ?? SelectedInstance?.Id;
        if (id is null || _api is null)
            return;

        await action(id);
        await RefreshAsync();
    }

    /// <summary>
    /// Opens a path in the file manager or a URL in the browser. Which program that is belongs to the
    /// platform: Explorer, the desktop's own handler, or Finder.
    /// </summary>
    public static void OpenUrl(string target)
    {
        try
        {
            if (Directory.Exists(target) || File.Exists(target))
                PlatformLoader.Current.Shell.Reveal(target);
            else
                PlatformLoader.Current.Shell.OpenUrl(target);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            MessageBox.Show($"Could not open '{target}'.", "LiveClaude", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _ticker.Stop();

        if (_api is not null)
            await _api.DisposeAsync();

        if (_localSupervisor is not null)
        {
            await _localSupervisor.StopAllAsync();
            await _localSupervisor.DisposeAsync();
        }
    }
}
