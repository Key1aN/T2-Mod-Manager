# v0.1 release identity

- Product name: `T2 Mod Manager`
- Repository: `T2-Mod-Manager`
- Manager source branch: `feature/t2-mod-manager-v0.1`
- Completed standalone EXE source branch (legacy build identifier):
  `feature/community-mod-manager-v0.1-ultimate-slashuur-standalone-us`
- Completed standalone EXE workflow (legacy build identifier):
  `build-community-mod-manager-v0.1-ultimate-slashuur-standalone-us`
- Standalone build artifact:
  `TMNT2-Ultimate-Slashuur-Standalone-US-run-<run-number>`
- Standalone executable: `TMNT2_Ultimate_Slashuur_Standalone_US.exe`
- Standalone symbols: `TMNT2_Ultimate_Slashuur_Standalone_US.pdb`
- Standalone map: `TMNT2_Ultimate_Slashuur_Standalone_US.map`
- Workflow: `build-t2-mod-manager-v0.1`
- Windows artifact: `T2-Mod-Manager-v0.1-win-x64-run-<run-number>`
- Application: `T2-Mod-Manager.exe`
- Symbols: `T2-Mod-Manager.pdb`
- First mod package: `Ultimate_Slashuur.t2mod`
- EXE delta payload: `Ultimate_Slashuur.t2delta`
- LPAC payload: `Ultimate_Slashuur.lpac`

A linker MAP is not produced because the manager is a managed .NET/WPF
application. MAP naming remains mandatory for future native TMNT2 EXE builds.

## v0.2 composable-mod build (cumulative)

- Branch: `codex/t2-mod-manager-v0.2-composable-mods-us`
- Workflow: `build-t2-mod-manager-v0.2-composable-mods-us`
- Windows artifact:
  `T2-Mod-Manager-v0.2-Composable-Mods-US-win-x64-run-<run-number>`
- Application: `T2-Mod-Manager-v0.2-Composable-Mods-US.exe`
- Application symbols: `T2-Mod-Manager-v0.2-Composable-Mods-US.pdb`
- Core symbols: `T2-Mod-Manager-Core-v0.2-Composable-Mods-US.pdb`
- Packager: `T2-Mod-Packager-v0.2-Composable-Mods-US.dll`
- MAP: not produced because the launcher is a managed .NET/WPF application.
- Core family: `TMNT2_Composable_Core_v1_US`
- Standard native core outputs: `TMNT2_T2Mod_Composable_Core_v1_US.exe`,
  `.pdb`, and `.map`
- Slashuur native core outputs:
  `TMNT2_T2Mod_Composable_Core_Slashuur_v1_US.exe`, `.pdb`, and `.map`

## Game-folder-selection fix build

- Branch: `codex/t2-mod-manager-v0.1-game-folder-selection-fix`
- Workflow: `build-t2-mod-manager-v0.1-game-folder-selection-fix`
- Windows artifact:
  `T2-Mod-Manager-v0.1-Game-Folder-Selection-Fix-US-win-x64-run-<run-number>`
- Application: `T2-Mod-Manager-v0.1-Game-Folder-Selection-Fix-US.exe`
- Symbols: `T2-Mod-Manager-v0.1-Game-Folder-Selection-Fix-US.pdb` and
  `T2-Mod-Manager-Core-v0.1-Game-Folder-Selection-Fix-US.pdb`
- MAP: not produced; this is a managed .NET/WPF application rather than a
  native TMNT2 executable build.

## Dynamic-mod-list build (cumulative)

- Branch: `codex/t2-mod-manager-v0.1-dynamic-mod-list`
- Workflow: `build-t2-mod-manager-v0.1-dynamic-mod-list`
- Windows artifact:
  `T2-Mod-Manager-v0.1-Dynamic-Mod-List-US-win-x64-run-<run-number>`
- Application: `T2-Mod-Manager-v0.1-Dynamic-Mod-List-US.exe`
- Symbols: `T2-Mod-Manager-v0.1-Dynamic-Mod-List-US.pdb` and
  `T2-Mod-Manager-Core-v0.1-Dynamic-Mod-List-US.pdb`
- MAP: not produced; this is a managed .NET/WPF application rather than a
  native TMNT2 executable build.

## Clean-first-run catalog build (cumulative)

- Branch: `codex/t2-mod-manager-v0.1-clean-first-run-catalog`
- Workflow: `build-t2-mod-manager-v0.1-clean-first-run-catalog`
- Windows artifact:
  `T2-Mod-Manager-v0.1-Clean-First-Run-Catalog-US-win-x64-run-<run-number>`
- Application: `T2-Mod-Manager-v0.1-Clean-First-Run-Catalog-US.exe`
- Symbols: `T2-Mod-Manager-v0.1-Clean-First-Run-Catalog-US.pdb` and
  `T2-Mod-Manager-Core-v0.1-Clean-First-Run-Catalog-US.pdb`
- MAP: not produced; this is a managed .NET/WPF application rather than a
  native TMNT2 executable build.
