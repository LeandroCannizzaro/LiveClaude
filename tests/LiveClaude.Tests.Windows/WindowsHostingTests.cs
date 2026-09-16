using LiveClaude.Abstractions;
using LiveClaude.Platform.Windows;
using LiveClaude.Platform.Windows.Autostart;
using LiveClaude.Platform.Windows.Deployment;
using LiveClaude.Service;
using Xunit;

namespace LiveClaude.Tests.Windows;

public class ScheduledTaskXmlTests
{
    private static readonly WindowsSupervisorDeployment Deployment = new();

    [Fact]
    public void TheTaskRunsAtLogonAndAtBootAndRestartsOnFailure()
    {
        var xml = ScheduledTaskProvider.BuildXml(@"C:\Program Files\LiveClaude\LiveClaude.Service.exe", runAtBoot: true);

        Assert.Contains("<LogonTrigger>", xml);
        Assert.Contains("<BootTrigger>", xml);
        Assert.Contains("<RestartOnFailure>", xml);
        Assert.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>", xml);
        Assert.Contains("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>", xml);
        Assert.Contains("--supervise", xml);
    }

    [Fact]
    public void BootTriggerCanBeLeftOut()
    {
        var xml = ScheduledTaskProvider.BuildXml(@"C:\tools\LiveClaude.Service.exe", runAtBoot: false);
        Assert.DoesNotContain("<BootTrigger>", xml);
    }

    /// <summary>
    /// The boot trigger needs administrator rights, and UAC may be answered with another account —
    /// the task must still belong to the user whose session the servers run in.
    /// </summary>
    [Fact]
    public void TheTaskIsRegisteredForTheGivenUserNotTheElevatedOne()
    {
        var xml = ScheduledTaskProvider.BuildXml(@"C:\tools\LiveClaude.Service.exe", runAtBoot: true, userName: @"CANLE\cl");

        Assert.Contains(@"<UserId>CANLE\cl</UserId>", xml);
        Assert.Contains("<LogonType>InteractiveToken</LogonType>", xml);
    }

    /// <summary>
    /// sc.exe parses its own command line: every option is "key= value" with the space after the
    /// equals sign, and the executable is quoted inside the quoted binPath value so a path with
    /// spaces survives. Passing the pairs through ProcessStartInfo.ArgumentList instead produced
    /// "key= value" as one quoted token, which sc.exe rejected with a usage error.
    /// </summary>
    [Fact]
    public void TheServiceCommandLineIsShapedTheWayScExeExpects()
    {
        var line = WindowsServiceProvider.BuildCreateCommandLine(
            @"C:\Program Files\LiveClaude\LiveClaude.Service.exe",
            @"CANLE\cl",
            "secret pass");

        Assert.Contains(@"binPath= ""\""C:\Program Files\LiveClaude\LiveClaude.Service.exe\"" --service""", line);
        Assert.Contains("start= auto", line);
        Assert.Contains(@"obj= ""CANLE\cl""", line);
        Assert.Contains(@"password= ""secret pass""", line);
        Assert.DoesNotContain("\"binPath=", line);
    }

    [Fact]
    public void WithoutAnAccountNoCredentialsGoOnTheCommandLine()
    {
        var line = WindowsServiceProvider.BuildCreateCommandLine(@"C:\tools\LiveClaude.Service.exe");

        Assert.DoesNotContain("obj=", line);
        Assert.DoesNotContain("password=", line);
    }

    [Fact]
    public void TheFailureActionsRestartTheServiceThreeTimes()
    {
        var line = WindowsServiceProvider.BuildFailureCommandLine();

        Assert.Contains("reset= 86400", line);
        Assert.Contains("actions= restart/5000/restart/10000/restart/30000", line);
    }

    [Theory]
    [InlineData("[SC] StartService FAILED 1069:", "Log on as a service")]
    [InlineData("[SC] OpenSCManager FAILED 5: Access is denied.", "elevated")]
    [InlineData("[SC] CreateService FAILED 1073:", "already exists")]
    public void ServiceFailuresAreExplained(string output, string expected) =>
        Assert.Contains(expected, WindowsServiceProvider.Explain(new ProcessResult(1, output, "")));

