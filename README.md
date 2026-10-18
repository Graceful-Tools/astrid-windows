# Astrid for Windows

Native Windows app for [Astrid](https://astrid.cc) task management, for the Microsoft Store and
direct download, on x64 and ARM64.

**Repository:** https://github.com/Graceful-Tools/astrid-windows
**Web app and API:** https://github.com/Graceful-Tools/astrid-web
**iOS and Mac apps:** https://github.com/Graceful-Tools/astrid-ios

> **Status: in development, and the app runs.** The WinUI shell (M2) is done — windows, the task
> list and detail, board view, search, chat, the notification inbox — with MSIX packaging, a
> release workflow and UI smoke tests. It is not in the Microsoft Store yet: a release is a
> deliberate, manually triggered act, so there is nothing to install *for you* until one is cut.
> [docs/PROGRESS.md](./docs/PROGRESS.md) is where the milestones actually stand.

## How it is built

A Rust core holds everything that is not Windows-specific — models, the API client, the offline
SQLite cache, the Outbox write journal, sync, and every rule shared with the other clients. A WinUI 3
shell in C# renders it and nothing more.

That split is the point. The Mac app adds zero business logic on top of the iOS service layer, so a
change on web reaches it for free. Windows cannot compile Swift, so the shared layer is rebuilt once
in Rust and the same rule applies: **the shell decides nothing**. One port, paid once, instead of a
second set of business rules to keep in step forever.

What is in this repository:

```
app/Astrid.App          WinUI 3, C#   windows, XAML, key dispatch, platform adapters
app/Astrid.Core.Bindings              hand-written C# over the core's C ABI, checked by a test
crates/astrid-ffi       Rust          the C ABI over the core, and the Windows platform services
```

And where it sits among the four repositories — this app is one of two clients of a core it does
not contain:

```
astrid-windows   app/Astrid.App (WinUI, C#)  ──  crates/astrid-ffi (C ABI)  ──┐
astrid-ios       Astrid App / Astrid Mac (Swift)  ──  UniFFI (astrid-ios/core) ──┤──  astrid-core
                                                                                 │
                                                        HTTPS /api/v1/*  ◄───────┘
                                                        astrid-web (astrid.cc)
```

[astrid-core](https://github.com/Graceful-Tools/astrid-core) — models, services, the cache, the
Outbox, sync and the contract fixtures — is **its own repository**, pinned here by revision in
`crates/astrid-ffi/Cargo.toml`. A core change reaches this app only when somebody bumps that pin.

Rules that must read identically across web, Apple and Windows — the keyboard scheme, repeating-task
rollover, permissions, date handling — are locked by fixtures generated from the astrid-web sources.
Those fixtures and the check over them live in astrid-core, not here; see its
[`contracts/README.md`](https://github.com/Graceful-Tools/astrid-core/blob/main/contracts/README.md)
for how they are made.

## Getting started

Install the toolchain (see [docs/context/stack.md](./docs/context/stack.md) for exact versions):

```powershell
winget install Rustlang.Rustup
winget install Microsoft.DotNet.SDK.9
winget install Microsoft.VisualStudio.2022.BuildTools   # with the ARM64 MSVC component
```

Then:

```powershell
npm run predeploy       # the standard gate: format, lint, test, ARM64 cross-build, shell build and tests
cargo test --workspace  # the inner loop
```

## Documentation

| File | Purpose |
|---|---|
| [docs/ASTRID.md](./docs/ASTRID.md) | Architecture and the rules that govern this repo — read first |
| [docs/CONTRACTS.md](./docs/CONTRACTS.md) | Shared rules, and where the clients currently disagree |
| [docs/context/stack.md](./docs/context/stack.md) | Pinned versions and machine setup |
| [astrid-core](https://github.com/Graceful-Tools/astrid-core) | The shared core, and how the contract fixtures are generated |
| [docs/AUTOMATION.md](./docs/AUTOMATION.md) | The task loops, and the secrets they need |
| [CONTRIBUTING.md](./CONTRIBUTING.md) | How to contribute |

## License

MIT — see [LICENSE](./LICENSE).
