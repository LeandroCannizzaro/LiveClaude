# Contributing

Thanks for taking a look. Issues and pull requests are welcome.

## Ground rules

- **C# only.** No Node, no Python, no web views, no native dependencies beyond each platform's own
  libc or Win32 P/Invoke. The terminal emulator and the pseudo-terminal hosts are part of the
  codebase on purpose.
- **Three platforms, one seam.** Windows, Linux and macOS are equals. New operating-system code goes
  in a platform assembly behind `IPlatform`; `LiveClaude.Core` contains no `#if`, no
  `RuntimeInformation.IsOSPlatform` switch and no `net10.0-windows` target. If something will not fit
  behind the contract, widen the contract — do not reach around it.
- Match the surrounding style: nullable enabled, file-scoped namespaces, comments that explain *why*
  rather than restating the code.

## Building

```bash
dotnet build
dotnet test
dotnet run --project src/LiveClaude.App
```

`LiveClaude --terminal <directory>` opens straight into the embedded terminal, which is handy when
working on the VT emulator.

The build selects the platform assembly from the `RuntimeIdentifier`, falling back to the machine you
are on. `dotnet publish -r linux-x64` therefore produces a Linux package from anywhere — except a
Windows package, which needs Windows, because `LiveClaude.Platform.Windows` targets `net10.0-windows`.

## Things worth knowing before you dig in

- A process that owns a console gives that console to every child it starts, and Windows then ignores
  the pseudo-console attribute. That is why `LiveClaude.Service` is a windowed executable **on
  Windows** that only attaches to the caller's console for one-shot CLI commands. If a change makes
  the supervisor own a console there, terminal capture silently stops working. POSIX has no such
  rule, and the same executable is an ordinary console program on Linux and macOS.
- On POSIX the pseudo terminal is started with `posix_spawn`, never `fork` or `forkpty`. Forking a
  multi-threaded .NET process leaves the child holding runtime locks nothing will release. Two
  consequences follow and both are easy to undo by accident: the runtime does not reap a child it did
  not start, so `ChildReaper` waits on ours; and reading a pty master whose slave has closed gives
  EIO on Linux rather than end of file, which is what a clean shutdown looks like.
- The child gets a new session and the pty slave as its controlling terminal. That is the only reason
  writing `0x03` reaches the server as SIGINT, and the only reason it can deregister its bridge
  environment instead of being killed.
- `claude remote-control --continue` only reattaches for about four hours after the previous server
  stopped. `ClaudeArgs.CanReattach` encodes that rule and is covered by tests.
- The configuration file is shared state: the app writes it, the supervisor watches it. Keep
  `ConfigStore` the only writer.

## Tests

`dotnet test` runs everything and needs no network. Three projects, and which one a test belongs in
is worth getting right:

- `LiveClaude.Tests` — no platform involved at all. Runs everywhere.
- `LiveClaude.Tests.Platform` — goes through `IPlatform`, and runs everywhere. Anything that is a
  pure function of its input (a systemd unit, a launchd property list, shell quoting) is asserted on
  every operating system, because a wrong unit file announces itself at the next reboot and that is
  too late to learn it from CI. Tests that genuinely need one OS use `[PlatformFact("linux")]`,
  which reports a skip rather than a pass.
- `LiveClaude.Tests.Windows` — the Windows platform assembly's own internals. Targets
  `net10.0-windows`, so it only builds and runs on the Windows CI job.

The pty tests adapt to whether the host owns a console, so they behave under both the test runner and
a windowless host.

## Pull requests

- One topic per pull request.
- Add or update tests when you change behaviour.
- Update `CHANGELOG.md` under an *Unreleased* heading.
