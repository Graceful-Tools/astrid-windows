# Parity with the Mac app

*What the Windows app does, what it does not yet, and what it deliberately never will.*

The target is **the Mac app minus the Apple-only surfaces**. That is the honest comparison: the Mac
and Windows apps are the two desktop clients, they read the same API, and a person moving between a
work laptop and a home machine should not have to learn a different product.

This file is the M3 deliverable and is kept current as work lands. `docs/PROGRESS.md` tracks the
milestones; this tracks the *product*.

---

## Done

| What | Mac | Windows | Notes |
|---|---|---|---|
| Sidebar: lists, favourites, virtual lists | ✅ | ✅ | Today, Not in a List, I've Assigned |
| Task list with subtasks and windowing | ✅ | ✅ | Windows sends a window of rows; the Mac loads the list |
| Quick add, with `#list` autocomplete | ✅ | ✅ | Same parser, in the core |
| Task detail: title, description, priority, lists | ✅ | ✅ | Field order comes from the core |
| Completion, including repeat rollover | ✅ | ✅ | One path, `TaskService::complete` |
| Due dates and quick picks | ✅ | ✅ | Instants computed in the core, not in XAML |
| Reminders | ✅ | ✅ | Windows adds a banner with Complete and Snooze |
| Repeat editor, presets and custom | ✅ | ✅ | The summary is one function on both |
| Assignee picker | ✅ | ✅ | Windows offers agents; the Mac's picker predates them (CONTRACTS D8) |
| Comments | ✅ | ✅ | |
| Search | ✅ | ✅ | Over the cache on both — there is no server search |
| Board view | ✅ | ✅ | Drag a card, or move it from its menu — which a keyboard can also do |
| Filters and sort | ✅ | ✅ | All seven filters, fixture-checked values |
| Chat | ✅ | ✅ | |
| List settings, sharing, members | ✅ | ✅ | |
| Keyboard scheme | ✅ | ✅ | The same table, from one fixture |
| Offline-first with an Outbox | ✅ | ✅ | |
| Sign-in through the browser | ✅ | ✅ | |
| Localisation | ✅ (12 languages) | ✅ (English) | Windows has the mechanism and one language |
| Attachments | ✅ | ✅ | Open, and attach from disk — see below for what is not queued |
| Timer on a task | ✅ | ✅ | Windows keeps a running timer across a restart; the Mac's is in memory |
| Account screen and reminder settings | ✅ | ✅ | Push, email, default offset, digest, quiet hours |
| Global quick-add hotkey | ✅ | ✅ | Ctrl+Shift+A; not yet rebindable |
| Command palette | ✅ | ✅ | Ctrl+K, with the Mac's own fuzzy ranking |
| First-run tour | ✅ | ✅ | The three things nobody can discover by looking |
| External sync: connect, link a list | ✅ | ✅ | Both providers |
| External sync: mirroring Google Tasks | ✅ | ✅ | Pull and push, every five minutes and on demand |
| Profile numbers and data export | ✅ | ✅ | JSON or CSV, written where you choose |

## Not yet

| What | Mac | Windows | What it needs |
|---|---|---|---|
| An attachment queued offline | ✅ | ❌ | Everything else here writes through the Outbox; an upload needs a connection and says so |
| Deleting a Google twin when a task is deleted here | ✅ | ❌ | Needs a local ledger: the server's link row cascades away with the task, so the evidence is gone by the next pass. A deleted task simply stops being pushed |
| Google auto-link modes (all lists, bidirectional) | ✅ | ➖ | The planner is ported and tested; nothing calls it yet — links are made by hand |
| My Tasks filters | ✅ | ➖ | The filter sheet covers the virtual lists; the account-wide My Tasks preferences endpoint is not implemented |
| Agent Hub, AI keys | ✅ | ❌ | |
| MSIX packaging and updates | n/a | ❌ | M4 |

## Never — Apple-only by nature

| What | Why |
|---|---|
| Apple Reminders sync | An Apple framework. Windows has no equivalent to mirror. |
| Apple Foundation Models (on-device AI) | Ships with the OS. The server-side agents are the cross-platform path. |
| Contacts picker | Windows has no comparable shared address book to read. |
| iCloud handoff, Shortcuts, widgets | Platform features with Windows analogues that are not the same product — a Windows widget would be its own design, not a port. |

## Where Windows is ahead

- **The rules are in one place and tested.** Filters, repeats, board columns, permissions and the
  keyboard scheme are a Rust core with 500-odd tests, and four of them are locked against
  astrid-web's own implementation by generated fixtures. The Apple clients implement the same rules
  twice — iOS and Mac — which is where several of the divergences in `docs/CONTRACTS.md` came from.
- **Selection opens a task.** The Mac and web both do this; Windows briefly required a double-tap,
  which is fixed, and the fix is what made the detail pane reachable from a keyboard.
- **An assignee picker that offers agents from every surface**, which on the Mac is detail-only.
- **A timer that survives a restart.** The start time is in the cache rather than in memory, so
  quitting the app does not lose a session somebody started an hour ago and never noticed.
- **A reminder that says what it is for.** Windows offers offsets from the due time per task; the
  Mac has offsets only as a global default.

## Known divergences

Every difference in *behaviour* — as opposed to a screen that exists on one client and not the
other — is written up in [CONTRACTS.md](./CONTRACTS.md) with the reason it was reproduced rather
than fixed. D7 (an unknown due-date filter hides undated tasks) and D8 (the two Apple clients order
the assignee picker differently) are the ones that touch this app today.
