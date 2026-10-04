# The task loops

*How work reaches this repo from the Astrid board, and what has to be configured for it to.*

Three paths, two of them the pair astrid-web and astrid-ios run:

| Path | Harness | What it does |
|---|---|---|
| [`scripts/fixall-loop.ps1`](../scripts/fixall-loop.ps1) | Claude Code CLI, on this machine | Every 30 minutes at :10 and :40, works whatever the **Astrid Windows To-do** board has marked *Ready*. **This is the one that runs today** |
| [`.github/workflows/fixall.yml`](../.github/workflows/fixall.yml) | GitHub Copilot | The same queue, on a GitHub runner. **By hand only** — its schedule was removed; see below |
| [`.github/workflows/fixstuff.yml`](../.github/workflows/fixstuff.yml) | GitHub Copilot | Run by hand against one task id |

## The local loop

**Assigning a task to Claude Agent in Astrid starts nothing.** In polling mode Astrid calls out to
no one, by design (astrid-web `docs/AGENT_POLLING_MODE.md`, after the 2026-08-23 retry storm), so
something has to poll. On the Mac that is launchd; here it is Task Scheduler, and the pass it runs
is `scripts/fixall-loop.ps1` — a port of `astrid-web/scripts/fixall-loop.sh` with the same three
guards and the same one-line contract.

```powershell
# install, at :10 and :40 every hour, for the logged-on user, no elevation
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/install-fixall-task.ps1

# prove it, and read what it did
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/install-fixall-task.ps1 -RunNow
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/install-fixall-task.ps1 -Status

# one pass by hand; -DryRun stops before Claude starts
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/fixall-loop.ps1 -DryRun

# stop it again
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/install-fixall-task.ps1 -Uninstall
```

One caveat on the `RESULT:` contract: it holds for every way the loop can end *itself*, including
a watchdog kill. It cannot hold when the loop is killed from outside — `Stop-ScheduledTask`, or
Task Scheduler's own execution limit — because the process tree goes down before the last line is
written. A log entry that ends at `-> /fixall` with no `RESULT:` therefore means somebody stopped
it, and the working-tree lock it held is left behind marked STALE, which the next run reclaims by
liveness rather than by a timeout.

It runs on the **Claude Code CLI subscription, never the Anthropic API**: `claude -p` is the whole
runtime, no API key is read anywhere in that path, and the budget bound is a CLI safety limit
rather than metered spend. The log is `%LOCALAPPDATA%\Astrid\logs\fixall-windows.log`, and its
last line is always exactly one `RESULT:` line — `OK`, `SKIPPED` or `FAILED` — so a glance tells
you what happened. **A skip is the healthy common case:** most ticks have nothing to do.

Three guards, cheapest first, because the expensive thing is starting a session at all:

1. **One session per working tree.** `fixall-session.ts acquire`, keyed to this repo's git
   directory, so this tree and astrid-web's never see each other and a dead holder's lock is
   reclaimed by liveness rather than by a timeout.
