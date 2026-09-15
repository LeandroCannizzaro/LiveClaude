using LiveClaude.Core.Hosting;
using LiveClaude.Service;

var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "--supervise";

// Supervising must happen without a console of our own (see ConsoleBridge); every other command is
// a one-shot CLI call whose output belongs in the terminal the user typed it into.
if (command is not ("--service" or "--supervise"))
    ConsoleBridge.AttachToParent(allocateIfMissing: true);

return command switch
{
    "--service" => await RunHostAsync(args, asService: true),
    "--supervise" => await RunHostAsync(args, asService: false),
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

    Console.WriteLine("""
        LiveClaude supervisor

        Usage:
          LiveClaude.Service.exe [--supervise]          Supervise windowless (scheduled task host)
          LiveClaude.Service.exe --service              Run as a Windows service
          LiveClaude.Service.exe install-service [--account DOMAIN\user --password ***]
                                                 [--exe <path> --args <arguments>]
          LiveClaude.Service.exe uninstall-service
          LiveClaude.Service.exe start-service | stop-service
          LiveClaude.Service.exe install-task [--no-boot] [--user DOMAIN\user]
                                              [--exe <path> --args <arguments>]
          LiveClaude.Service.exe uninstall-task
          LiveClaude.Service.exe status

        --exe and --args register another executable, which is how the desktop application
        registers itself on installs that cannot start this one (ClickOnce).
        """);

    return unknown is null ? 0 : 64;
}
