//! The C# bindings speak only commands the core understands.
//!
//! `app/Astrid.Core.Bindings/Commands.cs` is hand-written — deliberately, and deliberately
//! partial: a command with no caller yet has no record. What that leaves unguarded is the other
//! direction. A variant renamed in the core, or a `kind` mistyped here, is a command the core
//! answers with "bad request" — and the shell shows that as a button that does nothing, which is
//! the hardest kind of bug to report. So the two lists are read together, here, in the gate that
//! already runs on every push.
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

/// Not a failure: the bindings are a list of what the shell uses, not of what exists. Printed so
/// the gap is visible in a test log rather than only discoverable by diffing two files.
#[test]
fn the_commands_the_shell_does_not_send_yet_are_listed() {
    let rust = astrid_core::app::command_kinds();
    let csharp = csharp_kinds();
    let unused: Vec<&String> = rust.difference(&csharp).collect();
    eprintln!("commands with no C# caller yet: {unused:?}");
}
