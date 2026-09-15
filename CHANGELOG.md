# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[semantic versioning](https://semver.org/).

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

[1.0.1]: https://github.com/LeandroCannizzaro/LiveClaude/releases/tag/v1.0.1
[1.0.0]: https://github.com/LeandroCannizzaro/LiveClaude/releases/tag/v1.0.0
