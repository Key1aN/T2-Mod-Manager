# T2 Mod Manager

T2 Mod Manager v0.1 is a safety-first mod manager for the US/NA
32-bit PC release of *TMNT 2: Battle Nexus*.

The first supported mod is named exactly `Ultimate_Slashuur`. It is intended
to install playable Slashuur with Adaptive Teleport v2 without enabling MSAA,
Intro Skip, widescreen, or difficulty modifications.

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
- Resource claims allow file, archive-entry, LPAC, and EXE-code conflicts to be
  detected before any write begins.

## Current state

This source package contains the v0.1 architecture, UI, package validator,
AFS/LPAC readers, block-delta implementation, backup/transaction engine,
packager, and dependency/conflict model.

The game-folder selector accepts any directory name containing `TMNT2.exe`
and `TMNT.DAT`. A missing stale saved location is not displayed, and selecting
a replacement verified installation is supported while all mods are inactive.

Installed mods are displayed from `.t2mod` manifest metadata rather than
hard-coded launcher content. Selecting a mod shows its package description,
and its checkbox activates or deactivates it through the verified rebuild
pipeline.

The first distributable package, `Ultimate_Slashuur.t2mod`, has been generated
from the verified standalone EXE and final expanded 77-chunk LPAC. The package
contains only the EXE delta, modded LPAC, and declarative manifest; it contains
no original retail EXE or DAT archive. The source repository itself does not
contain the LPAC payload.

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
build named `T2-Mod-Manager.exe`.

## First supported retail fingerprint

| File | Size | SHA-256 |
| --- | ---: | --- |
| `TMNT2.exe` | 4,796,416 | `b827478461e1687f9404679211fc2d2d47bd7222aeb81a8554bd793575a89564` |
| `TMNT.DAT` | 502,067,200 | `cadae3cea3ad60a1f61acf4984f47adb15efccc880fe82c254b332196caa04b2` |

See `docs/ORIGINAL_US_FINGERPRINTS.md` for the archive-entry fingerprint.
See `docs/RELEASE_NAMES.md` for the unique branch, workflow, artifact, binary,
symbol, and package names reserved for v0.1.

## Public-release rule

Do not commit, package, or publish original `TMNT2.exe`, original `TMNT.DAT`,
or extracted retail assets. A public mod package contains only author-supplied
mod payloads and/or deltas plus a manifest.

## Licensing

A public software license has not yet been selected. Choose and review one
before accepting external code contributions or publishing this project as
open source. Third-party and game-derived material must be reviewed
independently.
