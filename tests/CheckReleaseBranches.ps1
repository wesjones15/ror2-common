param(
    [Parameter(Mandatory=$true)][string]$HostDir,
    [Parameter(Mandatory=$true)][string]$NativeDir,
    [string]$CommonDir = (Join-Path $PSScriptRoot '..'),
    [ValidateSet('modern/1.5.0','main')][string]$HostBranch = 'modern/1.5.0',
    [ValidateSet('modern/1.5.0','main')][string]$NativeBranch = 'modern/1.5.0'
)
$ErrorActionPreference = 'Stop'
foreach ($branchCheck in @(@($HostDir,$HostBranch), @($NativeDir,$NativeBranch), @($CommonDir,'modern/1.5.0'))) {
    $actualBranch = (& git -C $branchCheck[0] branch --show-current)
    if ($LASTEXITCODE -ne 0 -or $actualBranch -ne $branchCheck[1]) {
        throw ("Refusing to stage {0} from '{1}'; expected '{2}'. Use the primary modern/main checkout." -f $branchCheck[0], $actualBranch, $branchCheck[1])
    }
}
Write-Host 'PASS: staging sources are on their required modern/main branches'
