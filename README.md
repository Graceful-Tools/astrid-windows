# Astrid for Windows

Native Windows app for [Astrid](https://astrid.cc) task management, for the Microsoft Store and
direct download, on x64 and ARM64.

**Repository:** https://github.com/Graceful-Tools/astrid-windows
**Web app and API:** https://github.com/Graceful-Tools/astrid-web
**iOS and Mac apps:** https://github.com/Graceful-Tools/astrid-ios

> **Status: early development.** The shared core is being built; the app shell has not landed yet.
> There is nothing to install. Watch the milestones in [docs/ASTRID.md](./docs/ASTRID.md) §4.

## How it is built

A Rust core holds everything that is not Windows-specific — models, the API client, the offline
SQLite cache, the Outbox write journal, sync, and every rule shared with the other clients. A WinUI 3
shell in C# renders it and nothing more.

That split is the point. The Mac app adds zero business logic on top of the iOS service layer, so a
change on web reaches it for free. Windows cannot compile Swift, so the shared layer is rebuilt once
in Rust and the same rule applies: **the shell decides nothing**. One port, paid once, instead of a
second set of business rules to keep in step forever.

```
app/Astrid.App          WinUI 3, C#   windows, XAML, key dispatch, platform adapters
app/Astrid.Core.Bindings              generated C# over the core's C ABI
crates/astrid-core      Rust          models, services, cache, Outbox, sync, contracts
        |
        v  HTTPS /api/v1/*
astrid-web (astrid.cc)
```

Rules that must read identically across web, Apple and Windows — the keyboard scheme, repeating-task
rollover, permissions, date handling — are locked by fixtures generated from the astrid-web sources,
so a change there fails this repo's tests instead of shipping a silent divergence.

## Getting started

Install the toolchain (see [docs/context/stack.md](./docs/context/stack.md) for exact versions):

```powershell
winget install Rustlang.Rustup
winget install Microsoft.DotNet.SDK.9
winget install Microsoft.VisualStudio.2022.BuildTools   # with the ARM64 MSVC component
```

Then:

```powershell
npm run predeploy       # the standard gate: format, lint, test, ARM64 cross-build, contracts
cargo test --workspace  # the inner loop
```

## Documentation

| File | Purpose |
|---|---|
| [docs/ASTRID.md](./docs/ASTRID.md) | Architecture and the rules that govern this repo — read first |
| [docs/CONTRACTS.md](./docs/CONTRACTS.md) | Shared rules, and where the clients currently disagree |
| [docs/context/stack.md](./docs/context/stack.md) | Pinned versions and machine setup |
| [contracts/README.md](./contracts/README.md) | How the contract fixtures are generated |
| [docs/AUTOMATION.md](./docs/AUTOMATION.md) | The task loops, and the secrets they need |
| [CONTRIBUTING.md](./CONTRIBUTING.md) | How to contribute |

## License

MIT — see [LICENSE](./LICENSE).
