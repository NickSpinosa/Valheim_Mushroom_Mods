# Verifies SpawnOverrideDecision against the logout/bed truth table.
# Exit 1 on any mismatch.
#   powershell -NoProfile -ExecutionPolicy Bypass -File "Separate Spawns/scripts/verify-spawn-override.ps1"

$ErrorActionPreference = "Stop"
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not (Test-Path (Join-Path $PSScriptRoot "..\SeparateSpawns\SpawnOverrideDecision.cs"))) {
    $root = Split-Path $PSScriptRoot -Parent
}

$decisionPath = Join-Path $PSScriptRoot "..\SeparateSpawns\SpawnOverrideDecision.cs"
$decisionPath = (Resolve-Path $decisionPath).Path

# Mirror of the OLD buggy guard (what SpawnPatches used to do).
function Old-ShouldLeaveVanillaAlone([bool]$usedLogoutPoint, [bool]$haveCustomSpawnPoint) {
    return $usedLogoutPoint -or $haveCustomSpawnPoint
}

Add-Type -TypeDefinition (Get-Content -Raw $decisionPath) -Language CSharp

$cases = @(
    @{ Name = "logout wait (no bed)"; AfterDeath = $false; HaveLogout = $true;  UsedLogout = $false; HaveBed = $false; ExpectLeave = $true  }
    @{ Name = "logout success";       AfterDeath = $false; HaveLogout = $false; UsedLogout = $true;  HaveBed = $false; ExpectLeave = $true  }
    @{ Name = "bed wait/success";     AfterDeath = $false; HaveLogout = $false; UsedLogout = $false; HaveBed = $true;  ExpectLeave = $true  }
    @{ Name = "first join / no bed";  AfterDeath = $false; HaveLogout = $false; UsedLogout = $false; HaveBed = $false; ExpectLeave = $false }
    @{ Name = "death no bed";         AfterDeath = $true;  HaveLogout = $false; UsedLogout = $false; HaveBed = $false; ExpectLeave = $false }
    @{ Name = "death with bed";       AfterDeath = $true;  HaveLogout = $false; UsedLogout = $false; HaveBed = $true;  ExpectLeave = $true  }
    @{ Name = "death ignores logout"; AfterDeath = $true;  HaveLogout = $true;  UsedLogout = $false; HaveBed = $false; ExpectLeave = $false }
    @{ Name = "bed destroy + login";  AfterDeath = $false; HaveLogout = $true;  UsedLogout = $false; HaveBed = $false; ExpectLeave = $true  }
)

$failed = 0
$oldBugReproduced = $false

Write-Host "=== Spawn override truth table ==="
foreach ($c in $cases) {
    $actual = [SeparateSpawns.SpawnOverrideDecision]::ShouldLeaveVanillaAlone(
        $c.AfterDeath, $c.HaveLogout, $c.UsedLogout, $c.HaveBed)
    $old = Old-ShouldLeaveVanillaAlone $c.UsedLogout $c.HaveBed

    $ok = ($actual -eq $c.ExpectLeave)
    $status = if ($ok) { "PASS" } else { "FAIL"; $failed++ }
    $oldNote = ""
    if ($old -ne $c.ExpectLeave) {
        $oldVerb = if ($old) { "leave" } else { "override" }
        $oldNote = "  [old bug would $oldVerb]"
        if ($c.Name -eq "logout wait (no bed)" -or $c.Name -eq "bed destroy + login") {
            $oldBugReproduced = $true
        }
    }
    Write-Host ("{0}: {1} (leave={2}, expected={3}){4}" -f $status, $c.Name, $actual, $c.ExpectLeave, $oldNote)
}

if (-not $oldBugReproduced) {
    Write-Host "FAIL: expected old buggy guard to disagree on logout-wait cases (loop not red-capable)."
    exit 1
}

if ($failed -gt 0) {
    Write-Host "FAILED $failed case(s)."
    exit 1
}

Write-Host "All cases passed. Old logout-wait bug remains detectable against this table."
exit 0
