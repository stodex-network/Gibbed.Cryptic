# Building the .NET 10 tools

Install a .NET 10 SDK and initialize the three library submodules. The existing game schema submodule declarations and dependency revisions are unchanged by the .NET changes. This branch starts from upstream `32c0b99` plus Joel Day's `00618f5` generic 64-bit repair because upstream references omitted x64 definitions and cannot build without that prerequisite. Retail STO fixes and STOdex-specific tooling are maintained separately.

```console
git submodule update --init projects/Gibbed.IO projects/Gibbed.PortableExecutable projects/NDesk.Options
dotnet build "Cryptic Studios.sln" --configuration Release
dotnet run --project tests/SerializerPort/SerializerPort.csproj --configuration Release
```

`Directory.Build.props` selects .NET 10 for the library projects without modifying their source. `global.json` accepts the latest installed .NET 10 SDK feature band. The converter, generator, unpacker and exporter use the modern runtime; the exporter still requires Windows and access to a compatible client process.

Serializer generation uses `PersistedAssemblyBuilder`. Persisted types cannot be inspected as executable runtime types, so generic method references are mapped through `TypeBuilder`, and enum default metadata comes from the schema definition. Long branch operands keep generated callbacks valid. Assembly description metadata is retained; the legacy Win32 version resource is omitted.

The regression executable generates a serializer from public synthetic schemas, loads the saved assembly and exercises enum wrappers, more than twenty list fields and DataContract serialization. It requires no game files or running game process. This upstream port deliberately leaves game-specific formats, memory layouts, extraction behavior, schema revisions and resource mappings unchanged.
