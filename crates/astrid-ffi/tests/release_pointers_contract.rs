//! A release moves the pointer on every repo it shipped from, not just this one.
//!
//! `prod` in this repo says which commit the public download was built from (task `3ef3397b`). But
//! this app is built from TWO repositories: the WinUI shell here, and `astrid-core`, which is its
//! own repository and is pinned by revision. A release that moves only this repo's `prod` records
//! half of what it shipped — and the missing half is the one nobody can reconstruct later, because
//! `main` in astrid-core runs ahead of every pin and a sha in a `Cargo.toml` is not a ref anyone
//! can `git log` against.
//!
//! So a release also moves `windows-prod` in astrid-core to the pinned sha and tags it
//! `windows-v<version>-…`, which makes both halves readable the same way:
//!
//!   git log origin/prod..origin/main                     (here)   what the next release would ship
//!   git log origin/windows-prod..origin/main             (core)   what the core has moved on to
//!
//! Spec of record: astrid-web `docs/specs/PROD_POINTERS_FOR_DEPENDENCIES.md`. Task `b34ca8ab`.
//!
//! These are rules about a workflow file, and this repo has no JS test harness to hold them, so
//! they live here — beside `bindings_contract.rs`, which already reads across the repo with
//! `include_str!` for the same reason: this is the gate that actually runs.

const RELEASE_YML: &str = include_str!("../../../.github/workflows/release.yml");
const FFI_CARGO_TOML: &str = include_str!("../Cargo.toml");
const ADVANCE_SCRIPT: &str = include_str!("../../../scripts/advance-prod-branch.sh");

/// The line in `crates/astrid-ffi/Cargo.toml` that pins astrid-core.
fn core_pin_line() -> &'static str {
    FFI_CARGO_TOML
        .lines()
        .find(|line| line.trim_start().starts_with("astrid-core"))
        .expect("crates/astrid-ffi/Cargo.toml pins astrid-core")
}

/// The body of the step that moves the core's pointer, so the assertions below read one step and
/// cannot be satisfied by a stray mention of `windows-prod` in a comment elsewhere.
fn core_pointer_step() -> &'static str {
    const NAME: &str = "- name: Move astrid-core's windows-prod to the released pin";
    let at = RELEASE_YML
        .find(NAME)
        .expect("release.yml has a step that moves astrid-core's windows-prod");
    let body = &RELEASE_YML[at..];
    // Up to the next step at the same indentation, or the end of the file.
    match body[NAME.len()..].find("\n      - ") {
        Some(end) => &body[..NAME.len() + end],
        None => body,
    }
}

/// A release can only move a pointer in astrid-core if the pin is a real commit. A path dependency
/// — the `[patch]` trick CLAUDE.md allows while iterating beside a local checkout — pins nothing,
/// and committing one would make every later release point `windows-prod` at whatever the last git
/// pin happened to be. Cheap to assert, and it fails on the commit that did it.
#[test]
fn astrid_core_is_pinned_by_git_revision() {
    let pin = core_pin_line();
    assert!(
        pin.contains("git = \"https://github.com/Graceful-Tools/astrid-core.git\""),
        "astrid-core must be a git dependency for a release to have a sha to point at, got: {pin}"
    );
    assert!(
        !pin.contains("path ="),
        "a path dependency on astrid-core pins nothing — the local `[patch]` must never be \
         committed (CLAUDE.md, Quick start), got: {pin}"
    );

    let rev = pin
        .split("rev = \"")
        .nth(1)
        .and_then(|rest| rest.split('"').next())
        .expect("the astrid-core dependency carries a rev");
    assert_eq!(
        rev.len(),
        40,
        "the pin must be a full 40-char sha, got {rev:?}"
    );
    assert!(
        rev.chars().all(|c| c.is_ascii_hexdigit()),
        "the pin must be a sha, got {rev:?}"
    );

    // A branch or tag would move under the pin and make it unreproducible.
    for moving in ["branch = ", "tag = "] {
        assert!(
            !pin.contains(moving),
            "astrid-core must be pinned by rev alone, not by {moving:?}: {pin}"
        );
    }
}

/// The sha the release reads has to be the sha cargo builds against. If the step grew its own way
/// of finding the pin, the two could drift silently — so it must read the file that IS the pin.
#[test]
fn the_release_reads_the_pin_from_the_file_that_is_the_pin() {
    let step = core_pointer_step();
    assert!(
        step.contains("crates/astrid-ffi/Cargo.toml"),
        "the step must read the sha from crates/astrid-ffi/Cargo.toml, not restate it:\n{step}"
    );
}

