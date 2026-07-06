<img src="assets/logo.svg" width="72" align="left" alt="NoKill life ring logo"/>

# NoKill

A Windows rescue tool for applications that freeze or go "Not Responding" — built on the principle that killing the process is the last resort, not the first. The life ring is the mission: **rescue without harm**.

Task Manager is a hammer. NoKill detects hung apps, figures out *why* they're stuck (hidden dialogs, wait chains, blocked dependencies), preserves recovery artifacts, and only then talks about anything drastic.

## Safety doctrine

- NoKill never kills, restarts, or injects into a process by default.
- NoKill preserves recovery artifacts **before** attempting any intervention.
- Detection is conservative: a window is only reported "Not Responding" when multiple independent signals agree. Busy ≠ hung.
- Local-first: no telemetry, no cloud, no uploads.

## Current state (v0.1 slice)

- `NoKill.Win32` — Win32 interop (CsWin32): window enumeration (z-order, owner chain, enabled state), `IsHungAppWindow`, `WM_NULL` ping via `SendMessageTimeout`. All read-only except `WindowActions` — the single, auditable place allowed to touch another window (z-order raise only, no activation, no input).
- `NoKill.Diagnostics` — `WindowInventoryService` (desktop snapshot), `HangScorer` and `BlockerClassifier` (pure, tested signal/window-facts logic), and `WaitChainAnalyzer` + `WaitChainInterpreter`: Wait Chain Traversal answers *why* a process is stuck — deadlock cycles, mutex owners, cross-process waits, SendMessage/COM/RPC waits, network I/O — in plain English. Honest about its limits: waits WCT can't attribute (events, .NET managed locks) are reported as such, never guessed at.
- `NoKill.Automation` — `HiddenDialogDetector` (finds modal dialogs hiding behind their owner or off-screen) + `DialogContentReader` (UI Automation, reads what the dialog is asking). Offers one action: **Reveal** — raise the dialog so the user can answer it.
- `NoKill.Vault` — the Recovery Vault: preserves rescue evidence (JSON + human-readable reports, process info, window list, screenshot, wait chains, minidump, recovery artifacts) into a per-incident folder under `Documents\NoKill\Vault\`. Copy-only: sources are never modified, entries are never overwritten, and problems become warnings, not aborted preserves.
- Minidump capture (`MiniDumpWriter`, Win32): every preserve includes a **triage** dump by default — thread stacks, handle data, unloaded modules, small enough to keep — with `--dump full` for full-memory dumps and `--dump none` to skip. Read-only observation: the target is briefly suspended for a consistent snapshot and resumes untouched. Dumps are staged on the vault volume and *moved* into the entry, never written twice.
- `NoKill.App` — WPF dashboard with two tabs: **Live monitor** (window list with hang status, Preserve/Diagnose per row, suspected-blockers panel with Reveal, auto-refresh every 3 s) and **Freeze history** (recorded incidents with durations and insights, top-offenders summary, one-click jump to each incident's vault entry).
- `NoKill.Cli` — same scan as a console table (`--flagged-only`, `--reveal`, `--preserve <pid> [--dump triage|full|none]`, `--waitchain <pid>`, `--snapshot <pid> --research`; exit code 3 signals a detected deadlock).
- `NoKill.Research` / `NoKill.Sdk` — the **research branch**, walled off from stable mode. `NoKill.Research` wraps `PssCaptureSnapshot` for diagnostic process snapshots (a copy-on-write inspection view, *not* a runnable clone). `NoKill.Sdk` (netstandard2.0) is the cooperative recovery library apps embed: `RecoveryCheckpoint` journals unsaved state from a background thread so a checkpoint survives even a frozen UI, and NoKill discovers it. Nothing here runs in the default rescue path — it is reachable only via the CLI's `--research` opt-in.
- **Freeze history** — a local SQLite log (`Documents\NoKill\history.db`) of every incident: process, start/end times, trigger (manual vs watchdog), vault entry, and the top diagnostic insight. `NoKill.Cli --history [n]` shows recent incidents and the top offenders, turning one-off rescues into patterns ("Blender has frozen 9 times this month"). Local-first like everything else.
- **Tray / startup mode** — the dashboard lives in the notification area: closing or minimizing hides to the tray (the icon turns red while any app is Not Responding, and balloon tips announce watchdog preserves), double-click reopens, and the tray menu offers Open / Watchdog toggle / Start with Windows / Exit. `NoKill.App --minimized` starts hidden with the watchdog armed — exactly what the run-at-login registration uses (per-user Run key, no elevation; also scriptable via `--install-startup` / `--uninstall-startup`).
- **Watchdog mode** — NoKill as a guardian instead of a tool you reach for: `NoKill.Cli --watch` (or the dashboard's "Auto-preserve on freeze" toggle) scans continuously and automatically preserves the full evidence package the moment any app is confirmed frozen. Conservative by construction: a freeze must persist past a confirm window (default 10 s) before it becomes an incident, each incident preserves exactly once, recovery re-arms detection, and a per-process cooldown (default 2 min) prevents evidence spam from a flapping app. Detection stays read-only — the watchdog preserves, it never intervenes.
- `samples/HungDemoApp` — deliberately misbehaving lab rat: freeze-on-click, deadlock, hidden modal dialog; `--auto-freeze <delayMs> <durationMs>` and `--auto-hidden-modal` for automated tests.
- `NoKill.Profiles` — rescue profiles as pure data, covering **any** process, not just known apps:
  - a **universal heuristic profile** applies to every process (crash dumps, `%APPDATA%`/`%LOCALAPPDATA%` folders matching the process name, `%TEMP%` files, logs beside the executable, autosave/backup filename patterns) with conservative age/count caps;
  - **built-in profiles** layer precision on top for 21 apps: Blender, Roblox Studio/Player, Visual Studio, VS Code, Notepad++, Office, Chrome, Discord, Photoshop, Premiere Pro, FL Studio, OBS Studio, Unity, Firefox, Edge, Windows 11 Notepad (full unsaved-tab contents), Audacity, Krita, the JetBrains IDE family, Sublime Text (unsaved buffers in the session file), and Paint.NET;
  - **user JSON profiles** in `Documents\NoKill\Profiles\*.json` add or override any of the above without recompiling.
  Windowless services and background processes are first-class: preserve works by PID with no window (no screenshot, everything else intact).

## Build & test

```
dotnet build
dotnet test
dotnet run --project src/NoKill.Cli        # console scan
dotnet run --project src/NoKill.App        # dashboard
```

## Packaging & signing

```
powershell -ExecutionPolicy Bypass -File scripts\package.ps1
```

Produces `artifacts\NoKill-<version>-win-x64.msi`: a signed, self-contained
win-x64 installer (no .NET runtime required on the target machine). It installs
the dashboard and CLI to Program Files, adds a Start-menu shortcut and puts the
install folder on PATH so `NoKill.Cli --history` works from any terminal.
Versioning comes from `Directory.Build.props`; the MSI carries proper upgrade
logic, so newer versions install over older ones cleanly.

**Updates**: NoKill checks GitHub Releases for a newer version once a day (and
on demand from the tray menu). This is the product's *only* outbound network
call — an anonymous GET of the public releases feed, carrying nothing but the
request itself — and it can be turned off from the tray ("Check for updates
automatically"). When an update exists, an "Update to x.y.z" button appears in
the dashboard header and a tray balloon mentions it once; one click downloads
the MSI and upgrades in place.

**Signing**: executables and the MSI are Authenticode-signed and timestamped.
By default a self-signed `CN=NoKill Dev Signing` certificate is created/reused —
structurally valid signatures that prove the pipeline but are untrusted on
other machines. For distribution, set `NOKILL_CERT_THUMBPRINT` to the
thumbprint of a real code-signing certificate (Azure Trusted Signing or an
OV/EV certificate) in the current-user store; no script changes needed.
WiX v5 is pinned as a repo-local dotnet tool (v6+ requires a fee-based EULA).

## Roadmap

1. ✅ Window inventory + conservative hang detection
2. ✅ Hidden-dialog / blocker detection + Reveal (UI Automation)
3. ✅ Recovery Vault (reports, screenshot, process info; artifact engine ready for profiles)
4. ✅ Rescue profiles: universal heuristics + built-ins + user JSON, windowless preserve
5. ✅ Wait Chain Traversal diagnostics (deadlock cycles, blocker attribution, vault integration)
6. ✅ Minidump capture into the vault (triage default, full opt-in)
7. ✅ Background watchdog mode (auto-preserve on confirmed freeze, CLI + dashboard)
8. ✅ Freeze history (SQLite incident log, `--history` query, top offenders)
9. ✅ Tray / startup mode (resident guardian, minimize-to-tray, run-at-login)
10. ✅ Research branch: Pss process snapshots + cooperative recovery SDK (walled off, `--research` gated) — see [docs/responsive-clone-research.md](docs/responsive-clone-research.md)
