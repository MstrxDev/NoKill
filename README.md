# NoKill

A Windows rescue tool for applications that freeze or go "Not Responding" — built on the principle that killing the process is the last resort, not the first.

Task Manager is a hammer. NoKill detects hung apps, figures out *why* they're stuck (hidden dialogs, wait chains, blocked dependencies), preserves recovery artifacts, and only then talks about anything drastic.

## Safety doctrine

- NoKill never kills, restarts, or injects into a process by default.
- NoKill preserves recovery artifacts **before** attempting any intervention.
- Detection is conservative: a window is only reported "Not Responding" when multiple independent signals agree. Busy ≠ hung.
- Local-first: no telemetry, no cloud, no uploads.

## Current state (v0.1 slice)

- `NoKill.Win32` — read-only Win32 interop (CsWin32): window enumeration, `IsHungAppWindow`, `WM_NULL` ping via `SendMessageTimeout`.
- `NoKill.Diagnostics` — `WindowInventoryService` (desktop snapshot) + `HangScorer` (pure, tested signal combination).
- `NoKill.App` — WPF dashboard listing windows with live hang status, auto-refresh every 3 s.
- `NoKill.Cli` — same scan as a console table, for scripting and testing (`--flagged-only`).
- `samples/HungDemoApp` — deliberately misbehaving lab rat: freeze-on-click, deadlock, hidden modal dialog, and `--auto-freeze <delayMs> <durationMs>` for automated tests.
- `NoKill.Core` / `NoKill.Vault` / `NoKill.Profiles` — models today; vault and app-specific rescue profiles are the next milestones.

## Build & test

```
dotnet build
dotnet test
dotnet run --project src/NoKill.Cli        # console scan
dotnet run --project src/NoKill.App        # dashboard
```

## Roadmap

1. ✅ Window inventory + conservative hang detection
2. Hidden-dialog / blocker detection (UI Automation)
3. Recovery Vault (preserve autosaves, logs, screenshots before intervention)
4. Wait Chain Traversal diagnostics
5. App-specific rescue profiles
6. Research branch: process snapshots, cooperative recovery SDK
