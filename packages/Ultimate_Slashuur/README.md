# Ultimate_Slashuur

This directory intentionally contains only a manifest template. The supplied
final LPAC was validated on 2026-08-30:

The package version is `1.0.1`. Its manifest description is the source of the
text displayed by the launcher's dynamic mod list; no Slashuur name or feature
description is hard-coded in the launcher UI.

- Size: `886,656` bytes
- LPAC chunks: `77`
- SHA-256:
  `004b8a19dddffe6aeef991a96ea49c33a8ab97ee21a86f26f7eff6a554bbb7fc`

The standalone `Ultimate_Slashuur` US/NA EXE was built from the isolated branch
and verified as PE32 x86:

- Size: `4,641,792` bytes
- SHA-256:
  `c3a9d43ec34cac12b80cce808cce6808aa96d46df473b4b63b7743806afa0315`

The resulting `Ultimate_Slashuur.t2mod` was deterministically rebuilt and its
EXE delta reproduced the standalone executable byte-for-byte.

The packager accepts the untouched retail files locally, validates their known
hashes, and emits `Ultimate_Slashuur.t2mod`. It does not copy either retail file
into the result.
