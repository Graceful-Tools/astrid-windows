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
//! scans its own `pub enum Command` and lowercases the first letter, so a wire spelling such as
//! `reconnectStream` appears nowhere in the core's source; and the C# kinds are string literals
//! inside factory bodies. The two sets, compared here, are the only reliable comparison — a grep
//! for one of these names will confidently tell you it does not exist.
//!
//! The core's half is `astrid_core::app::command_kinds()`, which every shell reads its own bindings
//! against; this file is the Windows shell's half. It lives with the FFI crate because that is the
//! Rust that stays beside the C#: the core is its own repository and knows nothing of `Commands.cs`.

use std::collections::BTreeSet;

const COMMANDS_CS: &str = include_str!("../../../app/Astrid.Core.Bindings/Commands.cs");

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
        "resolveIds",
        "Which real ids temporary ones became, for a shell holding its own copies of them. This          one redraws from the core after a create, so the temporary id leaves with the row.",
    ),
    // ── Surfaces this shell does not have yet ─────────────────────────────────────────────────
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
