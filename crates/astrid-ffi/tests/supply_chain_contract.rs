//! `deny.toml`'s repo-local half, asserted where it can actually run — task `e750d9c1`.
//!
//! `cargo deny check` is CI's gate, not `predeploy`'s, and `deny.toml` says why: the advisories
//! half needs the RustSec database, which means the network, and cargo-deny is not installed on
//! the dev machines. `cargo install cargo-deny --locked` does not fix that here — it dies in
//! `ring`'s build script with `ToolNotFound: failed to find tool "clang"`.
//!
//! But `.github/workflows/ci.yml` runs cargo-deny as TWO jobs precisely because the two halves
//! fail for unrelated reasons: `advisories` goes red when the WORLD changed, `licenses bans
//! sources` when THIS REPO changed. Only the first half needs the network. The second is a
//! question about a lockfile and a config file that are both sitting right here, and until this
//! file existed the only way to ask it was to push and wait — which is how the wildcard
//! requirement on the `astrid-core` git pin went red in CI with nothing local to say so.
//!
//! So this is not a second copy of cargo-deny. It is the subset of `deny.toml` that is decidable
//! offline, read out of `deny.toml` itself rather than restated, so that editing the config moves
//! the test with it and the two cannot drift. `advisories` stays CI's alone.
//!
//! The facts come from `cargo metadata --format-version 1 --all-features`, the same command
//! `deny.toml`'s own header cites for how its allow list was computed, and `all-features` for the
//! reason given there: a feature nobody enables today is enabled by the next caller.
//!
//! Where this deliberately stops short of cargo-deny: it evaluates an SPDX expression against the
//! allow list and nothing else. It does not implement `exceptions`, `clarify`, `private`, or the
//! OSI/FSF classifications, because `deny.toml` uses none of them — every one of those would be a
//! new decision in the config, and the config is the thing being read. An expression this
//! evaluator cannot parse is a failure asking for a person, never a quiet pass.

use std::collections::BTreeSet;
use std::path::PathBuf;
use std::process::Command;
use std::sync::OnceLock;

use serde_json::Value;

/// The workspace root: this crate sits at `crates/astrid-ffi`.
fn repo_root() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("..")
        .join("..")
}

fn deny_toml() -> &'static str {
    static DENY: OnceLock<String> = OnceLock::new();
    DENY.get_or_init(|| {
        let path = repo_root().join("deny.toml");
        std::fs::read_to_string(&path)
            .unwrap_or_else(|e| panic!("deny.toml is the gate being asserted; {path:?}: {e}"))
    })
}

/// The resolved graph, asked for once however many tests want it.
fn metadata() -> &'static Value {
    static META: OnceLock<Value> = OnceLock::new();
    META.get_or_init(|| {
        let cargo = std::env::var("CARGO").unwrap_or_else(|_| "cargo".into());
        let out = Command::new(&cargo)
            .args(["metadata", "--format-version", "1", "--all-features"])
            .arg("--manifest-path")
            .arg(repo_root().join("Cargo.toml"))
            .output()
            .unwrap_or_else(|e| panic!("`{cargo} metadata` must run: {e}"));
        assert!(
            out.status.success(),
            "`cargo metadata` failed:\n{}",
            String::from_utf8_lossy(&out.stderr)
        );
        serde_json::from_slice(&out.stdout).expect("cargo metadata emits JSON")
    })
}

