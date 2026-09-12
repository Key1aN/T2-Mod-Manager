# Composable mod package set v1 US

Core family: `TMNT2_Composable_Core_v1_US`

## Native cores

- Standard core: `TMNT2_T2Mod_Composable_Core_v1_US.exe`
  - Size: `4,639,232` bytes
  - SHA-256: `f78983dbce8a001d1bbf45e1978bd0fb2b1443380704ff196768b4dc0dc7c137`
  - Priority in packages: `10`
  - Capabilities: `intro-skip`, `msaa`, `brutal-difficulties`, `widescreen`
- Slashuur core: `TMNT2_T2Mod_Composable_Core_Slashuur_v1_US.exe`
  - Size: `4,651,008` bytes
  - SHA-256: `0f4cdc43718d99120e429816ec0ce0f3cc255f098b36d6f66df9d3bc6b10e3ff`
  - Priority in Ultimate Slashuur: `100`
  - Capabilities: all standard capabilities plus `ultimate-slashuur`

## Packages

| Package | Version | Size | SHA-256 |
| --- | ---: | ---: | --- |
| `Intro_Skip.t2mod` | 1.1.0 | 1,933,105 | `658cd640e8eff5f9778e54cbfc05b0bc48941c78c6031feb356bb8a44d021f64` |
| `MSAA.t2mod` | 4.1.0 | 1,933,183 | `7ea036e655f198c87c1cb6b4ffce84ba462ce2da9cb3fc7260fa65c01267ace7` |
| `New_Difficulties.t2mod` | 3.1.0 | 1,933,177 | `1d79fd1e95bbea9104417a383d2a9dd52ca5021e9134b062e6c904cb952245f9` |
| `Ultimate_Slashuur.t2mod` | 1.1.0 | 2,414,955 | `5114e3613f2dc44e8ecac56ff47ee4c2529f6b815349fb2b8b6a3f4762634420` |
| `Widescreen_Support.t2mod` | 1.1.0 | 1,933,181 | `79de1bbe94d1562a44e3d09da40829a91d2062eb1d4777e269ca2d0431c1ee3a` |

Each ordinary package contains only `manifest.json` and one T2DELTA1 payload.
Ultimate Slashuur additionally contains the existing final 77-chunk LPAC.
No package contains the retail executable or retail DAT archive.

The v0.2 manager integration test activated the packages cumulatively, checked
the exact selected core and sorted feature-state file after every transition,
verified that EXE-only combinations left `TMNT.DAT` byte-identical, verified
the Ultimate Slashuur AFS entry, then deactivated every package and confirmed
exact retail EXE and DAT restoration.
