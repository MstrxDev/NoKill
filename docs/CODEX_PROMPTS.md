# Codex / Cheap-Model Prompt Pack (save this in every repo as docs/CODEX_PROMPTS.md)

These are copy-paste prompts designed so a cheaper model or coding agent can execute
without needing the full conversation history. Fill the {BRACES} each time.

---

## 0. The Packet Template (use for EVERY task)

```
ROLE: You are implementing one small, self-contained change in an existing repo.
REPO CONTEXT: Read AGENTS.md (or CLAUDE.md), ARCHITECTURE.md, and ROADMAP.md first. Do not contradict them.

TASK: {one sentence}
FILES YOU MAY TOUCH: {explicit list — nothing else}
FILES YOU MUST READ FIRST: {list}
ACCEPTANCE CRITERIA:
- {testable statement 1}
- {testable statement 2}
- Existing tests still pass: {command}
OUT OF SCOPE (do NOT do): {list — refactors, renames, new deps unless listed}
CONSTRAINTS: no new dependencies unless listed; keep the public API stable; match existing code style.
WHEN DONE: run {build/test command}, show me the diff summary, list any deviations from this spec.
```

Rule: if a task needs more than ~5 files touched, split it into two packets.

---

## 1. NoKill — ship v0.1.0 release packet

```
Read AGENTS.md and README.md. Task: produce a shippable v0.1.0 of NoKill.
1. Add a GitHub Actions workflow (.github/workflows/release.yml) that on tag push v*:
   builds NoKill.App and NoKill.Cli in Release (self-contained win-x64),
   builds the installer in /installer, and attaches installer + zip to a GitHub Release.
2. Fix any build warnings that block Release build. Do not change behavior.
3. Write RELEASE_NOTES for v0.1.0 from README "Current state" section, 15 lines max.
Acceptance: workflow YAML validates; local `dotnet publish -c Release` succeeds for both heads.
Do not touch NoKill.Research or NoKill.Sdk.
```

```
Task: prepare a winget manifest for NoKill using the installer artifact from the
v0.1.0 GitHub Release. Create manifests under my winget-pkgs fork following
microsoft/winget-pkgs contribution rules (manifest version, InstallerSha256, silent
install switches). Output the exact `git` and `gh pr create` commands I should run.
```

---

## 2. Agent Flight Recorder (working name: "blackbox") — scaffold packet

```
Create a new Rust workspace called blackbox with two crates:
- blackbox-core: append-only JSONL event log with hash chaining (each record contains
  prev_sha256), event types: SessionStart, ToolCall{name,args_digest}, FileDiff{path,patch},
  ShellCommand{cmd,exit}, Approval{decision}, SessionEnd. Serde types + writer + verifier
  (verify() walks the chain and reports first broken link).
- blackbox-cli: `blackbox record -- <command>` wraps a child process, captures a session id,
  writes events; `blackbox verify <file>`; `blackbox show <file>` pretty-prints a timeline.
For v0, "recording" means: log start/end, capture the child's stdout/stderr to the vault
dir (~/.blackbox/sessions/<id>/), and snapshot `git diff` before/after into a FileDiff event.
Acceptance: `cargo test` green; `blackbox record -- echo hi` produces a session that
`blackbox verify` passes and `blackbox show` renders.
No network code. No async runtime unless required. MIT license, README with 10-line pitch.
```

Follow-up packets (one at a time):
- Hook mode: parse Claude Code / Codex hook JSON on stdin, emit ToolCall events (read the
  hooks docs, put the schema in docs/HOOKS.md first).
- `blackbox review`: TUI or plain-text diff review of everything an agent session changed,
  with per-file accept/revert (revert = `git checkout -- <path>` only, never destructive beyond git).
- Policy file v0: ~/.blackbox/policy.toml with allow/deny globs for paths and commands;
  deny = log + block + exit code, nothing fancier.

---

## 3. Doc-pack generator (run once per repo with any strong model)

```
Read this entire repo. Generate four files:
1. ARCHITECTURE.md — modules, data flow, invariants, "things that look wrong but are
   intentional", max 200 lines.
2. ROADMAP.md — next 10 tasks as packets (title, files, acceptance criteria), ordered.
3. AGENTS.md — instructions for coding agents: build/test commands, style rules, files
   never to touch, definition of done.
4. DECISIONS.md — table of decisions already made (choice, alternative rejected, why),
   inferred from the code and README.
Be specific to this repo. No generic advice. If you are unsure about intent, write
"ASSUMPTION:" in front of the line so I can correct it.
```

---

## 4. Cheap-model code review packet

```
Here is a diff produced by a coding agent for the task: "{task}".
Review ONLY for: (1) acceptance criteria met, (2) security issues, (3) silent scope creep,
(4) broken invariants listed in ARCHITECTURE.md. Output: PASS or FAIL, with a numbered
list of concrete problems and the exact lines. Do not restyle or rewrite the code.
{paste diff}
```

---

## 5. Frontier-model consult packet (use sparingly — this is the expensive one)

```
I get one expensive question. Context: {3-sentence project state}. Attached:
ARCHITECTURE.md + the specific file(s). Question: {one precise architectural or debugging
question}. Give me: the answer, the reasoning in brief, and the exact packet spec I should
hand to a cheaper model to implement it.
```

---

## 6. Weekly planning packet (any model)

```
Read ROADMAP.md and `git log --oneline -20`. Tell me: (1) what shipped vs planned,
(2) the single highest-leverage packet for next week, (3) anything on the roadmap that
new information makes obsolete. Update ROADMAP.md accordingly. Keep it under 40 lines.
```