/// A `key = ["a", "b"]` list out of a named table in `deny.toml`.
///
/// Hand-rolled rather than parsed with the `toml` crate on purpose: this file guards the
/// dependency graph, and adding a dependency to it would be the one change most likely to need
/// guarding. The shape it reads is the shape `deny.toml` is written in — a `[table]` header, then
/// `key = [` and quoted entries until `]` — and a list it cannot find is a panic, never an empty
/// allow list that would make every assertion below vacuous.
fn string_list(table: &str, key: &str) -> BTreeSet<String> {
    let src = deny_toml();
    let table_at = src
        .find(&format!("[{table}]"))
        .unwrap_or_else(|| panic!("deny.toml has no [{table}] table"));
    let rest = &src[table_at..];
    // Stop at the next table header so a key is never read out of the wrong one.
    let rest = match rest[1..].find("\n[") {
        Some(end) => &rest[..end + 1],
        None => rest,
    };
    let key_at = rest
        .find(&format!("{key} = ["))
        .unwrap_or_else(|| panic!("deny.toml's [{table}] has no `{key}` list"));
    let open = rest[key_at..].find('[').expect("just matched");
    let close = rest[key_at + open..]
        .find(']')
        .unwrap_or_else(|| panic!("deny.toml's [{table}].{key} list is unterminated"));
    let body = &rest[key_at + open + 1..key_at + open + close];
    body.split(',')
        .filter_map(|entry| {
            let entry = entry.split('#').next().unwrap_or("").trim();
            let entry = entry.trim_matches('"');
            (!entry.is_empty()).then(|| entry.to_string())
        })
        .collect()
}

/// A `key = "value"` string out of a named table, for the lint levels.
fn lint_level(table: &str, key: &str) -> String {
    let src = deny_toml();
    let table_at = src
        .find(&format!("[{table}]"))
        .unwrap_or_else(|| panic!("deny.toml has no [{table}] table"));
    let rest = &src[table_at..];
    let line = rest
        .lines()
        .find(|l| l.trim_start().starts_with(&format!("{key} = ")))
        .unwrap_or_else(|| panic!("deny.toml's [{table}] has no `{key}`"));
    line.split('=')
        .nth(1)
        .expect("just matched")
        .split('#')
        .next()
        .unwrap_or("")
        .trim()
        .trim_matches('"')
        .to_string()
}

fn packages() -> impl Iterator<Item = &'static Value> {
    metadata()["packages"]
        .as_array()
        .expect("cargo metadata lists packages")
        .iter()
}

fn workspace_member_ids() -> BTreeSet<&'static str> {
    metadata()["workspace_members"]
        .as_array()
        .expect("cargo metadata lists workspace members")
        .iter()
        .map(|id| id.as_str().expect("an id is a string"))
        .collect()
}

fn name_and_version(pkg: &Value) -> String {
    format!(
        "{} {}",
        pkg["name"].as_str().unwrap_or("?"),
        pkg["version"].as_str().unwrap_or("?")
    )
}

/// cargo-deny's `[bans] wildcards`, over the crates this repository actually writes.
///
/// A wildcard requirement is a dependency whose version nothing records, so the lockfile is the
/// only thing that knows what shipped — `deny.toml`'s own words. A `{ git, rev }` dependency is
/// one: cargo reports its requirement as `*`, and `astrid-core` reached this workspace that way.
/// Naming a `version` alongside the `rev` costs nothing, is checked against the pinned checkout at
/// build time, and means the requirement is written down in the manifest where a person reads it.
#[test]
fn no_workspace_crate_takes_a_wildcard_dependency_task_e750d9c1() {
    assert_eq!(
        lint_level("bans", "wildcards"),
        "deny",
        "this test exists to enforce `[bans] wildcards`; if deny.toml has stopped denying them, \
         delete the test rather than leaving it asserting something nobody asked for"
    );

    let members = workspace_member_ids();
    let mut wildcards = Vec::new();
    for pkg in packages() {
        let id = pkg["id"].as_str().unwrap_or_default();
        if !members.contains(id) {
            continue;
        }
        for dep in pkg["dependencies"].as_array().into_iter().flatten() {
            if dep["req"].as_str() == Some("*") {
                wildcards.push(format!(
                    "{} -> {} (kind {})",
                    pkg["name"].as_str().unwrap_or("?"),
                    dep["name"].as_str().unwrap_or("?"),
                    dep["kind"].as_str().unwrap_or("normal"),
                ));
            }
        }
    }

    assert!(
        wildcards.is_empty(),
        "cargo-deny's `licenses bans sources` job fails on a wildcard requirement. Give each of \
         these an explicit `version`:\n  {}",
        wildcards.join("\n  ")
    );
}

