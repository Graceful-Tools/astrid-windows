# Build progress

*Living checklist. Update it as work lands — it is the memory an agent picking this up mid-way
reads first. The milestones themselves are defined in [ASTRID.md](./ASTRID.md) §4.*

## M0 — foundation spike — mostly done

See [M0_NOTES.md](./M0_NOTES.md). Toolchain, contract mechanism, hand-off sign-in, repeating
rollover and list permissions are settled. The runtime spikes listed there stay open until the
shell exists (M2).

## M1 — core contracts and services

| Piece | Path | State |
|---|---|---|
| Wire models | `crates/astrid-core/src/model/` | done |
| Dates and all-day handling | `crates/astrid-core/src/model/date.rs` | done |
| API client | `crates/astrid-core/src/api/` | done |
| SQLite cache | `crates/astrid-core/src/store/` | done |
| Outbox write journal | `crates/astrid-core/src/outbox/` | done |
| Services | `crates/astrid-core/src/services/` | done |
| Sync + SSE | `crates/astrid-core/src/{sync,realtime}/` | done |
| Filters, sorting, subtasks | `crates/astrid-core/src/filters/` | done |
| Row and label projections | `crates/astrid-core/src/rows/` | done |
| Quick-add `#list` autocomplete | `crates/astrid-core/src/parse/` | done |

## M2 — shell — done

The app exists and runs: create a list, add a task, complete it, sync, relaunch and it is all
still there. Built and tested for x64 and ARM64 by `npm run predeploy`, and driven through its own
accessibility tree by `npm run predeploy:full`.

