# Standalone EXE-only packages

The `pack-exe-only` command creates a `.t2mod` containing one verified
`T2DELTA1` operation against the untouched US/NA retail `TMNT2.exe`.

The command also verifies the untouched retail `TMNT.DAT` fingerprint but does
not include, rebuild, patch, or replace that archive. Each package declares an
exclusive `file:TMNT2.exe` resource and one feature-specific resource.

The intended packages are:

- `Intro_Skip.t2mod`
- `MSAA.t2mod`
- `New_Difficulties.t2mod`
- `Widescreen_Support.t2mod`

Compiled EXE inputs and generated packages are local artifacts and must never
be committed to this repository.

Core integration tests can load the packages through the same install and
activation path used by the launcher, verify the declared output EXE hash,
confirm that `TMNT.DAT` remains byte-identical, and verify retail restoration
after deactivation.
