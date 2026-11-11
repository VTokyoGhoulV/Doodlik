$ErrorActionPreference = 'Stop'
# Compiles the actual pure-C# scheduler, without launching Unity or touching saves.
$projectRoot = Split-Path -Parent $PSScriptRoot
Add-Type -TypeDefinition (Get-Content -LiteralPath (Join-Path $projectRoot 'Assets/PickupSpawnSchedule.cs') -Raw)
$coins = 0
$bonuses = 0
for ([uint32]$seed = 1; $seed -le 256; $seed++) {
    $schedule = [PickupSpawnSchedule]::new($seed)
    $duplicate = [PickupSpawnSchedule]::new($seed)
    $last = -3
    $lastCoin = 0
    $lastBonus = 0
    $lastKind = 0
    for ($index = 0; $index -lt 3000; $index++) {
        $kind = -1
        $sameKind = -1
        $spawned = $schedule.TryGetKind($index, [ref]$kind)
        $sameSpawned = $duplicate.TryGetKind($index, [ref]$sameKind)
        if ($spawned -ne $sameSpawned -or $kind -ne $sameKind) { throw 'Independent schedule/reset mismatch' }
        $ignoredKind = -1
        if ($schedule.TryGetKind($index, [ref]$ignoredKind)) { throw 'Duplicate platform accepted' }
        if (-not $spawned) { continue }
        if ($index - $last -lt 3 -or $index -lt 6) { throw 'Pickups clustered or appeared too early' }
        $last = $index
        if ($kind -eq 0) {
            if ($index - $lastCoin -lt 6 -or $index - $lastCoin -gt 15) { throw 'Coin spacing outside 6-15 platforms' }
            $lastCoin = $index
            $coins++
        } else {
            if ($index - $lastBonus -lt 18 -or $index - $lastBonus -gt 28) { throw 'Bonus spacing outside 18-28 platforms' }
            if ($lastKind -ne 0 -and $kind -ne ($lastKind % 3 + 1)) { throw 'Bonus rotation mismatch' }
            $lastKind = $kind
            $lastBonus = $index
            $bonuses++
        }
    }
}
Write-Output "RARE_PICKUP_SCHEDULE_PASS: 768000 platforms / 256 independent attempts; $coins coins, $bonuses bonuses; spacing, duplicate and reset checks passed."
