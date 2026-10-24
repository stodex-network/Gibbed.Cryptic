# Gibbed.Cryptic

Tools for unpacking Star Trek Online's game files and converting its binary data to XML. This is the STOdex fork of [Rick "gibbed"'s tools](https://github.com/gibbed/Gibbed.Cryptic), updated for the 64-bit STO client and .NET 10.

## What the tools do

- **Unpack** extracts files from `.hogg` archives, or lists their contents.
- **ExportSchemas** reads a running client or a memory dump and exports XML descriptions of the game's data structures.
- **GenerateSerializer** compiles XML descriptions or validated STOdex schema JSON into a serializer DLL.
- **ConvertResource** and **ConvertObject** use a serializer to convert binary game data to and from XML.

To decode a BIN file, you need the converter, a serializer built from matching schemas, and a JSON mapping that tells the converter which class to use. The schemas describe the data layout; the mapping connects a BIN's parser hash to that layout. A game patch can change either one.

The [STOdex HOGG server](https://github.com/stodex-network/stodex-game-resources-server) uses these tools and serializers to make game resources available to the STOdex app.

## Getting started

Install the .NET 10 SDK. Python 3 is also required to build a complete STO bundle.

```console
git clone --branch stodex --recurse-submodules https://github.com/stodex-network/Gibbed.Cryptic.git
cd Gibbed.Cryptic
dotnet build "Cryptic Studios.sln" --configuration Release
```

Building the tools does not give you a game-specific serializer. Game files and schema XML are not included in this repository. Bring XML or a validated JSON export for the client build you want to read, then follow [BUILDING.md](BUILDING.md) to generate the bundle and decode a resource.

The included resource mappings cover costume skeletons, costume geometry, ground region data, model headers, English client messages, UI fonts, color palettes and named colors. They were checked against build `ST.372.20260910a.24`. Other resources need matching schemas and mappings; copying a mapping or changing its build label is not enough.

## Branches

`stodex` is the default branch and the one to use for STOdex development. It contains the retail STO fixes and bundle tooling. `port/net10` keeps the .NET migration separate for a possible upstream contribution. See [NET10.md](NET10.md) for the branch relationship and older Framework builds.

## Credits and license

The original tools are by [Rick "gibbed"](https://github.com/gibbed). This fork builds on [Joel Day's 64-bit update](https://github.com/joelday/Gibbed.Cryptic/tree/64bit-update) and includes export error handling and adapted locator code from [NicholasJohn16's fork](https://github.com/NicholasJohn16/Gibbed.Cryptic/tree/claude/64-bit-compatibility-opv2yq). The STO compatibility fixes and STOdex tooling are maintained by Kurgh.

The original [license](LICENSE.txt) applies. Dependencies retain their own licenses.
