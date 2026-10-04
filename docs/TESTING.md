# Testing — what runs, and when

*The gates are listed in [CLAUDE.md](../CLAUDE.md). This file answers the question that one does
not: what covers the UI smoke tests, and when.*

---

## The layers

| Layer | Where | Runs in |
|---|---|---|
| The rules, the services, the Outbox, sync | astrid-core (its own repo) | astrid-core's gate |
| The bindings contract — `Commands.cs` against the core's command list | `crates/astrid-ffi/tests/` | `npm run predeploy` |
| Shell view-models, against a fake core | `app/Astrid.App.Tests/` | `npm run predeploy` |
| The app itself: a real window, the real core, a real SQLite file | `app/Astrid.App.UITests/` | `npm run predeploy:full` **only** |

The first three need nothing but a checkout. The fourth needs a desktop.

## The UI smoke suite runs when a person runs it

**`npm run predeploy:full`, by hand, at an unlocked machine. Nothing runs it automatically, and
that is deliberate.**

The suite is ten tests and a couple of minutes. It is not in `npm run predeploy` — the standard
gate, and the one the scheduled `/fixall` loop runs — because it needs a desktop session that the
loop does not have. So between deliberate `:full` runs, the things only it covers can break and
every task still reports green. That is a real gap, and it is the gap that hid task `f850514f` for
five days.

Three ways to close it were considered. The reasons the first two were not taken:

- **On a schedule (nightly).** The machine that would run it is locked whenever nobody is at it,
  and two of these tests cannot run on a locked session at all (below). A nightly job would
  therefore report failure every night for a reason that is never the code — which is worse than
  no job, because it trains everyone to ignore it.
- **After every merge to `main`.** Same objection: the merges happen inside the scheduled loop,
  on the same locked machine.

So the honest answer is the third: **the mouse-driven coverage is tied to a person being here.**
Run `npm run predeploy:full` before a release, and after any change to the task rows, the board,
the header or a flyout. The release path is manual anyway — the Store and the update feed are a
deliberate act, never a push — so this sits in the same place.

A note for anyone who later gets a CI runner with an interactive session: that removes the
objection, and this decision should be revisited rather than inherited.

## A locked session cannot run the mouse-driven tests

`A_row_dragged_below_another_stays_there_task_7883f710` and
`A_card_dragged_onto_a_column_lands_in_it_task_b8e42e70` drive a real mouse, because that is the
only thing that can catch what they exist to catch — a card that is a WinUI `Button`, which the
framework never starts a drag from, passes every unit test there is.

A real mouse goes to whatever window is in front. On a locked machine that is
`LockApp` — "Windows Default Lock Screen" — and nothing can be activated past it:
`SetForegroundWindow`, `BringWindowToTop` and `SwitchToThisWindow` all fail, quietly.

What makes this worth writing down is how it presents. **UI Automation does not care about the
lock screen.** It finds every element, reports every bounding rectangle, and the pointer travels
exactly where it was aimed — so the test gets all the way to its assertion and reports *the row
did not stay where it was dropped*. It reads precisely like a drag regression, and it was
diagnosed as one: task `f850514f` was filed naming fifteen commits and a prime suspect, and a
`git bisect` across them found the same failure at every commit, including ones that predated the
suspect by a day. Nothing had broken. The machine had locked.

`AstridApp.Activate()` is the fix. It brings the window to the front before any real-mouse
interaction and **throws, naming what is in front and which process owns it**, when it cannot. A
locked machine now says

```
could not bring the app to the front: its window is 1835966 and in front is
2819016 "Windows Default Lock Screen" (class Windows.UI.Core.CoreWindow, LockApp pid 3076).
```

which is the difference between a question and an answer.

`Dismiss()` asks with `TryActivate()` and carries on if it cannot: light-dismissing a flyout is a
nicety there, and the seven tests that do not need a mouse still pass on a locked machine. Keep
that split — a hard `Activate()` in a test that does not need the pointer would throw away
coverage for nothing.

## Running them

```powershell
npm run predeploy:full        # the shell build plus the UI suite

# or directly, which is the inner loop while working on a test:
dotnet build app/Astrid.App/Astrid.App.csproj -c Debug -r win-arm64 --self-contained false
dotnet test app/Astrid.App.UITests/Astrid.App.UITests.csproj -c Debug
```

The build must be for the **machine's** architecture, which on this hardware is ARM64 even though
Git Bash and PowerShell both report x64 — they run emulated. `rustc -vV` tells the truth, and
`scripts/predeploy.ps1` reads the registry. `AstridApp.ExecutablePath` looks for the host RID's
build, so a shell built for the wrong one is simply not found.

## What a failure usually means

| Message | Cause |
|---|---|
| `could not bring the app to the front … LockApp` | The session is locked. Unlock it; nothing is wrong with the code. |
| `the app did not start` / `never showed a window` | Usually no `astrid_ffi.dll` for this architecture, or a stale one. `cargo build -p astrid-ffi --target aarch64-pc-windows-msvc`. |
| `the core did not load` in the crash log | The wrong `astrid_ffi.dll` landed in `bin/`. Check which candidate `Astrid.Core.Bindings.csproj` picked — a stale `target/release` can shadow a fresh `target/<triple>/debug`. |
| `no element named '…' on screen. Saw: …` | The app drew a different screen, and the names say which. Not usually a test bug. |
| `error MSB3027 … locked by: "Astrid.App"` | A copy of the app is running and holds the DLLs the build wants to overwrite. Close it. |
