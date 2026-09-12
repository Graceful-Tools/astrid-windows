# Astrid for Windows — single source of truth for AI agents

*Read by every AI agent working in this repo, and by anyone new to it.*

This file owns the architecture and the rules. [CLAUDE.md](../CLAUDE.md) and
[AGENTS.md](../AGENTS.md) are thin operational adapters holding commands and workflow, and they
point here for everything else. **When architecture changes, change it here and nowhere else** —
duplicating it into the adapters is how the sibling repos drifted before.

**Read this before touching code that involves:** tasks, task completion, repeating tasks, the
Outbox, sync, chat, list members, permissions, or any API call.

---

## 0. Non-negotiable rules

These carry over from `astrid-ios/ASTRID.md` §0. Each one exists because breaking it shipped a
regression on another platform.

1. **All backend writes go through a service.** Never call the API client directly from the shell, a
   timer, a notification handler or a sync worker. If a service lacks the method you need, add it to
   the service first.
2. **Complete a task ONLY via `TaskService::complete_task`.** Never `update_task(completed: true)` —
   that path skips repeat rollover.
3. **Completing from a view that lets the user edit fields first?** Copy the edited fields (due date,
   all-day flag, repeating, repeating data, repeat-from) into the task argument first, so rollover
   anchors on what the user sees rather than on stale cache.
4. **Next-occurrence math lives ONLY in `repeating`.** Never inline pattern math. Mirror any change
   into `astrid-web/types/repeating.ts` and `astrid-ios/.../RepeatingTaskHandler.swift`.
5. **Preserve offline behaviour.** Task, list, comment, chat, attachment and account-settings
   writes journal through the Outbox; local-first caching and dedup must keep working. A few
   writes are online-only **on purpose** — membership (an optimistic member row would fool every
   permission check), board columns (shared configuration the server derives from), My Tasks
   filters (a filter replayed a week later moves a screen), agents, API access, share links and
   account deletion — and each says so in its service's doc comment and on screen. Anything
   else that reaches `ApiClient` from a service without an Outbox entry is a bug.
6. **All API paths are `/api/v1/...`.** Every path this client speaks is a constant or a builder
   in `api::endpoints`, and the request guard in `api::path` refuses anything else.
7. **TDD for bug fixes:** write a RED regression test naming the task id, watch it fail, then make
   it green. Run `npm run predeploy` before calling a task done.
8. **For breaking API changes, add a new version** — keep the existing one working.
9. **No business logic in `app/`.** The shell is windows, XAML, key dispatch and platform adapters.
   If it decides anything about tasks, lists, sync, permissions or user-facing copy, it belongs in
   `astrid-core`.
10. **Reuse before you write.** Permission helpers, i18n resources and the shared row view-model
    already exist; never inline a role comparison or a user-facing string literal. User-facing
    copy lives in `app/Astrid.App/Strings/<lang>/Resources.resw` and is read through
    `Strings.Get`. About half the shell's strings still bypass that (see `docs/PROGRESS.md`,
    "Release readiness"); do not add to them, and take a few out when passing.

---

## 1. Why the code is shaped this way

Astrid ships a web app (`astrid-web`, which is also the API server) and an Apple repo
(`astrid-ios`) containing two apps. The Mac app is the model this repo follows: it adds **zero**
business logic and is a thin shell over the iOS service layer, so a change that flows web → iOS
reaches Mac for free.

Windows cannot compile that Swift, so the shared layer is rebuilt once in Rust as `astrid-core`,
and the WinUI 3 shell sits on it under the same rule. The cost of the port is paid once; the cost
of a second set of business rules would be paid forever.

```
app/Astrid.App          WinUI 3, C#      windows, XAML, key dispatch, platform adapters
app/Astrid.Core.Bindings                 hand-written C# over the core's C ABI — partial by
                                         design, and read against the Command enum by a test
crates/astrid-core      Rust             models, API client, SQLite cache, Outbox, services,
                                         sync, SSE, auth, and every cross-platform contract
        |
        v  HTTPS /api/v1/*
astrid-web (astrid.cc)
```

`astrid-core` has **no Windows dependency**. Platform services — secure storage, notifications,
network reachability, file access — arrive through callback traits the shell implements. That keeps
the crate testable on any machine and keeps the platform boundary visible.

---

## 2. Cross-platform contracts

