# Contributing

Thanks for taking a look. Issues and pull requests are welcome.

## Ground rules

- **C# only.** No Node, no Python, no web views, no native dependencies beyond Win32 P/Invoke. The
  terminal emulator and the ConPTY host are part of the codebase on purpose.
- **Windows first.** The target is Windows 10 1809+ and Windows 11.
- Match the surrounding style: nullable enabled, file-scoped namespaces, comments that explain *why*
  rather than restating the code.

## Building

```powershell
dotnet build
dotnet test
dotnet run --project src\LiveClaude.App
```

`LiveClaude.exe --terminal <directory>` opens straight into the embedded terminal, which is handy when
working on the VT emulator.

## Things worth knowing before you dig in

- A process that owns a console gives that console to every child it starts, and Windows then ignores
  the pseudo-console attribute. That is why `LiveClaude.Service` is a windowed executable that only
  attaches to the caller's console for one-shot CLI commands. If a change makes the supervisor own a
  console, terminal capture silently stops working.
- `claude remote-control --continue` only reattaches for about four hours after the previous server
  stopped. `ClaudeArgs.CanReattach` encodes that rule and is covered by tests.
- The configuration file is shared state: the app writes it, the supervisor watches it. Keep
  `ConfigStore` the only writer.

## Tests

`dotnet test` runs everything and needs no network. The ConPTY tests adapt to whether the host owns a
console, so they behave under both the test runner and a windowless host.

## Pull requests

- One topic per pull request.
- Add or update tests when you change behaviour.
- Update `CHANGELOG.md` under an *Unreleased* heading.
