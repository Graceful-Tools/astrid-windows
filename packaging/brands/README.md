# Brand profiles

A `*.brand.json` here is a deployment profile, the **same shape** astrid-web and the Apple apps
read (`astrid-web/brands/README.md`). `scripts/package.ps1 -Brand <name>` fills the manifest, the
tile set and the build from one:

```powershell
powershell -File scripts/package.ps1 -Brand acme
```

`-Brand` takes a name — resolved against this directory first, then `../astrid-web/brands/` — or a
path to a `.json` file. **Omitted means hosted Astrid, byte for byte as before brand profiles
existed**; `BrandProfileTests` asserts it rather than leaving it to care.

## What a Windows build takes from a profile

From the shared `env` block, because one profile describes a deployment across platforms:

| Variable | Becomes |
|---|---|
| `NEXT_PUBLIC_BRAND_TITLE` | the name in Start, Settings and the Store |
| `NEXT_PUBLIC_BRAND_NAME` | the tile's short name, and the fallback for the above |
| `NEXT_PUBLIC_BRAND_TAGLINE` | the description |
| `NEXT_PUBLIC_BRAND_APP_URL_SCHEME` | the scheme the installer registers |
| `NEXT_PUBLIC_BRAND_DOMAIN` | the server the build talks to, as `https://<domain>` |

And from an optional `windows` block — the same arrangement as the `ios` one — for the four things
that have nowhere to live in a cross-platform profile:

| Key | Meaning |
|---|---|
| `publisherDisplayName` | must match the Partner Center account's publisher display name exactly |
| `identityName`, `identityPublisher` | the identity **that account's** reservation produced; overrides `packaging/store-identity.json` |
| `assets` | a directory of tiles, relative to the profile, replacing `packaging/Assets` |

Anything else in the file is another platform's and is skipped, not rejected. Every Windows key may
also be given as `displayName`, `shortName`, `description`, `scheme`, `schemeDisplayName` or
`serverUrl` when a partner wants Windows to differ from the rest of the deployment.

The scheme and the server are checked before anything is built: a scheme outside `[a-z0-9-]`
registers nothing and loses every sign-in, and a server that is not `https://<host>` would send the
session over the wire in the clear. Both fail here rather than twenty minutes into a publish.

**`publisherDisplayName` falls back to the brand name, not to Graceful Tools LLC.** A partner who
forgets it ships their own name — wrong, but theirs.

## The two profiles that must always exist

- **`astrid.brand.json`** — hosted Astrid, stated as a profile. `-Brand astrid` and no `-Brand` at
  all must produce identical values; that is how the plumbing proves it changed nothing.
- **`acme.brand.json`** — a fully rebranded build sharing nothing with Astrid. A profile that
  merely loads is not a white-labelled build, so the tests assert that every value moves and that
  no Astrid value is left behind.

Both live here rather than only in astrid-web because CI checks out this repo alone.

## What is still Astrid's, and why

- **The sign-in callback.** astrid-web already returns the browser to
  `<NEXT_PUBLIC_BRAND_APP_URL_SCHEME>://auth/callback`, and this app now registers and reads the
  configured scheme — but `astrid-core` compares the callback against its own `URL_SCHEME`
  constant (`crates/astrid-core/src/services/auth.rs`), so a build on any other scheme still has
  its sign-in refused. That is the core's half; until it lands, a partner build must keep
  `astrid` as its scheme.
- **The cache file name** (`astrid.db`, or `astrid-<host>.db` per server). A path on disk, not
  anything a reader sees, and renaming it per brand would strand the cache of every install that
  already exists.
- **The package's internal names** — `Astrid.App.exe` and the `Application Id`. Renaming them
  changes the AUMID for no gain a reader could see.
