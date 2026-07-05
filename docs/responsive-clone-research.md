# Responsive-clone research

Experimental territory, deliberately **walled off from stable mode**. Nothing
here runs in the default rescue path or the watchdog. It is reachable only
through the CLI's explicit `--research` opt-in, and lives in its own projects
(`NoKill.Research`, `NoKill.Sdk`).

The honest position, unchanged from the founding doctrine: **NoKill cannot
resurrect an arbitrary frozen process.** A live process is entangled with
kernel handles, window handles, GPU/driver state, sockets, and OS objects that
no memory copy can restore. What we *can* do is explored in two branches.

## Pillar 1 — Process snapshots (`PssCaptureSnapshot`)

`ProcessSnapshotService` captures a Windows process snapshot: a copy-on-write
clone of the target's virtual address space plus captured thread and handle
information. This is a **diagnostic terrarium, not a runnable clone** — you can
inspect it, but you cannot launch it.

- Requires opening the target with `PROCESS_CREATE_PROCESS` (the VA clone uses
  process reflection, which spawns a clone process from the target). If that
  privilege is denied, the service falls back to a clone-less snapshot so
  inspection still succeeds.
- `PssQuerySnapshot` demands an exact buffer length per information class
  (the thread/handle info structs are 8 bytes each).
- Verified against a **frozen** process: VA clone created, all threads
  captured, target left alive and untouched (read-only doctrine holds).
- Known limitation: handle capture (`HandlesCaptured`) is often 0 without
  handle tracing enabled on the target; treated as best-effort.

Future direction (not built): dump a snapshot with `MiniDumpWriteDump` fed
from the snapshot handle, so the original is suspended only for the brief
capture rather than the whole dump write.

## Pillar 2 — Cooperative recovery SDK (`NoKill.Sdk`)

The realistic path to a "responsive clone" *experience*: the app cooperates.
An application embeds `RecoveryCheckpoint` and journals its unsaved state
periodically from a **background thread** (thread-pool timer, not the UI
thread). When the UI freezes, the most recent checkpoint already sits on disk,
so a freeze becomes a recoverable event.

- Checkpoints live under `%LOCALAPPDATA%\NoKill\Cooperative\<app>\`.
- Writes are atomic (temp file + move); a bounded ring keeps the last N.
- `CooperativeCheckpointReader` is NoKill's read-only discovery side.
- Verified against a **frozen** app: the demo's background journal kept
  writing a fresh checkpoint every second while its UI thread was blocked,
  and the reader recovered all of them.

`netstandard2.0` so any .NET Framework 4.6.1+ / .NET Core / .NET 5+ app can
embed it. See `samples/HungDemoApp` (`--auto-checkpoint`) for a working
integration.

## The wall

- Stable projects never reference `NoKill.Research`.
- `NoKill.Cli` references it but every research entry point checks `--research`
  first and refuses otherwise.
- Snapshotting is **not** part of `--preserve` or the watchdog. Preserving
  evidence stays firmly in stable mode; snapshots are an opt-in experiment.
