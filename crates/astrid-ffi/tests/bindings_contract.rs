//! The C# bindings and the core's commands, read against each other in both directions.
//!
//! `app/Astrid.Core.Bindings/Commands.cs` is hand-written and partial — a command with no caller
//! has no record — so the two directions need different rules:
//!
//! - **Everything the shell sends must exist in the core.** A variant renamed in the core, or a
//!   `kind` mistyped here, is a command the core answers with "bad request", which the shell shows
//!   as a button that does nothing: the hardest kind of bug to report.
//! - **Everything the core has must be bound or registered.** Being partial is fine; being
//!   *accidentally* partial is not. Until this was enforced, the second half only `eprintln!`d its
//!   list — and cargo hides the output of a passing test, so twenty commands accumulated unnoticed
//!   while the Apple apps moved onto the shared core (task 70579f67). [`NOT_BOUND`] is now the
//!   written record, and a command the core adds tomorrow fails this gate until somebody says
//!   which it is.
//!
//! **Do not try to answer either question by grepping.** `astrid_core::app::command_kinds()`
//! takes the variant names from `strum::VariantNames` on the core's `Command` enum (astrid-core
//! `command.rs`, since AWTD2-139) and lowercases the first letter, so a wire spelling such as
//! `reconnectStream` appears nowhere in the core's source; and the C# kinds are string literals
//! inside factory bodies. The two sets, compared here, are the only reliable comparison — a grep
//! for one of these names will confidently tell you it does not exist.
//!
//! The core's half is `astrid_core::app::command_kinds()`, which every shell reads its own bindings
//! against; this file is the Windows shell's half. It lives with the FFI crate because that is the
//! Rust that stays beside the C#: the core is its own repository and knows nothing of `Commands.cs`.
//!
//! ## The third question: the FIELDS, not just the kinds (task 9ef7edb4)
//!
//! The two questions above are about the command's `kind` and nothing else — which left the ~6,000
//! lines of hand-written request and response shapes across the clients checked by nobody. And the
//! failure that produces is silent: `serde` **ignores an unknown request field**, so a field
//! renamed in the core arrives from this shell under its old name, decodes as `None`, and writes
//! nothing. No error, no log line, a control that half works.
//!
//! The core now publishes a JSON Schema derived from the types that ARE the wire
//! (`astrid_core::app::schema`), and the rest of this file holds `Commands.cs` and `Models.cs`
//! against it. As with `NOT_BOUND`, **leaving things out stays allowed** — this shell binds what it
//! draws — but **inventing a name does not**.

use std::collections::{BTreeMap, BTreeSet};

const COMMANDS_CS: &str = include_str!("../../../app/Astrid.Core.Bindings/Commands.cs");
const MODELS_CS: &str = include_str!("../../../app/Astrid.Core.Bindings/Models.cs");

/// Every `kind` the C# factories send: the first string literal handed to one of the request
/// records declared in the same file.
fn csharp_kinds() -> BTreeSet<String> {
    let records: Vec<&str> = COMMANDS_CS
        .lines()
        .filter_map(|line| {
            let after = line.trim_start().strip_prefix("private sealed record ")?;
            Some(after.split('(').next()?.trim())
        })
        .collect();
    assert!(
        records.len() >= 10,
        "found only {} request records in Commands.cs; the scan has stopped working",
        records.len()
    );

    let mut kinds = BTreeSet::new();
    for record in records {
        let needle = format!("new {record}(\"");
        let mut rest = COMMANDS_CS;
        while let Some(at) = rest.find(&needle) {
            let literal = &rest[at + needle.len()..];
            let end = literal.find('"').expect("a closed string literal");
            kinds.insert(literal[..end].to_string());
            rest = &literal[end..];
        }
    }
    kinds
}

#[test]
fn every_command_the_shell_sends_is_one_the_core_understands() {
    let rust = astrid_core::app::command_kinds();
    let csharp = csharp_kinds();
    assert!(
        csharp.len() >= 50,
        "found only {} kinds in Commands.cs; the scan has stopped working",
        csharp.len()
    );

    let unknown: Vec<&String> = csharp.difference(&rust).collect();
    assert!(
        unknown.is_empty(),
        "Commands.cs sends kinds the core has no variant for — each is a button that does \
         nothing: {unknown:?}"
    );
}

