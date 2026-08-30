# Verified US/NA retail baseline

The following files were supplied as untouched US/NA retail inputs and were
read without modifying the uploaded ZIP.

## Container

- Uploaded ZIP SHA-256:
  `446cef1b871309eeff4078cdbd501324fc72f79b63667e906dabc281150af293`
- ZIP entries: `TMNT2.exe`, `TMNT.DAT`

## Game executable

- Filename: `TMNT2.exe`
- Size: `4,796,416`
- SHA-256:
  `b827478461e1687f9404679211fc2d2d47bd7222aeb81a8554bd793575a89564`
- Format: PE32 GUI executable, Intel 80386, 13 sections

## Main archive

- Filename: `TMNT.DAT`
- Size: `502,067,200`
- SHA-256:
  `cadae3cea3ad60a1f61acf4984f47adb15efccc880fe82c254b332196caa04b2`
- Format: AFS, 237 data entries
- Name-table offset: `502,054,912`
- Name-table size: `11,376`

## Original playable Slashuur entry

- Logical path: `player/slashuur/slashuur.lpac`
- Zero-based AFS entry ID: `143`
- Offset: `339,374,080`
- Size: `605,632`
- SHA-256:
  `e3f93aa1e63efefb403854a5eea603cc322a9de6b953de89292e804e76777451`
- LPAC chunk count: `49`

The manager identifies this target using the archive ID, expected entry size,
and expected entry hash. It does not rely on the AFS name table because this
retail archive does not provide unique logical paths there.

