# The task loops

*How work reaches this repo from the Astrid board, and what has to be configured for it to.*

Two workflows, the same pair astrid-web and astrid-ios run:

| Workflow | What it does |
|---|---|
| [`.github/workflows/fixall.yml`](../.github/workflows/fixall.yml) | Every 30 minutes, works whatever the **Astrid Windows To-do** board has marked *Ready* |
| [`.github/workflows/fixstuff.yml`](../.github/workflows/fixstuff.yml) | Run by hand against one task id |

## The queue is astrid-web's, on purpose

Neither workflow decides which tasks are ready. Both check out astrid-web and call its task
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

GitHub-hosted `windows-latest`, the same as `ci.yml`. The iOS loops use self-hosted runners because
Xcode has to be there; nothing here needs a machine of its own.

The shell is set to `bash` once at the top of each file rather than on every step: the task tooling
is the same bash the other two repos run, and a Windows runner defaults to PowerShell.

## Two deliberate differences from the iOS loops

- **astrid-web is checked out at its default branch, not a pinned commit.** The Windows board is
  newer than the commit the iOS loop pins, so a pinned checkout fails with "Unknown board". Pin one
  here once this repo's loop has a contract of its own worth freezing.
- **`post-session-link.ts` runs from the astrid-web checkout.** The iOS workflow calls it from its
  own repo, where the script does not exist, so the step can only ever print its warning.