/// Commands the core has that this shell does not send, and why.
///
/// THE REASONS ARE THE DOCUMENTATION. This register is the only written record of which parts of
/// the core Windows deliberately leaves alone, so "not needed" is not a reason and will not pass:
/// say what this shell does instead, or name the task that will bind it.
const NOT_BOUND: &[(&str, &str)] = &[
    (
        "clearCache",
        "For a shell whose isolation guard caught another account's rows. This shell has no such \
         guard; signing out already clears the cache in the core.",
    ),
    // ── This shell draws the core's projections; these answer in the wire shape ───────────────
    (
        "tasks",
        "Cached tasks in the wire shape, for a shell that keeps its own models and filters them          itself. This one binds to the row projections (`taskRows`, `board`), which is where the          rules about what a row shows already live.",
    ),
    (
        "chatMessages",
        "A channel's messages in the wire shape, for a shell drawing its own transcript. This one          sends `chat`, the projected transcript, and draws that.",
    ),
    (
        "projects",
        "Every board in the wire shape. The board screen here asks `board` for one resolved          board; nothing draws a list of them.",
    ),
    // ── The Apple shells' move onto this core; one-offs by construction ───────────────────────
    (
        "seedCache",
        "Fills an empty cache from a shell's own on first launch after moving onto this crate —          the Apple apps' Core Data. This shell has never had a cache of its own to carry over.",
    ),
    (
        "importJournalEntry",
        "Carries writes another client's journal already applied, so an offline edit made before          that move still reaches the server. Same reason: there is no earlier Windows journal.",
    ),
    (
        "importExternalLedger",
        "Merges a sync ledger built by another store — an Apple client's old UserDefaults — into          the core's. Same one-off as `seedCache`: this shell has only ever had the core's ledger.",
    ),
    (
        "resolveIds",
        "Which real ids temporary ones became, for a shell holding its own copies of them. This          one redraws from the core after a create, so the temporary id leaves with the row.",
    ),
    // ── Surfaces this shell does not have yet ─────────────────────────────────────────────────
    (
        "uploadContacts",
        "Sends the device's address book up in batches so people can be found by a number the          account never knew (iOS AATD-68). Windows has no address book to read: the Contacts          settings here list who the server already knows (`contacts`, `clearContacts`), and          there is no equivalent of iOS's permission prompt to ask before sending any.",
    ),
    (
        "dropBoardCard",
        "A drop at a SLOT in a column, writing the move and the list's manual order (AITD-461).          The board here drops onto a column, not between cards — `BoardView` hands `MoveAsync` a          column id and nothing else — so it sends `moveTaskToColumn`, which is that same move          without the reordering. Binding this needs a drop target that knows which two cards it          fell between, which the board does not draw.",
    ),
    (
        "listCounts",
        "How many tasks each list's saved filters keep, for a sidebar that draws the number beside          a saved filter (AITD-460). Superseded by `listBadges`, which answers this for a saved          filter and the open tasks for every other kind of list, and which is what the sidebar          here asks (`SidebarViewModel.NumbersAsync`, task 8d37457b). Nothing is left for the          narrower command to answer.",
    ),
    (
        "myTasksFilters",
        "My Tasks draws here (`myTasksList`, `refreshMyTasks`); its filter and sort bar does not          exist yet, so there is nothing to read the saved filters into.",
    ),
    (
        "setMyTasksFilters",
        "The other half of the same missing bar. Writing a filter this shell cannot show would          change My Tasks on every other device from a screen with no control for it.",
    ),
    (
        "createProject",
        "A board is opened and edited here — `board`, `addBoardCard`, the status-column commands          — but making one is a web and Apple surface so far.",
    ),
    (
        "createBoardForList",
        "Turning a list into a board in one request: the same missing surface as `createProject`.",
    ),
    (
        "deleteProject",
        "Deleting a board detaches its lists rather than deleting them, which needs explaining on          screen before it is offered. No such screen here yet.",
    ),
    (
        "taskProjectFields",
        "A GitHub board's project fields for one task, schema and values (AITD-491). Nothing here \
         draws a GitHub-backed list at all — `model::Task.fields` and `model::TaskList.remote`, \
         the two keys that carry one, are not modelled yet either (task faa32e20). There is no \
         task detail section for these rows to land in.",
    ),
    (
        "setTaskProjectField",
        "The write half of the same. Setting a field this shell cannot show would change a \
         GitHub project from a screen with no control for it — the same reason \
         `setMyTasksFilters` is not bound.",
    ),
    (
        "cancelInvitation",
        "A list can be invited to here (`inviteToList`), but a pending invitation has no surface          to list, re-role or withdraw it from.",
    ),
    (
        "setInvitationRole",
        "The same missing surface: changing a role before the invitation is accepted needs the          pending list that this shell does not draw.",
    ),
    (
        "loadChatMessages",
        "Fetches a page of older history. The chat pane draws what the cache holds and has no          scroll-back that would ask for more.",
    ),
    (
        "forgetChatMessage",
        "Takes a message out of this device's transcript. Chat has no server-side delete, so this          is a local gesture the pane does not offer.",
    ),
    (
        "resolveChatChannel",
        "Resolves a channel for a list, or a virtual one such as My Tasks. `chat` already          resolves the channel for the open list, and chat on a virtual list is not drawn here.",
    ),
    (
        "postAgentResponse",
        "Posts, as the agent, an answer this device produced itself. This shell configures agents          and asks the server to answer; it does not run one and speak for it.",
    ),
    (
        "requestAstridResponse",
        "Asks the server to answer as Astrid when the device cannot. Reached through the agent          and chat surfaces here rather than as a command of its own.",
    ),
    // ── Another platform's wake-up model ──────────────────────────────────────────────────────
    (
        "nextReminder",
        "The earliest reminder still to come, so a shell can schedule its own OS alarm — \
         Android's AlarmManager (AATD-21). This shell notices a reminder by asking \
         `remindersDue` while it is running (`ShellViewModel.RaiseRemindersAsync`) and leaves \
         the rest to the server's push and email. It registers no background task that would \
         have a wake-up to set.",
    ),
    // ── iOS surfaces this shell does not draw (arrived with the core pin bump, task 8cbed184) ──
    (
        "nestTask",
        "A row dropped inside a list's tree — onto a row, on a line between rows, or indented and \
         outdented (AATD-42). Rows arrive already subtask-spliced from `taskRows` and the list \
         draws them without a drop target, so nothing here can say which row a drop landed on. \
         The same absent gesture as `dropBoardCard`, one level down.",
    ),
    (
        "profileTasks",
        "The public tasks a profile shares, beneath its three numbers, as iOS's UserProfileView \
         draws them (task da326190). The only profile this shell has is the account page in \
         Settings — `profileStats` is bound and the three numbers are drawn there — and it is \
         your own account's settings, not a person's profile: nothing here opens somebody else's, \
         so there is no section for these rows and nobody to be reading about.",
    ),
    (
        "reminderTriageDeck",
        "iOS's swipeable deck: the reminded task first, then what else is due today (AATD-54). A \
         reminder here is one Windows toast with buttons on it (`Reminders.cs`, raised from \
         `remindersDue`), so there is no deck to swipe and no assistant for an up-swipe to hand a \
         task to.",
    ),
    (
        "resolveShortcode",
        "What an `astrid.cc/s/<code>` share link opens (AATD-59). This shell is activated on its \
         own URL scheme for the sign-in callback alone (`SignInCallback`, and the registration in \
         `Program.cs`); it claims no handler for a shared link, so no code ever arrives to \
         resolve.",
    ),
    (
        "shareList",
        "Mints a link to a LIST, as iOS's Share List does (AATD-44). `shareTask` is bound and a \
         task can be shared from here; the sidebar draws no share control on a list itself.",
    ),
    (
        "toggleAssignee",
        "Adds or removes one person in a MULTI-assignee picker, agents first and at most ten \
         (AATD-51). A task here carries a single assignee — `updateTask`'s `assigneeId`, chosen \
         from `assigneeOptions` — so picking somebody replaces the assignee rather than toggling \
         one of several.",
    ),
    // ── The reads iOS needs one at a time, which this shell asks as one hub (task c7a669c2) ───
    //
    // These arrived with the pin move past the SQLite advisory, from the core's AWTD-1610 — the
    // commands iOS needs to finish moving off its own API client. The first three are the Agent
    // Hub's answer, split into its parts; `Command::Agents` says in as many words why it is one
    // command, and this shell binds that one.
    (
        "agentCredentials",
        "Which services have a key stored, on its own. The Agent Hub here asks `agents`, whose \
         doc says why it is one command: a screen that drew the agents before it knew which had \
         a credential would show \"needs setup\" on all of them and then correct itself. The \
         writes beside it are bound (`setAgentCredential`, `testAgentCredential`), so the only \
         thing this narrower read would add is a second, partial answer to a question the hub \
         has already answered.",
    ),
    (
        "agentModes",
        "The agents' mailbox rows and their modes, on its own — the same part of the same hub. \
         `agents` carries the mode each agent is set to and `setAgentMode` changes it, both \
         bound; nothing here redraws the modes alone.",
    ),
    (
        "copilotStatus",
        "Whether Copilot is connected, on its own — again part of the hub's answer, and the \
         connect and disconnect halves are bound (`connectCopilot`, `disconnectCopilot`). This \
         shell learns the connection state when it draws the hub, not from a poll of its own.",
    ),
    (
        "availableAgents",
        "The agents this account can assign, mention or chat with. Assigning and inviting here \
         go through `searchUsers`, which takes `includeAgents` and is the same question asked \
         where the surface actually asks it; `agents` covers the hub. The flag that narrows this \
         to what the server runs itself picks iOS's DEFAULT ASSISTANT (AITD-297), and this shell \
         has no such setting for the answer to land in.",
    ),
    (
        "featureFlags",
        "The server's whole flag answer, fetched fresh, with the version a `featureFlagsUpdated` \
         frame is compared against. This shell reads `features`, the cached projection, which is \
         the question a view has — and deliberately: that read answers offline, where this one \
         fails, and keeping the cache current is the core's sync layer's job rather than a \
         command the shell sends.",
    ),
    (
        "sendVerification",
        "Sends the verification email for the account's address — which this shell already does, \
         through the older `resendVerification` that the core still has and the account screen's \
         Resend button sends. Binding both would give one button two commands. Which of the two \
         pair the core keeps is its own call, not this shell's: noted in docs/CONTRACTS.md.",
    ),
    (
        "cancelVerification",
        "Withdraws a pending email change. The account screen shows the pending address — \
         `HasPendingEmail` and `PendingEmail` draw it as a sentence under the verification state \
         — but as text only: there is no control beside it to take the change back, so there is \
         nothing here to send this from. `changeEmail` is bound, which is how a pending change \
         is made in the first place.",
    ),
    (
        "searchContacts",
        "Autocomplete over the imported contacts while adding a member. The Contacts page here \
         is a count, a flat list and a Clear button (`ContactsSection.xaml`) with no field to \
         type in, and adding a member searches the people the server already knows through \
         `searchUsers`. Nothing draws a suggestion list this could fill.",
    ),
    (
        "recommendedCollaborators",
        "The Astrid users among the imported contacts, mutual ones first, to suggest as \
         collaborators. The same missing surface as `searchContacts`: `contacts` and \
         `clearContacts` are bound and the page lists what was imported, but it has no \
         suggestions section, and `inviteToList` is reached by searching rather than from a \
         suggestion.",
    ),
];

