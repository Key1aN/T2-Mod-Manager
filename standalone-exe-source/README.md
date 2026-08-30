# Reconstructing the standalone EXE branch

These patches reproduce the isolated TMNT2 source branch used to build the
`Ultimate_Slashuur` EXE component.

- Canonical fork: `https://github.com/Key1aN/tmnt2`
- Start commit: `dabccc3e3dc02c8a01eac1638fe1aeb589e2723b`
- Branch:
  `feature/community-mod-manager-v0.1-ultimate-slashuur-standalone-us`
- Verified result tree: `c11babbcceee780f594d4e42c36f36b23c7b7ca7`

The complete series was reapplied to a clean detached worktree at the stated
base and produced the verified result tree above. Apply the numbered patches
in order with `git am`. The final workflow performs
a source-scope check before building and produces uniquely named EXE, PDB, MAP,
build-information, and Actions artifact files.

The patches contain no retail executable, DAT archive, or LPAC payload.
