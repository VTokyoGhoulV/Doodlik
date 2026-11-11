param(
    [Parameter(Mandatory = $true)][string]$AssemblyPath,
    [ValidateSet('Cheats', 'Normal')][string]$ExpectedVariant = 'Cheats',
    [string]$CecilPath = 'C:/Program Files/Unity/Hub/Editor/6000.5.2f1/Editor/Data/Managed/Unity.Cecil.dll'
)

$ErrorActionPreference = 'Stop'
# Inspect metadata/IL only: never load Assembly-CSharp into the CLR or run it.
$resolvedAssembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
$resolvedCecil = (Resolve-Path -LiteralPath $CecilPath).Path
[void][Reflection.Assembly]::LoadFrom($resolvedCecil)
$definition = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($resolvedAssembly)
$checks = 0

function Require([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
    $script:checks++
}

function Find-Type([string]$name) {
    $value = $definition.MainModule.Types | Where-Object { $_.FullName -eq $name } | Select-Object -First 1
    Require ($null -ne $value) ('Missing runtime type: ' + $name)
    return $value
}

function Find-Method($type, [string]$name) {
    $value = $type.Methods | Where-Object { $_.Name -eq $name } | Select-Object -First 1
    Require ($null -ne $value) ('Missing method: ' + $type.FullName + '.' + $name)
    return $value
}

function Is-ConstantReturn($method, [string]$opcode) {
    if (-not $method.HasBody) { return $false }
    $instructions = @($method.Body.Instructions | Where-Object { $_.OpCode.Name -ne 'nop' })
    return $instructions.Count -eq 2 -and $instructions[0].OpCode.Name -eq $opcode -and
        $instructions[1].OpCode.Name -eq 'ret'
}

try {
    $admin = Find-Type 'AdminTools'
    $runtime = Find-Type 'AdminRuntime'
    $progress = Find-Type 'PlayerProgress'
    $powers = Find-Type 'RunPowerups'
    $catalog = Find-Type 'ChallengeCatalog'
    $available = Find-Method $admin 'get_IsAvailable'
    $begin = Find-Method $progress 'BeginAdminSandbox'
    $setMovement = Find-Method $runtime 'SetMovementMultiplier'
    $grant = Find-Method $runtime 'GrantPower'
    if ($ExpectedVariant -eq 'Cheats') {
        Require (Is-ConstantReturn $available 'ldc.i4.1') 'Cheat panel is not compiled as available.'
        foreach ($methodName in @('Update', 'BuildInterface', 'Open', 'EndSandbox', 'ChangeCoins')) {
            [void](Find-Method $admin $methodName)
        }
        Require ($begin.HasBody -and $begin.Body.Instructions.Count -gt 5) 'Sandbox implementation is missing.'
        Require (-not (Is-ConstantReturn $setMovement 'ldc.i4.0')) 'Movement setter is an inert release stub.'
        Require (-not (Is-ConstantReturn $grant 'ldc.i4.0')) 'Power command is an inert release stub.'
        [void](Find-Method $powers 'GrantAdminPower')
        [void](Find-Method $powers 'ClearAdminPowers')
        foreach ($methodName in @('SetJumpMultiplier', 'SetGravityMultiplier', 'SetBushMultiplier',
                'SetGameSpeed', 'SetBuffDuration', 'ClearPowers', 'JumpNow', 'ResetTuning')) {
            [void](Find-Method $runtime $methodName)
        }
    }
    else {
        Require (Is-ConstantReturn $available 'ldc.i4.0') 'Ordinary release exposes cheats.'
        Require (@($admin.Methods | Where-Object { $_.Name -eq 'Update' }).Count -eq 0) 'Ordinary release contains the admin UI Update.'
        Require (Is-ConstantReturn $setMovement 'ldc.i4.0') 'Ordinary movement setter is not inert.'
        Require (Is-ConstantReturn $grant 'ldc.i4.0') 'Ordinary power command is not inert.'
        Require (@($powers.Methods | Where-Object { $_.Name -eq 'GrantAdminPower' }).Count -eq 0) 'Ordinary release contains the power grant implementation.'
    }
    $initializer = Find-Method $catalog '.cctor'
    $questConstructors = @($initializer.Body.Instructions | Where-Object {
        $_.OpCode.Name -eq 'newobj' -and $_.Operand.DeclaringType.FullName -eq 'ChallengeDefinition'
    })
    Require ($questConstructors.Count -eq 55) ('Unexpected challenge catalog size: ' + $questConstructors.Count)
    foreach ($typeName in @('RunChallenges', 'ChallengeSession', 'RunStatisticsEntry', 'RunJournal',
            'PaperRocketFlight', 'ChargingBumblebee', 'Player')) { [void](Find-Type $typeName) }
    Require (@($definition.MainModule.AssemblyReferences | Where-Object { $_.Name -like 'UnityEditor*' }).Count -eq 0) 'Player references UnityEditor.'
    $unexpectedTypes = @($definition.MainModule.Types | Where-Object { $_.Name -match 'Smoke|Validation|Codex' })
    Require ($unexpectedTypes.Count -eq 0) ('QA driver leaked into production: ' + ($unexpectedTypes.Name -join ', '))
    Write-Output ('CHEAT_ASSEMBLY_AUDIT_PASS variant={0} checks={1} catalog=55 types={2}' -f $ExpectedVariant, $checks, $definition.MainModule.Types.Count)
    Write-Output ('assembly=' + $resolvedAssembly)
    Write-Output ('sha256=' + (Get-FileHash -LiteralPath $resolvedAssembly -Algorithm SHA256).Hash)
}
finally { $definition.Dispose() }
