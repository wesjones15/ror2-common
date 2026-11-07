# Shared reference/staging resolution. References are caller-selected; output is workspace-local.
function Get-Ror2BuildPaths {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [string]$RoR2Dir,
        [string]$InstallDir
    )
    if ([string]::IsNullOrWhiteSpace($RoR2Dir)) {
        throw 'Supply -RoR2Dir explicitly with the directory containing the RoR2/BepInEx compilation references.'
    }
    $referenceRoot = (Resolve-Path -LiteralPath $RoR2Dir -ErrorAction Stop).ProviderPath
    foreach ($reference in @('Risk of Rain 2_Data/Managed/RoR2.dll', 'BepInEx/core/BepInEx.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $referenceRoot $reference) -PathType Leaf)) {
            throw "Missing compilation reference: $reference"
        }
    }
    $targetRoot = [IO.Path]::GetFullPath((Join-Path $ProjectRoot '../target'))
    $stageRoot = if ([string]::IsNullOrWhiteSpace($InstallDir)) { $targetRoot } else { [IO.Path]::GetFullPath($InstallDir) }
    if ($stageRoot -ne $targetRoot -and -not $stageRoot.StartsWith($targetRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Staging must stay inside the workspace target directory.'
    }
    if ($stageRoot -eq $referenceRoot -or $stageRoot.StartsWith($referenceRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Staging must be separate from the compilation reference directory.'
    }
    [pscustomobject]@{ References = $referenceRoot; Target = $targetRoot; Stage = $stageRoot }
}
