using LiveClaude.Abstractions;
using LiveClaude.Service;

var command = args.FirstOrDefault()?.ToLowerInvariant() ?? ServiceIdentity.SuperviseArgument;

// A package built for another operating system is a configuration mistake, not a crash. Say so in
// one line instead of a stack trace nobody can act on.
try
{
    _ = PlatformLoader.Current;
}
catch (PlatformNotAvailableException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 69;
}

// Supervising must happen without a console of our own on Windows (a console owner hands its console
// to every child, and the pseudo console is then ignored). Every other command is a one-shot CLI call
// whose output belongs in the terminal the user typed it into. Elsewhere this is a no-op: the
// supervisor is a normal console executable there.
if (command is not (ServiceIdentity.ServiceArgument or ServiceIdentity.SuperviseArgument))
    PlatformLoader.Current.AttachToParentConsole();

return command switch
{
    ServiceIdentity.ServiceArgument => await RunHostAsync(args, asService: true),
    ServiceIdentity.SuperviseArgument => await RunHostAsync(args, asService: false),
    "--help" or "-h" or "help" => ShowHelp(),
    _ when SupervisorCli.IsVerb(command) => await SupervisorCli.RunAsync(args),
    _ => ShowHelp(unknown: command)
};

static async Task<int> RunHostAsync(string[] args, bool asService)
{
    await SupervisorHost.RunAsync(args, asService);
    return 0;
}

static int ShowHelp(string? unknown = null)
{
    if (unknown is not null)
        Console.Error.WriteLine($"Unknown command '{unknown}'.");

    var platform = PlatformLoader.Current;
    var exe = platform.Deployment.SupervisorExecutable;
    var user = platform.UserAutostart;
    var system = platform.SystemAutostart;

    Console.WriteLine($"""
        LiveClaude supervisor — {platform.DisplayName}

        Usage:
          {exe} [--supervise]          Supervise in the foreground (the autostart host runs this)
          {exe} --service              Run under the system service manager

          {exe} install-autostart [--scope user|system]
                                       [--no-start-before-sign-in]
                                       [--user <account>] [--password ***]
                                       [--exe <path> --args <arguments>]
          {exe} uninstall-autostart [--scope user|system]
          {exe} start-autostart | stop-autostart [--scope user|system]
          {exe} status

        Autostart hosts on this platform:
          user  : {user.DisplayName} — {user.Summary}
          system: {(system is null ? "none" : $"{system.DisplayName} — {system.Summary}")}

        install-task / install-service (and their uninstall, start and stop forms) are kept as
        aliases for --scope user and --scope system.

        --exe and --args register another executable, which is how the desktop application
        registers itself on installs that cannot start this one (ClickOnce).
        """);

    return unknown is null ? 0 : 64;
}
