# Claude Code — Astrid Windows operational adapter

*Local Claude Code CLI workflow for the Astrid Windows app.*

**Repository:** https://github.com/Graceful-Tools/astrid-windows
**The shared core (separate repo, pinned by revision):** https://github.com/Graceful-Tools/astrid-core
**Web app + API (separate repo):** https://github.com/Graceful-Tools/astrid-web
**Apple apps (separate repo):** https://github.com/Graceful-Tools/astrid-ios

---

## Read docs/ASTRID.md before writing code

**[docs/ASTRID.md](./docs/ASTRID.md) is the single source of truth for architecture** — the service
layer, the Outbox, repeating tasks, sync, and the cross-platform contracts. This file holds only
commands and workflow. Open it before changing anything that touches tasks, completion, repeating
tasks, the Outbox, sync, chat, list members, permissions, or an API call.

### Critical rules (full detail in docs/ASTRID.md §0)

1. **Backend writes go through a service** — never the API client from the shell or a worker.
2. **Complete a task ONLY via `TaskService::complete_task`** — `update_task(completed: true)` skips
   repeat rollover.
3. **Next-occurrence math lives ONLY in `astrid_core::repeating`** — mirror changes into
   `astrid-web/types/repeating.ts` and `astrid-ios/.../RepeatingTaskHandler.swift`.
4. **No business logic in `app/`.** The WinUI shell renders and dispatches; it decides nothing.
5. **API paths are `/api/v1/...` only.**
6. **Preserve offline behaviour.** Everything writes through the Outbox.
7. **Bug fixes are TDD:** RED regression test naming the task id, then green, then
   `npm run predeploy`.
8. **Reuse before you write.** No inlined permission checks, no hardcoded user-facing strings — use
   the shared helpers and the `.resw` resources.

---

## Quick start

```powershell
npm run predeploy          # the standard gate before pushing
cargo test --workspace     # the FFI crate's tests, including the bindings contract
```

**The core is a separate repository since 2026-09-26.** `crates/astrid-ffi/Cargo.toml` pins
`astrid-core` by revision; a change to a rule, a service, the Outbox or sync is made in
[astrid-core](https://github.com/Graceful-Tools/astrid-core) and reaches this app when the pin is
bumped. Work it beside this checkout at `../astrid-core` and point the pin at a local path while
iterating (`[patch."https://github.com/Graceful-Tools/astrid-core.git"]` in `Cargo.toml`), never
commit the patch.

First time on a machine, see [docs/context/stack.md](./docs/context/stack.md) for the three winget
installs. The ARM64 MSVC component is required — `predeploy` cross-builds ARM64 every run.

## Quality gates

| Command | What it runs |
|---|---|
| `npm run predeploy:quick` | fmt, clippy, tests (no ARM64 cross-build) |
| `npm run predeploy` | the above plus the ARM64 cross-build and the shell build and tests — **the standard gate** |
| `npm run predeploy:full` | adds the UI smoke tests, which drive the built app |
| `powershell -File scripts/package.ps1` | the MSIX packages and the bundle — unsigned, see `packaging/README.md` |
| (in astrid-core) `cargo xtask check-contracts` | the fixtures against a local astrid-web checkout |

## Test locations

| Type | Path |
|---|---|
| Core tests | in astrid-core, beside the code and under `crates/astrid-core/tests/` |
| The bindings contract (`Commands.cs` against the core's command list) | `crates/astrid-ffi/tests/` |
| Shell view-model tests | `app/Astrid.App.Tests/` (from M2) |
| UI smoke tests | `app/Astrid.App.UITests/` (from M2) |

---

## How work is done here

**Port order for anything coming from Swift:** read the Swift tests first, write the Rust tests
(RED), then port the implementation (GREEN), then refactor. The Swift tests are the specification.
Do not improve behaviour while porting — a divergence found on the way goes in
[docs/CONTRACTS.md](./docs/CONTRACTS.md), not into the code.

**Contracts are fixtures, not prose.** Anything that must match web is locked by a generated file in
astrid-core's `contracts/fixtures/`. Changing shared behaviour is a cross-repo change: web first,
then regenerate in astrid-core, then bump the pin here, then astrid-ios.

**Per-task process** (canonical, cross-repo — see `astrid-web/docs/FIXALL_WORKFLOW.md`): post a
strategy comment, RED-GREEN-refactor with a task-id-linked regression test, run the gate, post a
completion report, then mark the task complete.

Tasks reach this repo from the **Astrid Windows To-do** board through the two loops in
[docs/AUTOMATION.md](./docs/AUTOMATION.md). The queue itself is astrid-web's, called rather than
copied: three boards, one implementation of "which tasks are ready".

---

## Approvals

**Always ask before:** publishing to the Microsoft Store or the public update feed, signing a
release, and deleting files. Those reach real users or are hard to undo.

**Autonomous:** code analysis, local builds and tests, implementation, local commits, documentation,
and pushing `main`.

Work lands on `main`. A push builds nothing that reaches anyone — releases are a deliberate,
manually triggered act, the same discipline astrid-web follows for production deploys.

---

## Documentation map

| File | Purpose |
|---|---|
| **[docs/ASTRID.md](./docs/ASTRID.md)** | **Architecture and rules — read first** |
| [docs/CONTRACTS.md](./docs/CONTRACTS.md) | Cross-platform rules and known divergences between clients |
| [docs/PARITY.md](./docs/PARITY.md) | What this app does against the Mac, and what it never will |
| [docs/PROGRESS.md](./docs/PROGRESS.md) | Where the milestones stand |
| [docs/context/stack.md](./docs/context/stack.md) | Pinned tool versions, machine setup |
| astrid-core's `contracts/README.md` | How the fixtures are generated |
| [docs/AUTOMATION.md](./docs/AUTOMATION.md) | The `fixall` / `fixstuff` loops and the secrets they need |
| [README.md](./README.md) | Project overview |

---

*This file is for Claude Code. Codex reads [AGENTS.md](./AGENTS.md) (same content). All architecture
lives in [docs/ASTRID.md](./docs/ASTRID.md) — do not duplicate it here.*
