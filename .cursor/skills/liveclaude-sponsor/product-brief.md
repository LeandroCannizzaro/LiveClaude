# LiveClaude product brief (sponsor)

## One-liner

Keep Claude Code Remote Control and Cursor My Machines alive across crashes, logouts, and reboots — one supervisor, many sessions, desktop app with a real embedded terminal. Windows, Linux, and macOS. Entirely C#.

## Problem

Claude Code Remote Control (and Cursor My Machines workers) run as foreground processes in a terminal. Close the window, hit a crash, or reboot — the phone / remote client loses the machine.

## Solution

LiveClaude turns that into infrastructure:

- Multiple agent products (Claude Code Remote Control, Cursor My Machines), late-loaded assemblies
- One long-lived process per session entry with product-native flags
- OS-native supervisor restart (Scheduled Task / Windows Service, systemd, LaunchAgent / LaunchDaemon)
- Avalonia desktop app: create/edit sessions, live state, logs, install/remove supervisor
- Embedded real pty (ConPTY on Windows) + VT emulator in C# for trust / login / attach

Not: Node, Python, tmux, or browser automation.

## Links (canonical)

| What | URL |
|------|-----|
| Repo | https://github.com/LeandroCannizzaro/LiveClaude |
| Product page | https://leandrocannizzaro.github.io/LiveClaude/ |
| Latest download | https://github.com/LeandroCannizzaro/LiveClaude/releases/latest |
| Issues | https://github.com/LeandroCannizzaro/LiveClaude/issues |

## Install one-liners (verify against README if stale)

- Windows winget: `winget install LeandroCannizzaro.LiveClaude`
- Linux: `curl -fsSL https://leandrocannizzaro.github.io/LiveClaude/install.sh | sh`
- macOS: `curl -fsSL https://leandrocannizzaro.github.io/LiveClaude/install-macos.sh | sh`

Also: portable zip, ClickOnce (Windows), `.deb` / `.rpm` / `.dmg`.

## Requirements (do not oversell)

- OS: Windows 10 1809+ / 11, Linux with systemd, macOS 12+
- Claude Remote Control needs Claude Pro/Max/Team/Enterprise (API keys / Bedrock / Vertex / Foundry not supported by Remote Control)
- Prefer native `claude install stable` over Claude desktop-bundled CLI paths

## Positioning do / don't

**Do say**

- Supervisor + desktop app for remote agent sessions
- Survives crash / sign-out / reboot (via OS autostart + restart policy)
- Cross-platform, same Avalonia app
- Open source (MIT), written in C# / .NET

**Don't say**

- "Official" Claude or Cursor product
- Guarantees of 100% uptime
- Support for API-key-only Claude Remote Control
- That macOS is notarised (it is not yet — Gatekeeper needs right-click Open / install script)

## Audience

1. Claude Code users who use Remote Control from phone / claude.ai
2. Cursor users of My Machines / remote workers
3. .NET / terminal enthusiasts (secondary)

## Tone

Direct, technical, slightly dry. Problem-first. No hype adjectives. Italian with Leandro when chatting; English for public posts unless asked otherwise.

## Competitive honesty

Alternatives people invent: `tmux` + scripts, systemd unit by hand, keeping a terminal window open. LiveClaude's edge: multi-session UI, product-aware flags, embedded terminal for one-shot auth/trust, one install path across OSes.