/// Every core command is either bound or registered — and a new one fails the gate.
#[test]
fn every_command_the_core_has_is_bound_or_registered() {
    let rust = astrid_core::app::command_kinds();
    let csharp = csharp_kinds();
    let missing = unregistered(&rust, &csharp, NOT_BOUND);
    assert!(
        missing.is_empty(),
        "the core has commands this shell neither sends nor accounts for. Bind each in          Commands.cs, or add it to NOT_BOUND with a reason saying what this shell does instead:          {missing:?}"
    );
}

/// Which core commands are neither bound nor registered.
///
/// A function rather than inline, so the enforcement itself can be tested on inputs of our own
/// rather than only on today's two lists — see `an_unregistered_command_is_caught`.
fn unregistered(
    core: &BTreeSet<String>,
    csharp: &BTreeSet<String>,
    register: &[(&str, &str)],
) -> Vec<String> {
    core.iter()
        .filter(|kind| !csharp.contains(*kind))
        .filter(|kind| !register.iter().any(|(listed, _)| listed == *kind))
        .cloned()
        .collect()
}

/// The register cannot rot: every entry is still a command, and none of them is bound.
///
/// Both halves matter. A kind the core renamed or removed leaves an entry that silently excuses
/// nothing, and an entry that has since been bound is a stale note claiming this shell does not do
/// something it now does — which is worse than no note, because somebody will believe it.
#[test]
fn the_register_describes_commands_that_exist_and_are_not_bound() {
    let rust = astrid_core::app::command_kinds();
    let csharp = csharp_kinds();

    for (kind, reason) in NOT_BOUND {
        assert!(
            rust.contains(*kind),
            "NOT_BOUND lists {kind:?}, which the core has no command for any more — delete the              entry or correct the spelling"
        );
        assert!(
            !csharp.contains(*kind),
            "NOT_BOUND says this shell does not send {kind:?}, but Commands.cs does — delete the              entry, it is now a note that lies"
        );
        assert!(
            reason.len() >= 40 && !reason.to_lowercase().contains("not needed"),
            "{kind:?} needs a reason saying what this shell does instead, or the task that will              bind it: {reason:?}"
        );
    }
}

