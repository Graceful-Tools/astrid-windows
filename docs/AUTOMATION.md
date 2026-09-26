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
2. **Never clobber work in progress.** A dirty tree, or a `HEAD` that is not `main`, skips. An
   interactive session takes no lock, so this is the only thing between a tick and your
   uncommitted work.
3. **Is there any work?** `ready-tasks.ts windows --json`, which sweeps the lanes first, so a
   Waiting task whose date arrived counts this tick. A queue that cannot be read is a reason to
   run and let the agent report, never a reason to go quiet.

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