| Piece | Path | State |
|---|---|---|
| Command layer (the one door) | `crates/astrid-core/src/app/` | done |
| C ABI | `crates/astrid-ffi/`, `include/astrid.h` | done |
| P/Invoke and the managed client | `app/Astrid.Core.Bindings/` | done |
| View models | `app/Astrid.App.ViewModels/` | done |
| WinUI window: sidebar, list, quick add | `app/Astrid.App/` | done |
| Tests either side of the boundary | `app/Astrid.App.Tests/` | done |
| Task detail | `app/Astrid.App/` → `taskDetail` | done |
| The shared keyboard scheme, dispatched | `astrid_core::keyboard` → shell | done |
| Sign-in through the browser hand-off | `astrid_core::services::auth` → shell | done |
| Board view | `astrid_core::board` (fixture-locked) → shell | done |
| Due-date quick picks | `astrid_core::rows::due_picks` → shell | done |
| Virtual lists (Today, Not in a List, I've Assigned) | `astrid_core::filters` → shell | done |
| Background sync and live updates | `astrid_core::app::background` | done — and since 2026-09-11 a pass that changes the cache tells the shell, and a journalled write goes out at once rather than on the next tick |
| Localisation (`.resw`) | `app/Astrid.App/Strings/` | mechanism done — English; a language is a folder. About half the shell's strings are still literals in `ShellPage.xaml` and a few `.cs` files; see "Release readiness" |
| UI smoke tests | `app/Astrid.App.UITests/` | done — five, in `npm run predeploy:full` |
| Assignee picker | `astrid_core::rows::assignee` → shell | done |
| Repeat editor | `astrid_core::rows::repeat` → shell | done |
| Search | `astrid_core::services::search` → shell | done |
| Reminders and toasts | `astrid_core::reminders`, `rows::reminder_picks` → shell | done |

## Performance

Measured by `cargo test -p astrid-core --release --test performance -- --nocapture`, on an account
of **ten thousand tasks in one list** — more than any real account, and the size the M0 transport
spike was worried about. On this machine (ARM64 laptop, release build):

| What | Before the borrow pass | Now |
|---|---|---|
| `rowsForList` — one window of 50 | 53 ms | **30 ms** |
| the same window at offset 5000 | 52 ms | **31 ms** |
| `board` — grouping every card into columns | 26 ms | **22 ms** |
| `searchTasks` over the whole account | 18 ms | **17 ms** |
| `completeTask` — one write | 0.24 ms | **0.18 ms** |
| My Tasks — one window of 50, over the whole account | — | **31 ms** |

Where the remaining 30 ms goes: about 18 ms is SQLite plus the JSON decode of ten thousand cached
rows, and the rest is filtering, splicing and building fifty rows. The pipeline no longer copies the
account: filtering, sorting, splicing and row-building all work on references, which was worth
nearly half the time and all of the allocation churn.

The tests assert a generous half-second bound rather than these numbers. What they catch is a
*shape* change — an accidental O(n²), a clone per row, a store read inside a loop — which shows up
as seconds rather than as a percentage.

## M3 — collaboration and remaining parity

| Piece | Path | State |
|---|---|---|
| List settings, sharing and members | `astrid_core::services::list` → shell | done |
| Chat | `astrid_core::services::chat`, `rows::chat` → shell | done |
| Attachments | `astrid_core::services::attachment` → shell | done — queued through the Outbox, bytes on disk |
| Filters and saved views | `astrid_core::filters`, `rows::filter_picks` → shell | done |
| My Tasks, and its account-wide filters | `astrid_core::filters::my_tasks` → shell | done |
| Account and settings screens | `astrid_core::services::account` → shell | done |
| `docs/PARITY.md` | `docs/PARITY.md` | done |

## Release readiness — measured against the web app, 2026-09-11

`docs/PARITY.md` measures against the Mac, and the Mac trails the web. This is the honest list
against the web, from a read of the source rather than of the docs, with what has landed since.

**Landed 2026-09-11**

- The shell is built and tested in CI (`.github/workflows/ci.yml`, job `shell`); it never was.
- `crates/astrid-core/tests/bindings_contract.rs` reads `Commands.cs` against the `Command` enum.
- The background pass publishes what it changed; the shell redraws without a click.
- `sync::policy` is wired: a person's refresh waits for the pass in flight instead of returning
  having fetched nothing (task 3173727d), and the shell no longer drops the second request.
- The pull is incremental (`updatedSince`, with the server's tombstones), stamped at pass start.
- `app::background::outbox_loop` delivers a journalled write as soon as it is made; before, the
  journal drained only at the top of the sixty-second pass.
- Reminder and smart-task settings go through the Outbox; they used to be lost offline.
- List settings open from the cache and then refresh (`listMembers` + `refreshListMembers`);
  one network call used to take the whole flyout down. The account flyout asks for its four
  server-side answers together rather than one after another.
- A row flips on the click and flips back only if the core refuses (`TaskListViewModel`).
- `app/dispatch.rs` is a directory, one file per domain; every literal in `ShellPage.xaml`
  carries an `x:Uid` with a resource behind it, and two tests keep it so.
- **The notification inbox** (`/api/v1/notifications`): a bell in the header with the unread
  count from the cache, refreshed on every sync pass, mark-read and mark-all-read.
- **The search grammar** (`assignee:me priority:high due:week is:open status:ready list:Work
  label:bug AST-142`), ported from `lib/search-query-parser.ts` and locked by
  `contracts/fixtures/search.json`; applied over the cache, which holds every task including
  the finished ones, so the server's search adds nothing here.
- **Task identifiers** (`AST-142`) on the model, the row and the search.
- **Label lists** (`listType: "label"`): chips on the rows they belong to, never a sidebar entry.
- **Copy a task** into a list, with or without its comments, from the task menu.
- **The editing-session machine** (`astrid_core::editing`), ported from `lib/editing-session.ts`
  and locked by `contracts/fixtures/editing.json`. The core half; the detail pane's editors are
  not yet routed through it.
- Two things the fuller gate found and fixed: the shell resolved the core's DLL by *last*
  candidate, so a week-old release build beat a fresh debug one; and the account photo binding
  threw on every fresh launch (no photo → an empty string into an image), which had broken the
  UI smoke tests since 2026-09-09 without anybody running them.

**Still open — architecture**

- `ShellPage.xaml` (3.6k lines) and `ShellPage.xaml.cs` (2.9k) are one control; the settings
  flyout is nine sections toggled by visibility. `SettingsViewModel` (1.3k) is nine screens;
  `TaskDetailViewModel` (1.7k) fuses fields, comments, attachments and timer. Split along seams
  that already exist.
- The detail pane's editors each keep their own flag; routing them through
  `astrid_core::editing` is the remaining half of `PRODUCT_CONTRACT.md` §6.
- No drift test on response shapes (`Models.cs` against the Rust `Response`s).
- English only. The mechanism is complete — a language is a folder — but no second folder.

**Still open — features the web has**

Natural-language quick-add beyond `#list` (CONTRACTS D11); manual drag-reorder and
drag-to-list; transfer ownership; list image; copy-to-my-list and the public-list browser; the
`@astrid` model selector; calendar feed settings; per-user feature flags (`project_mode`,
`google_tasks`); full-screen detail; the localised unassigned mark; a rebindable hotkey; twelve
languages.

Not gaps, because the web has none either: multi-select, undo, calendar view, dependencies.

## M4 — distribution

| Piece | Path | State |
|---|---|---|
| MSIX packages and a bundle over both architectures | `scripts/package.ps1`, `packaging/` | done — unsigned |
| Signing | — | not started; needs a certificate and a decision |
| The Store listing | — | not started |
| The update feed (`.appinstaller`) | — | not started |

## M5 — external sync providers

| Piece | Path | State |
|---|---|---|
| The decisions, with their reasons and tests | `astrid_core::external` | done |
| Connect, disconnect, link a list | `astrid_core::services::external` → shell | done — both providers |
| The Google mirroring pass | `astrid_core::services::external` | done — remove, pull, push, task links, cursor commit |
| Task links written back after a pull or push | `astrid_core::services::external` | done — what stops a twin being remade on every pass |
| Deleting a Google twin on a local delete | `astrid_core::external::ledger` | done — captured at delete time, executed and tombstoned on the next pass |
| The server's tombstones (web and other devices) | `astrid_core::services::external` | done — merged from the integration metadata into their own store |
| The webhook, and an account's own agents | `astrid_core::services::agents` → shell | done — configure, test, register, remove |
| The auto-link modes | `astrid_core::external::auto_link` → `services::external` | done — chosen in the account flyout, carried out at the top of each pass |
| My Tasks ↔ Google's default list | `astrid_core::services::external` | done — runs beside the links in the all-lists modes |
| Saying no to a remote list whose Astrid list was deleted | `astrid_core::external::ledger` | done — recorded locally, shared with the account |
| GitHub | n/a | a cron on the server does it — a client only configures it |
