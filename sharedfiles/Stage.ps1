# Stages the wesleyasaurus-SharedFiles package's own files (two placeholder READMEs) under a plugins folder
# (target\BepInEx\plugins for a local deploy). Never touches anything else in that folder: the player's discs and
# extracted assets in an installed copy stay where they are.
#
#   powershell -File Stage.ps1 -PluginsDir <...\BepInEx\plugins>
param([Parameter(Mandatory)][string]$PluginsDir)
$ErrorActionPreference = "Stop"
$package = Join-Path $PluginsDir "wesleyasaurus-SharedFiles"
foreach ($file in @("put_games_here\README.txt", "assets\README.txt")) {
    $target = Join-Path $package $file
    New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $target -Force
}
# The old shared-library pointer; the mods no longer read it.
Remove-Item -LiteralPath (Join-Path $PluginsDir "gamecube_library.txt") -ErrorAction SilentlyContinue
Write-Host "staged $package"
