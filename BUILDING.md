# Building and using the STO tools

Install the .NET 10 SDK and Python 3. Run the commands below from the repository root. Exporting schemas from a running client requires Windows.

## Build the tools

```console
git submodule update --init --recursive
dotnet build "Cryptic Studios.sln" --configuration Release
dotnet run --project tests/SerializerPort/SerializerPort.csproj --configuration Release
```

The last command tests serializer generation using small synthetic schemas. It needs no game files.

## Supply matching schemas

A serializer is built from XML definitions, not from the BIN files you want to decode. Your schema directory must contain both `parses/` and `enums/`, including any definitions referenced by the resources you want to read:

```text
private/schemas/Star Trek Online/Live/
  parses/
    PCGeometryDef.parse.xml
    UIStyleFont.parse.xml
    ...
  enums/
    Color.enum.xml
    ...
```

Use an existing export for your client build, or export new schemas as described below. You can keep them anywhere; `private/` is ignored by Git for convenience. Review export failures and repair missing or incorrect definitions before generating a serializer.

## Build a STO bundle

Commit any source changes first: the build script records the source commit and requires a clean working tree.

```console
python scripts/build-sto.py --build ST.372.20260910a.24 --schema-dir "private/schemas/Star Trek Online/Live" --schema-id local-export --mappings scripts/sto-ST.372.20260910a.19.json
```

Use the actual client build for `--build`, your XML directory for `--schema-dir`, and a label for this export or repair for `--schema-id`. The mapping file's historical `.19` filename is retained because its parser hashes also match the six resources checked on `.24`. Check those hashes against your own BINs before using it for another patch.

The script builds the tools, generates the serializer, and writes a complete bundle to `bin-net10/`. Use `--output` to choose a different directory.

```text
bin-net10/
  Gibbed.Cryptic.ConvertResource.exe
  Gibbed.Cryptic.GenerateSerializer.exe
  Gibbed.Cryptic.ExportSchemas.exe
  Gibbed.Cryptic.Unpack.exe
  ...dependency DLLs and .NET runtime configuration...
  serializers/
    assemblies/STO.Serialization.ST.372.20260910a.24.dll
    resources/                    # JSON mappings
    manifests/                    # inputs, hashes and build logs
```

Keep the whole bundle together. Old active mappings are moved into `serializers/history/` before installing new ones. The historical mappings in `configs/legacy-serializers/` are reference material and are not loaded by the generated bundle.

## Check decoding

Run the structure-list regression with an absolute path to the generated bundle:

```console
dotnet run --project tests/StructureLists/StructureLists.csproj --configuration Release -- C:/path/to/Gibbed.Cryptic/bin-net10
```

It checks record boundaries, trailing fields, overrun rejection and generated list callbacks. No game files are needed beyond the generated serializer.

Extract a matching BIN from your game installation, then convert it. For example:

```console
bin-net10/Gibbed.Cryptic.Unpack.exe --help
bin-net10/Gibbed.Cryptic.ConvertResource.exe -x private/Bin/Costumegeometry.Bin private/decoded/Costumegeometry
```

Successful conversion writes both `@resource.xml` and `entries.xml` and reaches `Saving index...`. Inspect the records and compare with known working output when available. An exit code alone does not confirm decoding: an unsupported resource can return without producing entries.

The supplied mappings cover CostumeSkeleton, CostumeGeometry, DynGroundRegData, ModelHeaders, English client messages and UI fonts. Validate any additional mappings against real resources. The CURRENTFILE fallback, Materials dependencies and object-mode conversion remain experimental.

## Export schemas from a running client

Build the exporter into `bin-net10/` if you have not already built a bundle:

```console
dotnet build projects/Gibbed.Cryptic.ExportSchemas/Gibbed.Cryptic.ExportSchemas.csproj --configuration Release -p:PlatformTarget=x64 -p:OutputPath=../../bin-net10/
```

Find the intended GameClient process ID in Task Manager's Details tab. Replace `12345` below with that ID:

```console
bin-net10/Gibbed.Cryptic.ExportSchemas.exe --pid 12345 --project "Star Trek Online/Live" --output private/schemas
```

This writes `private/schemas/Star Trek Online/Live/`, which you can pass to `--schema-dir`. The exporter reads process memory without modifying it. Use `--help` for its other options, including memory-dump input.
