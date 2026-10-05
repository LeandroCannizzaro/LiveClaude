using LiveClaude.Abstractions;
using LiveClaude.Core.Model;
using LiveClaude.Core.Products;

namespace LiveClaude.App.ViewModels;

/// <summary>Editable form over a <see cref="SessionConfig"/>, with product-specific panels.</summary>
public sealed class SessionEditorViewModel : ObservableObject
{
    private string _id = "";
    private string _productId = ProductIds.Claude;
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
    private string _extraWorkerDirs = "";
    private bool _cursorVerbose;
    private bool _cursorDebug;
    private string _cursorApiKeyPath = "";
    private string _cursorAuthTokenFile = "";
    private bool _runLoginInTerminal;
    private bool _isNew = true;

    public static IReadOnlyList<SpawnMode> SpawnModes { get; } = Enum.GetValues<SpawnMode>();

    public static IReadOnlyList<PermissionMode> PermissionModes { get; } = Enum.GetValues<PermissionMode>();

    public IReadOnlyList<IAgentProduct> AvailableProducts => ProductHost.All;

    public IAgentProduct? SelectedProduct
    {
        get => AvailableProducts.FirstOrDefault(p =>
            string.Equals(p.Id, ProductId, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value is null)
                return;
            ProductId = value.Id;
            OnPropertyChanged();
        }
    }

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

    public string ProductId
    {
        get => _productId;
        set
        {
            if (!SetProperty(ref _productId, value))
                return;
            OnPropertyChanged(nameof(SelectedProduct));
            OnPropertyChanged(nameof(IsClaudeProduct));
            OnPropertyChanged(nameof(IsCursorProduct));
            OnPropertyChanged(nameof(ProductHelpText));
            OnPropertyChanged(nameof(NameLabel));
            OnPropertyChanged(nameof(DirectoryLabel));
            OnPropertyChanged(nameof(CommandPreview));
        }
    }

    public bool IsClaudeProduct =>
        string.Equals(ProductId, ProductIds.Claude, StringComparison.OrdinalIgnoreCase);

    public bool IsCursorProduct =>
        string.Equals(ProductId, ProductIds.Cursor, StringComparison.OrdinalIgnoreCase);

    public string ProductHelpText => IsCursorProduct
        ? "Cursor My Machines: one LiveClaude session is one long-lived worker. Name becomes --name; the project directory is the first --worker-dir."
        : "These map one-to-one onto the flags of claude remote-control. The command preview at the bottom shows exactly what the supervisor will run.";

    public string NameLabel => IsCursorProduct
        ? "Worker name (--name)"
        : "Name (shown at claude.ai/code)";

    public string DirectoryLabel => IsCursorProduct
        ? "Primary worker directory (--worker-dir)"
        : "Project directory";

    public string Name
    {
        get => _name;
        set { if (SetProperty(ref _name, value)) OnPropertyChanged(nameof(CommandPreview)); }
    }

    public string Directory
    {
        get => _directory;
        set { if (SetProperty(ref _directory, value)) OnPropertyChanged(nameof(CommandPreview)); }
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
        set { if (SetProperty(ref _spawn, value)) OnPropertyChanged(nameof(CommandPreview)); }
    }

    public PermissionMode PermissionMode
    {
        get => _permissionMode;
        set { if (SetProperty(ref _permissionMode, value)) OnPropertyChanged(nameof(CommandPreview)); }
    }

    public int Capacity
    {
        get => _capacity;
        set { if (SetProperty(ref _capacity, Math.Clamp(value, 1, 32))) OnPropertyChanged(nameof(CommandPreview)); }
    }

    public bool CreateSessionInDir
    {
        get => _createSessionInDir;
        set { if (SetProperty(ref _createSessionInDir, value)) OnPropertyChanged(nameof(CommandPreview)); }
    }

    public bool Sandbox
    {
        get => _sandbox;
        set { if (SetProperty(ref _sandbox, value)) OnPropertyChanged(nameof(CommandPreview)); }
    }

    public bool Verbose
    {
        get => _verbose;
        set { if (SetProperty(ref _verbose, value)) OnPropertyChanged(nameof(CommandPreview)); }
    }

    public string Model
    {
        get => _model;
        set { if (SetProperty(ref _model, value)) OnPropertyChanged(nameof(CommandPreview)); }
    }

    public string SessionNamePrefix
    {
        get => _sessionNamePrefix;
        set { if (SetProperty(ref _sessionNamePrefix, value)) OnPropertyChanged(nameof(CommandPreview)); }
    }

