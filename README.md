# T2 Mod Manager

T2 Mod Manager v0.2.3 is a portable, safety-first mod manager for the US/NA
32-bit PC release of *TMNT 2: Battle Nexus*.

Compatible `.t2mod` packages can be checked together. The initial composable
set contains Ultimate Slashuur, Intro Skip, MSAA Frame State Lock v4, the final
three brutal difficulties, and Hor+ widescreen with stretched original UI.

## Safety model

- Original game files are identified by exact SHA-256 fingerprints.
- Vanilla backups are immutable after creation and verification.
- Activate and deactivate operations are rebuilt from the vanilla backup.
- Candidate files are produced in a same-volume staging directory.
- The manager validates the PE, AFS, LPAC, payload hashes, and output hashes
  before replacing either live game file.
- A transaction journal records multi-file installation progress and supports
  rollback after a failure.
- Mod packages are declarative ZIP containers. They cannot execute scripts.
- Compatible EXE mods resolve to one verified runtime-gated T2 Core; resource
  claims still block real file, archive-entry, LPAC, and EXE-code conflicts.
- Selected feature flags are installed and recovered in the same transaction
  as the EXE and DAT.

## Portable data (v0.2.3)

The manager writes nothing to the Windows user profile. All manager data lives
in `T2-Mod-Manager-Data` beside the manager EXE:

| Path | Contents |
| --- | --- |
| `T2-Mod-Manager-Data\state.json` | Selected game folder, imported mods, checked state |
| `T2-Mod-Manager-Data\backups\<build>\vanilla\` | Immutable verified retail `TMNT2.exe` and `TMNT.DAT` |
| `T2-Mod-Manager-Data\packages\` | Imported `.t2mod` copies |
| `T2-Mod-Manager-Data\logs\manager-YYYYMMDD.log` | Manager activity and errors (30 days kept) |
| `T2-Mod-Manager-Data\reports\` | Diagnostic report ZIPs |

Package paths are stored relative to that folder, so the whole manager folder
can be moved or carried on another drive. The selected game folder is stored as
an absolute path; if it moves, select it again while all mods are inactive. The
game folder still holds `.t2-mod-manager\` (`features.txt`, journal, same-volume
staging, rollback) because the game reads the feature flags at runtime.

On the first v0.2.3 start, data from the old per-user folder
`%LOCALAPPDATA%\T2-Mod-Manager` is copied once into the portable folder. The old
folder is never modified or deleted; the copied vanilla backup is hash-verified
before use like any other backup. The manager folder must be writable (do not
place it in Program Files).

The published app is a single EXE plus a few native WPF DLLs in the same folder
(`IncludeNativeLibrariesForSelfExtract=false`), so .NET does not extract native
libraries into `%TEMP%`. Keep the published folder together.

## Logs and crash reports (v0.2.3)

**Logs** in the header opens the log page. It lists, newest first:

- manager logs (every operation, progress step, and full exception details);
- game files written beside `TMNT2.exe`: `TMNT2_Slashuur_trace.log`,
  `TMNT2_crash_*.log`, `TMNT2_crash_*.dmp`, `TMNT2_fatal_*.log`, other `*.log`;
- the installed `.t2-mod-manager\features.txt` and `transaction.json`.

**Create Report** writes one ZIP to `T2-Mod-Manager-Data\reports\` containing those
files (newest five dumps), `state.json`, and `system-info.txt` with the exact
current `TMNT2.exe` SHA-256 so the matching PDB/MAP can be selected. The ZIP
contains no retail EXE or DAT. Games launched with **Launch Game** are monitored:
a non-zero exit code or a new crash/fatal file is logged and opens the log page.

## Current state

This source package contains the v0.2.3 architecture, UI, package validator,
AFS/LPAC readers, block-delta implementation, backup/transaction engine,
packager, and dependency/conflict model.

The game-folder selector accepts any directory name containing `TMNT2.exe`
and `TMNT.DAT`. A missing stale saved location is not displayed, and selecting
a replacement verified installation is supported while all mods are inactive.

Installed mods are displayed from `.t2mod` manifest metadata rather than
hard-coded launcher content. Selecting a mod shows its package description,
and checkboxes activate or deactivate compatible mods together through the
verified rebuild pipeline.

The v0.2.2 dynamic-catalog migration clears entries left by earlier launcher
and package-validation runs when their saved game installation is unavailable.
This prevents an old active package from appearing as a checked, disabled mod.
The first Mods view is empty, and mods appear only after the user imports their
`.t2mod` files. An active legacy record is retained only when its managed game
installation still exists and may require safe restoration.

Composable packages contain a delta from the verified retail EXE to a shared
core plus declarative core capabilities and feature flags. Ultimate Slashuur
also contains the existing final expanded 77-chunk LPAC. No package contains
the original retail EXE or DAT archive, and this repository contains no retail
payload.

## Build

Requirements:

- Windows 10/11 x64
- .NET 10 SDK

```powershell
dotnet build T2-Mod-Manager.slnx -c Release
dotnet run --project tests/T2ModManager.Core.Tests -c Release
dotnet publish src/T2ModManager.App/T2ModManager.App.csproj `
  -c Release -r win-x64 --self-contained true
```

The GitHub Actions workflow publishes a self-contained Windows build named
`T2-Mod-Manager-v0.2.3-Portable-Logs-US.exe` with its native WPF DLLs beside it.

## First supported retail fingerprint

| File | Size | SHA-256 |
| --- | ---: | --- |
| `TMNT2.exe` | 4,796,416 | `b827478461e1687f9404679211fc2d2d47bd7222aeb81a8554bd793575a89564` |
| `TMNT.DAT` | 502,067,200 | `cadae3cea3ad60a1f61acf4984f47adb15efccc880fe82c254b332196caa04b2` |

See `docs/ORIGINAL_US_FINGERPRINTS.md` for the archive-entry fingerprint.
See `docs/RELEASE_NAMES.md` for the unique branch, workflow, artifact, binary,
symbol, and package names reserved for the cumulative releases.

## Public-release rule

Do not commit, package, or publish original `TMNT2.exe`, original `TMNT.DAT`,
or extracted retail assets. A public mod package contains only author-supplied
mod payloads and/or deltas plus a manifest.

## Licensing

A public software license has not yet been selected. Choose and review one
before accepting external code contributions or publishing this project as
open source. Third-party and game-derived material must be reviewed
independently.
