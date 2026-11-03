# Developer install: builds the Poultry mod and copies it into <game>\Mods\Poultry.
# Needs Dungeonmans ModKit installed in the game first (https://github.com/chibranche/dungeonmans-mod-toolkit).
param([string]$GameDir = "E:\SteamLibrary\steamapps\common\Dungeonmans")
$ErrorActionPreference = "Stop"
if (-not (Test-Path (Join-Path $GameDir "ModKit\DungeonmansModKit.dll"))) { throw "ModKit isn't installed in $GameDir. Install dungeonmans-mod-toolkit first." }
if (Get-Process -Name Dungeonmans -ErrorAction SilentlyContinue) { throw "GAME RUNNING: close Dungeonmans first." }

dotnet build "$PSScriptRoot\DungeonmansPoultry.csproj" -c Release -p:GameDir="$GameDir" -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$out = Join-Path $PSScriptRoot "bin\Release\net8.0"
$dest = Join-Path $GameDir "Mods\Poultry"
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item (Join-Path $out "DungeonmansPoultry.dll"), (Join-Path $out "mod.json") $dest -Force
# Documentation pages shown by the "Docs" button in Mod Settings.
$docs = Join-Path $PSScriptRoot "docs"
if (Test-Path (Join-Path $dest "docs")) { Remove-Item (Join-Path $dest "docs") -Recurse -Force }
if (Test-Path $docs) { Copy-Item $docs $dest -Recurse }
Write-Host "Installed to $dest. Log: $GameDir\ModKit\modkit.log"
