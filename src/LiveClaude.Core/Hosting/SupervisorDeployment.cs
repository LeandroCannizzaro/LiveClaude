using System.Runtime.InteropServices;

namespace LiveClaude.Core.Hosting;

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
    public static string EnsureDeployed(string? sourceDirectory = null)
    {
        sourceDirectory ??= AppContext.BaseDirectory;
        var source = sourceDirectory.TrimEnd('\\', '/');
        var localExecutable = Path.Combine(source, SupervisorExecutable);

        if (!IsVolatileLocation(source))
            return localExecutable;

        var target = StableDirectory;
        Directory.CreateDirectory(target);
        DeployTo(source, target);

        var deployed = Path.Combine(target, SupervisorExecutable);
        return File.Exists(deployed) ? deployed : localExecutable;
    }

    /// <summary>Describes what the app did, for the message shown after installing.</summary>
    public static string? DescribeDeployment(string supervisorPath) =>
        supervisorPath.StartsWith(StableDirectory, StringComparison.OrdinalIgnoreCase)
            ? $"The supervisor was copied to {StableDirectory} so the registration survives app updates."
            : null;

    /// <summary>Copies what changed and clears the Mark of the Web from every copy.</summary>
    public static void DeployTo(string source, string target)
    {
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
                // A file in use (the running app itself) is not worth failing the deployment over.
            }
        }
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
