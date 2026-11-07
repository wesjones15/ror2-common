param([Parameter(Mandatory = $true)][string]$RoR2Dir)
$ErrorActionPreference = 'Stop'
$cecilPath = [IO.Path]::GetFullPath((Join-Path $RoR2Dir 'BepInEx/core/Mono.Cecil.dll'))
$null = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($cecilPath))
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $RoR2Dir 'Risk of Rain 2_Data/Managed/RoR2.dll'))
try {
    $type = $game.MainModule.Types | Where-Object FullName -eq 'RoR2.OverlapAttack'
    if (-not $type) { throw 'Missing RoR2.OverlapAttack' }
    foreach ($spec in @(
        @{ Name = 'Fire'; Parameters = 'System.Collections.Generic.List`1<RoR2.HurtBox>'; Return = 'System.Boolean' },
        @{ Name = 'Reset'; Parameters = ''; Return = 'System.Void' },
        @{ Name = 'ResetIgnoredHealthComponents'; Parameters = ''; Return = 'System.Void' }
    )) {
        $methods = @($type.Methods | Where-Object {
            $_.Name -eq $spec.Name -and
            (($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ',') -eq $spec.Parameters
        })
        if ($methods.Count -ne 1 -or -not $methods[0].IsPublic -or $methods[0].IsStatic -or
            $methods[0].ReturnType.FullName -ne $spec.Return) { throw "Melee hook signature changed: $($spec.Name)" }
    }
    # The native victim cache must be reset on the same boundaries as RoR2's victims.
    foreach ($name in @('Reset', 'ResetIgnoredHealthComponents')) {
        $method = $type.Methods | Where-Object Name -eq $name
        # Publicized reference assemblies (the RiskOfRain2.GameLibs NuGet package CI builds against) are stripped:
        # every body is just "ldnull; throw". The signatures above are still checked; the body cannot be.
        if (@($method.Body.Instructions).Count -le 2) { Write-Warning "Stripped reference assembly: cannot check the $name ignore-list lifecycle."; continue }
        if (-not @($method.Body.Instructions | Where-Object {
            $_.OpCode.Name -eq 'ldfld' -and $_.Operand.Name -eq 'ignoredHealthComponentList'
        }).Count) { throw "Melee ignore-list lifecycle changed: $name" }
    }
    Write-Host 'PASS: mob melee Fire/reset hook signatures and victim-list lifecycle.'
} finally { $game.Dispose() }