/// The enforcement itself, on lists of our own — so this file proves it fails when it should
/// rather than only that today's core passes.
#[test]
fn an_unregistered_command_is_caught() {
    let core: BTreeSet<String> = ["bound", "registered", "neither"]
        .iter()
        .map(|kind| kind.to_string())
        .collect();
    let csharp: BTreeSet<String> = std::iter::once("bound".to_string()).collect();
    let register = &[(
        "registered",
        "this shell does something else instead, at length",
    )][..];

    assert_eq!(
        unregistered(&core, &csharp, register),
        vec!["neither".to_string()],
        "a command that is neither bound nor registered is the whole point"
    );
    assert!(
        unregistered(&core, &csharp, &[("registered", "…"), ("neither", "…")]).is_empty(),
        "and registering it is what makes the gate pass again"
    );
}

// ── The fields (task 9ef7edb4) ──────────────────────────────────────────────────────────────────

/// Every property name the core's wire schema knows, anywhere in it.
///
/// The request vocabulary and the response shapes together, because a shell's names come from
/// both and the question asked of each is the same: is this a name this core knows at all?
///
/// Deliberately NOT "is this field legal on command X". That needs every C# call site mapped to a
/// `Command` variant, which is a C# parser nobody should write — and the flat question already
/// catches the whole class of bug this exists for.
fn schema_names() -> BTreeSet<String> {
    use astrid_core::app::schema;
    let mut names = schema::property_names(&schema::command());
    names.extend(schema::property_names(&schema::wire()));
    assert!(
        names.len() >= 200,
        "the core's schema names only {} properties; the derive has stopped seeing fields",
        names.len()
    );
    names
}

