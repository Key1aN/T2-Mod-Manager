# T2 Mod Manager

T2 Mod Manager v0.2.2 is a safety-first mod manager for the US/NA
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

## Current state

This source package contains the v0.2.2 architecture, UI, package validator,
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

The GitHub Actions workflow publishes a self-contained, single-file Windows
build named `T2-Mod-Manager-v0.2.2-Clean-Unchecked-First-Run-US.exe`.

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
