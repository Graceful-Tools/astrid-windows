using System.Collections.Generic;

namespace Astrid.Core.Bindings;

/// <summary>
/// The catalog keys whose word is chosen by CLDR category, mirroring
/// <c>astrid_rules::plurals::PLURAL_KEYS</c> (task b656efce).
/// </summary>
/// <remarks>
/// <para>
/// Named here rather than typed at the call sites because the core can rename a key, and a renamed
/// key fails nothing that compiles — the lookup simply misses and the reader sees the raw key.
/// <c>crates/astrid-ffi/tests/copy_catalog_contract.rs</c> asserts this table against the pinned
/// <c>astrid_rules::plurals::PLURAL_KEYS</c>, in Rust, where the crate is actually linked.
/// </para>
/// <para>
/// The core publishes the list for a reason a client feels immediately: <c>copy_keys()</c> answers
/// which keys exist, and for a plural key that is the BASE name — so a shell checking its own
/// coverage would look for a <c>format.ordinal</c> that is not supposed to exist and conclude it
/// was missing a string it had already translated four times.
/// </para>
/// </remarks>
public static class Plurals
{
    /// <summary>How a number is spelled as a position — "3rd", "3.", "3".</summary>
    public const string OrdinalKey = "format.ordinal";

    /// <summary>Every plural key, and which set of CLDR rules its categories come from.</summary>
    public static readonly IReadOnlyList<(string Key, CountForm Form)> Keys =
    [
        (OrdinalKey, CountForm.Ordinal),
        ("repeat.every_n_days", CountForm.Cardinal),
        ("repeat.every_n_months", CountForm.Cardinal),
        ("repeat.every_n_weeks", CountForm.Cardinal),
        ("repeat.every_n_years", CountForm.Cardinal),
    ];
}