/// Every field name this shell SENDS: the `[property: JsonPropertyName("…")]` on the request
/// records at the foot of `Commands.cs`.
///
/// That attribute form appears nowhere else in the file — `Models.cs` writes the plain
/// `[JsonPropertyName]` on properties — so the scan needs no notion of where the records begin.
fn csharp_request_fields() -> BTreeSet<String> {
    let fields = attribute_values(COMMANDS_CS, "[property: JsonPropertyName(\"");
    assert!(
        fields.len() >= 50,
        "found only {} request fields in Commands.cs; the scan has stopped working",
        fields.len()
    );
    fields.into_iter().collect()
}

/// Each `public sealed record` in `Models.cs` and the wire names it decodes, in file order.
fn csharp_model_records() -> BTreeMap<String, Vec<String>> {
    let mut records: BTreeMap<String, Vec<String>> = BTreeMap::new();
    let mut current: Option<String> = None;
    for line in MODELS_CS.lines() {
        if let Some(rest) = line.strip_prefix("public sealed record ") {
            let name: String = rest
                .chars()
                .take_while(|c| c.is_ascii_alphanumeric() || *c == '_')
                .collect();
            if !name.is_empty() {
                records.entry(name.clone()).or_default();
                current = Some(name);
            }
            continue;
        }
        let Some(record) = current.as_ref() else {
            continue;
        };
        for field in attribute_values(line, "[JsonPropertyName(\"") {
            records.entry(record.clone()).or_default().push(field);
        }
    }
    assert!(
        records.len() >= 80,
        "found only {} records in Models.cs; the scan has stopped working",
        records.len()
    );
    records
}

