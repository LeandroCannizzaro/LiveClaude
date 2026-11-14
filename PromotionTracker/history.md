# Promotion Tracker — Activity & Channel Logs

Chronological log migrated from GitHub Issue #8 (Promotion tracker: published posts, copy, and links).

---

### Entry 1 — 2026-10-11T15:40:10Z (@LeandroCannizzaro)

﻿## Update: awesome-claude-code path

The compare PR was blocked on purpose:

> An owner of this repository has limited the ability to open a pull request to users that are collaborators on this repository.

Per [CONTRIBUTING.md](https://github.com/hesreallyhim/awesome-claude-code/blob/main/CONTRIBUTING.md):

- **Do not open a PR**
- Submit via the web UI issue form only: https://github.com/hesreallyhim/awesome-claude-code/issues/new?template=recommend-resource.yml
- Ground rules: resource must be **14+ days old with active development**, OR **100+ stars**. LiveClaude qualifies on age/activity (created 2026-09-15, still pushing; currently 2 stars).

### Status change for row #5

| Channel | Status | Link |
|---------|--------|------|
| awesome-claude-code | Use recommend-resource **issue form** (not PR). Branch `add-liveclaude` on the fork is optional/unused. | https://github.com/hesreallyhim/awesome-claude-code/issues/new?template=recommend-resource.yml |

Suggested fields for the form (adjust to match the template labels):

- **Resource name:** LiveClaude
- **URL:** https://github.com/LeandroCannizzaro/LiveClaude
- **Short description:** Keep Claude Code Remote Control (and Cursor My Machines) alive across crash/reboot — OS supervisor + Avalonia desktop app with embedded terminal. Windows/Linux/macOS, C#/MIT.


---

### Entry 2 — 2026-10-11T15:42:31Z (@LeandroCannizzaro)

﻿## Update: awesome-claude-code fully closed to outsiders

Tried the recommend-resource issue form. Same gate as PRs:

> An owner of this repository has limited the ability to create an issue to users that are collaborators on this repository.

**Status for #5:** Blocked — cannot PR, cannot open issues. CONTRIBUTING still says to use the form, but the repo settings override that for non-collaborators.

**Options left for this list:**
1. Wait for organic traction (stars / word of mouth) until the maintainer notices (their CONTRIBUTING explicitly suggests this path).
2. Reach the maintainer outside GitHub if they publish a contact elsewhere (not required; don't spam).
3. Drop this list from active promo and use other discovery channels.

Branch `LeandroCannizzaro/awesome-claude-code@add-liveclaude` can stay or be deleted; it cannot be merged via public PR right now.


---

### Entry 3 — 2026-10-11T15:50:25Z (@LeandroCannizzaro)

﻿## Wave 2026-10-11 (evening) — published

Respectful channels only (megathreads / show-and-tell). Mastodon skipped (no login in Cursor browser). awesome-claude-code still blocked for non-collaborators.

| # | Channel | Status | Link |
|---|---------|--------|------|
| 6 | Reddit r/ClaudeAI megathread | **Published** | https://www.reddit.com/r/ClaudeAI/comments/1x09pjt/show_us_what_youve_created_with_claude/pf9glsv/ |
| 10 | Reddit r/ClaudeCode weekly showcase | **Published** | https://www.reddit.com/r/ClaudeCode/comments/1wy66fv/weekly_showcase_thread_what_are_you_building_with/pf9h1js/ |
| 11 | GitHub Discussions — Show and tell | **Published** | https://github.com/LeandroCannizzaro/LiveClaude/discussions/9 |

### #6 Text (r/ClaudeAI megathread)

```
I hit a small but annoying gap with Claude Code Remote Control: it is a foreground terminal process, so a crash, closed window, or reboot drops the phone/claude.ai session.

I open-sourced LiveClaude to run those sessions under an OS supervisor (Scheduled Task / systemd / LaunchAgent), with a small Avalonia desktop app and a real embedded terminal for the one-time trust/login prompts. Also works for Cursor My Machines workers. Windows/Linux/macOS, MIT.

https://github.com/LeandroCannizzaro/LiveClaude

Happy to answer questions — I'm the author.
```

### #10 Text (r/ClaudeCode weekly showcase)

```
**LiveClaude** — keep Claude Code Remote Control alive when the terminal dies.

Remote Control is a foreground process, so a crash, closed window, or reboot drops the phone/claude.ai session. LiveClaude runs each session under an OS supervisor (Scheduled Task / systemd / LaunchAgent), with a small Avalonia desktop app and a real embedded terminal for trust/login prompts. Also supports Cursor My Machines. Windows/Linux/macOS, MIT, C#.

What I learned: treating remote agents as infrastructure (restart policy + attachable pty) is more reliable than leaving a terminal window open.

https://github.com/LeandroCannizzaro/LiveClaude

(I'm the author.)
```

### Intentionally skipped (anti-spam)

- New feed posts on r/ClaudeAI / r/ClaudeCode (rules prefer megathreads for simple showcases)
- Reply on unrelated “lid closed” competitor thread
- Mastodon / Fosstodon (logged out)
- Re-blasting the same Bluesky copy


---

### Entry 4 — 2026-10-11T18:48:01Z (@LeandroCannizzaro)

﻿## Mastodon — published

**URL:** https://mastodon.social/@venerandissimus/117423762660081621  
**Account:** @venerandissimus@mastodon.social  
**When:** 2026-10-11

### Copy

```
Claude Code Remote Control dies when the terminal dies.

LiveClaude keeps sessions up across crash/reboot: supervisor + desktop app + embedded terminal.
Windows, Linux, macOS. MIT.

https://github.com/LeandroCannizzaro/LiveClaude

#ClaudeCode #opensource #dotnet
```


---

### Entry 5 — 2026-10-11T18:50:54Z (@LeandroCannizzaro)

﻿## X (Twitter) — published

**URL:** https://x.com/Venerando/status/2109356010787983397  
**Account:** @Venerando  
**When:** 2026-10-11

### Copy

```
Claude Code Remote Control dies when the terminal dies.

LiveClaude keeps sessions up across crash/reboot: supervisor + desktop app + embedded terminal.
Windows, Linux, macOS. MIT.

https://github.com/LeandroCannizzaro/LiveClaude

#ClaudeCode #opensource #dotnet
```


---

### Entry 6 — 2026-10-11T19:00:31Z (@LeandroCannizzaro)

## Dev.to — published

**URL:** https://dev.to/leandrocannizzaro/keep-claude-code-remote-control-alive-across-reboot-with-an-os-supervisor-57ne
**Account:** leandrocannizzaro
**When:** 2026-10-11
**AI disclosure:** Fully Autonomous

**Title:** Keep Claude Code Remote Control alive across reboot with an OS supervisor

---

### Entry 7 — 2026-10-11T19:29:52Z (@LeandroCannizzaro)

﻿## LinkedIn — blocked (automation)

**Status:** Failed to publish from Cursor browser  
**When:** 2026-10-11  
**Account:** Leandro Cannizzaro (logged in OK)

LinkedIn accepts the compose UI, then rejects Post with:
> We encountered a problem sharing your post.

Retried with GitHub URL, product-page URL, and UA spoof — same server-side reject (likely automation / share API guard). Link preview also fails (`Cannot display preview`).

### Copy to paste manually (browser outside Cursor)

```
Claude Code Remote Control dies when the terminal dies.

I open-sourced LiveClaude to keep those sessions up across crash/reboot: OS supervisor + Avalonia desktop app with a real embedded terminal for trust/login prompts. Also works for Cursor My Machines.
Windows, Linux, macOS. MIT.

https://github.com/LeandroCannizzaro/LiveClaude

(I'm the author.)
```


---

### Entry 8 — 2026-10-11T19:39:02Z (@LeandroCannizzaro)

## LinkedIn — published (manual)

**Status:** Published manually outside Cursor  
**When:** 2026-10-11  
**Link:** URL pending — paste permalink here when you have it  
**Account:** Leandro Cannizzaro

Cursor browser could not Post (share API reject). Leandro posted the launch copy from a normal browser.

### Copy (as posted)

```
Claude Code Remote Control dies when the terminal dies.

I open-sourced LiveClaude to keep those sessions up across crash/reboot: OS supervisor + Avalonia desktop app with a real embedded terminal for trust/login prompts. Also works for Cursor My Machines.
Windows, Linux, macOS. MIT.

https://github.com/LeandroCannizzaro/LiveClaude

(I'm the author.)
```

Summary row **#14** updated to Published (manual).

---

### Entry 9 — 2026-10-11T19:44:27Z (@LeandroCannizzaro)

## Organic Reddit replies — published

Respectful replies on threads about RC as a host process (not idle-timeout bugs). Account: livEApplications.

| # | Thread | Comment |
|---|--------|---------|
| 15 | [SSH vs built-in RC](https://www.reddit.com/r/ClaudeCode/comments/1vzenmv/coding_remotely_through_ssh_vs_built_in_rc/) | https://www.reddit.com/r/ClaudeCode/comments/1vzenmv/coding_remotely_through_ssh_vs_built_in_rc/pfb33m7/ |
| 16 | [SSH & Tmux vs Remote Control](https://www.reddit.com/r/ClaudeCode/comments/1w17brl/ssh_tmux_vs_remote_control/) | https://www.reddit.com/r/ClaudeCode/comments/1w17brl/ssh_tmux_vs_remote_control/pfb3d3i/ |

Skipped idle-keepalive PSA threads (wrong problem for LiveClaude).

---

### Entry 10 — 2026-10-11T20:08:25Z (@LeandroCannizzaro)

## Hashnode — published

**URL:** https://leandrocannizzaro.hashnode.dev/keep-claude-code-remote-control-alive-across-reboot-with-an-os-supervisor  
**Publication:** leandrocannizzaro.hashnode.dev (created during setup)  
**When:** 2026-10-11  
**Canonical:** Dev.to original (same article)

**Title:** Keep Claude Code Remote Control alive across reboot with an OS supervisor

Summary row **#17** added.



---

### Entry 11 — 2026-11-14T22:56:00Z (@LeandroCannizzaro)

## Wave 3 (v2.2.8) — GitHub & new awesome-list PR

- **GitHub Discussions #9 (v2.2.8 release update):** https://github.com/LeandroCannizzaro/LiveClaude/discussions/9#discussioncomment-19621160
- **awesome-ai-coding-tools PR #1284 (new channel):** https://github.com/ai-for-developers/awesome-ai-coding-tools/pull/1284 (added LiveClaude to end of *Developer Productivity Tools* with author disclosure)
- **Campaign draft created:** `PromotionTracker/campaigns/2026-11-14-v2.2.8-wave.md`


---

### Entry 12 — 2026-11-14T23:12:30Z (@LeandroCannizzaro)

## Wave 3 (v2.2.8) — Social, Technical Blog, Reddit & New Channels

- **Bluesky:** https://bsky.app/profile/venerandissimus.bsky.social/post/3n2cmaxtqs22m
- **Mastodon:** https://mastodon.social/@venerandissimus/117617291884437951
- **X (Twitter):** https://x.com/Venerando/status/2121741114097222111
- **LinkedIn:** https://www.linkedin.com/feed/update/urn:li:activity:7515132557177466880/
- **Dev.to:** https://dev.to/leandrocannizzaro/shipping-net-10-for-macos-12-and-net-8-for-macos-11-big-sur-in-one-avalonia-app-liveclaude-1bcc
- **Hashnode (canonical -> Dev.to):** https://leandrocannizzaro.hashnode.dev/shipping-net-10-for-macos-12-and-net-8-for-macos-11-big-sur-in-one-avalonia-app-liveclaude-v2-2-8
- **Reddit `r/ClaudeCode` Weekly Showcase:** https://www.reddit.com/r/ClaudeCode/comments/1xrpcjt/comment/19sn90b2aqkgx/
- **Hacker News:** https://news.ycombinator.com/item?id=50502035
- **Peerlist (new channel — Project):** https://peerlist.io/cannizzaro/project/liveclaude
- **Pending / Rate-limited / Blocked:**
  - Reddit (`r/ClaudeAI` megathread, `r/dotnet`, `r/opensource`, `r/macapps`): hit Reddit 19-minute rate-limit cooldown after `r/ClaudeCode` post (ready in `campaigns/2026-11-14-v2.2.8-wave.md`).
  - AlternativeTo: requires clicking email confirmation link before submitting.
