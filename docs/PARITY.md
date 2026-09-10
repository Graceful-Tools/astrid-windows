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
| My Tasks | ✅ | ✅ | The view the app opens on — yours and nobody's — with filters that belong to the account and follow you between machines |
| Task list with subtasks and windowing | ✅ | ✅ | Windows sends a window of rows; the Mac loads the list |
| Quick add, with `#list` tags | ✅ | ✅ | When the account's smart parsing is on, `#health` files the task in Health and leaves the title, by the web's rule in the core; off, the title is kept as typed. Dates and priority words are not parsed here (CONTRACTS D11) |
| Task detail: title, description, priority, lists | ✅ | ✅ | Field order comes from the core. The description is drawn as the web draws it — GFM plus the @/#/! pills, rendered in the core — and clicked to edit. The Lists row edits: add, remove, or create a list from a search, with the web's privacy and colour rules in the core. The action menu is the web's: Copy link, Share (a shortcode minted on the server), Status (the board's columns, the same move a dragged card makes), Won't do / Reopen (`closedReason`, no repeat rollover), Delete |
| Completion, including repeat rollover | ✅ | ✅ | One path, `TaskService::complete` |
| Due dates and quick picks | ✅ | ✅ | Instants computed in the core, not in XAML |
| Reminders | ✅ | ✅ | Windows adds a banner with Complete and Snooze |
| Repeat editor, presets and custom | ✅ | ✅ | The summary is one function on both |
| Assignee picker | ✅ | ✅ | Windows offers agents; the Mac's picker predates them (CONTRACTS D8) |
| Comments | ✅ | ✅ | Replies nest under their parent; your own can be edited and deleted; all of it offline through the Outbox. `@person`, `#list` and `!task` autocomplete in the box, inserting the form the server resolves; comment text renders through the same markdown as a description, so a mention is a pill |
| Search | ✅ | ✅ | Over the cache on both — there is no server search |
| Board view | ✅ | ✅ | Drag a card, or move it from its menu — which a keyboard can also do. A tapped card opens its task in place, inside the column, as the web's does; the list keeps its side pane. Columns are added, renamed, reordered and removed from list settings, with the web's rules locked by a fixture |
| Filters and sort | ✅ | ✅ | All seven filters, fixture-checked values |
| Chat | ✅ | ✅ | |
| List settings, sharing, members | ✅ | ✅ | Colour (the web's palette), favourite, privacy, the coding agent and its repository, and the defaults for new tasks have controls in the flyout; quick-add applies the defaults in the core, as the web does (CONTRACTS D10) |
| Keyboard scheme | ✅ | ✅ | The same table, from one fixture |
| Offline-first with an Outbox | ✅ | ✅ | |
| Sign-in through the browser | ✅ | ✅ | |
| Localisation | ✅ (12 languages) | ✅ (English) | Windows has the mechanism and one language |
| Themes | ✅ | ✅ | Ocean, light, dark and auto — the same four the Apple clients store, chosen per machine. Appearance also carries the account's task-detail layout (list / project), which rows and the detail draw with at once; smart task creation on/off; and where subtasks appear — indented in lists, or inside the parent only |
| Task settings | ✅ | ✅ | The web's Tasks page: Email-to-Task on/off and address, default due date and time for emailed tasks — stored on the server through `/api/v1/users/me/smart-tasks`, shaped and validated in the core |
| Attachments | ✅ | ✅ | Open, attach from disk, paste from the clipboard; drawn in the comment they arrived on. A picture draws as a thumbnail from bytes already on this machine — the Outbox's own copy for one just posted — rather than being fetched back (AITD-308). Queued offline, with the bytes copied so the original can move away |
| Timer on a task | ✅ | ✅ | Windows keeps a running timer across a restart; the Mac's is in memory |
| Account screen and reminder settings | ✅ | ✅ | Push, email, default offset, digest, quiet hours. The Account page carries the web's sections: profile photo and display name, email verification with resend, account information, and account deletion behind the typed phrase the server requires. Passkeys are listed, renamed and revoked over the web's `/api/v1/users/me/passkeys`; adding one is the browser's WebAuthn ceremony |
| Global quick-add hotkey | ✅ | ✅ | Ctrl+Shift+A; not yet rebindable |
| Command palette | ✅ | ✅ | Ctrl+K, with the Mac's own fuzzy ranking |
| First-run tour | ✅ | ✅ | The three things nobody can discover by looking |
| External sync: connect, link a list | ✅ | ✅ | Both providers |
| External sync: mirroring Google Tasks | ✅ | ✅ | Pull and push, every five minutes and on demand |
| My Tasks ↔ Google's default list | ✅ | ✅ | Unlisted tasks assigned to you, mirrored against the list Google files stray tasks in |
| Google auto-link modes (all lists, bidirectional) | ✅ | ✅ | Chosen in the account flyout; the pass makes counterparts on both sides, adopting a same-name list rather than duplicating it (CONTRACTS D9) |
| Deleting a Google twin when a task is deleted here | ✅ | ✅ | A ledger captures the link at delete time — the server's link row cascades away with the task — and the next pass removes the twin and tombstones the id |
| Agent Hub | ✅ | ✅ | Modes, credentials, Copilot, the webhook editor and the agents an account registers of its own |
| Profile numbers and data export | ✅ | ✅ | JSON or CSV, written where you choose |
| Contacts, Help & Support, Privacy, Terms | ✅ | ✅ | Contacts lists what the account imported and can clear it (no import here: Windows has no address book to read); Help, Privacy and Terms open the web's own pages in the browser |

## Not yet

| What | Mac | Windows | What it needs |
|---|---|---|---|
| Signing the MSIX bundle | n/a | ➖ | Not needed: Astrid ships through the Store, which signs at submission. A certificate of ours would only be replaced by theirs. A self-hosted download would need one — see the Store section below |
| The Store listing | n/a | ⏳ | The package is Store-shaped: packaged build, full tile set, `.msixupload` container. Blocked on a Partner Center company account for Graceful Tools LLC — business verification gates the app-name reservation, and the reservation is what supplies the package identity |
| The update feed (`.appinstaller`) | n/a | ❌ | Superseded for now: the Store updates Store installs. A self-hosted feed would need our own certificate, and Windows disables the `ms-appinstaller:` handler by default, so it would be a download-and-double-click rather than a web install |

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
than fixed. D7 (an unknown due-date filter hides undated tasks), D8 (the two Apple clients order
the assignee picker differently) and D9 (auto-link duplicates a list Apple cannot get to the
server) are the ones that touch this app today.
