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

## M2 — shell

The app exists and runs: create a list, add a task, complete it, sync, relaunch and it is all
still there. Built and tested for x64 and ARM64 by `npm run predeploy`.

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
| Board view | — | not started |
| Due-date quick picks | `astrid_core::rows::due_picks` → shell | done |
| Virtual lists (Today, Not in a List, I've Assigned) | `astrid_core::filters` → shell | done |
| Background sync and live updates | `astrid_core::app::background` | done |
| Localisation (`.resw`) | — | not started |
| UI smoke tests | `app/Astrid.App.UITests/` | not started |
| Assignee picker | `astrid_core::rows::assignee` → shell | done |
| Repeat editor | `astrid_core::rows::repeat` → shell | done |
| Search | `astrid_core::services::search` → shell | done |
| Reminders and toasts | — | not started |

## M3 — collaboration and remaining parity

Not started. `docs/PARITY.md` starts here.

## M4 — distribution

Not started.

## M5 — external sync providers

Not started.
