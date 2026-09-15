# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[semantic versioning](https://semver.org/).

## [1.0.11] - 2026-09-15

### Fixed

- **The scheduled task was never created, while the app reported success.** The command that
  registers it ends with `--args --supervise`, and the app decided its mode by looking for
  `--supervise` *anywhere* on the command line. The elevated installer therefore became a supervisor
  instead of installing anything; the "only one supervisor" guard then stopped it, it exited 0, and
  that zero was read as "installed". Only the first argument selects the mode now. The same mistake
  affected `install-service` through `--args --service`.
- Installing no longer trusts the exit code: the task and the service are queried afterwards, and a
  success that left nothing behind is reported as such — the task install falls back to the
  unelevated path instead of claiming it worked.

## [1.0.10] - 2026-09-15

### Fixed

- **"The publisher could not be verified" before the elevation prompt.** Everything a ClickOnce
  install puts on disk carries the internet zone marker, `File.Copy` carries it to the supervisor
  copy, and launching that copy through the shell (how elevation works) added a second, scarier
  dialog on top of UAC. The marker is now cleared from every deployed file.
- Installing the task without elevation verifies afterwards and reports what Task Scheduler actually
  says, instead of claiming success blindly — and names the declined or cancelled prompt as the
  reason there is no boot trigger.

## [1.0.9] - 2026-09-15

### Fixed

- **"Cannot access a closed pipe" when closing the app.** The IPC read loop's `StreamReader` owned
  the pipe, so ending it closed the connection under the writer, which then threw while flushing on
  shutdown — straight into an error dialog on the way out. Both ends now keep the pipe open for their
  own lifetime, closing it is tolerant of a connection that already went away, and the window's close
  handler no longer lets a shutdown failure reach the dispatcher.

## [1.0.8] - 2026-09-15

### Fixed

- **The scheduled task card said "not installed" for a task that was running.** Any failure of
  `schtasks /Query` was treated as "the task is not there", including *Access is denied* — which is
  what Windows answers when the task was registered through the elevation prompt by a different
  administrator account. The three outcomes are now told apart: installed, missing, and "installed
  but not readable by this account", with an explanation instead of a wrong state. When a supervisor
  launched by the task is connected, the card says it is running.
- The task status is read in a locale-independent way. `schtasks /FO LIST` returns localised field
  names, so on a non-English Windows the status was always empty.
- Installing reports when Windows will not let the account read the task back, so a successful
  install no longer looks like it did nothing.

## [1.0.7] - 2026-09-15

### Fixed

- **On a ClickOnce install the supervisor could not start at all.** ClickOnce deploys
  `LiveClaude.Service.exe` but not its `runtimeconfig.json`, and .NET refuses to start such an
  executable: "You must install .NET Desktop Runtime". A scheduled task registered against it failed
  at every logon. The desktop application can now host the supervisor itself
  (`LiveClaude.exe --supervise` / `--service`) and registers *that* whenever the supervisor
  executable is not runnable. The zip and winget packages are unaffected and keep using the
  supervisor executable.
- **Two supervisors no longer run at once.** One starting while another is already up now stops with
  an explanation instead of silently supervising the same directories twice.
- A supervisor that cannot own the IPC endpoint says so once, instead of logging the same failure
  every second.
- The main window is created in code: clearing `StartupUri` throws, which crashed the headless modes
  before this.

## [1.0.6] - 2026-09-15

### Fixed

- **The Windows service could never be installed, elevation or not.** Its `sc.exe` options were
  passed through `ProcessStartInfo.ArgumentList`, which turns each `key= value` pair into a single
  quoted token; sc.exe parses its own command line and answered with a usage error (1639). Verified
  against the real sc.exe: the command lines LiveClaude builds now stop only at the permission check.
- **The service account gets the "Log on as a service" right.** `sc create obj=` does not grant it, so
  the service was created and then failed to start with error 1069. The elevated install now adds
  `SeServiceLogonRight` for the account.
- Start and stop of the service retry with elevation instead of failing with access denied, and
  sc.exe failures are translated (1069, 1057, 1073, access denied) rather than shown raw.
- A blank password for a user account is called out before the install, since Windows always refuses
  it for a service logon.

### Added

- `LiveClaude.Service.exe start-service` and `stop-service`.

## [1.0.5] - 2026-09-15

### Fixed

- **"Install / update task" failed with `ERROR: Access is denied`.** Windows only lets an
  administrator register a task with a boot trigger, and the app deliberately runs unelevated (a
  ClickOnce install cannot be launched as administrator at all). Installing now asks for elevation
  through the supervisor executable, and if the prompt is declined it falls back to a logon-only
  task, which a standard user may register — saying so instead of failing.
- The task is registered for the signed-in user even when UAC is answered with a different
  administrator account.
- **Registrations no longer break on update.** ClickOnce and winget replace the application folder on
  every update, leaving the task or service pointing at a path that no longer exists. When the app
  runs from such a location the supervisor is copied to `%LOCALAPPDATA%\LiveClaude\supervisor` first
  and registered from there.
- The outcome of a scheduled-task operation is shown under the scheduled-task card; it used to appear
  under the Windows service one.

## [1.0.4] - 2026-09-15

### Fixed

- **A running server's own environment could be deleted.** Protection relied on having resolved the
  environment id over the API, which is best effort; when that lookup had not happened (or failed),
  the environment a live server had just registered was shown as stale and could be selected. Force
  then appeared to "do nothing", because the running server registered again within seconds.
  Protection is now local and deterministic: a directory with a running server protects every
  registration made during that run — including one created after a delete — whether or not the id
  was ever resolved. Leftovers from earlier runs of the same directory stay deletable.
