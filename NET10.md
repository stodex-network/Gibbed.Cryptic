# Branches and .NET versions

`stodex` is the default development branch. Its history starts with the separate .NET 10 port, then adds the retail STO fixes and STOdex tooling:

```text
upstream/main (32c0b99)
  -> Joel Day's generic 64-bit build repair (00618f5)
  -> port/net10
  -> stodex
```

The upstream commit references missing x64 definitions and cannot build on its own, so Joel Day's repair is a prerequisite for the port. His authorship is preserved.

`port/net10` changes target frameworks, persisted serializer generation and executable location lookup, and adds a synthetic serializer regression. It keeps STO-specific format fixes, resource mappings, fork URLs and schema removal separate. Its own [BUILDING.md](https://github.com/stodex-network/Gibbed.Cryptic/blob/port/net10/BUILDING.md) documents that build.

Use [BUILDING.md](BUILDING.md) in this branch for the current STO bundle. It needs a .NET 10 SDK to build and a .NET 10 runtime to run. A serializer and converter must use the same runtime and matching schemas; a .NET Framework serializer is not a substitute for the .NET 10 one.

The older Framework source remains on `64bit-update-kurgh`. The earlier combined port is kept on `archive/net10-with-stodex-patches`. Local Framework bundles belong in `bin-net48/`, separate from the current `bin-net10/` output. These ignored bundles are not supplied by cloning the repository.

The HOGG server's JSON protocol did not change with this port, so app clients can use the modern server independently. Loading game schemas from JSON at runtime is separate work; this build still generates a serializer DLL from XML.
