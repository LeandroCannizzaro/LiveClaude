using System.Runtime.InteropServices;

namespace LiveClaude.Core.Hosting;

/// <summary>
/// Outcome of refreshing the stable copy: which files could not be replaced, and the version that
/// ended up there.
/// </summary>
public sealed record DeploymentResult(string ExecutablePath, IReadOnlyList<string> Locked, string? Version)
{
    public bool UpToDate => Locked.Count == 0;
}

/// <summary>
/// Gives the supervisor a path that does not move.
///
/// A ClickOnce install lives under %LOCALAPPDATA%\Apps\2.0\&lt;random&gt;\ and a winget portable one
/// under a versioned package folder: both change on every update, which would leave a scheduled task
/// or a service pointing at an executable that no longer exists. When the app is running from such a
/// place, the supervisor is copied next to the configuration instead and registered from there.
/// </summary>
public static class SupervisorDeployment
{
    public const string SupervisorExecutable = "LiveClaude.Service.exe";

    public static string StableDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LiveClaude",
        "supervisor");

    /// <summary>True when the folder the app runs from is replaced on update.</summary>
    public static bool IsVolatileLocation(string directory) =>
        directory.Contains(@"\Apps\2.0\", StringComparison.OrdinalIgnoreCase) ||
        directory.Contains(@"\WinGet\Packages\", StringComparison.OrdinalIgnoreCase) ||
        directory.Contains(@"\Temp\", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the supervisor path to register. Copies the application files to
    /// <see cref="StableDirectory"/> first when the current location is a volatile one.
    /// </summary>
    public static string EnsureDeployed(string? sourceDirectory = null) => Deploy(sourceDirectory).ExecutablePath;

    /// <summary>
    /// Refreshes the stable copy and reports what it could not replace.
    ///
    /// The scheduled task runs the copy, so while it is running those files are locked and the
    /// refresh silently kept an old build around — which is how an install could run yesterday's
    /// code and do nothing at all. The caller is expected to stop the supervisor and try again.
    /// </summary>
    public static DeploymentResult Deploy(string? sourceDirectory = null)
    {
        sourceDirectory ??= AppContext.BaseDirectory;
        var source = sourceDirectory.TrimEnd('\\', '/');
        var localExecutable = Path.Combine(source, SupervisorExecutable);

        if (!IsVolatileLocation(source))
            return new DeploymentResult(localExecutable, [], ReadVersion(localExecutable));

        var target = StableDirectory;
        Directory.CreateDirectory(target);
        var locked = DeployTo(source, target);

        var deployed = Path.Combine(target, SupervisorExecutable);
        var path = File.Exists(deployed) ? deployed : localExecutable;

        // The app executable is what gets registered on a ClickOnce install, so report its version.
        var appCopy = Path.Combine(target, "LiveClaude.exe");
        return new DeploymentResult(path, locked, ReadVersion(File.Exists(appCopy) ? appCopy : path));
    }

    public static string? ReadVersion(string executablePath)
    {
        try
        {
            return File.Exists(executablePath)
                ? System.Diagnostics.FileVersionInfo.GetVersionInfo(executablePath).ProductVersion
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Describes what the app did, for the message shown after installing.</summary>
    public static string? DescribeDeployment(string supervisorPath) =>
        supervisorPath.StartsWith(StableDirectory, StringComparison.OrdinalIgnoreCase)
            ? $"The supervisor was copied to {StableDirectory} so the registration survives app updates."
            : null;

    /// <summary>
    /// Copies what changed and clears the Mark of the Web from every copy. Returns the files that
    /// could not be replaced — normally because a supervisor started from this folder is running.
    /// </summary>
    public static IReadOnlyList<string> DeployTo(string source, string target)
    {
        var locked = new List<string>();

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(directory.Replace(source, target, StringComparison.OrdinalIgnoreCase));

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = file.Replace(source, target, StringComparison.OrdinalIgnoreCase);

            try
            {
                if (File.Exists(destination) &&
                    File.GetLastWriteTimeUtc(destination) >= File.GetLastWriteTimeUtc(file) &&
                    new FileInfo(destination).Length == new FileInfo(file).Length)
                {
                    continue;
                }

                File.Copy(file, destination, overwrite: true);
                RemoveMarkOfTheWeb(destination);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                locked.Add(Path.GetFileName(file));
            }
        }

        return locked;
    }

    /// <summary>
    /// Drops the Zone.Identifier stream from a copied file.
    ///
    /// Files that arrived from the internet — everything a ClickOnce install puts on disk — carry
    /// that stream, and File.Copy carries it along. Launching such a file through the shell (which
    /// is how the elevation prompt works) then adds "The publisher could not be verified. Are you
    /// sure you want to run this software?" on top of UAC. .NET cannot address an alternate data
    /// stream, so this goes through Win32.
    /// </summary>
    private static void RemoveMarkOfTheWeb(string path)
    {
        try
        {
            DeleteFileW(path + ":Zone.Identifier");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EntryPointNotFoundException)
        {
            // The warning is cosmetic; never fail a deployment over it.
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteFileW(string path);
}
