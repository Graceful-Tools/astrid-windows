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
deliberate act with a real certificate — see the approvals section of `CLAUDE.md`. For a local
install with a self-signed certificate, `signtool` and a trusted test certificate are what is
needed, and adding one to a machine's certificate store is a change worth asking about first.

## The assets

`Assets/` holds three flat-colour PNGs, generated rather than drawn, so the package is valid and
the repository has no binary nobody can explain. They are placeholders: a real icon replaces them
without touching anything else.
