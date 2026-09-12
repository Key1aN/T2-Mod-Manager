# Composable EXE-only packages

The `pack-composable-exe` command creates a `.t2mod` containing one verified
`T2DELTA1` operation from the untouched US/NA retail `TMNT2.exe` to the shared
runtime-gated T2 Core.

The command also verifies the untouched retail `TMNT.DAT` fingerprint but does
not include, rebuild, patch, or replace that archive. Each package declares the
shared composable core family, its own runtime feature flag, an exclusive
`file:TMNT2.exe` resource, and one feature-specific resource.

The intended packages are:

- `Intro_Skip.t2mod`
- `MSAA.t2mod`
- `New_Difficulties.t2mod`
- `Widescreen_Support.t2mod`

Compiled EXE inputs and generated packages are local artifacts and must never
be committed to this repository.

Core integration tests load all packages through the same checkbox path used
by the launcher, verify combined feature-state transitions and core selection,
confirm that EXE-only combinations leave `TMNT.DAT` byte-identical, and verify
retail restoration after deactivation.
