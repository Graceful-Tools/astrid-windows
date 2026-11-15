Build, sign, install and launch the Astrid Windows app from this checkout, so a change can be seen running rather than only tested.

## Goal

**Get the app on screen from the code that is in this working tree right now, and say which
commit that was.** The gate proves the tests pass; this proves the app runs. Both matter, and
`npm run predeploy` does not do this one — it never packages (see *The gate is not this*).

End by reporting the **version, the commit and the pid**, so a screenshot taken afterwards can be
tied to a commit. That is the whole point: on 2026-10-28 Jon tested `0.1.2.0` while `main` was
**24 UI commits ahead**, and the bug he reported had already been fixed. An app that lags is worse
than no app, because it produces confident, wrong conclusions.

Optional argument: a **brand** (`/astridwin acme`), passed through to `package.ps1 -Brand`. With no
argument the build is Astrid's own, byte for byte — that invariant is asserted by
`BrandProfileTests`, so do not pass `-Brand ''` explicitly; omit it.

## Before building

1. **Say what is being packaged.** `git log --oneline -1` and `git status --short`. A dirty tree is
   fine here — seeing uncommitted work running is often the reason to do this — but **report it**,
   so nobody takes the screenshot as evidence about `main`. If `HEAD` is not `main`, name the
   branch.
2. **Is the app already running?** `Get-Process Astrid.App`. A running instance holds the ARM64
   output DLLs open and the publish fails with `MSB3027` and zero compile errors. **Do not kill it
   unasked** — say which pid holds it and ask. (Same failure mode blocks `predeploy`.)
3. **Pick a fresh version.** `ls dist/Astrid-*-arm64.msix`, take the highest, bump the **third**
   part: `0.1.5.0` -> `0.1.6.0`. Never reuse a version — `Get-AppxPackage` reporting the version you
   just built is the only proof the install actually replaced the old one, and `dist/` still holds
   stale artifacts back to 2026-09-27. The last part stays `0` (a Store submission requires it) and
   all four parts are mandatory; `package.ps1` throws on anything else.

## The sequence

Four steps. Run them one per call, and do not improvise a shortcut — each line is there because
something quieter failed.

```powershell
# 1. Build and package. ~4 minutes. ARM64 ONLY - see below.
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1 -Version <v> -Architectures arm64

# 2. Sign with the local test certificate. NO -Install switch - see below.
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/sign-local.ps1 -Version <v> -Architectures arm64

# 3. Install, unelevated.
powershell -NoProfile -Command "Add-AppxPackage -Path dist/signed-local/Astrid-<v>.msixbundle -ForceUpdateFromAnyVersion"

# 4. Launch from the packaged identity, not the exe.
powershell -NoProfile -Command "Start-Process explorer.exe 'shell:AppsFolder\GracefulToolsLLC.AstridTasks_gsgtre3g2jjb6!Astrid'"
```

**`-Architectures arm64` alone.** This machine is ARM64. `package.ps1` otherwise builds x64 too and
doubles a ~4-minute publish for a package that cannot run here. A single-architecture run writes no
`.msixbundle` at the top of `dist/`; step 2 bundles the one package itself, which is why step 3
reads from `dist/signed-local/`.

**No `-Install` switch, deliberately.** `sign-local.ps1 -Install` requires
`-TrustScope LocalMachine`, and that branch hard-throws unless the shell is elevated — it needs
admin only to **write** machine trust. That write was already done on 2026-09-13 (task `edf273c3`,
Jon approved it): the cert `9BE48580...4CD3`, friendly name *Astrid local test signing*, is in
`CurrentUser\My`, `CurrentUser\TrustedPeople` **and** `LocalMachine\TrustedPeople`, valid to
2027-09-27. So the signature is already trusted machine-wide and `Add-AppxPackage` needs no
elevation. Signing at the default `CurrentUser` scope produces the same bytes; the scope only
decides where the public half is imported, and both stores already hold it.

**Launch by identity, not by `Astrid.App.exe`.** The app is framework-dependent on the Windows App
SDK runtime. Started outside its package it resolves the runtime through the bootstrapper, which is
exactly the path that hid a real bug for weeks: until 2026-10-18 the manifest declared no
`PackageDependency` for the runtime, so every package ever built installed fine and then died on the
first line of `Main` with `REGDB_E_CLASSNOTREG`. `F5` and the UI smoke tests never saw it. Launching
the packaged identity is what catches it.

## If the signing step is refused

Under **auto mode** the classifier denies `sign-local.ps1` as a `[Production Deploy]`, even with no
`-Install`. Nothing about the certificate or elevation has changed — the classifier is the obstacle,
and it is why `0.1.3.0`, `0.1.4.0` and `0.1.5.0` were all built and never installed.

When that happens: **step 1 still ran**, so say so, and hand Jon steps 2-4 as three exact
copy-pasteable lines with the version filled in. Do not try to route around it. In particular
`Add-AppxPackage -Register dist/publish/win-arm64/AppxManifest.xml` — the no-certificate dev path —
fails `0x80073CFB` (*an unpackaged version cannot replace a packaged one*), because the installed
package is signed with the same publisher the manifest declares. A signed upgrade works; a loose
registration cannot.

## Verify, then report

```powershell
powershell -NoProfile -Command "Get-AppxPackage -Name GracefulToolsLLC.AstridTasks | Select-Object Version,Architecture; Get-Process Astrid.App | Select-Object Id,StartTime"
```

An install on its own proves almost nothing — see `REGDB_E_CLASSNOTREG` above. The app has to be
**running**, with a `StartTime` of seconds ago. Report the version, the commit from step 1 and the
pid together.

**There are two Astrids installed on this machine**, with separate caches:
`GracefulToolsLLC.AstridTasks` (this one, native, ARM64) and `www.astrid.cc-25DFFA7A` (the PWA,
`1.0.0.3`). Always name which one is running. "The Windows app shows the wrong list" has to
establish which app first.

## Not this skill's job

- **Never `-Uninstall`, and never remove the package to clear a problem.** An uninstall takes the
  app's local data with it. CLAUDE.md requires asking first; ask.
- **Never `-RemoveCertificate`.** It is a change to the machine, not the repo.
- **No Store upload, no signing with a real certificate, no publishing to the update feed.** Those
  reach real users and are Jon's deliberate act (CLAUDE.md -> Approvals).
- **`scripts/wack.ps1`** (the Windows App Certification Kit) is a release gate, not part of seeing
  the app run.

## The gate is not this

`npm run predeploy` does **not** run `package.ps1`. On 2026-10-25 packaging had been broken for six
days behind a green gate, because nothing in the gate builds a package. So a green `predeploy` is no
evidence the app packages, installs or starts — and this skill is no evidence the tests pass. Run
both when it matters.
