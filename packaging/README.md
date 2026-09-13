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

## The assets

`Assets/` holds three flat-colour PNGs, generated rather than drawn, so the package is valid and
the repository has no binary nobody can explain. They are placeholders: a real icon replaces them
without touching anything else.
