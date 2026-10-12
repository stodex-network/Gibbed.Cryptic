param([string]$Bundle = "$PSScriptRoot/../bin-net10")
$ErrorActionPreference = 'Stop'
$Bundle = (Resolve-Path $Bundle).Path
dotnet run --project "$PSScriptRoot/../tests/StructureLists/StructureLists.csproj" -c Release "-p:GibbedBundle=$Bundle" -- $Bundle
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