    /// <summary>
    /// A ClickOnce install ships LiveClaude.Service.exe without its runtime configuration, and .NET
    /// refuses to start such an executable. Registering it would leave a task that fails at every
    /// logon, so the desktop application has to host the supervisor there instead.
    /// </summary>
    [Fact]
    public void TheAppIsRegisteredWhenTheSupervisorCannotStart()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"liveclaude-clickonce-{Guid.NewGuid():n}");
        Directory.CreateDirectory(directory);

        try
        {
            // What ClickOnce actually deploys: both executables, but only the app's runtimeconfig.
            File.WriteAllText(Path.Combine(directory, "LiveClaude.Service.exe"), "");
            File.WriteAllText(Path.Combine(directory, "LiveClaude.exe"), "");
            File.WriteAllText(Path.Combine(directory, "LiveClaude.runtimeconfig.json"), "{}");

            var command = Deployment.ResolveCommand(directory, AutostartScope.User);

            Assert.Equal(Path.Combine(directory, "LiveClaude.exe"), command.ExecutablePath);
            Assert.Equal("--supervise", command.Arguments);
            Assert.Contains("ClickOnce", command.Note);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TheSupervisorIsRegisteredWhenItsRuntimeConfigurationIsThere()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"liveclaude-zip-{Guid.NewGuid():n}");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(Path.Combine(directory, "LiveClaude.Service.exe"), "");
            File.WriteAllText(Path.Combine(directory, "LiveClaude.Service.runtimeconfig.json"), "{}");
            File.WriteAllText(Path.Combine(directory, "LiveClaude.exe"), "");
            File.WriteAllText(Path.Combine(directory, "LiveClaude.runtimeconfig.json"), "{}");

            var command = Deployment.ResolveCommand(directory, AutostartScope.System);

            Assert.Equal(Path.Combine(directory, "LiveClaude.Service.exe"), command.ExecutablePath);
            Assert.Equal("--service", command.Arguments);
            Assert.Null(command.Note);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A task registered through the elevation prompt by another administrator cannot be read back
    /// by the standard account. Reporting that as "not installed" made the card contradict a task
    /// that was demonstrably running.
    /// </summary>
    [Fact]
    public void AnUnreadableTaskIsNotReportedAsMissing()
    {
        var state = ScheduledTaskProvider.Classify(new ProcessResult(1, "", "ERROR: Access is denied."));

        Assert.Null(state.Installed);
        Assert.Contains("still runs", state.Detail);
        Assert.Equal("installed, but this account cannot read it", state.Describe());
    }

    [Theory]
    [InlineData("ERROR: The system cannot find the file specified.")]
    [InlineData("ERROR: Impossibile trovare il file specificato.")]
    public void AMissingTaskIsReportedAsMissing(string output)
    {
        var state = ScheduledTaskProvider.Classify(new ProcessResult(1, output, ""));

        Assert.False(state.Installed);
        Assert.Equal("not installed", state.Describe());
    }

    /// <summary>
    /// The install command carries "--args --supervise" as the arguments to register. Deciding the
    /// mode by scanning the whole command line therefore turned the installer into a supervisor:
    /// it registered nothing and exited 0, so the app reported success for a task that never existed.
    /// Only the first argument may select the mode.
    /// </summary>
    [Theory]
    [InlineData(new[] { "--supervise" }, true, false)]
    [InlineData(new[] { "--service" }, true, true)]
    [InlineData(new[] { "install-task", "--user", @"CANLE\cl", "--exe", @"C:\app\LiveClaude.exe", "--args", "--supervise" }, false, false)]
    [InlineData(new[] { "install-service", "--exe", @"C:\app\LiveClaude.exe", "--args", "--service" }, false, false)]
    [InlineData(new string[0], false, false)]
    public void OnlyTheFirstArgumentSelectsTheMode(string[] args, bool expectSupervise, bool expectService)
    {
        var mode = args.FirstOrDefault();
        var asService = string.Equals(mode, "--service", StringComparison.OrdinalIgnoreCase);
        var supervise = asService || string.Equals(mode, "--supervise", StringComparison.OrdinalIgnoreCase);

        Assert.Equal(expectSupervise, supervise);
        Assert.Equal(expectService, asService);

        // The same command lines must still be recognised as install verbs.
        if (!supervise && args.Length > 0)
            Assert.True(SupervisorCli.IsVerb(args[0]));
    }

    [Fact]
    public void TheRegisteredArgumentsEndUpInTheTaskAndTheService()
    {
        var xml = ScheduledTaskProvider.BuildXml(@"C:\app\LiveClaude.exe", runAtBoot: false, arguments: "--supervise");
        Assert.Contains("<Arguments>--supervise</Arguments>", xml);
        Assert.Contains(@"<Command>C:\app\LiveClaude.exe</Command>", xml);

        var line = WindowsServiceProvider.BuildCreateCommandLine(@"C:\app\LiveClaude.exe", arguments: "--service");
        Assert.Contains(@"binPath= ""\""C:\app\LiveClaude.exe\"" --service""", line);
    }

    /// <summary>
    /// Files a ClickOnce install put on disk carry the internet zone marker, File.Copy carries it
    /// along, and launching such a copy through the shell — which is how the elevation prompt works —
    /// adds "The publisher could not be verified" on top of UAC.
    /// </summary>
    [Fact]
    public void DeployedFilesLoseTheInternetZoneMarker()
    {
        var source = Path.Combine(Path.GetTempPath(), $"liveclaude-src-{Guid.NewGuid():n}");
        var target = Path.Combine(Path.GetTempPath(), $"liveclaude-dst-{Guid.NewGuid():n}");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);

        var file = Path.Combine(source, "LiveClaude.Service.exe");
        File.WriteAllText(file, "binary");
        File.WriteAllText($"{file}:Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3");

        try
        {
            Assert.True(HasZoneIdentifier(file), "the source file should start marked");

            Deployment.DeployTo(source, target);

            var copy = Path.Combine(target, "LiveClaude.Service.exe");
            Assert.True(File.Exists(copy));
            Assert.False(HasZoneIdentifier(copy), "the deployed copy must not carry the marker");
        }
        finally
        {
            Directory.Delete(source, recursive: true);
            Directory.Delete(target, recursive: true);
        }
    }

    /// <summary>
    /// The scheduled task runs the deployed copy, so those files are locked while it runs. Skipping
    /// them silently left an old build in place, and every install then ran that old build.
    /// </summary>
    [Fact]
    public void FilesThatCannotBeReplacedAreReported()
    {
        var source = Path.Combine(Path.GetTempPath(), $"liveclaude-src-{Guid.NewGuid():n}");
        var target = Path.Combine(Path.GetTempPath(), $"liveclaude-dst-{Guid.NewGuid():n}");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);

        // The stale copy must look older and different, or the refresh rightly skips it.
        File.WriteAllText(Path.Combine(target, "LiveClaude.exe"), "old");
        File.SetLastWriteTimeUtc(Path.Combine(target, "LiveClaude.exe"), DateTime.UtcNow.AddHours(-2));

        File.WriteAllText(Path.Combine(source, "LiveClaude.exe"), "a newer build");
        File.WriteAllText(Path.Combine(source, "other.dll"), "a newer build");

        // Hold the destination open, the way a running supervisor holds its own executable.
        using (var _ = new FileStream(Path.Combine(target, "LiveClaude.exe"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try
            {
                var locked = Deployment.DeployTo(source, target);

                Assert.Contains("LiveClaude.exe", locked);
                Assert.DoesNotContain("other.dll", locked);
                Assert.Equal("old", File.ReadAllText(Path.Combine(target, "LiveClaude.exe")));
                Assert.Equal("a newer build", File.ReadAllText(Path.Combine(target, "other.dll")));
            }
            finally
            {
                // released below
            }
        }

        // With nothing holding it, the same call replaces the file and reports nothing locked.
        Assert.Empty(Deployment.DeployTo(source, target));
        Assert.Equal("a newer build", File.ReadAllText(Path.Combine(target, "LiveClaude.exe")));

        Directory.Delete(source, recursive: true);
        Directory.Delete(target, recursive: true);
    }

    /// <summary>
    /// One folder across updates: a scheduled task or service registration keeps pointing at the
    /// same path. What made an old build survive there is handled by stopping its process, not by
    /// moving the folder.
    /// </summary>
    [Fact]
    public void TheDeploymentPathDoesNotChangeBetweenBuilds() =>
        Assert.Equal(WindowsSupervisorDeployment.StableRoot, Deployment.StableDirectory);

    /// <summary>
    /// The guard that made the difference: an installer of an older build stayed alive in the
    /// deployment folder, holding LiveClaude.exe open, so every later refresh skipped it and every
    /// install ran that same old build again.
    /// </summary>
    [Fact]
    public void ProcessesRunningFromTheDeploymentFolderAreStopped()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"liveclaude-guard-{Guid.NewGuid():n}");
        Directory.CreateDirectory(folder);

        var executable = Path.Combine(folder, "probe.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), executable);

        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable)
        {
            Arguments = "/c pause",
            UseShellExecute = false,
            RedirectStandardInput = true,
            CreateNoWindow = true
        })!;

        try
        {
            Assert.False(process.HasExited);

            var stopped = Deployment.StopProcessesIn(folder);

            Assert.Contains(stopped, entry => entry.Contains("probe"));
            Assert.True(process.WaitForExit(5000), "the process should have been stopped");
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);

            process.Dispose();
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    private static bool HasZoneIdentifier(string path)
    {
        try
        {
            return File.ReadAllText($"{path}:Zone.Identifier").Length > 0;
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    [Theory]
    [InlineData(@"C:\Users\cl\AppData\Local\Apps\2.0\ABC123\LiveClaude", true)]
    [InlineData(@"C:\Users\cl\AppData\Local\Microsoft\WinGet\Packages\Publisher.App\1.0", true)]
    [InlineData(@"C:\Program Files\LiveClaude", false)]
    [InlineData(@"C:\Users\cl\Programs\LiveClaude", false)]
    public void UpdatableInstallLocationsAreRecognised(string directory, bool expected) =>
        Assert.Equal(expected, Deployment.IsVolatileLocation(directory));
}
