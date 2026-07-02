# NoKill

A Windows rescue tool for applications that freeze or go "Not Responding" — built on the principle that killing the process is the last resort, not the first.

Task Manager is a hammer. NoKill detects hung apps, figures out *why* they're stuck (hidden dialogs, wait chains, blocked dependencies), preserves recovery artifacts, and only then talks about anything drastic.

## Safety doctrine

- NoKill never kills, restarts, or injects into a process by default.
- NoKill preserves recovery artifacts **before** attempting any intervention.
- Detection is conservative: a window is only reported "Not Responding" when multiple independent signals agree. Busy ≠ hung.
- Local-first: no telemetry, no cloud, no uploads.

## Current state (v0.1 slice)

- `NoKill.Win32` — Win32 interop (CsWin32): window enumeration (z-order, owner chain, enabled state), `IsHungAppWindow`, `WM_NULL` ping via `SendMessageTimeout`. All read-only except `WindowActions` — the single, auditable place allowed to touch another window (z-order raise only, no activation, no input).
- `NoKill.Diagnostics` — `WindowInventoryService` (desktop snapshot), `HangScorer` and `BlockerClassifier` (pure, tested signal/window-facts logic).
- `NoKill.Automation` — `HiddenDialogDetector` (finds modal dialogs hiding behind their owner or off-screen) + `DialogContentReader` (UI Automation, reads what the dialog is asking). Offers one action: **Reveal** — raise the dialog so the user can answer it.
- `NoKill.Vault` — the Recovery Vault: preserves rescue evidence (JSON + human-readable reports, process info, window list, screenshot, recovery artifacts) into a per-incident folder under `Documents\NoKill\Vault\`. Copy-only: sources are never modified, entries are never overwritten, and problems become warnings, not aborted preserves.
- `NoKill.App` — WPF dashboard: live window list with hang status + per-row Preserve button, suspected-blockers panel with per-row Reveal button, auto-refresh every 3 s.
- `NoKill.Cli` — same scan as a console table (`--flagged-only`, `--reveal`, `--preserve <pid>`).
- `samples/HungDemoApp` — deliberately misbehaving lab rat: freeze-on-click, deadlock, hidden modal dialog; `--auto-freeze <delayMs> <durationMs>` and `--auto-hidden-modal` for automated tests.
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
2. ✅ Hidden-dialog / blocker detection + Reveal (UI Automation)
3. ✅ Recovery Vault (reports, screenshot, process info; artifact engine ready for profiles)
4. App-specific rescue profiles (supply autosave/log/temp artifact sources to the vault)
5. Wait Chain Traversal diagnostics
6. Research branch: process snapshots, cooperative recovery SDK
