Check the **Ready** list on the Astrid Windows To-do and autonomously work every task on it to completion. Designed to be safe to re-run on a schedule.

## Goal

**Drive the Ready list to empty — and clear `RECHECK` / `REVIEW`.** This does not ask which
task to work on — it takes them in priority order and keeps going until nothing is left. The
queue script also sweeps the board's lanes honest (dated Ready work parks in Waiting; met
conditions promote back) and may print `RECHECK` (re-verify an external condition, then promote
or bump its date) and `REVIEW` (Waiting with no recorded condition — give it one or hand it
back) sections: those are part of the run, not commentary. It stops on its own when all three
are clear, so a scheduled re-run that finds nothing is a no-op, not busywork.

## The workflow itself is shared

**Read [`../astrid-web/docs/FIXALL_WORKFLOW.md`](../../../astrid-web/docs/FIXALL_WORKFLOW.md)
— it is the canonical description** of the queue (board ∩ Ready ∩ assignee ∩ due date), the
board etiquette (`Doing` / `Waiting` / handing back), the per-task loop (strategy comment →
branch → RED-GREEN TDD → gates → report), filing the other repo's half, and re-checking after
every task.

That file is shared with astrid-web and astrid-ios because it is one workflow, not three. This
file holds only what is different **here**.

## Finding the board

**The Windows board is resolved by name, never by a hardcoded id** — see
[docs/AUTOMATION.md](../../docs/AUTOMATION.md). A stale id fails by returning an empty list,
which reads exactly like a quiet day. So look it up each run:

```
get_lists {}                       → find the list named "Astrid Windows To-do", take its id
get_agent_queue { agent: "claude", listId: <that id> }
```

Pull the queue with the identity of the harness that is actually running this command:

```bash
# Claude Code
get_agent_queue { agent: "claude", listId: <Astrid Windows To-do id> }

# GitHub Copilot CLI / Copilot app
get_agent_queue { agent: "copilot", listId: <Astrid Windows To-do id> }

# Queue debugging only (never use the DB). There is no .env.local in this repo; the script
# runs from the astrid-web checkout and loads that repo's:
#   cd ../astrid-web && npx tsx scripts/ready-tasks.ts windows --harness claude-code
#   cd ../astrid-web && npx tsx scripts/ready-tasks.ts windows --harness github-copilot
```

`agent` never defaults: identify the current runtime, then pass its matching mailbox. Copilot
must not poll Claude's assignments, and Claude Code must not poll Copilot's. Only tasks assigned
to that selected identity are returned; unassigned Ready tasks are someone's untriaged note.

If the MCP tools are not loaded, they are deferred — load them with
`ToolSearch "select:mcp__astrid__get_lists,mcp__astrid__get_agent_queue,mcp__astrid__get_task,mcp__astrid__get_task_comments,mcp__astrid__add_comment,mcp__astrid__update_task,mcp__astrid__create_task"`.
If only `mcp__astrid__authenticate` exists, the server needs OAuth: call it, give Jon the URL,
and stop until he has authorised — do not fall back to the database.

## What is different here

- **A push to `main` builds nothing that reaches anyone.** Releases (Microsoft Store, the public
  update feed, signing) are a separate, manually triggered act. So, per CLAUDE.md → *Approvals*,
  local commits and pushing `main` are autonomous. **Push once, at the end of the run**, not per
  task — the same discipline as astrid-ios — and say in the completion report that the work is
  on `main`, never that it shipped.
- **Always ask before:** publishing to the Store or the update feed, signing a release, and
  deleting files.
- **A task is DONE when it is merged into `main` with `npm run predeploy` green.** Re-run the
  gate on the merged tree, not just the branch.
- **One isolated branch/worktree per task.** In a Copilot app session, use the branch and
  worktree the session already created; do not run raw branch-creation commands inside it.
  Other harnesses should reuse an already-isolated task branch or create one with their native
  session/worktree workflow.
