# Dependencies: lockfiles and the Socket gate (ADEV-353)

Every NuGet version the shell restores is **locked**. Each project under `app/` commits a
`packages.lock.json` that lists its direct and transitive packages, for both runtimes (win-x64,
win-arm64). `app/Directory.Build.props` turns this on for every project. In CI (where `CI=true`)
restores run in **locked mode**: a restore that would change a lockfile fails rather than quietly
resolving something new.

The Rust side is locked by `Cargo.lock`, as before.

Because the lockfiles name every version, astrid-web's Socket gate (`socket-package-gate.yml`, run
on every pull request) checks the whole NuGet tree, not only the `PackageReference`s.

## Adding or upgrading a package

**On Windows:**

1. Change the `PackageReference` in the `.csproj`.
2. Run `dotnet restore app/Astrid.sln`, then
   `dotnet restore app/Astrid.App.UITests/Astrid.App.UITests.csproj`. Lockfiles update locally,
   because locked mode is CI-only.
3. Read the lockfile diff: it lists every version that came in with yours.
4. Before building, check the new versions with Socket:
   `node ../astrid-web/scripts/deps-gate.mjs check`. The pull request runs the same check.
5. Commit the `.csproj` and the lockfiles together.

**From a machine without the .NET SDK (the dev Mac):** change the `.csproj` and push the branch.
`.github/workflows/nuget-locks.yml` runs on any pull request that touches a project file. It writes
the lockfiles on Windows and uploads them as the `nuget-lockfiles` artifact, then fails because they
differ from what's committed. Download the artifact, commit its files, and push again.

**Dependabot** updates the lockfiles in its own NuGet pull requests.

## The checks

| Check | What it catches |
|---|---|
| `crates/astrid-ffi/tests/nuget_locks_contract.rs` (in `cargo test`, so predeploy and CI) | A project without a lockfile, a `PackageReference` its lockfile does not record, or lockfiles or locked mode turned off |
| `.github/workflows/nuget-locks.yml` | Lockfiles that NuGet would resolve differently, checked for every restore CI and a release make (each RID, self-contained, the UI tests and the solution) |
| `ci.yml` shell job | Any build or test restore that would change a lockfile (locked mode) |
