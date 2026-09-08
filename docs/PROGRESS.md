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
| Background sync and live updates | `astrid_core::app::background` | done |
| Localisation (`.resw`) | `app/Astrid.App/Strings/` | done — English; a language is a folder |
| UI smoke tests | `app/Astrid.App.UITests/` | done — four, in `npm run predeploy:full` |
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
| Attachments | `astrid_core::services::attachment` → shell | done |
| Filters and saved views | `astrid_core::filters`, `rows::filter_picks` → shell | done |
| Account and settings screens | `astrid_core::services::account` → shell | done |
| `docs/PARITY.md` | `docs/PARITY.md` | done |

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
| The Google mirroring pass | `astrid_core::services::external` | done — remove, pull, push, cursor commit |
| Deleting a Google twin on a local delete | `astrid_core::external::ledger` | done — captured at delete time, executed and tombstoned on the next pass |
| The server's tombstones (web and other devices) | `astrid_core::services::external` | done — merged from the integration metadata into their own store |
| The auto-link modes | `astrid_core::external::auto_link` | planned and tested; nothing calls it yet |
| GitHub | n/a | a cron on the server does it — a client only configures it |
