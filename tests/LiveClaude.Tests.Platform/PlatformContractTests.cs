using LiveClaude.Abstractions;
using Xunit;

namespace LiveClaude.Tests.Platform;

/// <summary>
/// The parts of a platform assembly that are pure functions of their input, and therefore worth
/// pinning: how an argument vector is rendered, where things live, and whether the seam itself is
/// wired up the way the loader expects.
/// </summary>
public class PlatformContractTests
{
    [Fact]
    public void ThePlatformAssemblyForThisOperatingSystemLoads()
    {
        var platform = PlatformLoader.Current;

        Assert.Equal(PlatformFactAttribute.Current, platform.Id);
        Assert.False(string.IsNullOrWhiteSpace(platform.DisplayName));
    }

    /// <summary>Every seam is expected to be filled; a null one would fail far from here.</summary>
    [Fact]
    public void EverySeamIsImplemented()
    {
        var platform = PlatformLoader.Current;

        Assert.NotNull(platform.Pty);
        Assert.NotNull(platform.Paths);
        Assert.NotNull(platform.Ipc);
        Assert.NotNull(platform.Processes);
        Assert.NotNull(platform.Elevation);
        Assert.NotNull(platform.UserAutostart);
        Assert.NotNull(platform.Claude);
        Assert.NotNull(platform.Shell);
        Assert.NotNull(platform.Deployment);
    }

    [Fact]
    public void TheUserScopedAutostartHostIsTheUserScopedOne()
    {
        Assert.Equal(AutostartScope.User, PlatformLoader.Current.UserAutostart.Scope);

        if (PlatformLoader.Current.SystemAutostart is { } system)
            Assert.Equal(AutostartScope.System, system.Scope);
    }

    /// <summary>
    /// Only the Windows service needs a password, and only a host that can name an account could ever
    /// need one. Asking for a password the platform cannot use is how a UI ends up lying.
    /// </summary>
    [Fact]
    public void OnlyAHostThatNamesAnAccountCanRequireAPassword()
    {
        foreach (var provider in Providers())
        {
            if (provider.RequiresPassword)
                Assert.True(provider.SupportsAccount, $"{provider.Kind} requires a password but cannot name an account.");
        }
    }

    [Fact]
    public void TheRootDirectoryCanBeOverridden()
    {
        var previous = Environment.GetEnvironmentVariable(PathLayout.RootOverrideVariable);
        var expected = Path.Combine(Path.GetTempPath(), "liveclaude-root-test");

        try
        {
            Environment.SetEnvironmentVariable(PathLayout.RootOverrideVariable, expected);
            Assert.Equal(expected, PlatformLoader.Current.Paths.RootDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(PathLayout.RootOverrideVariable, previous);
        }
    }

    /// <summary>
    /// Windows quotes the way CommandLineToArgvW parses; a POSIX shell uses single quotes. Both have
    /// to survive a path with a space in it, because that is what every Windows directory looks like.
    /// </summary>
    [PlatformFact("windows")]
    public void WindowsRendersArgumentsTheWayCreateProcessParsesThem()
    {
        var line = PlatformLoader.Current.Processes.FormatCommandLine(
            @"C:\Program Files\claude\claude.exe", ["remote-control", "--name", "my project"]);

        Assert.Equal(@"""C:\Program Files\claude\claude.exe"" remote-control --name ""my project""", line);
    }

    [PlatformFact("linux", "macos")]
    public void PosixRendersArgumentsTheWayAShellWouldAcceptThem()
    {
        var line = PlatformLoader.Current.Processes.FormatCommandLine(
            "/usr/local/bin/claude", ["remote-control", "--name", "my project"]);

        Assert.Equal("/usr/local/bin/claude remote-control --name 'my project'", line);
    }

    /// <summary>
    /// A dangling symlink in ~/.local/bin is the usual leftover of an uninstall, and File.Exists is
    /// happy with it. Only an executability test catches it.
    /// </summary>
    [Fact]
    public void APathThatIsNotAFileIsNotExecutable()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"liveclaude-nothing-{Guid.NewGuid():n}");
        Assert.False(PlatformLoader.Current.Claude.IsExecutable(missing));
    }

    [PlatformFact("linux", "macos")]
    public void APlainFileWithoutTheExecutableBitIsNotExecutable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"liveclaude-notexec-{Guid.NewGuid():n}");
        File.WriteAllText(path, "#!/bin/sh\necho hi\n");

        try
        {
            Assert.False(PlatformLoader.Current.Claude.IsExecutable(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// A Unix domain socket path is limited to 104 bytes on macOS and 108 on Linux, terminator
    /// included, and exceeding it fails at bind with an exception that names a parameter rather than
    /// the problem. The first version of this put the socket under
    /// ~/Library/Application Support/LiveClaude/run/, which fits for a short user name and does not
    /// for a long one.
    ///
    /// POSIX-only, and it has to be: on Windows the endpoint is a pipe name, not a path, and has no
    /// such limit. CI runs this on the Linux and macOS jobs, which is where it would have caught it.
    /// </summary>
    [PlatformFact("linux", "macos")]
    public void TheSocketPathFitsWhatTheKernelAccepts()
    {
        var endpoint = PlatformLoader.Current.Ipc.EndpointDescription;

        Assert.True(
            endpoint.Length <= 104,
            $"the supervisor socket path is {endpoint.Length} characters, over the 104-byte limit: {endpoint}");
    }

    /// <summary>
    /// The same limit, but for the longest name anything actually asks for — the per-run endpoint the
    /// IPC tests create, whose name carries a GUID. Production names are far shorter, so this is the
    /// worst case rather than the typical one.
    /// </summary>
    [PlatformFact("linux", "macos")]
    public void EvenATestScopedEndpointFits()
    {
        var endpoint = PlatformLoader.Current
            .CreateIpcEndpoint($"test-{Guid.NewGuid():n}")
            .EndpointDescription;

        Assert.True(
            endpoint.Length <= 104,
            $"a test-scoped socket path is {endpoint.Length} characters, over the 104-byte limit: {endpoint}");
    }

    private static IEnumerable<IAutostartProvider> Providers()
    {
        yield return PlatformLoader.Current.UserAutostart;

        if (PlatformLoader.Current.SystemAutostart is { } system)
            yield return system;
    }
}
