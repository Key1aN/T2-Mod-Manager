# Ultimate_Slashuur v1.0.1 build information

## Package

- Filename: `Ultimate_Slashuur.t2mod`
- Size: `2,160,105` bytes
- SHA-256:
  `aaa5b733596823dc0ea0ce1d7a5f4ea01dd6c71e3a578395633de3e9fa6f6443`
- Entries: `manifest.json`, `payload/Ultimate_Slashuur.t2delta`, and
  `payload/Ultimate_Slashuur.lpac`

## Standalone EXE output

- Size: `4,641,792` bytes
- SHA-256:
  `c3a9d43ec34cac12b80cce808cce6808aa96d46df473b4b63b7743806afa0315`

## Executable delta

- Size: `1,688,501` bytes
- SHA-256:
  `6e576bb809b735e569d1b160bbcdda53d0fbbbbc30897fc69dc9af81f422389f`

## Final LPAC

- Size: `886,656` bytes
- Chunk count: `77`
- SHA-256:
  `004b8a19dddffe6aeef991a96ea49c33a8ab97ee21a86f26f7eff6a554bbb7fc`

The package was built twice independently with byte-identical results. The
delta was applied back to the verified original US/NA executable and reproduced
the standalone executable byte-for-byte. ZIP integrity and the three-entry
allowlist were also verified.

Version `1.0.1` changes only `manifest.json`: it adds the expanded description
consumed by the launcher's dynamic mod list. The EXE delta and final LPAC bytes
and hashes are unchanged from version `1.0.0`.
