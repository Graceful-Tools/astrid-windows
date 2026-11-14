//! Every key the pinned core can hand this shell has a word here, or a written reason (task b63d8679).
//!
//! Rule 10 is that the core returns keys and never words: a due pick comes back as `picker.tomorrow`
//! and `Strings.Get` says the word. The hole in that rule was that **nothing said what the whole set
//! of keys was**. A key the core started emitting reached the window as the key itself —
//! `repeat.daily` drawn in the filter sheet where "Daily" belongs — which compiles, passes every
//! test, and looks to a reporter like a rendering bug.
//!
//! astrid-core closed its half in task `1bdf84d7`: [`astrid_core::copy_keys`] is **computed** from
//! the same const tables and picker functions that emit the keys, so it cannot fall behind them.
//! This file is the Windows shell's half, and it retires the hand-copied array of fourteen keys in
//! `CorePinFollowsIosTests.cs` that astrid-core's `copy.rs` names by file as the thing the catalog
//! replaces.
//!
//! ## Both places, because they fail differently
//!
//! A word lives twice here. `Strings/en-US/Resources.resw` is what a translator adds a language
//! beside, and the `Fallback` table in `Strings.cs` is what an **unpackaged** build falls back on
//! when its `.pri` did not land next to the executable. A key missing from the first is the key on
//! screen in a packaged build; missing from the second, the key on screen in an unpackaged one.
//! `Strings.Get` ends in `Fallback.TryGetValue(key, out var text) ? text : key`, so a key missing
//! from both is drawn as itself.
//!
//! ## The spelling is the flatten, and that is the point
//!
//! The core's keys cannot be nested — `filter.due` is a filter's own title **and** the parent of
//! `filter.due.today` — and the PRI compiler refuses a flat resource whose name is also a scope. So
//! `Strings.Get` stores them with the dots turned into underscores, and this gate asks for exactly
//! that name. **A Windows-invented name does not satisfy it**: `tradeoff_very_good` held the right
//! word under a spelling no other client could have guessed, and the point of a shared registry is
//! that the four clients agree on what a key is called.
//!
//! ## Partial is fine; accidentally partial is not
//!
//! [`NOT_SAID`] is the same trade `NOT_BOUND` makes next door in `bindings_contract.rs`. This shell
//! does not draw every surface the core can describe, so a key with no word is allowed — but it has
//! to be *said out loud*, with a reason, and a key the core adds tomorrow fails this gate until
//! somebody writes which it is. The second test keeps the register from going stale in either
//! direction.

use std::collections::BTreeSet;

const RESW_EN: &str = include_str!("../../../app/Astrid.App/Strings/en-US/Resources.resw");
const STRINGS_CS: &str = include_str!("../../../app/Astrid.App/Strings.cs");

/// The core's spelling turned into a resource name, the swap `Strings.Get` makes on every lookup.
fn flatten(key: &str) -> String {
    key.replace('.', "_")
}

/// Every `<data name="…">` in the English resources.
fn resource_names() -> BTreeSet<String> {
    let names: BTreeSet<String> = RESW_EN
        .lines()
        .filter_map(|line| line.trim_start().strip_prefix("<data name=\""))
        .filter_map(|rest| rest.split('"').next())
        .map(str::to_string)
        .collect();
    assert!(
        names.len() >= 500,
        "found only {} resources in en-US/Resources.resw; the scan has stopped working",
        names.len()
    );
    names
}

/// Every key in `Strings.cs`'s `Fallback` table, read from its source the way the C# tests do.
///
/// The table is keyed by the core's **dotted** spelling — the flatten happens inside `Strings.Get`,
/// against the resource map only — so these are compared to `copy_keys()` as they come.
fn fallback_keys() -> BTreeSet<String> {
    let keys: BTreeSet<String> = STRINGS_CS
        .lines()
        .filter_map(|line| line.trim_start().strip_prefix("[\""))
        .filter_map(|rest| rest.split_once("\"]").map(|(key, _)| key.to_string()))
        .collect();
    assert!(
        keys.len() >= 100,
        "found only {} fallback keys in Strings.cs; the scan has stopped working",
        keys.len()
    );
    keys
}

