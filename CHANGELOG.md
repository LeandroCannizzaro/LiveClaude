# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[semantic versioning](https://semver.org/).

## [Unreleased]

### Added

- **Linux and macOS.** LiveClaude runs on Windows, Linux and macOS, with the same supervisor, the
  same management app and the same embedded terminal. See [#2](https://github.com/LeandroCannizzaro/LiveClaude/issues/2)
  for the plan this followed.
- **Autostart on every platform.** A systemd user unit or a LaunchAgent for the signed-in session, a
  systemd system unit or a LaunchDaemon for before sign-in — alongside the scheduled task and the
  Windows service, which are unchanged. Each host carries its own failure knowledge: missing
  lingering, an unreachable session bus, launchd's bootstrap errors, and Windows' 1069 and friends.
- **Native packages.** `.deb`, `.rpm` and self-contained tar.gz for Linux (x64 and arm64), an `.app`
  bundle in a `.dmg` for macOS (Intel and Apple silicon). The Windows zip, winget and ClickOnce
  packages are unchanged.
- `install-autostart` / `uninstall-autostart` / `start-autostart` / `stop-autostart`, with
  `--scope user|system`. `install-task` and `install-service` remain as aliases.
- `LIVECLAUDE_ROOT` overrides the configuration root on any platform. The systemd system unit uses it
  to point a daemon at a shared location instead of root's home directory.

### Changed

- **The operating system lives behind one contract.** Everything that has to differ — the pseudo
  terminal, autostart, elevation, the IPC transport, file locations, finding the CLI — sits behind
  `IPlatform` in `LiveClaude.Abstractions` and is implemented in `LiveClaude.Platform.<OS>`, loaded
  by name at run time. Nothing references those assemblies at compile time, so `LiveClaude.Core`
  dropped from `net10.0-windows` to `net10.0` and a package for one system ships no binaries
  belonging to another.
- **The management app moved from WPF to Avalonia**, on Windows too. One UI for three systems rather
  than one per system. The theme shrank by more than half, because Avalonia's Fluent theme supplies
  the control chrome that WPF made us template by hand.
- The VT emulator moved into `LiveClaude.Vt`, which has no UI dependency and is testable anywhere.
- The test suite is now three projects: portable, platform (runs everywhere through `IPlatform`) and
  Windows-only.
- On macOS, Claude Code's credentials are read from the login Keychain, which is where the CLI keeps
  them there; the `~/.claude/.credentials.json` file remains the source on Windows and Linux.

### Fixed

- Listening on the IPC endpoint twice inside one process now fails on Windows as it already did on
  POSIX. Windows is happy to let a process open many instances of its own named pipe — that is how
  one serves several clients — so a duplicate supervisor in a single process went unnoticed there.
- The supervisor socket on macOS no longer exceeds the 104-byte limit a Unix domain socket path has
  there. It lived under `~/Library/Application Support/LiveClaude/run/`, which fit for a short user
  name and failed for a long one; it now sits in a short per-user directory created 0700
  (`/tmp/liveclaude-<uid>/`). An over-long path is reported with its own length instead of as an
  `ArgumentOutOfRangeException` naming a parameter.

## [1.0.15] - 2026-09-15

### Changed

- **Back to a single deployment folder**, and a guard instead. 1.0.14 gave each build its own folder
  to dodge the file locks; that also moved the path on every update, which is exactly what a
  scheduled task or service registration must not do. The copy lives at
  `%LOCALAPPDATA%\LiveClaude\supervisor` again, and **anything running from that folder is stopped
  before it is refreshed** — the hosts first, then any process left over from an earlier attempt.
  Per-version folders left behind by 1.0.14 are removed.

### Fixed

- The real holder of the lock has a name now: an installer from a build old enough to still have the
  argument-parsing bug would turn itself into a supervisor and never exit, keeping `LiveClaude.exe`
  open. Every refresh then skipped that file and every install ran — and re-created — the same old
  build. The guard above breaks that loop.

## [1.0.14] - 2026-09-15

### Fixed

- **The registered copy could never be updated, so installs kept running an old build.** The
  supervisor was deployed to one fixed folder and launched from there, which meant it held its own
  executable open: an update replaced the files nothing was using and silently skipped
  `LiveClaude.exe` and `LiveClaude.dll`. Every elevated install therefore ran the build from before
  the update — on an old enough build, one that exits 0 without doing anything. Each build is now
  deployed to a folder named after its version, which cannot be in use when it is created; folders of
  builds nothing is running any more are removed.
- Anything still holding files in the deployment folder is stopped before registering, and the result
  says which processes were stopped.
- Every install result — success or failure — now states the build that was registered, and failures
  include the notes (locked files, chosen executable) that were previously dropped.
- The card shows the running build and the deployed one side by side, so a stale copy is visible
  before it causes trouble.
- The app logs each elevation attempt (executable, build, exit code) to `install.log`, so an elevated
  process that writes nothing itself still leaves evidence of what ran.
- Choosing to install as LocalSystem no longer clears the account box behind your back.

## [1.0.13] - 2026-09-15

### Fixed

- **Installs kept running an old build.** The scheduled task runs the copy of LiveClaude under
  `%LOCALAPPDATA%\LiveClaude\supervisor`, so while the task is running those files are locked — and
  the refresh skipped them without a word. Every elevated install therefore ran whatever build was
  there before the update, which on an older build meant doing nothing at all and exiting 0. The copy
  is refreshed before any registration now: what is running is stopped first, and if a file still
  cannot be replaced the card says so instead of registering a stale build.
- The result of an install names the build it registered, so "nothing happened" can be told apart
  from "an old build happened".

## [1.0.12] - 2026-09-15

### Added

- **An install log.** Install and uninstall commands normally run elevated, in a process nobody sees,
  so a failure inside them left no trace anywhere. Everything they print, plus the command line (with
  the password redacted) and the exit code, is now written to
  `%ProgramData%\LiveClaude\logs\install.log`, and the app shows the last lines when the result does
  not match what was asked for.

### Fixed

- A service for a user account is refused before elevating when the password box is empty: Windows
  never accepts a blank password for a service logon, so the old path elevated, created the service
  and only then failed with 1069.

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

[1.0.15]: https://github.com/LeandroCannizzaro/LiveClaude/releases/tag/v1.0.15
[1.0.14]: https://github.com/LeandroCannizzaro/LiveClaude/releases/tag/v1.0.14
[1.0.13]: https://github.com/LeandroCannizzaro/LiveClaude/releases/tag/v1.0.13
[1.0.12]: https://github.com/LeandroCannizzaro/LiveClaude/releases/tag/v1.0.12
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