/// The release extracts the pin with a `sed` this test cannot run (no sed on the gate's path), so
/// instead it holds the manifest to the exact shape that `sed` requires. That is the half worth
/// guarding anyway: the workflow is edited once, `Cargo.toml` is edited every time the pin moves,
/// and a reformat there — indenting the dependency, putting `rev` before `git`, switching to a
/// `[dependencies.astrid-core]` table — would leave the extraction silently matching nothing and
/// fail only inside a release. The `if [ -z "$pin" ]` guard in the step turns that into a loud
/// error rather than a pointer moved to the empty string, but this fails first, on the commit.
#[test]
fn the_pin_still_has_the_shape_the_releases_sed_extracts() {
    const SED: &str = r#"sed -n 's/^astrid-core = .*rev = "\([0-9a-f]\{40\}\)".*/\1/p'"#;
    assert!(
        core_pointer_step().contains(SED),
        "this test mimics the release's sed by hand; it changed, so re-check them together:\n{SED}"
    );

    // `^astrid-core = ` — column zero, no leading whitespace, exactly this spelling.
    let matches: Vec<&str> = FFI_CARGO_TOML
        .lines()
        .filter(|line| line.starts_with("astrid-core = "))
        // `.*rev = "\([0-9a-f]\{40\}\)"` — greedy `.*` means the LAST `rev = "` on the line.
        .filter_map(|line| line.rsplit_once("rev = \""))
        .filter_map(|(_, rest)| rest.get(..40))
        .filter(|sha| {
            sha.chars()
                .all(|c| c.is_ascii_hexdigit() && !c.is_ascii_uppercase())
        })
        .collect();

    assert_eq!(
        matches.len(),
        1,
        "the release's sed must extract exactly one sha from crates/astrid-ffi/Cargo.toml, \
         got {matches:?} — the dependency's formatting changed out from under it"
    );
}

/// Only a real release moves it. `workflow_dispatch` exists so a release can be rehearsed, and a
/// rehearsal that moved `windows-prod` would leave the core's branch claiming a build nobody can
/// download — the same reason this repo's own `prod` step carries this guard.
#[test]
fn only_a_tag_push_moves_the_core_pointer() {
    let step = core_pointer_step();
    assert!(
        step.contains("github.event_name == 'push'"),
        "a workflow_dispatch rehearsal must move nothing:\n{step}"
    );
}

/// It runs in the `release` job, which `needs: build` and is itself gated on `refs/tags/v`, and it
/// sits after the publish step — a pointer written before the release exists would, if the publish
/// then failed, name a commit that was never downloadable.
#[test]
fn the_core_pointer_moves_after_the_release_publishes() {
    let publish = RELEASE_YML
        .find("uses: softprops/action-gh-release@v2")
        .expect("release.yml publishes the release");
    let pointer = RELEASE_YML
        .find("- name: Move astrid-core's windows-prod to the released pin")
        .expect("release.yml moves astrid-core's windows-prod");
    assert!(
        publish < pointer,
        "the core's pointer must move only after the release has published"
    );
}

/// The tag carries the version, as asked on task `b34ca8ab`: reading the core's tag list should say
/// which Astrid release each core commit went out in, without cross-referencing anything.
#[test]
fn the_core_tag_carries_the_windows_version() {
    let step = core_pointer_step();
    assert!(
        step.contains("windows-prod"),
        "the core's branch is windows-prod:\n{step}"
    );
    assert!(
        step.contains("windows-v"),
        "the tag prefix must carry the version as windows-v<version>:\n{step}"
    );
}

/// Whether a move is a fast-forward or a warned rollback is a rule, and astrid-web already has one
/// implementation of it. Copied rather than retold, so the two cannot drift into disagreeing about
/// what a non-linear move means.
#[test]
fn the_move_reuses_astrid_webs_script() {
    let step = core_pointer_step();
    assert!(
        step.contains("advance-prod-branch.sh"),
        "the step must call the shared script, not reimplement the move:\n{step}"
    );
    assert!(
        ADVANCE_SCRIPT.contains("is NOT a fast-forward"),
        "scripts/advance-prod-branch.sh must be astrid-web's, which warns on a non-linear move"
    );
    assert!(
        ADVANCE_SCRIPT.contains("BRANCH=\"${2:-prod}\"")
            && ADVANCE_SCRIPT.contains("PREFIX=\"${3:-prod}\""),
        "the script must still take <sha> [branch] [tag-prefix] — the call below passes all three"
    );
}

/// `github.token` is scoped to THIS repository, so it cannot push to astrid-core. The step needs a
/// credential Jon creates, and it must not quietly fall back to a token that cannot work — that
/// would fail at push time inside a released build, which is the worst place to find out.
#[test]
fn the_core_push_uses_a_credential_that_can_reach_the_other_repo() {
    let step = core_pointer_step();
    assert!(
        step.contains("ASTRID_CORE_TOKEN"),
        "the step must use a secret that can write to astrid-core:\n{step}"
    );
    assert!(
        !step.contains("github.token"),
        "github.token cannot push to astrid-core — using it would fail mid-release:\n{step}"
    );
}

/// Until that secret exists the step must skip with a visible warning, not fail. By the time it
/// runs the release has already published; failing the job would report a successful release as
/// broken. A `::warning::` is loud enough to not rot and quiet enough to not lie.
#[test]
fn a_missing_credential_warns_rather_than_failing_a_published_release() {
    let step = core_pointer_step();
    assert!(
        step.contains("::warning::"),
        "a missing ASTRID_CORE_TOKEN must warn on the run:\n{step}"
    );
    assert!(
        step.contains("exit 0"),
        "a missing ASTRID_CORE_TOKEN must not fail a release that already published:\n{step}"
    );
}
