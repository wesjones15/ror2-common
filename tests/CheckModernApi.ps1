param([Parameter(Mandatory = $true)][string]$RoR2Dir)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $RoR2Dir 'BepInEx/core/Mono.Cecil.dll')
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $RoR2Dir 'Risk of Rain 2_Data/Managed/RoR2.dll'))
try {
    function Get-GameType([string]$name) {
        $type = $assembly.MainModule.Types | Where-Object FullName -eq $name
        if (-not $type) { throw "Missing type: $name" }
        return $type
    }
    function Get-GameMethod([string]$type, [string]$name, [string[]]$parameters) {
        $method = (Get-GameType $type).Methods | Where-Object {
            $_.Name -eq $name -and (($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ',') -eq ($parameters -join ',')
        }
        if (@($method).Count -ne 1) { throw "Missing/ambiguous hook: $type.$name($parameters)" }
        return $method
    }
    $null = Get-GameMethod 'RoR2.Skills.SkillDef' 'OnFixedUpdate' @('RoR2.GenericSkill','System.Single')
    $null = Get-GameMethod 'RoR2.HealthComponent' 'Heal' @('System.Single','RoR2.ProcChainMask','System.Boolean')
    $null = Get-GameMethod 'RoR2.SkillLocator' 'ApplyAmmoPack' @()
    $null = Get-GameMethod 'RoR2.CharacterBody' 'GetNotMoving' @()
    $null = Get-GameMethod 'RoR2.Glyphs' 'GetGlyphString' @('RoR2.UI.MPEventSystem','System.String','Rewired.AxisRange','RoR2.UI.MPEventSystem/InputSource','System.Boolean')
    $null = Get-GameMethod 'RoR2.InputBindingDisplayController' 'Refresh' @()
    $null = Get-GameMethod 'RoR2.UI.AllyCardManager' 'PopulateCharacterDataSet' @('RoR2.UI.AllyCardManager/CharacterDataSet')
    $null = Get-GameMethod 'EntityStates.Headstompers.HeadstompersIdle' 'FixedUpdateAuthority' @()
    $method = Get-GameMethod 'RoR2.HealthComponent' 'TakeDamageProcess' @('RoR2.DamageInfo')
    $matches = @($method.Body.Instructions | Where-Object {
        $_.OpCode.Name -eq 'callvirt' -and $_.Operand.FullName -eq 'UnityEngine.Vector3 RoR2.CharacterBody::get_corePosition()' -and
        $_.Next.OpCode.Name -eq 'ldloc.0' -and $_.Next.Next.Operand.Name -eq 'damageInfo' -and
        $_.Next.Next.Next.Operand.FullName -eq 'UnityEngine.Vector3 RoR2.DamageInfo::position' -and
        $_.Next.Next.Next.Next.Operand.FullName -eq 'UnityEngine.Vector3 UnityEngine.Vector3::op_Subtraction(UnityEngine.Vector3,UnityEngine.Vector3)'
    })
    if ($matches.Count -ne 1) { throw 'Minion attacker-position IL pattern changed.' }
    Write-Host 'PASS: modern skill and runtime-hook signatures; unique minion damage IL match.'
} finally { $assembly.Dispose() }