/// Every string literal that follows `opener` in `source`.
fn attribute_values(source: &str, opener: &str) -> Vec<String> {
    let mut values = Vec::new();
    let mut rest = source;
    while let Some(at) = rest.find(opener) {
        let literal = &rest[at + opener.len()..];
        let end = literal.find('"').expect("a closed string literal");
        values.push(literal[..end].to_string());
        rest = &literal[end..];
    }
    values
}

/// **Every field the shell sends must be a name the core knows.**
///
/// This is the acceptance criterion of task 9ef7edb4 on the request side, and the half that
/// catches the silent regression: `serde` ignores an unknown request field, so until this existed
/// a rename in the core made this shell's write do nothing, quietly, with no way to tell from the
/// outside but noticing the value had not changed.
///
/// It passes clean today — all 97 fields `Commands.cs` sends are in the schema — which is the
/// point of adding it now rather than after the first rename.
#[test]
fn every_field_the_shell_sends_is_one_the_core_knows_9ef7edb4() {
    let known = schema_names();
    let unknown: Vec<String> = csharp_request_fields()
        .into_iter()
        .filter(|field| !known.contains(field))
        .collect();
    assert!(
        unknown.is_empty(),
        "Commands.cs sends field names the core's schema has no property for. A command does NOT \
         fail on one — serde ignores it — so each of these is a write that silently does nothing: \
         {unknown:?}"
    );
}

/// The records in `Models.cs` that mirror ONE named type in the core, and which.
///
/// The pair is the whole point, and the flat `schema_names()` set is not enough for this half:
/// `authorName` exists on `CommentRow` as well as `MessageRow`, so renaming it on one of them
/// leaves the name in the schema and a union check sees nothing. Holding each record against **its
/// own type's** properties is what makes the rename fail — the acceptance criterion of task
/// 9ef7edb4 over the response side.
///
/// **A ratchet, not a register of exceptions.** The other ~85 records are not listed, and their
/// reason is one fact rather than eighty-five: either they mirror a shape the core builds by hand
/// with `serde_json::json!` in `app/dispatch/` (`row_json.rs` alone is 663 lines of it), so there
/// is no Rust type to name here; or they are a composite a dispatch assembles from several. Giving
/// those projections types is astrid-core's own task — "Move list-row assembly into astrid-rules;
/// return TaskRow projections from both doors" — and each one that lands can be added here as a
/// pair.
///
/// A record is listed only when the pairing is TRUE. Several unlisted ones would pass a mechanical
/// check today by accident — a two-field record of `id` and `name` resolves against half the
/// schema — and listing those would claim a guarantee this is not giving.
const SCHEMA_BACKED_MODELS: &[(&str, &str)] = &[
    ("AgentCredential", "CredentialRow"),
    ("AssigneeOption", "AssigneeOption"),
    ("DefaultPick", "DefaultPick"),
    ("FilterGroup", "FilterGroup"),
    ("FilterPick", "FilterPick"),
    ("ListDefaultPicks", "ListDefaultPicks"),
    ("ListMember", "ListMember"),
    ("ListPick", "ListPick"),
    ("ListPicks", "ListPicks"),
    ("ListSummary", "TaskList"),
    ("ListSupports", "ListSupports"),
    ("MarkdownBlock", "Block"),
    ("MarkdownCell", "GridCell"),
    ("MarkdownInline", "Inline"),
    ("MarkdownListItem", "ListItem"),
    ("MessageRow", "MessageRow"),
    ("PublicListOwner", "User"),
    ("ReminderPick", "ReminderPick"),
    ("RepeatPreset", "RepeatPreset"),
    ("SidebarSections", "Sections"),
    ("SummaryPart", "SummaryPart"),
    ("UserSummary", "User"),
];

