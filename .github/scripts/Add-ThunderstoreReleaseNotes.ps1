<#
.SYNOPSIS
  Appends a table of Thunderstore package links to a GitHub release's notes.

.DESCRIPTION
  Reads the publish-result.json that Publish-ThunderstorePackages.ps1 writes
  and adds an "On Thunderstore" section to the release body, one row per mod,
  saying whether this release uploaded it or it was already there.

  GitHub releases have no comments, so the notes are the place. The section is
  fenced by hidden markers and replaced on re-runs rather than appended again.

  -PrintOnly writes the section to stdout instead of touching the release, for
  checking the output locally.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ResultPath,
    [string] $ReleaseTag = '',
    [switch] $PrintOnly
)

$ErrorActionPreference = 'Stop'
if (-not $PrintOnly -and -not $ReleaseTag) { throw 'ReleaseTag is required unless -PrintOnly is set.' }

$result = Get-Content -Raw -Path $ResultPath -Encoding UTF8 | ConvertFrom-Json
$packages = @($result.packages | ForEach-Object { $_ } | Sort-Object name)
if ($packages.Count -eq 0) { throw "No packages recorded in $ResultPath." }

$begin = '<!-- thunderstore-links:begin -->'
$end   = '<!-- thunderstore-links:end -->'

$rows = foreach ($pkg in $packages) {
    $note = switch ($pkg.status) {
        'published'         { 'uploaded by this release' }
        'already-published' { 'unchanged since an earlier release' }
        default             { $pkg.status }
    }
    "| [$($pkg.name)]($($pkg.url)) | $($pkg.version) | $note |"
}

$section = @(
    $begin,
    '## On Thunderstore',
    '',
    "Each mod is its own package under the **$($result.namespace)** team. A mod manager",
    'installs them one at a time, with Mushroom Sync pulled in where required.',
    '',
    '| Mod | Version | This release |',
    '|---|---|---|'
) + $rows + @($end)
$sectionText = $section -join "`n"

if ($PrintOnly) {
    Write-Output $sectionText
    return
}

$body = gh release view $ReleaseTag --json body --jq .body
if ($LASTEXITCODE -ne 0) { throw "Could not read release '$ReleaseTag'." }
$body = ($body -join "`n").TrimEnd()

# Replace a previous section from an earlier run, otherwise append.
$pattern = [regex]::Escape($begin) + '[\s\S]*?' + [regex]::Escape($end)
if ($body -match $pattern) {
    $newBody = [regex]::Replace($body, $pattern, { param($m) $sectionText })
} else {
    $newBody = if ($body) { "$body`n`n$sectionText" } else { $sectionText }
}

$notesFile = Join-Path ([IO.Path]::GetTempPath()) ("release-notes-" + [IO.Path]::GetRandomFileName() + '.md')
[IO.File]::WriteAllText($notesFile, $newBody + "`n", (New-Object System.Text.UTF8Encoding $false))
try {
    gh release edit $ReleaseTag --notes-file $notesFile
    if ($LASTEXITCODE -ne 0) { throw "Could not update the notes of release '$ReleaseTag'." }
} finally {
    Remove-Item $notesFile -ErrorAction SilentlyContinue
}

Write-Host "Added Thunderstore links for $($packages.Count) package(s) to release $ReleaseTag."
