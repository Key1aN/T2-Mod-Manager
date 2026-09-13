# Ultimate_Slashuur

This directory intentionally contains only a manifest template. The supplied
dual-costume LPAC was validated on 2026-09-13.

The composable package version is `1.2.0`. Its manifest description is the
source of the text displayed by the launcher's dynamic mod list; no Slashuur
name or feature description is hard-coded in the launcher UI.

- Size: `1,314,496` bytes
- LPAC chunks: `79`
- SHA-256:
  `7dbafbba6072184644de9fe3d05fc1a51b557022601642e0270cb0eaebaa3ef7`

The 79-entry LPAC preserves every payload and header from the verified final
77-entry Slashuur LPAC except the intentionally expanded `player` texture
dictionary. It adds only `slashuur_proto` model and motion-parameter records.
All ten distinct repeated `common_ef` texture chunks remain byte-identical.

The Slashuur-capable composable US/NA core was built from the isolated branch
and verified as PE32 x86:

- Size: `4,651,008` bytes
- SHA-256:
  `0e926dd1e97ea6a09d155e58c390ecb2c3b0e04b4c1152196b6e49318945ed56`

This core keeps the Slashuur runtime trace open and buffers gameplay
breadcrumbs, flushing on crash, fatal report, or clean shutdown. It removes
the measured 15-25 ms synchronous disk stalls from special attacks without
changing their controls, timing, damage, effects, or targeting.

The resulting package declares the shared `TMNT2_Composable_Core_v1_US`
family at priority 100. It supplies the `ultimate-slashuur` and
`slashuur-dual-costumes` flags and can satisfy all six current core
capabilities, so the launcher selects it whenever Slashuur is active with any
other package.

The packager accepts the untouched retail files locally, validates their known
hashes, checks the requested LPAC chunk count, and emits the package. It does
not copy either retail file into the result. Normal selection keeps the white
Slashuur costume; holding R1 while confirming Slashuur selects the prototype
costume. Result/victory scenes still use the game's separate `sls` asset.
