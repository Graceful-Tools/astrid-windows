# The design tokens, vendored

`tokens.resolved.json` is a copy of
[astrid-core](https://github.com/Graceful-Tools/astrid-core)'s
`design/generated/tokens.resolved.json` at the revision in [`REVISION`](./REVISION). It is the one
source for how Astrid looks — every theme's colours, and the brand's accent — and it is to
appearance what the core's `contracts/fixtures` are to behaviour.

**Nothing compiles against it.** No code reads this file; `DesignTokenTests` does, and compares it
to the brushes in `app/Astrid.App/App.xaml`. That is the arrangement astrid-core's
`design/README.md` names for the clients it has no emitter for yet: "Android (Compose) and Windows
(XAML) emitters are added to `crates/xtask/src/design.rs` in the change that adopts them… Until
then those clients can read `tokens.resolved.json` in a test."

Before this file existed, the brushes in `App.xaml` were hand-copied from astrid-web's
`styles/themes/*.css`, with a comment saying so and nothing checking it. That is how the accent sat
at `#3B82F6` for a day after every other client had taken `#2563EB`.

## Taking a new revision

1. Copy astrid-core's `design/generated/tokens.resolved.json` over this one and write its full
   forty-character sha into `REVISION`.
2. Run the shell tests. `DesignTokenTests` fails for each brush that no longer matches, naming the
   token and both values, so the diff tells you what to change rather than you hunting for it.
3. Change `App.xaml`, not this file. A value never changes in a client's copy — that rule is
   astrid-core's and it is the only reason a vendored copy is worth having.

## What does not follow the tokens, and why

Two things, and neither is a themed brush in `App.xaml` — `DesignTokenTests` has no exception list,
because a brush that is listed is a brush nobody is checking. Adding one back means deleting an
assertion, which is a reviewable act; the reason then belongs in astrid-core's register, not here.

- **The priority palette** (`PriorityPalette`, `Priority*Brush`) is sampled from
  `app/Astrid.App/Assets/Checkboxes/check_box_{0..3}.png` and pinned to those files by
  `PriorityPaletteTests`. The images win *for now*: they are pixels this shell cannot restyle, and a
  swatch that disagrees with the mark beside it is the bug task `204c9d98` fixed. astrid-core carries
  it as DS2 and DS21, and both rows read **open on Windows** — the shared art became `currentColor`
  in AWTD-1605 and there is a `generated/windows/AstridCheckbox.xaml` to vendor, so the fix is to
  draw that art in the priority tokens and drop the sampled values. Under a partner accent, low
  priority stays Astrid blue until then.
- **Ocean** has no theme dictionary here. In the tokens it differs from Light in the page
  background and its border, and this shell paints that as a surface behind the chrome. One set of
  brushes, two appearances.
