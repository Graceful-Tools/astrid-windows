# Microsoft Store listing — Astrid Tasks

*Drafted 2026-09-13 for task f91d2f07, to match the iOS App Store listing
([apps.apple.com/us/app/astrid-tasks/id6755752694](https://apps.apple.com/us/app/astrid-tasks/id6755752694))
so the two stores describe one product. Everything below is pasted into Partner Center by hand;
nothing here is read by a build. Partner Center shows each field's character limit beside the
field — the counts noted here are the limits as of 2026-09.*

The upload is `dist/Astrid-<version>.msixupload` from `scripts/package.ps1`, with the identity in
`packaging/store-identity.json` (filled in 2026-09-13). Product name: **Astrid Tasks** (reserved;
"Astrid" was taken).

---

## Store listing — English (United States)

### Product name

Astrid Tasks

### Description (up to 10,000 characters)

Astrid is your personal assistant dedicated to helping you be your best self — happier, healthier, and more productive. Astrid helps you and the people around you get the important things done, in life and at work. It is inspired by the retired Astrid Tasks of the early 2010s, entirely re-imagined and rebuilt, and powered by AI.

Astrid Tasks for Windows is the native desktop app for astrid.cc. It keeps your tasks on your PC and everywhere else you use Astrid — the web, iPhone, iPad and Mac — so a task you add on the bus is on your desk when you sit down.

WORK TOGETHER
• Share a list with family, friends, or co-workers. Everyone sees the same tasks, and who is doing what.
• Assign tasks to people — or to an AI agent — and follow what happens in the task's comments.
• Every list has its own chat, so the conversation about the work stays next to the work.
• Project boards for the lists that need them: Inbox, Ready, Doing, Waiting, Done, with your own statuses if you want them.

MADE FOR WINDOWS
• Ctrl+Shift+A brings Astrid forward from anywhere, ready for a new task. Change the shortcut if you like.
• Ctrl+K finds a list, opens a task, or runs a command. Every shortcut works without a modifier: n for a new task, x to finish one, ? to see them all.
• Reminders arrive as Windows notifications.
• Works offline. Everything you do is saved on your PC first and synced when you are back online, so a lost connection never loses a task.
• Runs natively on x64 and ARM64 PCs.

ORGANISE YOUR WAY
• Lists, favourites, and colours. Type "Pushups #health" and the task files itself in Health.
• Due dates, times, and repeating tasks — daily, weekly, monthly, or your own pattern.
• Priorities, subtasks nested as deep as you need, descriptions, comments, and attachments.
• My Tasks, Today, and saved filters show you what matters now, across every list.
• Search everything, including finished tasks.

CONNECTED
• Sync a list both ways with Google Tasks.
• Mirror a list with GitHub Issues, and let a coding agent work the tasks you assign it.
• Import contacts for collaborator suggestions.
• Sign in with a passkey, Google, or a magic link — all from your browser, once.

Astrid is free. Your data is yours: export it any time, and delete your account whenever you choose.

### Short description / promotional text (keep under 270 characters)

Tasks, shared with the people you share them with. Lists, boards, reminders and chat, synced across Windows, web, iPhone, iPad and Mac — and it works offline.

### Product features (up to 20, each up to 200 characters)

1. Share lists with family, friends and co-workers, and see who is doing what
2. Assign tasks to people or to AI agents, and follow the work in comments
3. A chat for every list, next to the tasks it is about
4. Project boards: Inbox, Ready, Doing, Waiting, Done — or your own statuses
5. Ctrl+Shift+A opens Astrid from anywhere, ready for a new task
6. Ctrl+K finds a list, opens a task or runs a command
7. Reminders as Windows notifications
8. Works offline; every change syncs when you are back online
9. Repeating tasks: daily, weekly, monthly or your own pattern
10. Subtasks, priorities, descriptions and attachments
11. Two-way sync with Google Tasks
12. Mirror a list with GitHub Issues
13. Native on x64 and ARM64 PCs
14. English and German

### What's new (first submission)

The first release of Astrid Tasks for Windows.

### Search terms (up to 7, each up to 30 characters)

1. to-do list
2. tasks
3. shared lists
4. reminders
5. project board
6. google tasks
7. productivity

### Copyright and trademark info (up to 200 characters)

© 2026 Graceful Tools LLC

### Additional license terms

Leave empty (the Standard Application License Terms apply).

### Website

https://astrid.cc

### Support contact info

support@astrid.cc — Jon's answer, 2026-09-14 (task 4732ef2f), and **the mailbox is live**; he
confirmed it receives mail the same day. That is the half certification actually tests: it writes
to the address and expects a person to answer, so a plausible-looking address nobody reads fails
where no address at all would only have been a missing field. Not the Partner Center account
address, which is `jon@gracefultools.com`; the support contact should be the one people are meant
to write to.

### Privacy policy URL

https://www.astrid.cc/privacy — read on 2026-09-14 (task 4732ef2f), not just pinged: 200, and
genuinely about this app rather than a template, which is what certification actually checks. It
names Astrid, the account data, task content and usage collected, the infrastructure providers
(Neon, Vercel), and carries a full Google Tasks section covering the sync, the token handling and
deletion.

Two things it does not yet name: **GitHub**, though a list can be mirrored to GitHub Issues and the
account stores a GitHub credential; and **imported contacts**, used for collaborator suggestions.
The Properties page below answers "Google and GitHub sync are opt-in" — an opt-in the policy does
not describe. Filed on the Astrid Web board as task ece00dc6, since the page lives there. It does
not block this submission: the policy is adequate as it stands.

---

## Store listing — German (Germany), optional

The package declares `de-DE`, so a German listing is allowed but not required. If you add one,
reserve the name **Astrid Tasks** for it too (Manage app names → reserve more names) — the same
name works in every language.

### Beschreibung

Astrid ist dein persönlicher Assistent, der dir hilft, die beste Version deiner selbst zu sein — glücklicher, gesünder und produktiver. Astrid hilft dir und den Menschen um dich herum, die wichtigen Dinge zu erledigen, im Leben und bei der Arbeit. Inspiriert vom Astrid Tasks der frühen 2010er, vollständig neu gedacht, neu gebaut und mit KI.

Astrid Tasks für Windows ist die native Desktop-App für astrid.cc. Deine Aufgaben sind auf deinem PC und überall sonst, wo du Astrid benutzt — im Web, auf iPhone, iPad und Mac.

GEMEINSAM ARBEITEN
• Teile eine Liste mit Familie, Freunden oder Kollegen. Alle sehen dieselben Aufgaben und wer was erledigt.
• Weise Aufgaben Personen zu — oder einem KI-Agenten — und verfolge in den Kommentaren, was passiert.
• Jede Liste hat ihren eigenen Chat, damit das Gespräch über die Arbeit bei der Arbeit bleibt.
• Projekt-Boards für die Listen, die sie brauchen: Eingang, Bereit, In Arbeit, Wartend, Erledigt.

FÜR WINDOWS GEMACHT
• Strg+Umschalt+A holt Astrid von überall nach vorn, bereit für eine neue Aufgabe.
• Strg+K findet eine Liste, öffnet eine Aufgabe oder führt einen Befehl aus.
• Erinnerungen kommen als Windows-Benachrichtigungen.
• Funktioniert offline. Alles wird zuerst auf deinem PC gespeichert und synchronisiert, sobald du wieder online bist.
• Läuft nativ auf x64- und ARM64-PCs.

DEINE ORDNUNG
• Listen, Favoriten und Farben. Fällige Termine, Uhrzeiten und wiederkehrende Aufgaben.
• Prioritäten, Unteraufgaben, Beschreibungen, Kommentare und Anhänge.
• Meine Aufgaben, Heute und gespeicherte Filter zeigen dir, was jetzt zählt.

VERBUNDEN
• Synchronisiere eine Liste in beide Richtungen mit Google Tasks.
• Spiegele eine Liste mit GitHub Issues.
• Anmeldung mit Passkey, Google oder Magic Link — einmal, im Browser.

Astrid ist kostenlos. Deine Daten gehören dir: exportiere sie jederzeit und lösche dein Konto, wann du willst.

### Kurzbeschreibung

Aufgaben, geteilt mit den Menschen, mit denen du sie teilst. Listen, Boards, Erinnerungen und Chat, synchronisiert über Windows, Web, iPhone, iPad und Mac — auch offline.

---

## Screenshots

`packaging/listing/screenshots/`, seven PNGs at 2538×1589 (the Store wants at least 1366×768,
PNG, up to ten per device family). Taken 2026-09-13 from the app signed in as the demo account
grace@astrid.cc, window sized to 2560×1600 physical pixels on a 200 % display. Upload them under
**Desktop** in this order; the captions are optional Store fields.

| File | What it shows | Caption |
|---|---|---|
| `01-list.png` | A list, its tasks, the add box, and the header actions | Your lists, your way |
| `02-task-detail.png` | A task open beside the list: assignee, date, reminder, repeat, priority, and an AI agent's answer in the comments | Everything about a task in one place |
| `03-command-palette.png` | Ctrl+K with the commands and their single-key shortcuts | Ctrl+K does it all |
| `04-shared-list.png` | A shared list with repeating tasks and due dates | Share a list with the people who matter |
| `05-repeating-tasks.png` | Repeating tasks with times | Daily, weekly, or your own pattern |
| `06-list-settings.png` | List settings: colour, image, favourite, privacy, coding agent, defaults | Private, shared, or public |
| `07-appearance.png` | Appearance settings: theme, the global shortcut, task layout | Made for Windows |

Not included: the German interface. Only the core's words follow `ASTRID_LANGUAGE`; the window
chrome follows the Windows display language, so a fully German screenshot needs a machine set to
German.

Store logo: `listing/store-logo-1024.png`, 1024×1024 with a transparent background, written by
`scripts/make-icons.ps1` from the same master the tiles come from. Partner Center's minimum is
300×300 and this is the master's own size — the file astrid-web calls `icon-4096x4096.png` is
actually 1024 square, so anything larger would be an enlargement. Poster art (2:3, 720×1080) and
trailers are optional.

---

## Properties page

| Field | Value |
|---|---|
| Category | Productivity |
| Subcategory | none (Productivity has none) |
| Privacy policy URL | https://www.astrid.cc/privacy |
| Website | https://astrid.cc |
| Support contact | support@astrid.cc |
| Supports accessibility | No (do not claim it until the checklist has been walked; the app is fully keyboard- and screen-reader-driven, which is a good start) |
| Product declarations | none of the boxes apply (no in-app purchases, no dependencies on non-Microsoft drivers or software) |
| System requirements | Windows 10 version 1809 (build 17763) or later; x64 or ARM64; internet connection for sync |

## Age ratings (IARC questionnaire)

Answer the questionnaire as the app is, not as the web is. Draft answers:

| Question | Answer | Why |
|---|---|---|
| Violence, sexual content, language, controlled substances, gambling, horror | No | none in the app |
| Can users interact or exchange content with each other? | Yes | shared lists, comments, chat |
| Does the app share the user's location with other users? | No | the package requests no location capability |
| Does the app share personal information with third parties? | No | Google and GitHub sync are opt-in, initiated by the user |
| Digital purchases | No | free, nothing to buy |
| User-generated content that is unmoderated | Yes | task text, comments, chat |

Expect an IARC "3+ / Everyone" rating with a "Users Interact" descriptor. The iOS listing is 13+
because Apple asks different questions (unrestricted web access); the two do not have to match.

## Pricing and availability

| Field | Value |
|---|---|
| Markets | All markets |
| Pricing | Free |
| Free trial | none |
| Discoverability | Make this product available and discoverable in the Store |
| Schedule | as soon as it passes certification |
| Organisational licensing | allow (default) |

## Submission options

Notes for certification (the box on the last page), so the tester can sign in:

> Astrid needs an account. Sign-in opens the default browser at astrid.cc; use "Continue with
> Google" or a magic link with any email address. A test account is not required — a new one is
> created on first sign-in. Ctrl+Shift+A from any app brings Astrid forward; Ctrl+K opens the
> command palette.

**No test account** — Jon's decision, 2026-09-14 (task 4732ef2f). The note above is what the
tester gets: they create an account on first sign-in, on the real service, like any other user.
That is normally what certification wants. If a run ever needs the tester kept off the real
service, make a fresh account on astrid.cc and paste its email and a typable password here — a
magic-link-only account is no use to them, since they cannot read the mailbox.
