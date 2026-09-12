# Source-level checks for the Super Mist Torch placement/remove mitigations.
# Full signal is in-game; this catches regressions that drop a mitigation.
# Usage: powershell -NoProfile -File haldor-expansion/tools/verify-mist-torch-fix.ps1

$ErrorActionPreference = 'Stop'
$modRoot = Split-Path $PSScriptRoot -Parent
$src = Join-Path $modRoot 'src\SuperMistTorch.cs'
$patches = Join-Path $modRoot 'src\Patches.cs'
$text = Get-Content -Raw $src
$pt = Get-Content -Raw $patches
$failed = @()

function Expect([string]$label, [bool]$ok) {
    if ($ok) { Write-Host "OK  $label" }
    else { Write-Host "FAIL $label"; $script:failed += $label }
}

Expect 'does not assign VisualScale onto clone root localScale' (
    $text -notmatch 'clone\.transform\.localScale\s*=\s*source\.transform\.localScale\s*\*\s*VisualScale'
)
Expect 'scales children by VisualScale' (
    $text -match 'child\.localScale\s*\*=\s*VisualScale'
)
Expect 'adds Rigidbody for item half' ($text -match 'AddComponent<\s*Rigidbody\s*>')
Expect 'moves solid colliders to item layer' ($text -match 'NameToLayer\(\s*"item"\s*\)')
Expect 'StripItemDropAfterMakePiece exists' ($text -match 'StripItemDropAfterMakePiece')
Expect 'MakePiece postfix calls strip' (
    $pt -match 'ItemDropMakePiecePatch' -and $pt -match 'StripItemDropAfterMakePiece'
)
Expect 'strip refuses to destroy the shared prefab' (
    $text -match 'drop\.gameObject\s*==\s*_prefab'
)
Expect 'comment mentions SetQuality / GetScale trap' (
    $text -match 'SetQuality' -and $text -match 'GetScale'
)
Expect 'comment mentions feast / canRemovePieces trap' (
    $text -match 'canRemovePieces' -or $text -match 'IsPiece'
)

if ($failed.Count -gt 0) {
    Write-Host "`n$($failed.Count) check(s) failed."
    exit 1
}
Write-Host "`nAll SuperMistTorch mitigation checks passed."
exit 0
