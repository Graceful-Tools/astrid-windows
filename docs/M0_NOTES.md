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
| MSIX on ARM64 | Does an x64 + ARM64 bundle install and run on Windows 11 ARM? | Separate per-architecture packages |

### Settled — the global hotkey, 2026-09-07

**Ctrl+Shift+A, and the app really holds it.** Registered with `RegisterHotKey(IntPtr.Zero, …)` on a
thread of its own: passing a null window posts `WM_HOTKEY` to the *thread's* queue, so this needs a
thread with a message loop and nothing else — no window class, no subclassing of the WinUI window,
and no interference with XAML's own message handling.

Proved by trying to register the same chord from a second process while the app runs, which fails,
and succeeds again once the app exits. So the chord is free on a default install and the
registration is real rather than silently ignored.

**A taken chord is reported rather than swallowed.** `RegisterHotKey` fails when another app got
there first and tells nobody; `GlobalHotkey.IsRegistered` carries that, and the failure is logged.
The fallback in the original plan — ship rebindable from day one — is still worth doing and is not
done: there is one chord and no way to change it.

**What is still unverified: that it brings the window forward.** The handler runs and calls
`Restore` + `Activate` + `SetForegroundWindow`, but this session has no foreground window at all
(`GetForegroundWindow` returns 0, the same reason the toast spike could not see a banner), so
nothing here can tell a working activation from a refused one. Somebody at a desk pressing
Ctrl+Shift+A while in another app settles it.

### Settled — toasts from an unpackaged app, 2026-09-07

**Registration works.** `AppNotificationManager.Default.Register()` succeeds from the unpackaged
build and creates the AUMID under `HKCU\Software\Classes\AppUserModelId\` keyed by the exe path,
so no packaged identity and no hand-made Start-menu shortcut are needed. Registration is wrapped in
a `try` regardless: a machine with notifications off by policy is a normal machine, and the app runs
there without banners rather than refusing to start.

**The payload carries both buttons.** Driving the built app with a reminder due in the cache
produced exactly one banner, with `Complete` and `Snooze 10 min` as actions and the task id in the
arguments of each:

```
<toast launch='action=open;taskId=…'>… <actions>
  <action content='Complete' arguments='action=complete;taskId=…'/>
  <action content='Snooze 10 min' arguments='action=snooze;minutes=10;taskId=…'/>
</actions></toast>
```

**What is still unverified: a person clicking one.** `Show` returned without error, but nothing
appeared on screen and Windows recorded no notification for the AUMID, because
`SHQueryUserNotificationState` reported `QUNS_NOT_PRESENT` in this session — the system suppresses
display when it does not believe a user is at the machine. The activation path
(`NotificationInvoked` → `complete` / `snooze`) is written and its handlers are the same commands
the UI uses, but it needs somebody at a desk to press the button before it can be called settled.

### Settled — the boundary, 2026-09-07

**Not UniFFI.** The question was whether a 10k-row snapshot could cross fast enough to draw a list.
The answer was to stop sending snapshots: `rowsForList` returns the window on screen and the total
behind it, so a list of ten thousand crosses as the fifty rows being looked at. With the transport
question answered that way, UniFFI's C# generator — a third-party project that has to keep step with
UniFFI itself — bought nothing over the boundary that is there now: seven C functions and one
callback, in `crates/astrid-ffi/include/astrid.h`, with everything else a JSON command. Adding a
feature is a `Command` variant in the core rather than a symbol, a header entry, a `DllImport` and a
marshalling rule.

Two lifetime rules came out of building it, both tested and both the kind of bug that shows up as a
process vanishing with no stack: the completion delegate is a static field (a per-call one is
collected before the answer arrives), and `Dispose` stops the core *before* releasing its
`GCHandle`, because the contract `astrid_stop` can honour is "no callback starts after this
returns" — not "everything in flight finishes".

### Settled — protocol activation, 2026-09-07

It works cold and warm, and the fallback that was held in reserve is the design. `Program.cs` is
hand-written (`DISABLE_XAML_GENERATED_MAIN`), takes the single-instance key, and redirects any
later activation into the instance that owns the app. That is not a nicety: the sign-in callback
arrives as a *launch*, and a second copy of the app has none of the PKCE flow the first one is
waiting with — so without the redirect the hand-off could never complete.

Verified on this machine: launching the executable twice leaves one process with one window, and
`astrid://auth/callback?…` resolves and reaches the running instance without starting another. The
scheme is registered under HKCU on every start rather than at install time, because an unpackaged
app can be moved and a stale registration silently stops sign-in from ever completing.

### Settled — the credential at rest, 2026-09-07

The fallback, on purpose. `PasswordVault` is a WinRT API with apartment sensitivity, called from
whichever pool thread the runtime picks — which is exactly what the spike was worried about. DPAPI
is a flat Win32 call with neither problem, and `astrid-ffi`'s `ProtectedFileStore` uses it, with a
test asserting the token is not readable in the file. The `SecureStore` trait keeps this a decision
rather than a commitment: a Credential Locker implementation can be handed in later and nothing
above it changes.

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
