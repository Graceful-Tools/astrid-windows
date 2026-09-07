# M0 — foundation notes

*What the spike settled, and what is still open. Companion to [ASTRID.md](./ASTRID.md).*

M0 exists to prove the risky seams before anything large is ported: the toolchain on both
architectures, the contract mechanism, and the platform boundaries the shell will have to cross.

---

## Settled

### Toolchain on both architectures

Rust 1.98.1, .NET SDK 9.0.317, VS Build Tools 2022 with MSVC 14.44 for x64 **and** ARM64, Windows
SDK 10.0.26100. Exact versions and the winget commands are in [context/stack.md](./context/stack.md).

`rust-toolchain.toml` pins the channel, components and both targets, so a fresh checkout installs
what it needs rather than failing later with a confusing linker error. ARM64 is cross-built on
every `npm run predeploy`; a dependency that cannot target it is caught the day it lands.

### The contract mechanism works, and it reads the guard

`contracts/export-from-web.mjs` generates `contracts/fixtures/shortcuts.json` from
`astrid-web/hooks/useKeyboardShortcuts.ts`, and the Rust tests compile it in with `include_str!`.

The part worth recording: the shortcut table alone is only half the contract. Whether a shortcut
requires a selected task lives in the dispatch `switch`, as `if (selectedTask)`, not in the table —
so the exporter walks the switch and reads the guard per case. A fixture built from the table alone
would have looked complete and locked down nothing about when a key is allowed to fire.

The parser is deliberately anchored on markers (`switch (key) {`) rather than a shape-sensitive
regex. Line endings and indentation differ between checkouts, and a brittle match fails as
"contract missing" rather than "parser stale" — the worse of the two errors to be handed.

### Sign-in exists on both sides, and is testable without a server

The browser hand-off is built end to end: `astrid-web` serves `/auth/desktop`,
`POST /api/auth/desktop/grant` and `POST /api/v1/auth/desktop/exchange`; this crate has the client
half in `auth::desktop_handoff`. Both are pure enough to test with no network — 17 tests here, 40 on
the server — and the rules are stated once per side in [CONTRACTS.md](./CONTRACTS.md) §4 so neither
can quietly drift.

Two things came out of building it that were not obvious from the plan:

- **The plan said Redis; it is a database table instead.** Redis is optional in that deployment and
  returns null with no `REDIS_URL`, so a Redis-backed code store would have made sign-in impossible
  against a local dev server — which is exactly what M0's exit criterion requires. The table also
  gets hashing at rest and an atomic single-use claim for free, matching what astrid-web already
  decided for OAuth codes.
- **The server has to name the session cookie.** Production issues
  `__Secure-next-auth.session-token` and development does not, and a native client picks a name
  before it has ever seen a server cookie. So the exchange returns `sessionCookieName`, and
  `session_cookie::replacing_token_named` uses it. Without it, signing in against a dev server
  succeeds and then reads as signed out on the very next request.

What is still untested is the part that needs a running shell: whether protocol activation actually
delivers the callback to a packaged app, cold and already-running. That spike is still open below.

### Three divergences between the existing clients

Found while porting the repeating calculator, recorded in [CONTRACTS.md](./CONTRACTS.md) rather
than fixed locally: iOS uses the device calendar for month and year steps where web uses UTC;
weekly custom patterns ignore their `interval` on both; and "same weekday of the month" drops the
time of day on both. Each needs a cross-repo change. Fixing any of them here alone would make this
client schedule occurrences the other clients do not have.

### Decisions taken with Jon, 2026-09-06

| Decision | Choice |
|---|---|
| Stack | Rust core, WinUI 3 (C#) shell, UniFFI between them |
| Sign-in for v1 | Browser hand-off to astrid.cc; native Windows Hello and Google later |
| External sync (Google Tasks, GitHub) | After core parity, as M5 |
| Packaging | MSIX for both the Store and the web download feed |

---

## Open — the runtime spikes

These need a running shell and cannot be settled from the core alone. Each is a checklist item
before M0 is called done; record the result here when it is.

| Spike | Question it answers | Fallback if it fails |
|---|---|---|
| UniFFI C# bindings | Does a 10k-row snapshot cross the boundary fast enough to render a list without hitching? | Move `TaskRow` transport to a compact binary buffer decoded in C#; keep the API shape |
| Protocol activation | Does `astrid://auth/callback` reach the app both cold and already-running? | Single-instance redirection via `AppInstance.GetCurrent()` |
| Global hotkey | Which default chord is free, and does `RegisterHotKey` behave in a packaged app? | Ship rebindable from day one; detect conflicts at registration |
| Toast actions | Do Complete and Snooze buttons activate the app and reach the core? | In-app reminder surface only, until it does |
| Credential Locker | Can the core's `SecureStore` trait be satisfied by `PasswordVault` from a background thread? | Encrypted file in the app's local data, DPAPI-protected |
| MSIX on ARM64 | Does an x64 + ARM64 bundle install and run on Windows 11 ARM? | Separate per-architecture packages |

**M0 is done when**, on x64 and ARM64: sign in through the browser hand-off against a local
astrid-web, go offline, create a task, relaunch and still see it, then reconnect and watch it appear
on the web.

---

## Notes for whoever picks this up

- The core builds and tests on macOS and Linux too. That is not an accident — it keeps the platform
  boundary honest, and it makes the inner loop fast on any machine.
- Read the Swift tests before porting a Swift module. They encode bugs that were expensive to find,
  and they are the specification.
- `npm run predeploy` skips the shell build until `app/Astrid.sln` exists, and says so. It should
  never quietly report success for a step it did not run.