2. **Never clobber work in progress.** A dirty tree, or a `HEAD` that is not `main`, skips — and it
   **names which checkout** stopped the tick, because `astrid-core is dirty` is a different
   instruction to a human than `working tree is dirty`. An interactive session takes no lock, so
   this is the only thing between a tick and your uncommitted work. The
   [start-of-tick sweep](#the-start-of-tick-sweep) runs just before it, since the leftovers it
   clears are exactly what this guard trips on.
3. **Is there any work?** `ready-tasks.ts windows --json`, which sweeps the lanes first, so a
   Waiting task whose date arrived counts this tick. A queue that cannot be read is a reason to
   run and let the agent report, never a reason to go quiet.

### One task per tick, and what happens when a tick dies

A tick takes **one** Ready task. `ASTRID_FIXALL_MAX_TASKS`, which the loop exports as `1`, is the
shared contract (`astrid-web/docs/FIXALL_WORKFLOW.md` → *One task per scheduled run*): stop after
that many, push, report, end the run. `RECHECK` / `REVIEW` do not count. An interactive `/fixall`
leaves it unset and drives the queue to empty.

The cap is a count of **tasks**, not of dollars, because the agent cannot see its own remaining
spend until it is nearly gone. On 2026-09-27 three runs in a row started a task they could not
finish — `claude` exits 1 the moment `--max-budget-usd` is hit, wherever it happens to be, and all
three times that was after the code was written and before it was committed, gated, pushed or
reported. The watchdog is 75 minutes for the same reason: one task with a ~10-minute `predeploy`
has to fit inside it.

**A tick that dies no longer wedges the loop.** Guard 2 refuses a dirty tree and a non-`main` HEAD
— rightly, since it cannot tell a died run's leftovers from your work in progress. The loop can:
it holds the lock and its child is gone, so on every ending it runs `Save-UnfinishedWork`
(`scripts/lib/fixall-cleanup.ps1`), which

- commits whatever is uncommitted as `wip: … UNFINISHED, UNVERIFIED`, with `--no-verify` because
  the work has passed no gate and a hook would only put us back where we started;
- puts it on the task's own branch — or a fresh `wip/fixall-windows-…` branch if the run dirtied
  `main` or a detached `HEAD`, so a died run can never commit to `main`;
- pushes the branch so the work is reviewable, and returns the checkout to `main`;
- and names the branch in the `RESULT:` line.

It does this for a **clean** task branch too: `95c7a68f` was committed and then died before it
could push or merge, and sat invisible for eleven ticks. Before any of this, `8aa5732c` cost
seventeen ticks and 8½ hours.

**So a branch whose tip reads `wip: … UNFINISHED, UNVERIFIED` is a resume point** — check it out
and continue from it rather than starting over. It is not verified and must never be shipped.

**The loop also reports on the board.** A died run cannot write its own completion comment, so the
loop comments the failure and the branch on the task the run had taken. It learns which task that
was from `ASTRID_FIXALL_TASK_FILE`: the loop hands the run a path, and the run writes the id there
as it takes the task. Not list chat — that needs a board id, and this board's is deliberately
nowhere in this repo. Not the pre-run queue either, which holds candidates rather than the choice;
a note on the wrong task is worse than none, so with no task file the loop stays quiet.

> **Not yet wired on the agent side.** `.claude/commands/fixall.md` still has to tell the run to
> write its task id to `ASTRID_FIXALL_TASK_FILE`. Until that line lands the file is never written
> and the board comment is simply skipped — the cleanup above, which is the part that unjams the
> loop, does not depend on it.

### The start-of-tick sweep

**A cleanup that only runs at death is the wrong shape.** Everything above happens *after* the
`claude` child exits, inside the loop — so a kill that takes the PowerShell process down reaches
none of it. On 2026-09-29 the 06:10 tick took a core task, worked in `../astrid-core` for about ten
minutes and was interrupted (`LastTaskResult 3221225786` = `STATUS_CONTROL_C_EXIT`). It wrote no
`RESULT:` line, made no WIP commit and left no comment; the `finally` that releases the lock is the
only thing PowerShell guarantees on a kill.

And **nothing looked at `astrid-core`.** Guard 2 and the save both read `$RepoRoot` only, so the
Windows tree was clean on `main`, guard 2 passed, and every tick for the next four days reported
`SKIPPED - nothing to do` while 449 uncommitted lines for a task in `Doing` sat in the other
checkout. The asymmetry is the sting: CLAUDE.md sends a task whose fix is a rule, a service, the
Outbox or sync *into* the core, so the checkout nothing checked is the one most likely to hold a
died run's work.

So `Invoke-StartOfTickSweep` (`scripts/lib/fixall-sweep.ps1`) runs at the **start** of a tick,
after guard 1 takes the lock and before guard 2 decides anything, over **both checkouts**. It
survives `kill -9`, a reboot and a closed console, because none of it depends on the dying process
doing anything. It delegates the commit, the push and the unwind to the same `Save-UnfinishedWork`,
and adds only the decision of whether and where.

**Dirty is not the same as stranded,** and that is the whole design. Acting on dirtiness alone would
trade this bug for a worse one, twice over: in this repo guard 2 exists *because* a dirty tree
cannot be told apart from your uncommitted work or a `/fixstuff` session, and `../astrid-core` is a
plain clone shared with the other repos' loops rather than a `git worktree`, where `git add -A` can
swallow a live run's files. So a checkout is swept only when it has work **and is quiet**:

- no **live** `fixall-session` holder (a *stale* holder is the died run itself, and must not protect
  its own remains), and
- nothing touched for `FIXALL_QUIET_MINUTES` — **30** by default, one whole tick interval. "Quiet
  for longer than a tick" is the rule; a long model turn or a `cargo build` is minutes, not thirty,
  and the real incident sat for four days. The asymmetry picks the default: sweeping a live run
  costs work nobody can get back, while waiting costs one skipped tick that says why.

A checkout with work that is *not* quiet is reported, and guard 2 then skips the tick naming it.

**The core is pushed but never returned to `main`.** Pushing a branch touches neither the HEAD nor
the working tree of a shared clone; `git checkout main` moves both, for every run in there — on
2026-09-27 that was nearly another run's work. Which is also why a core sitting on a branch is not a
reason to skip: it is the state the sweep itself leaves behind.

**A surviving task file is how a tick knows to look.** `ASTRID_FIXALL_TASK_FILE` is
`astrid-fixall-windows-task-<pid>.txt` and the loop deletes it in its `finally`, so a file that
outlives its pid means a run ended without writing a `RESULT:` line. The sweep reads the id, reports
the stranded run on that task — whether or not any code was left behind, because a task in `Doing`
with nobody on it is worth saying either way — and clears the file so no later tick re-reports it.
Its own file and any whose pid is still running are left alone.

Whatever the sweep did is appended to this tick's `RESULT:` line. A recovery nobody can see is as
good as the four days of `SKIPPED - nothing to do` it exists to end.

**What the local loop needs on the machine:** the astrid-web checkout beside this one with
`npm ci` run (its `tsx`, its `.env.local`, its OAuth pair), the **astrid-core checkout beside this
one**, the `claude` CLI on `PATH`, and `.claude/settings.json` — committed here, because a
scheduled run has no terminal to answer a permission prompt in, and `--permission-mode acceptEdits`
pre-approves file edits only. Without those grants the run can read the board and change nothing.

**Both sibling checkouts are passed to the session with `--add-dir`, and neither is optional in
practice.** A Claude Code session can open only the directories it was given: astrid-web because
with no astrid MCP server registered its OAuth scripts are the only path to the board, and
astrid-core because that is where a rule, a service, the Outbox and sync live — so a run without it
cannot fix a core-side task, and cannot even *read* the core to decide whether the fix belongs
there. The cargo git checkout under `~/.cargo` is no substitute; it is outside the allowed
directories too. A missing astrid-core only warns rather than failing the tick, since a shell-only
task is still workable, but the warning is loud: on 2026-09-26 all four Ready tasks turned out to
bottom out in core rules and the run could not read one line of them.

**What it does not do, unlike the Mac loop.** astrid-web's third guard calls
`agent-queue-status.ts`, which also reports `attention` — comments and chat nobody answered — and
keeps a seen-file so one unanswered item wakes one run rather than every tick. That script needs a
**list id**, and this board's id is deliberately nowhere in this repo, so the guard here asks
`ready-tasks.ts windows` instead: the queue and the lanes, both resolved by name. The inbox is
therefore not a reason this loop wakes. Adopt the richer preflight the day it can resolve a board
by name.

## The Copilot path is switched off

`fixall.yml` has failed on every scheduled tick since it was written — about seven seconds in, at
the astrid-web checkout — because the four repository secrets in the table below were never added.
Nothing was lost: the Copilot path has never run, and the local Claude loop above now covers the
board.

**The schedule was removed on 2026-09-26** (Jon), leaving `workflow_dispatch` so the workflow is
still runnable by hand. A schedule that has never once succeeded is a failure notification every
half hour for a path nobody is using, and the board is covered by the local Claude loop above.

To put it back: add the four secrets **first**, then restore the `schedule:` block — the cron line
it used is kept in a comment there, already interleaved with the other loops' clock minutes.

Note that the atomic claim this path depends on cannot work for this board yet either:
`claim-fixall-task.ts` POSTs to `astrid.cc`, whose allowlist
(`astrid-web/lib/fixall-claim.ts`, `DEFAULT_FIXALL_CLAIM_BOARD_IDS`) names only the web and iOS
boards, so every Windows task answers `CLAIM_CONFLICT`. Filed on the Astrid Web board as
`db965bb1`; it needs a deploy of astrid-web, not a change here.

## The queue is astrid-web's, on purpose

No path here decides which tasks are ready. All of them call astrid-web's task
tooling — `scripts/ready-tasks.ts windows`, `scripts/claim-fixall-task.ts`,
`scripts/post-session-link.ts`.

A second implementation of "which tasks are ready" would drift from the other two repos, and the
drift would be silent: a queue that is wrong looks exactly like a quiet day. So there is one
implementation and three boards, and `windows` is a board that repo knows about
(`lib/ready-queue-scope.ts`). A board name it does not recognise is rejected rather than widened
to the whole account.

**The board is resolved by name, never by id.** An id is account data, and a hardcoded one fails
by returning an empty list — which reads exactly like "nothing to do". This is why no list id
belongs in any environment file here.

## Ready is a field, not a list

`Task.statusRole` carries the status. It used to be membership in a `listType: 'status'` list, and
reading the old lists left the queue a shadow of the real state: a task marked Ready in the app
never appeared, and one that had moved on stayed queued. The shared script reads the field.

## The gate runs before any task is handed over

Both workflows run `npm run predeploy` first — the whole gate, not a subset. An agent handed a task
on a red `main` spends its run working out that the breakage was not its own.

## What has to be configured

Repository secrets, in **Settings → Secrets and variables → Actions**:

| Secret | Used by | What it is |
|---|---|---|
| `ASTRID_OAUTH_CLIENT_ID` | fixall | Reads the board. Client-credentials pair from the Astrid account |
| `ASTRID_OAUTH_CLIENT_SECRET` | fixall | The other half of the pair |
| `ASTRID_MCP_TOKEN` | both | Triggers the coding agent. Without it the trigger step fails loudly rather than passing vacuously |
| `ASTRID_WEB_READ_TOKEN` | both | Checks out astrid-web for the task tooling. Already used by `ci.yml` for the contract fixtures — a fine-grained PAT with Contents: read |
| `ASTRID_WEBHOOK_URL` | both | Optional. Defaults to `https://astrid.cc` |

There is **no `.env.local` in this repo, and none is needed.** The scripts that read one run from
the astrid-web checkout and load astrid-web's; in CI the values come from the secrets above. If
`ready-tasks.ts` reports `invalid_client` when run locally, the OAuth pair in astrid-web's
`.env.local` has been rotated or revoked — renew it there, not here.

## Runners

The local loop runs on this machine, as the logged-on user, at Limited run level — no elevation,
because the Claude CLI reads the user's own credentials and settings. Three Task Scheduler settings
are load-bearing on a laptop, and each is a way the loop would otherwise go quiet while looking
installed and healthy: `AllowStartIfOnBatteries` and `DontStopIfGoingOnBatteries` (a task without
them does not run unplugged), `StartWhenAvailable` (a tick missed while asleep runs once on wake),
and `MultipleInstances = IgnoreNew` with an execution limit a little above the loop's own watchdog
(one wedged run must not stack copies, nor outlive its watchdog).

The GitHub paths use GitHub-hosted `windows-latest`, the same as `ci.yml`. The iOS loops use
self-hosted runners because Xcode has to be there; nothing here needs a machine of its own.

The shell is set to `bash` once at the top of each file rather than on every step: the task tooling
is the same bash the other two repos run, and a Windows runner defaults to PowerShell.

## Two deliberate differences from the iOS loops

- **astrid-web is checked out at its default branch, not a pinned commit.** The Windows board is
  newer than the commit the iOS loop pins, so a pinned checkout fails with "Unknown board". Pin one
  here once this repo's loop has a contract of its own worth freezing.
- **`post-session-link.ts` runs from the astrid-web checkout.** The iOS workflow calls it from its
  own repo, where the script does not exist, so the step can only ever print its warning.