Rules that must read identically on web, Apple and Windows are locked by **generated fixtures**, not
by prose. `contracts/fixtures/*.json` is produced from the astrid-web sources by
`contracts/export-from-web.mjs`; the Rust tests compile those files in, so a web change fails this
crate's tests instead of shipping a silent divergence.

`cargo xtask check-contracts` (part of `npm run predeploy`) regenerates and diffs them.

| Contract | Canonical | Here | Status |
|---|---|---|---|
| Keyboard shortcuts (bare-key scheme + input/modal guard) | `astrid-web/hooks/useKeyboardShortcuts.ts` | `keyboard` | locked by `shortcuts.json` |
| Repeating-task rollover | `astrid-web/types/repeating.ts` | `repeating` | locked by `repeating.json`, generated by running web's own calculator |
| Session credential format | `astrid-ios/.../SessionCookie.swift` | `auth::session_cookie` | ported with its tests |
| Desktop hand-off sign-in (PKCE S256, state, callback shape) | `astrid-web/lib/auth/desktop-handoff.ts` | `auth::desktop_handoff` | ported with paired tests on both sides |
| List permissions | `astrid-web/lib/list-permissions.ts` | `permissions` | locked by `permissions.json`, generated by running web's own rules |
| Board columns, read and written | `astrid-web/lib/project-status.ts`, `lib/project-custom-states.ts` | `board` | locked by `board.json` and `statuses.json`, generated by running web's own rules |
| One editing session at a time (begin / end / cancel / commit-all) | `astrid-web/lib/editing-session.ts` | `editing`, held by the `App` and stepped by the `beginEditing` … `commitAllEditing` commands | locked by `editing.json`, generated by running web's own machine |
| All-day dates, parser, wire shapes, leading control | see plan §3 | — | to come |

Known divergences between the existing clients are recorded in [CONTRACTS.md](./CONTRACTS.md)
rather than silently resolved here.

**Change order for any contract:** change web first with its tests, regenerate the fixtures, update
this client, then mirror into astrid-ios. Deploy web before shipping a client that depends on it.

---

## 3. Where things live

| Area | Path | Notes |
|---|---|---|
| Models and wire shapes | `crates/astrid-core/src/model/` | serde; lenient decoding, because the server is permissive |
| API client | `crates/astrid-core/src/api/` | the only place that speaks HTTP; sends `x-platform: windows-app` |
| Local cache | `crates/astrid-core/src/store/` | SQLite; the read path never waits on the network |
| Outbox | `crates/astrid-core/src/outbox/` | the write path: idempotent, retrying, dependency-ordered, dead-lettering; delivered at once by `app::background::outbox_loop`, which a command rings when it journals something |
| Services | `crates/astrid-core/src/services/` | the canonical control points |
| Sync + real time | `crates/astrid-core/src/{sync,realtime}/` | a 60s pull that asks only for what moved since the last pass (with the server's tombstones), SSE on top, and every pass that changes the cache announces it to the shell |
| Contracts | `crates/astrid-core/src/{repeating,permissions,filters,parse,keyboard,rows}/` | pure, fixture-locked |
| Shell | `app/Astrid.App/` | no business logic; `ShellPage` arranges the parts under `Views/`, one UserControl per part of the window and per settings page |
| Automation | `crates/xtask/`, `scripts/` | `cargo xtask <command>`, `npm run predeploy` |

---

## 4. Milestones

The full plan, including what each milestone must prove before it is done, is in the approved build
plan. In short: **M0** foundation spike, **M1** core contracts and services, **M2** shell with
navigation, list, detail and keyboard, **M3** collaboration and remaining parity, **M4**
distribution, **M5** external sync providers.

Parity target is the Mac app, minus the Apple-only surfaces (Apple Reminders, on-device Apple
intelligence, native Sign in with Apple). `docs/PARITY.md` tracks it row by row from M2 onwards.

---

## 5. References

- [CLAUDE.md](../CLAUDE.md) / [AGENTS.md](../AGENTS.md) — commands and workflow
- [CONTRACTS.md](./CONTRACTS.md) — cross-platform rules and known divergences
- [context/stack.md](./context/stack.md) — pinned tool versions and machine setup
- `astrid-ios/ASTRID.md` — the Apple clients' architecture, which this mirrors
- `astrid-web/docs/PRODUCT_CONTRACT.md` — shared behaviour and copy
- `astrid-web/docs/API_CONTRACT.md` — the wire contract