/// The property names one named type in the core's schema has.
///
/// `None` when the schema has no such type — which is itself a failure worth reporting by name,
/// because a type renamed or deleted in the core leaves a pairing here pointing at nothing.
///
/// The union over a tagged enum's variants, because that is one C# record on this side:
/// `MarkdownBlock` decodes every `Block` variant through one shape with the unused fields left
/// null, which is how `System.Text.Json` reads a discriminated union without a converter per case.
fn schema_type_properties(type_name: &str) -> Option<BTreeSet<String>> {
    use astrid_core::app::schema;
    for document in [schema::command(), schema::wire()] {
        let Some(definition) = document.get("$defs").and_then(|defs| defs.get(type_name)) else {
            continue;
        };
        let mut names = BTreeSet::new();
        collect_properties(definition, &mut names);
        return Some(names);
    }
    None
}

/// Every `properties` key anywhere under one schema node, variants included.
fn collect_properties(node: &serde_json::Value, names: &mut BTreeSet<String>) {
    match node {
        serde_json::Value::Object(fields) => {
            for (key, value) in fields {
                if key == "properties" {
                    if let serde_json::Value::Object(properties) = value {
                        names.extend(properties.keys().cloned());
                    }
                }
                collect_properties(value, names);
            }
        }
        serde_json::Value::Array(items) => items
            .iter()
            .for_each(|item| collect_properties(item, names)),
        _ => {}
    }
}

/// **Every paired record still resolves against its own type.**
///
/// The response half of task 9ef7edb4's acceptance: rename a field on `User`, `TaskList`,
/// `MessageRow` or any other paired type in the core, and this goes red naming the record that
/// decodes it.
#[test]
fn every_schema_backed_model_still_resolves_9ef7edb4() {
    let records = csharp_model_records();
    let broken = unresolved(SCHEMA_BACKED_MODELS, &records, &schema_type_properties);
    assert!(
        broken.is_empty(),
        "a shape this shell decodes has moved in the core. Follow the rename here — or, if the \
         projection stopped having a type, take the pair off SCHEMA_BACKED_MODELS and say \
         why:\n  {}",
        broken.join("\n  ")
    );
}

/// Which pairs no longer hold, and how each fails.
///
/// A function over its inputs, so the enforcement is tested on lists of our own as well as on
/// today's two — see [`a_renamed_field_is_caught_9ef7edb4`]. A gate whose failing case is never
/// exercised is a gate nobody knows the shape of.
fn unresolved(
    pairs: &[(&str, &str)],
    records: &BTreeMap<String, Vec<String>>,
    properties: &dyn Fn(&str) -> Option<BTreeSet<String>>,
) -> Vec<String> {
    let mut broken = Vec::new();
    for (record, core_type) in pairs {
        let Some(fields) = records.get(*record) else {
            broken.push(format!(
                "{record} is paired with {core_type} but is no longer a record in Models.cs — \
                 delete the pair or correct the spelling"
            ));
            continue;
        };
        assert!(
            !fields.is_empty(),
            "{record} decodes no fields at all; the scan has stopped working"
        );
        let Some(known) = properties(core_type) else {
            broken.push(format!(
                "{record} is paired with {core_type}, which the core's schema has no type for — \
                 it was renamed or removed"
            ));
            continue;
        };
        let unknown: Vec<&String> = fields.iter().filter(|f| !known.contains(*f)).collect();
        if !unknown.is_empty() {
            broken.push(format!(
                "{record} decodes {unknown:?}, which {core_type} no longer has"
            ));
        }
    }
    broken
}

