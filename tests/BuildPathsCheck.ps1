$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../build/BuildPaths.ps1')
$scratch = Join-Path $PSScriptRoot '../../target/test-scratch/build-paths'
$workspace = Join-Path $scratch 'workspace'
$project = Join-Path $workspace 'host'
$first = Join-Path $scratch 'references one'
$second = Join-Path $scratch 'elsewhere/references two'
New-Item -ItemType Directory -Force -Path $project | Out-Null
foreach ($root in @($first, $second)) {
    foreach ($file in @('Risk of Rain 2_Data/Managed/RoR2.dll', 'BepInEx/core/BepInEx.dll')) {
        $path = Join-Path $root $file
        New-Item -ItemType Directory -Force -Path (Split-Path $path) | Out-Null
        [IO.File]::WriteAllText([IO.Path]::GetFullPath($path), 'synthetic reference marker')
    }
}
function Must-Fail([scriptblock]$Action, [string]$Message) {
    $failed = $false
    try { & $Action | Out-Null } catch { $failed = $true }
    if (-not $failed) { throw $Message }
}
$a = Get-Ror2BuildPaths -ProjectRoot $project -RoR2Dir $first
$b = Get-Ror2BuildPaths -ProjectRoot $project -RoR2Dir $second
$expected = [IO.Path]::GetFullPath((Join-Path $workspace 'target'))
if ($a.Stage -ne $expected -or $b.Stage -ne $expected) { throw 'Reference location changed staging.' }
if ($a.References -eq $b.References) { throw 'Explicit reference selection was ignored.' }
Must-Fail { Get-Ror2BuildPaths -ProjectRoot $project } 'Missing reference selection was accepted.'
Must-Fail { Get-Ror2BuildPaths -ProjectRoot $project -RoR2Dir $project } 'Missing reference DLLs were accepted.'
Must-Fail { Get-Ror2BuildPaths -ProjectRoot $project -RoR2Dir $first -InstallDir $second } 'Staging escaped target.'
Must-Fail { Get-Ror2BuildPaths -ProjectRoot $project -RoR2Dir $first -InstallDir (Join-Path $expected '../target-other') } 'Sibling prefix bypassed target containment.'
$nested = [IO.Path]::GetFullPath((Join-Path $expected 'preview'))
if ((Get-Ror2BuildPaths -ProjectRoot $project -RoR2Dir $first -InstallDir $nested).Stage -ne $nested) { throw 'Target-local staging override failed.' }
Write-Output 'PASS: explicit references, reference-independent target, missing-input errors and target containment'
