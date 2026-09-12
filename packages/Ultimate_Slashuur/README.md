# Ultimate_Slashuur

This directory intentionally contains only a manifest template. The supplied
final LPAC was validated on 2026-08-30:

The composable package version is `1.1.1`. Its manifest description is the source of the
text displayed by the launcher's dynamic mod list; no Slashuur name or feature
description is hard-coded in the launcher UI.

- Size: `886,656` bytes
- LPAC chunks: `77`
- SHA-256:
  `004b8a19dddffe6aeef991a96ea49c33a8ab97ee21a86f26f7eff6a554bbb7fc`

The Slashuur-capable composable US/NA core was built from the isolated branch
and verified as PE32 x86:

- Size: `4,651,008` bytes
- SHA-256:
  `3bdd7ffcf8d962e2adcca1bdc6778f248e5a38f81d44f94e030553f986b2a57e`

This core keeps the Slashuur runtime trace open and buffers gameplay
breadcrumbs, flushing on crash, fatal report, or clean shutdown. It removes
the measured 15-25 ms synchronous disk stalls from special attacks without
changing their controls, timing, damage, effects, or targeting.

The resulting `Ultimate_Slashuur.t2mod` declares the shared
`TMNT2_Composable_Core_v1_US` family at priority 100. It supplies the
`ultimate-slashuur` flag and can satisfy all five initial core capabilities,
so the launcher selects it whenever Slashuur is active with any other package.

The packager accepts the untouched retail files locally, validates their known
hashes, and emits `Ultimate_Slashuur.t2mod`. It does not copy either retail file
into the result. The existing LPAC bytes remain unchanged.
