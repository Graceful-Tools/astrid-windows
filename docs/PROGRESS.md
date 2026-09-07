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
| Row and label projections | `crates/astrid-core/src/rows/` | in progress |
| Natural-language quick add | `crates/astrid-core/src/parse/` | not started |

## M2 — shell

Not started. `app/Astrid.sln` does not exist; `npm run predeploy` skips the shell steps and says so.

## M3 — collaboration and remaining parity

Not started. `docs/PARITY.md` starts here.

## M4 — distribution

Not started.

## M5 — external sync providers

Not started.
