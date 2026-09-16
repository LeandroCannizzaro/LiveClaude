using LiveClaude.Core.Model;

namespace LiveClaude.App.ViewModels;

/// <summary>Editable form over a <see cref="SessionConfig"/>.</summary>
public sealed class SessionEditorViewModel : ObservableObject
{
    private string _id = "";
    private string _name = "";
    private string _directory = "";
    private bool _enabled = true;
    private bool _autoStart = true;
    private SpawnMode _spawn = SpawnMode.SameDir;
    private PermissionMode _permissionMode = PermissionMode.Default;
    private int _capacity = 32;
    private bool _createSessionInDir = true;
    private bool _sandbox;
    private bool _verbose;
    private string _model = "";
    private string _sessionNamePrefix = "";
    private bool _continuePrevious = true;
    private string _sessionId = "";
    private string _extraArgs = "";
    private bool _isNew = true;

    public static IReadOnlyList<SpawnMode> SpawnModes { get; } = Enum.GetValues<SpawnMode>();

    public static IReadOnlyList<PermissionMode> PermissionModes { get; } = Enum.GetValues<PermissionMode>();

    public string Id
    {
        get => _id;
        private set => SetProperty(ref _id, value);
    }

    public bool IsNew
    {
        get => _isNew;
        private set => SetProperty(ref _isNew, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Directory
    {
        get => _directory;
        set => SetProperty(ref _directory, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public bool AutoStart
    {
        get => _autoStart;
        set => SetProperty(ref _autoStart, value);
    }

    public SpawnMode Spawn
    {
        get => _spawn;
        set => SetProperty(ref _spawn, value);
    }

    public PermissionMode PermissionMode
    {
        get => _permissionMode;
        set => SetProperty(ref _permissionMode, value);
    }

    public int Capacity
    {
        get => _capacity;
        set => SetProperty(ref _capacity, Math.Clamp(value, 1, 32));
    }

    public bool CreateSessionInDir
    {
        get => _createSessionInDir;
        set => SetProperty(ref _createSessionInDir, value);
    }

    public bool Sandbox
    {
        get => _sandbox;
        set => SetProperty(ref _sandbox, value);
    }

    public bool Verbose
    {
        get => _verbose;
        set => SetProperty(ref _verbose, value);
    }

    public string Model
    {
        get => _model;
        set => SetProperty(ref _model, value);
    }

    public string SessionNamePrefix
    {
        get => _sessionNamePrefix;
        set => SetProperty(ref _sessionNamePrefix, value);
    }

    public bool ContinuePrevious
    {
        get => _continuePrevious;
        set => SetProperty(ref _continuePrevious, value);
    }

    public string SessionId
    {
        get => _sessionId;
        set => SetProperty(ref _sessionId, value);
    }

    public string ExtraArgs
    {
        get => _extraArgs;
        set => SetProperty(ref _extraArgs, value);
    }

    public string CommandPreview
    {
        get
        {
            var config = ToConfig();
            var args = Core.Claude.ClaudeArgs.BuildRemoteControl(config);

            // Quoted the way this platform's shell would, so what the preview shows is what someone
            // could paste into their own terminal — Windows and POSIX do not agree on that.
            return Abstractions.PlatformLoader.Current.Processes.FormatCommandLine(
                Abstractions.PlatformLoader.Current.Claude.ExecutableName, args);
        }
    }

    public void LoadNew()
    {
        Id = Guid.NewGuid().ToString("n");
        IsNew = true;
        Name = "";
        Directory = "";
        Enabled = true;
        AutoStart = true;
        Spawn = SpawnMode.SameDir;
        PermissionMode = PermissionMode.Default;
        Capacity = 32;
        CreateSessionInDir = true;
        Sandbox = false;
        Verbose = false;
        Model = "";
        SessionNamePrefix = "";
        ContinuePrevious = true;
        SessionId = "";
        ExtraArgs = "";
        OnPropertyChanged(nameof(CommandPreview));
    }

    public void LoadFrom(SessionConfig config)
    {
        Id = config.Id;
        IsNew = false;
        Name = config.Name;
        Directory = config.Directory;
        Enabled = config.Enabled;
        AutoStart = config.AutoStart;
        Spawn = config.Spawn;
        PermissionMode = config.PermissionMode;
        Capacity = config.Capacity;
        CreateSessionInDir = config.CreateSessionInDir;
        Sandbox = config.Sandbox;
        Verbose = config.Verbose;
        Model = config.Model ?? "";
        SessionNamePrefix = config.SessionNamePrefix ?? "";
        ContinuePrevious = config.ContinuePrevious;
        SessionId = config.SessionId ?? "";
        ExtraArgs = config.ExtraArgs;
        OnPropertyChanged(nameof(CommandPreview));
    }

    public SessionConfig ToConfig() => new()
    {
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("n") : Id,
        Name = Name.Trim(),
        Directory = Directory.Trim(),
        Enabled = Enabled,
        AutoStart = AutoStart,
        Spawn = Spawn,
        PermissionMode = PermissionMode,
        Capacity = Capacity,
        CreateSessionInDir = CreateSessionInDir,
        Sandbox = Sandbox,
        Verbose = Verbose,
        Model = string.IsNullOrWhiteSpace(Model) ? null : Model.Trim(),
        SessionNamePrefix = string.IsNullOrWhiteSpace(SessionNamePrefix) ? null : SessionNamePrefix.Trim(),
        ContinuePrevious = ContinuePrevious,
        SessionId = string.IsNullOrWhiteSpace(SessionId) ? null : SessionId.Trim(),
        ExtraArgs = ExtraArgs.Trim()
    };

    public void RefreshPreview() => OnPropertyChanged(nameof(CommandPreview));
}