/// Keys the pinned core emits that this shell has no word for, and why.
///
/// THE REASONS ARE THE DOCUMENTATION. This register is the only written record of which of the
/// core's words Windows deliberately leaves unsaid, so "not used" is not a reason and will not
/// pass: say what this shell draws instead, or name the task that will say it.
const NOT_SAID: &[(&str, &str)] = &[
    (
        "tasks.addNewTask",
        "The core's quick-add prompts. `quick_add::PLACEHOLDER_KEYS` is reached through the \
         quick-add parse, which this shell does not bind — `Commands.cs` has no record for it, and \
         the add-task box carries its own `x:Uid` literal that a translator reaches through the \
         resw like any other piece of shell chrome. Binding the parse is what would make these \
         four needed, and that is not this task.",
    ),
    (
        "tasks.addTaskPlaceholder",
        "As `tasks.addNewTask`: a quick-add prompt this shell never asks the core for.",
    ),
    (
        "tasks.addTaskShort",
        "As `tasks.addNewTask`: a quick-add prompt this shell never asks the core for.",
    ),
    (
        "tasks.addTaskToCurrentList",
        "As `tasks.addNewTask`: a quick-add prompt this shell never asks the core for. astrid-core's \
         `strings/README.md` singles this one out as having been returned by `placeholder()` for \
         months with no catalog on any platform carrying it.",
    ),
    (
        "tasks.addTaskToList",
        "As `tasks.addNewTask`, and it interpolates a list name — `Add a task to {list}` — which \
         the resw's positional `{0}` would have to be matched to at the same time.",
    ),
];

/// Every key the pinned core can hand this shell is sayable, or registered as deliberately not.
#[test]
fn every_copy_key_has_a_word_task_b63d8679() {
    let resources = resource_names();
    let fallback = fallback_keys();
    let registered: BTreeSet<&str> = NOT_SAID.iter().map(|(key, _)| *key).collect();

    let silent: Vec<String> = astrid_core::copy_keys()
        .into_iter()
        .filter(|key| !registered.contains(key.as_str()))
        .flat_map(|key| {
            [
                (!resources.contains(&flatten(&key))).then(|| format!("{key} — no resource")),
                (!fallback.contains(&key)).then(|| format!("{key} — no fallback")),
            ]
            .into_iter()
            .flatten()
        })
        .collect();

    assert!(
        silent.is_empty(),
        "the pinned core emits keys this shell draws as the key itself — give each a word under \
         the flattened name, or register it in NOT_SAID with a reason:\n  {}",
        silent.join("\n  ")
    );
}

/// And the register cannot go stale, in either direction.
///
/// A `NOT_SAID` entry for a key the core no longer emits is a reason nobody will ever re-read; an
/// entry for a key that has since been given a word is a lie about what this shell draws. Both are
/// how a register of exceptions turns into a list nobody trusts.
#[test]
fn the_not_said_register_is_current_task_b63d8679() {
    let emitted: BTreeSet<String> = astrid_core::copy_keys().into_iter().collect();
    let resources = resource_names();
    let fallback = fallback_keys();

    let stale: Vec<String> = NOT_SAID
        .iter()
        .flat_map(|(key, _)| {
            [
                (!emitted.contains(*key))
                    .then(|| format!("{key} — the pinned core no longer emits it")),
                (resources.contains(&flatten(key)) && fallback.contains(*key))
                    .then(|| format!("{key} — it has a word now, so drop it from the register")),
            ]
            .into_iter()
            .flatten()
        })
        .collect();

    assert!(
        stale.is_empty(),
        "NOT_SAID is out of date:\n  {}",
        stale.join("\n  ")
    );

    let reasonless: Vec<&str> = NOT_SAID
        .iter()
        .filter(|(_, reason)| reason.len() < 40)
        .map(|(key, _)| *key)
        .collect();
    assert!(
        reasonless.is_empty(),
        "a NOT_SAID entry is only as good as its reason: {reasonless:?}"
    );
}