/// `(`, `)` and `/` are tokens in their own right; everything else splits on whitespace.
fn tokenize(expr: &str) -> Vec<String> {
    let mut tokens = Vec::new();
    let mut current = String::new();
    let flush = |current: &mut String, tokens: &mut Vec<String>| {
        if !current.is_empty() {
            tokens.push(std::mem::take(current));
        }
    };
    for ch in expr.chars() {
        match ch {
            '(' | ')' | '/' => {
                flush(&mut current, &mut tokens);
                tokens.push(ch.to_string());
            }
            c if c.is_whitespace() => flush(&mut current, &mut tokens),
            c => current.push(c),
        }
    }
    flush(&mut current, &mut tokens);
    tokens
}

/// An SPDX expression evaluated straight to "does the allow list satisfy it".
///
/// `AND` binds tighter than `OR`, parentheses override that, and `/` is `OR` — the last because
/// cargo-deny parses in the `spdx` crate's lax mode, and it has to: `MIT/Apache-2.0` is what
/// `fnv`, `version_check` and a dozen other crates every Rust graph contains actually declare.
///
/// `A WITH B` stays ONE term that must appear in the allow list verbatim, so allowing `Apache-2.0`
/// does not also allow `Apache-2.0 WITH LLVM-exception`. That is `[licenses] version = 2`'s
/// behaviour, and the right one: an exception modifies the licence and is a separate decision.
/// Every such package in this graph is satisfied through a plain `MIT` or `Apache-2.0` branch
/// instead.
struct Spdx<'a> {
    tokens: Vec<String>,
    at: usize,
    allow: &'a BTreeSet<String>,
}

impl<'a> Spdx<'a> {
    fn peek(&self) -> Option<&str> {
        self.tokens.get(self.at).map(String::as_str)
    }

    /// `and ( ('OR' | '/') and )*`
    fn or_expr(&mut self) -> Result<bool, String> {
        let mut satisfied = self.and_expr()?;
        while matches!(self.peek(), Some("OR" | "/")) {
            self.at += 1;
            // Parsed before combining: a short circuit would leave the rest untokenised and a
            // malformed tail would pass unnoticed.
            let branch = self.and_expr()?;
            satisfied = satisfied || branch;
        }
        Ok(satisfied)
    }

    /// `term ( 'AND' term )*`
    fn and_expr(&mut self) -> Result<bool, String> {
        let mut satisfied = self.term()?;
        while self.peek() == Some("AND") {
            self.at += 1;
            let term = self.term()?;
            satisfied = satisfied && term;
        }
        Ok(satisfied)
    }

    /// `'(' or_expr ')' | licence ( 'WITH' exception )?`
    fn term(&mut self) -> Result<bool, String> {
        match self.peek() {
            Some("(") => {
                self.at += 1;
                let inner = self.or_expr()?;
                if self.peek() != Some(")") {
                    return Err("unbalanced `(`".into());
                }
                self.at += 1;
                Ok(inner)
            }
            Some("OR" | "AND" | "WITH" | ")" | "/") => Err(format!(
                "expected a licence, found `{}`",
                self.peek().unwrap()
            )),
            Some(name) => {
                let mut term = name.to_string();
                self.at += 1;
                if self.peek() == Some("WITH") {
                    self.at += 1;
                    let exception = self.peek().ok_or("`WITH` with no exception after it")?;
                    term = format!("{term} WITH {exception}");
                    self.at += 1;
                }
                Ok(self.allow.contains(&term))
            }
            None => Err("expression ended early".into()),
        }
    }
}

fn satisfied_by(expr: &str, allow: &BTreeSet<String>) -> Result<bool, String> {
    let mut parser = Spdx {
        tokens: tokenize(expr),
        at: 0,
        allow,
    };
    if parser.tokens.is_empty() {
        return Err("empty licence expression".into());
    }
    let satisfied = parser
        .or_expr()
        .map_err(|why| format!("`{expr}` could not be read: {why}"))?;
    if parser.at != parser.tokens.len() {
        return Err(format!(
            "`{expr}` has trailing tokens from `{}`",
            parser.tokens[parser.at]
        ));
    }
    Ok(satisfied)
}

