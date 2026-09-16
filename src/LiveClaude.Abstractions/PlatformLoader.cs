using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace LiveClaude.Abstractions;

/// <summary>
/// Finds and activates the platform assembly for the running operating system.
///
/// Nothing references the implementations at compile time, so the probe is by name:
/// <c>LiveClaude.Platform.Windows</c>, <c>.Linux</c> or <c>.MacOS</c>, loaded from the folder the
/// application runs in. A published package only ships the one it needs.
/// </summary>
public static class PlatformLoader
{
    private static readonly object Gate = new();
    private static IPlatform? _current;
    private static int _resolverInstalled;

    /// <summary>The platform for this process. Loaded once, on first use.</summary>
    public static IPlatform Current
    {
        get
        {
            if (_current is not null)
                return _current;

            lock (Gate)
                return _current ??= Load();
        }
    }

    /// <summary>
    /// Installs a platform explicitly. Used by tests, and by a host that wants to fail early with
    /// its own message rather than on first use.
    /// </summary>
    public static void Use(IPlatform platform)
    {
        lock (Gate)
            _current = platform;
    }

    /// <summary>"windows", "linux" or "macos" for the running OS.</summary>
    public static string CurrentId =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" :
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
        throw new PlatformNotAvailableException(
            $"LiveClaude supports Windows, Linux and macOS. This is {RuntimeInformation.OSDescription}.");

    /// <summary>The assembly that implements <see cref="IPlatform"/> for the running OS.</summary>
    public static string AssemblyNameForCurrentOs => "LiveClaude.Platform." + CurrentId switch
    {
        "windows" => "Windows",
        "linux" => "Linux",
        "macos" => "MacOS",
        var id => throw new PlatformNotAvailableException($"No platform assembly is defined for '{id}'.")
    };

    private static IPlatform Load()
    {
        var assemblyName = AssemblyNameForCurrentOs;
        var assembly = LoadPlatformAssembly(assemblyName);

        var attribute = assembly.GetCustomAttribute<LiveClaudePlatformAttribute>()
            ?? throw new PlatformNotAvailableException(
                $"{assemblyName} does not declare [assembly: LiveClaudePlatform(...)].");

        if (Activator.CreateInstance(attribute.PlatformType) is not IPlatform platform)
        {
            throw new PlatformNotAvailableException(
                $"{attribute.PlatformType.FullName} in {assemblyName} does not implement IPlatform.");
        }

        return platform;
    }

    private static Assembly LoadPlatformAssembly(string assemblyName)
    {
        InstallSiblingResolver();

        // By path, not by name. The platform assembly is deliberately absent from the application's
        // deps.json — nothing references it — and the default resolver only probes what is listed
        // there, so Assembly.Load would fail with the file sitting right next to the executable.
        var path = Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll");

        try
        {
            if (File.Exists(path))
                return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);

            // Single-file and trimmed publishes flatten everything into the bundle, where there is no
            // file to point at but the name does resolve.
            return Assembly.Load(new AssemblyName(assemblyName));
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            // Worth being specific: this is exactly what a package built for the wrong OS looks like,
            // and "could not load file or assembly" on its own sends people hunting for the runtime.
            throw new PlatformNotAvailableException(
                $"{assemblyName}.dll was not found next to the application ({AppContext.BaseDirectory}). " +
                $"This build of LiveClaude cannot run on {RuntimeInformation.OSDescription} — " +
                "install the package for this operating system.",
                ex);
        }
    }

    /// <summary>
    /// Lets a platform assembly find its own siblings — LiveClaude.Platform.Linux depends on
    /// LiveClaude.Platform.Posix, and that one is missing from deps.json for the same reason.
    /// </summary>
    private static void InstallSiblingResolver()
    {
        if (Interlocked.Exchange(ref _resolverInstalled, 1) != 0)
            return;

        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            if (name.Name is not { } simpleName || !simpleName.StartsWith("LiveClaude.", StringComparison.Ordinal))
                return null;

            var candidate = Path.Combine(AppContext.BaseDirectory, simpleName + ".dll");
            return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        };
    }
}
