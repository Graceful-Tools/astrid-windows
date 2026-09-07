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
| Wire models | `crates/astrid-core/src/model/` | not started |
| Dates and all-day handling | `crates/astrid-core/src/model/date.rs` | not started |
| API client | `crates/astrid-core/src/api/` | not started |
| SQLite cache | `crates/astrid-core/src/store/` | not started |
| Outbox write journal | `crates/astrid-core/src/outbox/` | not started |
| Services | `crates/astrid-core/src/services/` | not started |
| Sync + SSE | `crates/astrid-core/src/{sync,realtime}/` | not started |
| Filters, rows, parse | `crates/astrid-core/src/{filters,rows,parse}/` | not started |

## M2 — shell

Not started. `app/Astrid.sln` does not exist; `npm run predeploy` skips the shell steps and says so.

## M3 — collaboration and remaining parity

Not started. `docs/PARITY.md` starts here.

## M4 — distribution

Not started.

## M5 — external sync providers

Not started.