#[test]
fn the_spdx_evaluator_handles_the_shapes_the_graph_contains_task_e750d9c1() {
    let allow: BTreeSet<String> = ["Apache-2.0", "MIT", "BSD-3-Clause", "Unicode-3.0"]
        .iter()
        .map(|s| s.to_string())
        .collect();
    let yes = |e: &str| satisfied_by(e, &allow).unwrap_or_else(|w| panic!("{e}: {w}"));

    assert!(yes("MIT"));
    assert!(!yes("GPL-3.0"));
    // `/` is OR, which is the whole reason lax mode exists.
    assert!(yes("MIT/Apache-2.0"));
    assert!(yes("Apache-2.0 / MIT"));
    // AND binds tighter than OR, and parentheses override it.
    assert!(yes("(Apache-2.0 OR MIT) AND BSD-3-Clause"));
    assert!(yes("(MIT OR Apache-2.0) AND Unicode-3.0"));
    assert!(!yes("(MIT OR Apache-2.0) AND GPL-3.0"));
    assert!(yes("GPL-3.0 AND Nope OR MIT"), "AND binds tighter than OR");
    // An exception is a term of its own, satisfied here only by the MIT branch beside it.
    assert!(!yes("Apache-2.0 WITH LLVM-exception"));
    assert!(yes("Apache-2.0 WITH LLVM-exception OR Apache-2.0 OR MIT"));
    // Malformed expressions are errors, never a quiet `false`.
    assert!(satisfied_by("MIT AND", &allow).is_err());
    assert!(satisfied_by("(MIT OR Apache-2.0", &allow).is_err());
    assert!(satisfied_by("MIT Apache-2.0", &allow).is_err());
}

/// cargo-deny's `[licenses]`, over every package in the graph.
#[test]
fn every_licence_in_the_graph_is_allowed_task_e750d9c1() {
    let allow = string_list("licenses", "allow");
    assert!(
        !allow.is_empty(),
        "deny.toml's licence allow list parsed as empty, which would make this test vacuous"
    );

    let mut problems = Vec::new();
    for pkg in packages() {
        let who = name_and_version(pkg);
        match pkg["license"].as_str() {
            Some(expr) => match satisfied_by(expr, &allow) {
                Ok(true) => {}
                Ok(false) => problems.push(format!("{who}: [{expr}] is not in the allow list")),
                Err(why) => problems.push(format!("{who}: {why}")),
            },
            // A package with no `license` needs a cargo-deny clarification, which is a decision a
            // person makes once and records. Failing here is how it gets asked for.
            None => problems.push(format!(
                "{who}: no `license` field (license_file = {})",
                pkg["license_file"].as_str().unwrap_or("none")
            )),
        }
    }

    assert!(
        problems.is_empty(),
        "deny.toml's allow list does not cover the graph. Every entry there is REQUIRED by \
         something in the tree and says why, so read each of these before adding one:\n  {}",
        problems.join("\n  ")
    );
}

/// cargo-deny's `[sources]`, over every package that came from somewhere.
///
/// `unknown-registry` and `unknown-git` are both `deny`, and `allow-git` has exactly one entry:
/// the core's URL, matched by URL rather than revision so that bumping the pin — which is how
/// every shared-behaviour change reaches this app — does not need that list touched. This test
/// matches it the same way, or a pin bump would go red here instead of in CI.
#[test]
fn every_crate_came_from_an_accepted_source_task_e750d9c1() {
    const CRATES_IO: &str = "registry+https://github.com/rust-lang/crates.io-index";
    let allow_git = string_list("sources", "allow-git");

    let mut problems = Vec::new();
    for pkg in packages() {
        // Workspace members have no source: they are this repository.
        let Some(source) = pkg["source"].as_str() else {
            continue;
        };
        if source == CRATES_IO {
            continue;
        }
        if let Some(url) = source.strip_prefix("git+") {
            let url = url.split(['?', '#']).next().unwrap_or(url);
            if allow_git.contains(url) {
                continue;
            }
            problems.push(format!(
                "{}: git source {url} is not in [sources] allow-git",
                name_and_version(pkg)
            ));
            continue;
        }
        problems.push(format!(
            "{}: unknown registry {source}",
            name_and_version(pkg)
        ));
    }

    assert!(
        problems.is_empty(),
        "a crate arrived from somewhere nobody has accepted:\n  {}",
        problems.join("\n  ")
    );
}
