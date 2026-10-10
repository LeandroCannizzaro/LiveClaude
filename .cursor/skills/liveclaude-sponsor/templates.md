# Copy templates

Fill brackets from [product-brief.md](product-brief.md) and the latest release. Keep under each channel's soft limit.

---

## Show HN (Hacker News)

**Title**

```
Show HN: LiveClaude – keep Claude Code / Cursor remote sessions alive across reboot
```

**Body**

```
Claude Code Remote Control (and Cursor My Machines) run as a foreground process in a terminal.
Close the window, crash, or reboot — and your phone loses the machine.

LiveClaude turns that into a supervised service:

- one long-lived process per project/session
- OS-native restart (Scheduled Task / systemd / LaunchAgent)
- Avalonia desktop app + real embedded pty for trust/login flows
- Windows, Linux, macOS — all C# / .NET

MIT: https://github.com/LeandroCannizzaro/LiveClaude
Product page: https://leandrocannizzaro.github.io/LiveClaude/

Happy to answer questions about the supervisor model or the VT emulator.
```

**Notes:** Weekday morning US time. Attach dashboard screenshot in comments if useful. Don't ask for upvotes.

---

## Reddit (r/ClaudeAI or r/cursor)

**Title**

```
I built an open-source supervisor so Claude Code Remote Control survives reboot (LiveClaude)
```

For Cursor-focused subs, swap the product name in the title.

**Body**

```
**Problem:** Remote Control / My Machines dies if the terminal closes, the process crashes, or the machine reboots.

**What LiveClaude does:** runs each session under an OS supervisor, with a desktop app to manage many projects and an embedded terminal for the one-time trust / login prompts.

- Windows / Linux / macOS
- MIT, C#
- Install: see README (winget / curl installers / releases)

Repo: https://github.com/LeandroCannizzaro/LiveClaude
Site: https://leandrocannizzaro.github.io/LiveClaude/

I'm the author — feedback and bug reports welcome.
```

**Notes:** Use self-promotion flair if required. Disclose authorship. Prefer GIF of reboot → session still listed.

---

## Mastodon / Bluesky (short)

```
Claude Code Remote Control dies when the terminal dies.

LiveClaude keeps sessions up across crash/reboot: supervisor + desktop app + embedded terminal.
Windows, Linux, macOS. MIT.

https://github.com/LeandroCannizzaro/LiveClaude
```

Optional tags: `#ClaudeCode` `#Cursor` `#dotnet` `#opensource` (max 3).

---

## Helpful reply (someone else's thread)

Use only when their pain matches. Keep short.

```
I've hit the same thing — Remote Control is just a foreground process, so anything that
kills the terminal drops the session.

I open-sourced LiveClaude to run those sessions under an OS supervisor (plus a small
desktop UI). Might help: https://github.com/LeandroCannizzaro/LiveClaude

(I'm the author.)
```

---

## Release announcement

**Title**

```
LiveClaude [VERSION] — [one concrete change]
```

**Body**

```
[2–4 bullets of user-visible changes]

Download: https://github.com/LeandroCannizzaro/LiveClaude/releases/tag/[TAG]
Full notes: [link or "in the release"]

Upgrade:
- Windows: winget upgrade LeandroCannizzaro.LiveClaude  (or ClickOnce / zip)
- Linux / macOS: re-run the install script from the product page
```

---

## Awesome-list PR blurb

```
### [LiveClaude](https://github.com/LeandroCannizzaro/LiveClaude)

Keep Claude Code Remote Control and Cursor My Machines sessions alive across crashes and reboots — supervisor + cross-platform desktop app (C#).
```

Match the target list's entry format exactly when opening a PR.
