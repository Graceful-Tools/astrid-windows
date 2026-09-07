# Contributing to Astrid for Windows

Thanks for helping. Two documents matter before you write code:

- **[docs/ASTRID.md](./docs/ASTRID.md)** — the architecture and the rules. Read it first.
- **[docs/CONTRACTS.md](./docs/CONTRACTS.md)** — the behaviour shared with the web and Apple apps.

## The short version

1. **Business logic lives in `crates/astrid-core`, never in `app/`.** The WinUI shell renders and
   dispatches; if your change decides something about tasks, lists, sync, permissions or the words a
   user reads, it belongs in the core.
2. **Write the failing test first.** For a bug fix that is not optional: a RED test that reproduces
   it, named for the task id, then the fix. The test is the proof the bug existed and that you
   addressed it.
3. **Shared behaviour changes on web first.** Anything covered by a fixture in `contracts/fixtures`
   is canonical in astrid-web. Change it there with its tests, regenerate the fixtures, then update
   this repo, then astrid-ios.
4. **Run the gate before pushing:** `npm run predeploy`. It formats, lints, tests, cross-builds for
   ARM64 and checks the contracts.

## Porting from Swift

Much of the core is a port of `astrid-ios/Astrid App/Core/`. Read the Swift **tests** first and
write them as Rust tests before porting the implementation — they are the specification, and they
encode bugs that were expensive to find.

Do not improve behaviour while porting. If you find something wrong, matching it keeps the clients
in step; write the divergence into [docs/CONTRACTS.md](./docs/CONTRACTS.md) and raise it as a
cross-repo fix.

## Commits and branches

Work lands on `main`. A push builds nothing that reaches users — releases are triggered
deliberately. Keep commit messages in the imperative, and name the task id when there is one.
