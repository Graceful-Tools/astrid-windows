//! The ARM64 job is a gate, not a weather report (task `197acbdb`).
//!
//! This machine is ARM64 and the shell ships ARM64, but for most of this repo's life CI's only
//! *blocking* ARM64 signal was `cross-build`, which compiles for `aarch64-pc-windows-msvc` and
//! never runs a test. `ffi over the pinned core (arm64)` does run them, on a native
//! `windows-11-arm` runner — and it carried `continue-on-error`, so a test that passes on x64 and
//! fails on aarch64 (pointer width, `isize`, SQLite alignment, a `#[cfg(target_arch)]` arm)
//! reached `main` under a green tick.
//!
//! That excuse was written when native ARM64 runners were rolling out, and it outlived its reason:
//! measured over every non-scheduled `ci.yml` run from 2026-11-15 14:32 to 2026-11-16 21:38, the
//! job was **14 success, 0 failure** at about 159s, across 11 pushes and 3 pull requests.
//!
//! These are rules about a workflow file, and this repo has no JS test harness to hold them, so
//! they live here — beside `release_pointers_contract.rs`, which reads `release.yml` with
//! `include_str!` for exactly the same reason: this is the gate that actually runs.
//!
//! The point of the test, rather than just the edit: the next person to meet a flaky ARM64 run can
//! still excuse the job, but they have to delete a test that says why it was not excused, instead
//! of adding one quiet line that nothing objects to.

const CI_YML: &str = include_str!("../../../.github/workflows/ci.yml");

/// Is this the start of a top-level job — exactly two spaces, then a key?
///
/// Spelled out rather than `starts_with("  ")`, which is also true of every nested line in the
/// job and would end the body at `name:` on the line after the key. That bug made
/// [`the_arm64_job_is_not_excused_from_the_gate`] pass against a two-line slice, which is the
/// failure mode this whole file exists to prevent, so it is worth a named function.
fn is_job_key(line: &str) -> bool {
    line.starts_with("  ")
        && !line.starts_with("   ")
        && line.trim_end().ends_with(':')
        && !line.trim_start().starts_with('#')
}

/// The body of the `core:` job — from its key to the next job key at the same indentation — so the
/// assertions below read one job and cannot be satisfied or defeated by another job's text.
fn core_job() -> Vec<&'static str> {
    let mut lines = CI_YML
        .lines()
        .skip_while(|line| line.trim_end() != "  core:");
    let key = lines.next().expect("ci.yml defines a `core:` job");
    let body: Vec<&str> = std::iter::once(key)
        .chain(lines.take_while(|line| !is_job_key(line)))
        .collect();
    assert!(
        body.len() > 5,
        "parsed the `core:` job as {} line(s) — the slice is wrong, not the workflow:\n{}",
        body.len(),
        body.join("\n")
    );
    body
}

/// The lines of the `core:` job that are not comments, so a rule cannot be satisfied by prose.
fn core_job_code() -> String {
    core_job()
        .into_iter()
        .filter(|line| !line.trim_start().starts_with('#'))
        .collect::<Vec<_>>()
        .join("\n")
}

/// The job still has to *run* on ARM64 for any of this to mean anything. If the matrix entry goes
/// away, the assertion below about `continue-on-error` would pass vacuously — a gate deleted reads
/// exactly like a gate honoured.
#[test]
fn the_core_job_runs_natively_on_arm64() {
    let code = core_job_code();
    assert!(
        code.contains("runner: windows-11-arm"),
        "ci.yml's `core` job must still run a native ARM64 runner, or there is no ARM64 test \
         signal at all; `cross-build` compiles and never runs a test. Job body:\n{code}"
    );
    assert!(
        code.contains("target: aarch64-pc-windows-msvc"),
        "the ARM64 matrix entry must build for aarch64-pc-windows-msvc. Job body:\n{code}"
    );
}

/// The rule this task exists for. `continue-on-error` on the ARM64 entry means a red ARM64 job
/// leaves the run green, which is indistinguishable from the job having passed.
#[test]
fn the_arm64_job_is_not_excused_from_the_gate() {
    let code = core_job_code();
    assert!(
        !code.contains("continue-on-error"),
        "ci.yml's `core` job must not carry `continue-on-error`: the ARM64 half is the only CI \
         job that RUNS the tests on aarch64, so excusing it means a target-specific failure \
         reaches `main` under a green tick. It passed 14/14 over 2026-11-15..16 when this was \
         removed (task `197acbdb`). If it has genuinely become flaky, delete this test in the \
         same commit and say which runs failed. Job body:\n{code}"
    );
}

/// The cross-build is not a substitute, and a later tidy-up might think it is. It belongs to the
/// *cross* toolchain `npm run predeploy` uses, which is a different thing from running the suite
/// on the hardware — so both jobs stay.
#[test]
fn the_cross_build_still_runs_as_well() {
    assert!(
        CI_YML.contains("\n  cross-build:\n"),
        "ci.yml must keep the ARM64 cross-build job: it checks the cross toolchain that \
         `predeploy` uses on an x64 host, which the native job does not exercise"
    );
    assert!(
        CI_YML.contains("cargo build --workspace --target aarch64-pc-windows-msvc"),
        "the cross-build job must still cross-compile the workspace for aarch64"
    );
}
