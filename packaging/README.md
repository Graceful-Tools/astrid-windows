# Packaging

`scripts/package.ps1` builds the MSIX packages and the bundle over them:

```powershell
powershell -File scripts/package.ps1 -Version 0.1.0.0
```

It publishes the app per architecture, lays each folder out with `AppxManifest.xml` and `Assets/`,
and calls `makeappx` — the Desktop Bridge shape, which is what a Win32 app with no packaged-only
APIs needs. There is no packaging project: the app is one SDK-style csproj, and a `.wapproj` would
mean a second build and a second place for the version number to be wrong.

**The output is unsigned.** Windows will not install an unsigned package, and signing is a
deliberate act with a real certificate — see the approvals section of `CLAUDE.md`.

## Test-installing it on this machine

`scripts/sign-local.ps1` signs a copy of the build with a self-signed test certificate so the
bundle can be installed and started here, which is the only way to know that it does:

```powershell
powershell -File scripts/package.ps1 -Version 0.1.0.0
powershell -File scripts/sign-local.ps1 -Version 0.1.0.0 -Install -Launch
```

It creates the certificate in the current user's personal store with the manifest's Publisher as
its Subject (MSIX refuses any mismatch), trusts the public half in the current user's Trusted
People store, and writes the signed packages to `dist/signed-local/` — the unsigned outputs at the
top of `dist/` are what the Store takes and are never touched. With `-Install` it installs the
bundle and reports which architecture Windows chose; with `-Launch` it starts the app from its
packaged identity and checks the process is the packaged one.

Adding a certificate to a machine's store is a change to the machine, not to this repository,
which is why it took a decision (Jon approved it on 2026-09-13, task edf273c3). The certificate
is named "Astrid local test signing" in certmgr, and `-RemoveCertificate` takes it out again;
`-Uninstall` removes the package.

## Certifying it before the first upload

`scripts/wack.ps1` runs the Windows App Certification Kit over the bundle. Partner Center runs the
same checks at submission, so this only ever buys time — a failure found here arrives in ten
minutes instead of days later, after certification. Worth doing once before the first upload;
unnecessary before every later one.

```powershell
powershell -File scripts/package.ps1 -Version 0.1.0.0
powershell -File scripts/wack.ps1 -Version 0.1.0.0     # from an ADMINISTRATOR shell
```

Elevation is not a policy of ours: the kit installs the package in order to drive it. The run takes
about ten minutes and moves the mouse, so leave the machine alone. The report lands at `wack.xml`
(gitignored) and opens in a browser.

**The kit is not part of the build tools.** The Windows SDK that comes with the VS Build Tools
component `Windows11SDK.26100` does not include `appcert.exe`; it is a feature of the standalone
SDK installer, so the directory exists holding only the SupportedAPIs XML. `wack.ps1` says so and
names the install command rather than letting Windows report a missing file.

## The identity

`store-identity.json` holds the two values Partner Center assigned when the name was reserved
(2026-09-13): the package name `GracefulToolsLLC.AstridTasks` and the publisher, a `CN=` GUID.
The reserved name is **Astrid Tasks** ("Astrid" was taken), so the manifest's
`Package/Properties/DisplayName` and the application's `VisualElements DisplayName` both say
that — the Store rejects a package whose display names are not reserved names. The tile's
`ShortName` and the `astrid://` protocol's display name stay "Astrid"; neither is checked against
the reservation. If Jon later reserves "Astrid" as an additional name, those two display names can
go back.

## The listing

`listing/store-listing.md` is the Partner Center listing, drafted 2026-09-13 (task f91d2f07) to
match the iOS App Store listing for Astrid Tasks: description, features, search terms, the
age-rating answers, and what to put in every other field. `listing/screenshots/` holds the seven
Store screenshots, 2538x1589 PNGs taken from the app signed in as the demo account. Take new ones
the same way when the app changes: run the debug build into its own `ASTRID_DATA_DIR`, size the
window to at least 1366x768 physical pixels, and capture the DWM frame bounds.

## The assets

`Assets/` holds three flat-colour PNGs, generated rather than drawn, so the package is valid and
the repository has no binary nobody can explain. They are placeholders: a real icon replaces them
without touching anything else.
