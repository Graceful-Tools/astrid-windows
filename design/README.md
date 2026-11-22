# The design, vendored

Three copies of [astrid-core](https://github.com/Graceful-Tools/astrid-core)'s generated design
output, all at the single revision in [`REVISION`](./REVISION):

| File | Copy of | Checked by |
|---|---|---|
| `tokens.resolved.json` | `design/generated/tokens.resolved.json` | `DesignTokenTests`, against the brushes in `App.xaml` |
| `AstridCheckbox.xaml` | `design/generated/windows/AstridCheckbox.xaml` | `DesignTokenTests`, against its own `body-sha256` |
| `AstridTokens.xaml` | `design/generated/windows/AstridTokens.xaml` | `DesignTokenTests`, against its own `body-sha256` **and** against every themed brush in `App.xaml` |

Together they are the one source for how Astrid looks — every theme's colours, the brand's accent,
and the shared checkbox art — and they are to appearance what the core's `contracts/fixtures` are
to behaviour.

`REVISION` is deliberately the same sha as the `astrid-core` rev pinned in
`crates/astrid-ffi/Cargo.toml`: the art is XAML the shell draws beside behaviour the core decides,
and two revisions for one core is a question nobody wants to have to answer. Re-vendoring is
therefore cheapest done in the same change as a pin bump.

**Nothing compiles against any of them yet.** No code reads them; the tests do.

`AstridTokens.xaml` is the Windows emitter astrid-core's `design/README.md` used to say did not
exist — task `52a03550` (AWTD-1699) added it, and task `fdd8ef7f` vendored it here. It is a
`ResourceDictionary` of sixty-three brushes in three themes plus the radius, spacing and motion
scales, named for the keys this shell already uses, and the point of it is that `App.xaml`'s
hand-written brushes can eventually be deleted rather than merely checked.

**It is vendored and gated, not yet merged**, and three things have to come from astrid-core before
it can be. They are listed here rather than in the test because they are astrid-core's to fix:

1. **No `Default` theme dictionary.** The emitter writes `Light`, `Dark` and `HighContrast`. WinUI
   resolves `Default` — not `Light` — when the system reports no preference, and
   `ThemeBrushes.Resolve` asks for `"Default"` by name for every lookup from code. A merged
   dictionary with no `Default` answers those with the gray fallback.
2. **The two DS9 aliases.** This shell's `AstridSurface` is a different token in each theme —
   `bg-hover` on light, `bg-secondary` on dark — and `AstridSurfaceHover` is `bg-tertiary`. The
   emitter aliases `AstridSurface` to `bg-primary`, which is right on light by coincidence and
   wrong on dark.
3. **WinUI's accent-button names.** `AccentFillColorDefaultBrush`, `AccentFillColorSecondaryBrush`
   and `TextOnAccentFillColorPrimaryBrush` are WinUI's own keys, overridden here rather than
   derived (DS26). The values are generated; the names are not.

Until then, `Every_themed_brush_is_the_generated_value_task_fdd8ef7f` compares every brush
`App.xaml` writes to the value astrid-core generates for it, through a table of those five names.
That is a weaker thing than deleting the brushes and a stronger one than the token-by-token
assertions beside it: those compare a brush to the token *this test file claims* it is drawn from,
so the claim and the check share an author, where this compares it to the register's own output.

`AstridCheckbox.xaml` is a `ResourceDictionary` meant to be merged into `App.xaml` and drawn — that
is the adoption step, and it is what task `82e4ad32` carries. It is vendored ahead of that because
a copy that is present and gated cannot rot, while a copy that arrives with the change that uses it
gets hand-tweaked in the same breath. Its box and repeat styles stroke from
`{TemplateBinding Foreground}`, so whatever draws them chooses the colour; its tick does not, which
is why the tick is pinned to the `checkbox-check` token by a test of its own.

**Why the hash is checked and the tokens' is not.** astrid-core stamps every generated source file
with a `body-sha256` of everything below the header, so "a consumer checks that hash in its own
gate, with no access to this repository, and a hand-edited copy fails there". The tokens do not
carry one — they are data, and an edit to them surfaces as a brush assertion failing. The art would
not: a retyped colour or a nudged path in XAML the shell draws looks deliberate and fails nothing.

Before this file existed, the brushes in `App.xaml` were hand-copied from astrid-web's
`styles/themes/*.css`, with a comment saying so and nothing checking it. That is how the accent sat
at `#3B82F6` for a day after every other client had taken `#2563EB`.

## Taking a new revision

1. Copy **all three** files over these ones — `design/generated/tokens.resolved.json`,
   `design/generated/windows/AstridCheckbox.xaml` and
   `design/generated/windows/AstridTokens.xaml` — from the *same* astrid-core revision, and write
   its full forty-character sha into `REVISION`. Taking one without the others is how they drift,
   and nothing would report it: each file is checked against itself, not against the others.
2. Run the shell tests. `DesignTokenTests` fails for each brush that no longer matches, naming the
   token and both values, so the diff tells you what to change rather than you hunting for it; and
   it fails for the art if the `body-sha256` no longer describes the body, which means the copy was
   edited in transit rather than copied.
3. Change `App.xaml`, not these files. A value never changes in a client's copy — that rule is
   astrid-core's and it is the only reason a vendored copy is worth having.

The sha to take is normally whatever `crates/astrid-ffi/Cargo.toml` pins, since `REVISION` tracks
it. Taking a *newer* design revision than the pin is allowed — nothing here compiles — but say so
in the commit, because the next person to read `REVISION` will assume it is the pin.

**`REVISION` equals the pin** (`a8d279a`, task `c2c185cb`, after the bump in task `8c543a42`). A
copy taken ahead of the pin is still allowed. When that happens, say so here and close the gap
at the next bump.

## What does not follow the tokens, and why

One thing — `DesignTokenTests` has no exception list, because a brush that is listed is a brush
nobody is checking. Adding one back means deleting an assertion, which is a reviewable act; the
reason then belongs in astrid-core's register, not here.

The priority palette used to be the other exception, sampled from checkbox PNGs. Task `82e4ad32`
drew the mark from `AstridCheckbox.xaml` in the `AstridPriority*` tokens, and task `c4174b14` took
the last hand-written colours out of the views and the code: `DesignMetricsTests` holds
`xaml.hex-colours` and `code.hex-colours` at zero. A colour the tokens do not have is a question for
astrid-core's register, not a hex in a view.

- **Ocean** has no theme dictionary here. In the tokens it differs from Light in the page
  background and its border, and this shell paints that as a surface behind the chrome. One set of
  brushes, two appearances.
