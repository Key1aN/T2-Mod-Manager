# Installation safety model

## Invariants

1. The uploaded or user-selected retail files are never edited in place while
   a candidate is being built.
2. A vanilla backup is accepted only after its size and SHA-256 match a known
   retail fingerprint.
3. Every desired mod state is rebuilt from the vanilla backup.
4. A package operation may write only to a predefined target understood by the
   manager. Package scripts and arbitrary destination paths are unsupported.
5. Every package payload has an expected SHA-256 and uncompressed size.
6. Every generated output is validated before commit.
7. Conflicts are resolved before staging begins.
8. The game must not be running during commit or recovery.

## Commit and recovery

The EXE and DAT cannot be replaced as one operating-system atomic action.
Instead, the manager provides crash-recoverable transactional behavior:

1. Build and validate both candidate files.
2. Persist a journal with the `Prepared` state.
3. Replace the EXE while retaining a rollback copy; persist `ExeCommitted`.
4. Replace the DAT while retaining a rollback copy; persist `DatCommitted`.
5. Validate the installed pair and persist `Completed`.
6. If any step fails, restore every file already committed.
7. On the next launch, recover any journal not marked `Completed`.

Staging and rollback files are created beneath the selected game directory so
file replacement remains on the same volume.

## Conflict identities

Examples of resource IDs:

- `file:TMNT2.exe`
- `afs:TMNT.DAT:143`
- `lpac:TMNT.DAT:143:motion:Warp` 
- `exe:function:CSlashuur::OnTeleport`
- `feature:difficulty-profile`

Two enabled mods cannot claim the same exclusive resource. A future trusted
merge handler can explicitly convert selected resource types from exclusive
to mergeable; packages cannot provide executable merge code themselves.

