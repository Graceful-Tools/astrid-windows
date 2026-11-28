//! Every NuGet version the shell restores is locked — task ADEV-353.
//!
//! Each project under `app/` commits a `packages.lock.json`, so a transitive package cannot change
//! without a commit, and astrid-web's Socket gate (`socket-package-gate.yml`) checks every version
//! the lockfiles name rather than only the `PackageReference`s. CI restores in locked mode.
//!
//! This repo has no .NET on the dev Mac and no JS harness, so the rules live beside the other
//! cross-file contracts here: the gate that actually runs everywhere. What it cannot check offline
//! is whether NuGet itself would resolve the same graph; `.github/workflows/nuget-locks.yml` does
//! that on Windows, for every restore CI and a release make.

use std::fs;
use std::path::{Path, PathBuf};

const PROPS: &str = include_str!("../../../app/Directory.Build.props");
const LOCKS_YML: &str = include_str!("../../../.github/workflows/nuget-locks.yml");

fn app_dir() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("../../app")
}

/// Every `.csproj` under `app/`, one directory deep (where the projects live).
fn projects() -> Vec<PathBuf> {
    let mut found: Vec<PathBuf> = fs::read_dir(app_dir())
        .expect("app/ exists")
        .filter_map(|entry| entry.ok().map(|e| e.path()))
        .filter(|dir| dir.is_dir())
        .flat_map(|dir| {
            fs::read_dir(&dir)
                .into_iter()
                .flatten()
                .filter_map(|e| e.ok().map(|e| e.path()))
                .filter(|p| p.extension().is_some_and(|ext| ext == "csproj"))
                .collect::<Vec<_>>()
        })
        .collect();
    found.sort();
    found
}

/// `(name, version)` for each `PackageReference` with a `Version` attribute.
fn package_references(csproj: &str) -> Vec<(String, String)> {
    csproj
        .split("<PackageReference")
        .skip(1)
        .filter_map(|rest| {
            let tag = &rest[..rest.find('>')?];
            let attr = |key: &str| {
                let at = tag.find(&format!("{key}=\""))? + key.len() + 2;
                Some(tag[at..at + tag[at..].find('"')?].to_string())
            };
            Some((attr("Include")?, attr("Version")?))
        })
        .collect()
}

#[test]
fn adev_353_every_project_restores_with_a_lockfile_and_ci_restores_locked() {
    assert!(
        PROPS.contains("<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>"),
        "app/Directory.Build.props must turn lockfiles on for every project"
    );
    assert!(
        PROPS.contains("<RestoreLockedMode Condition=\"'$(CI)' == 'true'\">true</RestoreLockedMode>"),
        "CI must restore in locked mode, so a restore that would change a lockfile fails"
    );
    assert!(
        PROPS.contains("win-x64;win-arm64"),
        "every project names both runtimes, so -r builds and `dotnet test` produce the same lockfile"
    );
}

#[test]
fn adev_353_every_project_commits_a_lockfile_that_matches_its_references() {
    let projects = projects();
    assert!(projects.len() >= 5, "found only {} projects under app/", projects.len());
    for csproj in projects {
        let dir = csproj.parent().unwrap();
        let lock_path = dir.join("packages.lock.json");
        let lock = fs::read_to_string(&lock_path).unwrap_or_else(|_| {
            panic!(
                "{} has no packages.lock.json: run `dotnet restore`, or take the nuget-lockfiles artifact (docs/DEPENDENCIES.md)",
                csproj.display()
            )
        });
        assert!(lock.contains("\"dependencies\""), "{} is not a NuGet lockfile", lock_path.display());
        let text = fs::read_to_string(&csproj).unwrap();
        for (name, version) in package_references(&text) {
            assert!(
                lock.contains(&format!("\"{name}\": {{"))
                    && lock.contains(&format!("\"requested\": \"[{version}, )\"")),
                "{} references {name} {version}, which its packages.lock.json does not record: the lockfile is stale",
                csproj.display()
            );
        }
    }
}

#[test]
fn adev_353_windows_checks_the_lockfiles_against_every_ci_restore() {
    assert!(LOCKS_YML.contains("--locked-mode"), "nuget-locks.yml must restore in locked mode");
    for restore in ["-r $rid", "SelfContained=true", "Astrid.App.UITests", "app/Astrid.sln"] {
        assert!(LOCKS_YML.contains(restore), "nuget-locks.yml must cover the restore with {restore}");
    }
}
