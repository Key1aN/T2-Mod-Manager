# Ultimate_Slashuur v1.1.1 FPS-fix composable build information

## Package

- Filename: `Ultimate_Slashuur.t2mod`
- Size: `2,415,161` bytes
- SHA-256:
  `13f9deb1739c7da4e1de7184c1423cf76fc3a6236c3a22e414b8bc3a5870fdf3`
- Entries: `manifest.json`, `payload/Ultimate_Slashuur.t2delta`, and
  `payload/Ultimate_Slashuur.lpac`

## Slashuur composable core output

- Size: `4,651,008` bytes
- SHA-256:
  `3bdd7ffcf8d962e2adcca1bdc6778f248e5a38f81d44f94e030553f986b2a57e`

## Executable delta

- Size: `1,937,420` bytes
- SHA-256:
  `ad003a3a05735010d63da4d3d5b762ec34e01ddd59c83ea71a65abc682e62e0b`

## Final LPAC

- Size: `886,656` bytes
- Chunk count: `77`
- SHA-256:
  `004b8a19dddffe6aeef991a96ea49c33a8ab97ee21a86f26f7eff6a554bbb7fc`

The manager's full integration path applied this package together with all four
other composable packages, selected this priority-100 core, preserved the final
LPAC replacement, then restored the retail EXE and DAT after deactivation. ZIP
integrity and the three-entry allowlist were also verified.

Version `1.1.1` keeps the v1.1.0 composable feature behavior and changes only
the Slashuur-capable core's runtime trace I/O. Gameplay breadcrumbs are now
buffered instead of physically flushed for every special-attack event. The
final LPAC bytes and hash are unchanged from versions `1.0.1` and `1.1.0`.