- **Gates:** `npm run predeploy` is the standard gate (fmt, clippy, core tests, contract
  fixtures, the ARM64 cross-build, and the shell build/tests). `cargo test --workspace` is the
  inner loop; `npm run predeploy:quick` skips the cross-build; `npm run predeploy:full` adds the
  UI smoke tests, which drive the built app and take ~10 minutes — run it in the background.
- **Bug fixes are TDD:** a RED regression test naming the task id, then green, then the gate.
- **Porting from Swift:** read the Swift tests first — they are the specification — write the
  Rust tests RED, port GREEN, refactor. Do not improve behaviour while porting; a divergence
  found on the way goes in [docs/CONTRACTS.md](../../docs/CONTRACTS.md), not into the code.
- **Contracts are fixtures, not prose.** Anything that must match web is locked by a generated
  file in `contracts/fixtures/`. Changing shared behaviour is a cross-repo change: web first,
  then regenerate (`npm run contracts`), then here, then astrid-ios. File the other repos'
  halves as tasks on their boards rather than editing them from this run.
- **Critical rules from CLAUDE.md still hold inside the loop:** backend writes go through a
  service; complete a task only via `TaskService::complete_task`; next-occurrence math lives
  only in `astrid_core::repeating`; no business logic in `app/`; everything writes through the
  Outbox; no hardcoded user-facing strings — use the `.resw` resources.
- **A red predeploy files its own Astrid task.** If it was your own mid-refactor breakage,
  close that task with a one-line explanation rather than leaving a false alarm on the board.
- **If a task is blocked by something outside the repo**, park it in `Waiting` with the right
  condition (FIXALL_WORKFLOW.md → *Waiting carries its condition*): a decision only Jon can
  make → assign to Jon with the question; blocked on another task → `BLOCKED-BY: <id>`; blocked
  on an external event → `BLOCKED-ON: <condition>` plus a recheck due date. Do not close it,
  and do not work around the block by breaking users.
- **If every Ready task is blocked, say so in a few lines and stop.** A run that ends with
  "nothing actionable" is a correct run. Do not invent adjacent work to fill it.

## Environment gotchas

- **This machine is ARM64, and the shells lie about it.** Git Bash and PowerShell 5.1 both run
  as emulated x64, so `PROCESSOR_ARCHITECTURE` and `uname -m` say x64. The truth is
  `rustc -vV` (`aarch64-pc-windows-msvc`) or the registry. `scripts/predeploy.ps1` reads the
  registry; any ad-hoc build must too. For the UI smoke tests build
  `dotnet build app/Astrid.App/Astrid.App.csproj -c Debug -r win-arm64 --self-contained false`
  then `dotnet test app/Astrid.App.UITests/Astrid.App.UITests.csproj -c Debug`.
- **"The core did not load"** in a crash log means the wrong `astrid_ffi.dll` landed in `bin/`
  — check which candidate `Astrid.Core.Bindings.csproj` picked, not the P/Invoke. A stale
  `target/release` build can shadow a fresh `target/<triple>/debug` one.
- **WinUI `x:Uid` rules:** every `uid.*` resource is applied to every element with that uid, so
  a uid shared across elements with different property sets throws on page load; and a flat
  resw key cannot coexist with a uid scope of the same name — the PRI compiler fails the build.
  New XAML literals need a unique `x:Uid` per property set.
- `GET /api/v1/tasks/[id]` returns `{ task, meta }` — `body.lists` is undefined.
- **This file must exist on the branch you are working.** If `/fixall` behaves unlike this
  document, check `git ls-files .claude/commands/` first.

See [docs/ASTRID.md](../../docs/ASTRID.md) for architecture, [docs/AUTOMATION.md](../../docs/AUTOMATION.md)
for the scheduled loops and their secrets, and [CLAUDE.md](../../CLAUDE.md) for the approvals and
gate table.
