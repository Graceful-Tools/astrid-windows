# acme-assets

Deliberately empty. `acme.brand.json` points its `windows.assets` here so the tests exercise a
profile that brings its **own** tile set rather than inheriting `packaging/Assets` — if it pointed
back at Astrid's folder, the assets mapping would be indistinguishable from not mapping at all.

So `scripts/package.ps1 -Brand acme` fails, by design, on the layout check: *"the manifest names 7
asset(s) the package layout does not contain"*. That is the right failure and the one a partner
should see before a twenty-minute publish rather than at install time — a packaged build whose
splash screen is missing stops with 0x80073CF6 and names one file.

A real partner profile points `windows.assets` at a directory holding every tile
`packaging/AppxManifest.xml` names, at every scale: see `packaging/README.md` for the list and
`packaging/Assets` for the shape.
