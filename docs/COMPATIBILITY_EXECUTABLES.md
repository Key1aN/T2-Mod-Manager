# Optional compatibility executable

`Fix Obsolete Disc Check` is an optional baseline provider for the verified
US/NA 32-bit PC build. It is not a mod package and does not claim an EXE
resource, so a gameplay EXE mod can safely override it.

## Supported import

| Property | Value |
| --- | --- |
| ID | `tmnt2-pc-us-na-obsolete-disc-check-fix-v1` |
| Size | `4,796,416` bytes |
| SHA-256 | `86d508d0ba5905cb457577a3a43c3e397de5e835e2a221c97c3caca7132069ab` |
| Format | Win32 x86 PE |

The executable is intentionally not stored in this repository, a release
artifact, or a `.t2mod` package. The user selects a local file, and the manager
accepts it only when the size, SHA-256, and PE format match this allowlist.

## State precedence

1. An active gameplay EXE mod is rebuilt from the immutable retail backup.
2. If no gameplay EXE mod is active and the fix is enabled, the imported
   compatibility executable becomes the live `TMNT2.exe`.
3. If the fix is disabled, the untouched retail executable becomes live.

`Restore Retail Original` deactivates all mods and disables this fix. Disabling
`Ultimate_Slashuur` does not disable the fix, so it automatically returns to
the fixed-vanilla executable while restoring vanilla `TMNT.DAT`.

The imported copy lives in the manager's compatibility store, separate from
the immutable retail backup, and its hash is verified every time it is used.
Toggling only this fallback uses an EXE-only crash-recoverable transaction;
`TMNT.DAT` is verified but is not staged, rebuilt, or replaced.