- The reason a row cannot be deleted is spelled out on the row: *"A server is running for this
  directory. Stop 'name' in the Dashboard first."*
- When a delete succeeds but the entry comes straight back, the app says so instead of looking inert.
- The bridge environment of a run is now resolved as soon as the server starts, with retries, rather
  than waiting for a "ready" line that did not always appear.

### Added

- The version is shown in the bottom-right corner and opens an **About** window with the description,
  copyright, licence and links.

## [1.0.3] - 2026-09-15

### Added

- **Force delete.** Some environments cannot be removed because the API still counts session records
  against them (`409 Conflict — Environment has N active sessions. Use force=true to delete anyway.`),
  which is what made a couple of entries impossible to clear. Failures now offer a **Force delete**
  button that retries with `force=true`, removing the environment together with those records, behind
  its own confirmation.
- **Failures are visible instead of silent.** The reason a delete failed is shown on the row itself
  and in a panel above the list, including Anthropic's `request-id`.
- **An environments log.** Every list and delete — status, request id, API message — is appended to
  `%ProgramData%\LiveClaude\logs\environments.log`, with an *Open log* button in the toolbar.

## [1.0.2] - 2026-09-15

### Added

- **Environments tab.** Lists the bridge environments registered on your Claude account — one per
  `claude remote-control` process, and they outlive the process, which is what fills the Claude
  Desktop picker with dead copies of the same project. Entries are grouped by directory and marked
  live, stale or untracked. **Select duplicates** keeps the live (or newest) environment per directory
  and selects the rest; **Select stale** picks the ones whose directory LiveClaude supervises but has
  no server for. Deletion asks for confirmation and can never include an environment in use.
- Each supervised server now records the bridge environment it registered, so the live one is
  identified exactly rather than guessed. Switch off with *Track which environment each server
  registers*.

### Fixed

- **Stopping a server no longer creates a dead entry.** Stop and restart used to terminate the
  process outright, so the CLI never deregistered. LiveClaude now sends Ctrl+C and waits (*Clean
  shutdown wait*, 12 s by default) before terminating.

## [1.0.1] - 2026-09-15

### Fixed

- **Dark theme for every control that still used the system chrome.** The spawn-mode and
  permission-mode pickers rendered as a white drop-down with near-white text, which made them
  unreadable. `ComboBox`, `ComboBoxItem`, `CheckBox`, `PasswordBox`, scrollbars, tooltips and context
  menus are now templated to match the rest of the app, and text boxes use the accent colour for
  selection.

### Changed

- Spawn and permission modes are shown exactly as the CLI spells them (`same-dir`, `worktree`,
  `session`, `default`, `acceptEdits`, `auto`, `bypassPermissions`, `dontAsk`, `plan`), in the pickers
  and on the dashboard, so the UI and the command preview read the same.

## [1.0.0] - 2026-09-15

First release.

### Added

- **Supervisor** that runs one `claude remote-control` server per project directory, restarts it with
  exponential backoff after a crash, and reattaches to the previous session with `--continue` while
  Remote Control's four-hour window is still open.
- **Two hosts**: a Windows scheduled task (logon + boot triggers, restart on failure, no time limit)
  and a Windows service (automatic start, `sc.exe` failure actions), both installable from the app or
  the CLI.
- **Desktop app** (WPF): dashboard with live instance state and session URLs, session editor covering
  every `remote-control` flag with a command preview, log viewer, and hosting management.
- **Embedded terminal**: ConPTY host and a VT/ANSI emulator written in C#, used for the workspace-trust
  prompt, the one-time Remote Control confirmation and `/login` — and for attaching to a running server.
- **Named-pipe IPC** between the app and the supervisor, including terminal streaming, so the app can be
  closed and reopened without touching the running servers.
- Claude CLI discovery across native installs, `PATH` and the versioned copy shipped with the Claude
  desktop app.
- Rolling per-instance logs and a supervisor log under `%ProgramData%\LiveClaude\logs`.
- Packaging: portable zips (x64, arm64, self-contained), ClickOnce, and winget manifests.

[1.0.11]: https://github.com/LeandroCannizzaro/LiveClaude/releases/tag/v1.0.11
[1.0.10]: https://github.com/LeandroCannizzaro/LiveClaude/releases/tag/v1.0.10
[1.0.9]: https://github.com/LeandroCannizzaro/LiveClaude/releases/tag/v1.0.9
[1.0.8]: https://github.com/LeandroCannizzaro/LiveClaude/releases/tag/v1.0.8
[1.0.7]: https://github.com/LeandroCannizzaro/LiveClaude/releases/tag/v1.0.7
[1.0.6]: https://github.com/LeandroCannizzaro/LiveClaude/releases/tag/v1.0.6
[1.0.5]: https://github.com/LeandroCannizzaro/LiveClaude/releases/tag/v1.0.5
[1.0.4]: https://github.com/LeandroCannizzaro/LiveClaude/releases/tag/v1.0.4
[1.0.3]: https://github.com/LeandroCannizzaro/LiveClaude/releases/tag/v1.0.3
[1.0.2]: https://github.com/LeandroCannizzaro/LiveClaude/releases/tag/v1.0.2
[1.0.1]: https://github.com/LeandroCannizzaro/LiveClaude/releases/tag/v1.0.1
[1.0.0]: https://github.com/LeandroCannizzaro/LiveClaude/releases/tag/v1.0.0