    public bool ContinuePrevious
    {
        get => _continuePrevious;
        set { if (SetProperty(ref _continuePrevious, value)) OnPropertyChanged(nameof(CommandPreview)); }
    }

    public string SessionId
    {
        get => _sessionId;
        set { if (SetProperty(ref _sessionId, value)) OnPropertyChanged(nameof(CommandPreview)); }
    }

    public string ExtraArgs
    {
        get => _extraArgs;
        set { if (SetProperty(ref _extraArgs, value)) OnPropertyChanged(nameof(CommandPreview)); }
    }

    /// <summary>Newline- or semicolon-separated extra <c>--worker-dir</c> paths for Cursor.</summary>
    public string ExtraWorkerDirs
    {
        get => _extraWorkerDirs;
        set { if (SetProperty(ref _extraWorkerDirs, value)) OnPropertyChanged(nameof(CommandPreview)); }
    }

    public bool CursorVerbose
    {
        get => _cursorVerbose;
        set { if (SetProperty(ref _cursorVerbose, value)) OnPropertyChanged(nameof(CommandPreview)); }
    }

    public bool CursorDebug
    {
        get => _cursorDebug;
        set { if (SetProperty(ref _cursorDebug, value)) OnPropertyChanged(nameof(CommandPreview)); }
    }

    public string CursorApiKeyPath
    {
        get => _cursorApiKeyPath;
        set { if (SetProperty(ref _cursorApiKeyPath, value)) OnPropertyChanged(nameof(CommandPreview)); }
    }

    public string CursorAuthTokenFile
    {
        get => _cursorAuthTokenFile;
        set { if (SetProperty(ref _cursorAuthTokenFile, value)) OnPropertyChanged(nameof(CommandPreview)); }
    }

    public bool RunLoginInTerminal
    {
        get => _runLoginInTerminal;
        set { if (SetProperty(ref _runLoginInTerminal, value)) OnPropertyChanged(nameof(CommandPreview)); }
    }

    public string CommandPreview
    {
        get
        {
            try
            {
                var config = ToConfig();
                var product = ProductHost.Get(config.ProductId);
                var app = new AppConfig();
                var plan = product.BuildLaunch(config, app, new LaunchContext());
                return PlatformLoader.Current.Processes.FormatCommandLine(plan.ExecutablePath, plan.Arguments);
            }
            catch (Exception ex)
            {
                return $"({ex.Message})";
            }
        }
    }

    public void LoadNew()
    {
        Id = Guid.NewGuid().ToString("n");
        IsNew = true;
        ProductId = ProductIds.Claude;
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
        ExtraWorkerDirs = "";
        CursorVerbose = false;
        CursorDebug = false;
        CursorApiKeyPath = "";
        CursorAuthTokenFile = "";
        RunLoginInTerminal = false;
        OnPropertyChanged(nameof(CommandPreview));
    }

    public void LoadFrom(SessionConfig config)
    {
        Id = config.Id;
        IsNew = false;
        ProductId = string.IsNullOrWhiteSpace(config.ProductId) ? ProductIds.Claude : config.ProductId;
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
        ExtraWorkerDirs = string.Join(Environment.NewLine, config.Cursor.ExtraWorkerDirs);
        CursorVerbose = config.Cursor.Verbose;
        CursorDebug = config.Cursor.Debug;
        CursorApiKeyPath = config.Cursor.ApiKeyPath ?? "";
        CursorAuthTokenFile = config.Cursor.AuthTokenFile ?? "";
        RunLoginInTerminal = config.Cursor.RunLoginInTerminal;
        OnPropertyChanged(nameof(CommandPreview));
    }

    public SessionConfig ToConfig() => new()
    {
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("n") : Id,
        ProductId = string.IsNullOrWhiteSpace(ProductId) ? ProductIds.Claude : ProductId.Trim(),
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
        ExtraArgs = ExtraArgs.Trim(),
        Cursor = new CursorSessionOptions
        {
            ExtraWorkerDirs = ParseWorkerDirs(ExtraWorkerDirs),
            Verbose = CursorVerbose,
            Debug = CursorDebug,
            ApiKeyPath = string.IsNullOrWhiteSpace(CursorApiKeyPath) ? null : CursorApiKeyPath.Trim(),
            AuthTokenFile = string.IsNullOrWhiteSpace(CursorAuthTokenFile) ? null : CursorAuthTokenFile.Trim(),
            RunLoginInTerminal = RunLoginInTerminal
        }
    };

    public void RefreshPreview() => OnPropertyChanged(nameof(CommandPreview));

    private static List<string> ParseWorkerDirs(string raw) =>
        raw.Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .ToList();
}