/// The enforcement itself, on inputs of our own — so this file proves it FAILS when it should
/// rather than only that today's core passes.
///
/// The middle case is the one a flat union of every property name in the schema cannot catch, and
/// the reason this check is per-type: `authorName` moving off `MessageRow` leaves the name in the
/// schema, on `CommentRow`.
#[test]
fn a_renamed_field_is_caught_9ef7edb4() {
    let records = BTreeMap::from([(
        "MessageRow".to_string(),
        vec!["id".to_string(), "authorName".to_string()],
    )]);
    let pairs = &[("MessageRow", "MessageRow")][..];

    let intact = |_: &str| Some(BTreeSet::from(["id".to_string(), "authorName".to_string()]));
    assert!(
        unresolved(pairs, &records, &intact).is_empty(),
        "a pair that still holds must not be reported"
    );

    // The field was renamed on ITS type, and the old name lives on elsewhere in the schema.
    let renamed = |_: &str| Some(BTreeSet::from(["id".to_string(), "writtenBy".to_string()]));
    let broken = unresolved(pairs, &records, &renamed);
    assert_eq!(broken.len(), 1, "the rename is the whole point: {broken:?}");
    assert!(
        broken[0].contains("authorName") && broken[0].contains("MessageRow"),
        "it should name the field and the type: {}",
        broken[0]
    );

    // And a type that left the core entirely says so, rather than passing for want of properties.
    let gone = |_: &str| None;
    assert!(
        unresolved(pairs, &records, &gone)[0].contains("no type for"),
        "a missing type is its own failure"
    );
}

/// The pairs do not rot: every record is in `Models.cs`, every type is in the schema, and no
/// record is paired twice.
#[test]
fn the_pairs_name_things_that_exist_9ef7edb4() {
    let records = csharp_model_records();
    let listed: BTreeSet<&str> = SCHEMA_BACKED_MODELS.iter().map(|(name, _)| *name).collect();
    assert_eq!(
        listed.len(),
        SCHEMA_BACKED_MODELS.len(),
        "SCHEMA_BACKED_MODELS pairs a record twice"
    );
    for (record, core_type) in SCHEMA_BACKED_MODELS {
        assert!(
            records.contains_key(*record),
            "{record} is not a record in Models.cs"
        );
        assert!(
            schema_type_properties(core_type).is_some(),
            "{core_type} is not a type in the core's schema"
        );
    }
}

/// The pin is one revision, written twice.
///
/// `bindings_contract.rs` needs the core's `schema` feature, which is a DEV dependency so that
/// `schemars` stays out of the shipped `astrid_ffi.dll` — and a dev dependency has to name the
/// source again, which means naming the `rev` again. Two revisions that can drift is exactly the
/// trap `CONSUMERS.md` exists to close, so the two are held together here: a pin bump that touched
/// one and not the other would otherwise run this whole gate against the OLD core while the shell
/// built against the new one, and report that everything agreed.
///
/// Read as "every forty-character sha in the manifest", deliberately, rather than by TOML shape.
/// The two are written differently on purpose — the normal one inline, because the release
/// workflow's `sed` extracts it from a line starting `astrid-core = `; the dev one sectioned,
/// because a SECOND such line would make that `sed` print two shas
/// (`release_pointers_contract.rs`). A check that knew only one spelling would stop seeing the
/// other the moment somebody reformatted it, and say nothing.
#[test]
fn the_dev_and_normal_core_pins_are_the_same_revision_9ef7edb4() {
    const MANIFEST: &str = include_str!("../Cargo.toml");
    let revisions: Vec<&str> = MANIFEST
        .lines()
        .map(|line| line.split('#').next().unwrap_or_default())
        .filter_map(|line| line.rsplit_once("rev = \""))
        .filter_map(|(_, rest)| rest.get(..40))
        .filter(|sha| {
            sha.chars()
                .all(|c| c.is_ascii_hexdigit() && !c.is_uppercase())
        })
        .collect();
    assert_eq!(
        revisions.len(),
        2,
        "Cargo.toml should pin astrid-core twice — once for the shell, once for the tests' \
         `schema` feature — and found {revisions:?}"
    );
    assert_eq!(
        revisions.iter().collect::<BTreeSet<_>>().len(),
        1,
        "the two astrid-core pins have drifted apart: {revisions:?}"
    );
}
