# `.t2mod` package format v1

A `.t2mod` is a ZIP file with a UTF-8 `manifest.json` at its root. All other
entries are inert payloads referenced by the manifest.

Every manifest must provide a non-empty `displayName`, numeric dotted
`version`, and non-empty `description`. The launcher uses these fields to build
the mod list and selected-mod details without mod-specific UI code.

Packages may not contain absolute paths, parent-directory traversal, device
paths, symbolic links, or executable scripts. The manager reads only manifest-
declared entries and never performs a general ZIP extraction.

## Supported operations

### `apply-t2delta`

Applies a `T2DELTA1` block delta to a known file. Both source and result length
and SHA-256 are verified.

### `replace-afs-entry`

Rebuilds an AFS archive while replacing one exact zero-based entry. The
operation verifies the original entry hash and replacement LPAC hash. All
unchanged entries and the original name-table bytes are copied directly.

## Resource claims

Every operation must be covered by an exclusive resource claim. The manager
also supports higher-level feature claims for conflicts such as mutually
exclusive difficulty overhauls.

## Composable T2 Core metadata

EXE-changing packages can opt into safe composition by declaring a `core`
object and one or more `featureFlags`. All packages in a compatible family use
the same core ID. Each package still contains one verified executable delta,
but the manager installs only one core: the highest-priority active package
whose declared capabilities cover the complete requested feature set.

Packages with the same core ID may share the `file:TMNT2.exe` claim. Other
exclusive resources continue to conflict normally. Legacy whole-EXE packages
cannot be mixed with composable packages.

The selected flags are written transactionally to
`.t2-mod-manager/features.txt`. The shared executable defaults every optional
feature to off when that file is missing or contains no matching flag.

## Future compatibility

Unknown schema versions or operation types are rejected rather than guessed.
New built-in operation types can be added in later manager versions without
allowing package-supplied code execution.

