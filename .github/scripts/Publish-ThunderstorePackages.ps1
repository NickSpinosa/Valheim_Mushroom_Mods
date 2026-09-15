<#
.SYNOPSIS
  Uploads the zips built by Build-ThunderstorePackages.ps1 to Thunderstore.

.DESCRIPTION
  Reads <PackagesDir>/packages.json and publishes each package with the
  Thunderstore CLI (tcli), in dependency order so a mod never references a
  Mushroom Sync version that is not on Thunderstore yet.

  A version that Thunderstore already has is skipped, not failed: a release
  rebuilds every mod, but only the ones whose BepInPlugin version was bumped
  have anything new to upload. Thunderstore rejects re-uploads of an existing
  version, so this is what lets one release carry a change to a single mod.

  The token comes from the TCLI_AUTH_TOKEN environment variable, which is how
  tcli itself reads it, so it never appears on a command line.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PackagesDir,
    [string] $Community = 'valheim',
    [string] $Repository = 'https://thunderstore.io',
    # Build every config and print every command without uploading anything.
    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'
if (-not $DryRun -and -not $env:TCLI_AUTH_TOKEN) { throw 'TCLI_AUTH_TOKEN is not set.' }

$indexPath = Join-Path $PackagesDir 'packages.json'
if (-not (Test-Path $indexPath)) { throw "No packages.json in $PackagesDir; run Build-ThunderstorePackages.ps1 first." }
$index = Get-Content -Raw -Path $indexPath -Encoding UTF8 | ConvertFrom-Json
$namespace = $index.namespace
$packages = @($index.packages)

# --- Dependency order --------------------------------------------------------
$ordered = @()
$remaining = [System.Collections.ArrayList]@($packages)
while ($remaining.Count -gt 0) {
    $progress = $false
    foreach ($pkg in @($remaining)) {
        $unmet = @($pkg.internalDependencies | Where-Object { $_ -notin ($ordered | ForEach-Object { $_.name }) })
        if ($unmet.Count -eq 0) {
            $ordered += $pkg
            $remaining.Remove($pkg)
            $progress = $true
        }
    }
    if (-not $progress) { throw "Dependency cycle among: $(($remaining | ForEach-Object { $_.name }) -join ', ')" }
}

# --- Helpers -----------------------------------------------------------------
function Test-Published([string] $ns, [string] $name, [string] $version) {
    $url = "$Repository/api/experimental/package/$ns/$name/$version/"
    try {
        $null = Invoke-WebRequest -Uri $url -Method Get -UseBasicParsing
        return $true
    } catch {
        $status = $null
        if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
        if ($status -eq 404) { return $false }
        throw "Could not check $url ($status): $($_.Exception.Message)"
    }
}

function ConvertTo-TomlString([string] $value) {
    return '"' + $value.Replace('\', '\\').Replace('"', '\"') + '"'
}

# tcli publish --file still reads its project config for the namespace, name,
# version, repository, community and categories, so one is generated per
# package. The [build] section is required by its config validation even
# though --file means nothing is built.
function Write-TcliConfig($pkg, [string] $path) {
    $cats = ($pkg.categories | ForEach-Object { ConvertTo-TomlString $_ }) -join ', '
    $lines = @(
        '[config]',
        'schemaVersion = "0.0.1"',
        '',
        '[package]',
        "namespace = $(ConvertTo-TomlString $namespace)",
        "name = $(ConvertTo-TomlString $pkg.name)",
        "versionNumber = $(ConvertTo-TomlString $pkg.version)",
        "description = $(ConvertTo-TomlString $pkg.description)",
        "websiteUrl = $(ConvertTo-TomlString $pkg.websiteUrl)",
        'containsNsfwContent = false',
        '[package.dependencies]',
        '',
        '[build]',
        'icon = "./icon.png"',
        'readme = "./README.md"',
        'outdir = "./build"',
        '',
        '[publish]',
        "repository = $(ConvertTo-TomlString $Repository)",
        "communities = [ $(ConvertTo-TomlString $Community) ]",
        '[publish.categories]',
        "$Community = [ $cats ]"
    )
    [IO.File]::WriteAllText($path, ($lines -join "`n") + "`n", (New-Object System.Text.UTF8Encoding $false))
}

# --- Publish -----------------------------------------------------------------
$configRoot = Join-Path ([IO.Path]::GetTempPath()) ("tcli-" + [IO.Path]::GetRandomFileName())
New-Item -ItemType Directory -Force -Path $configRoot | Out-Null

$published = @(); $skipped = @(); $results = @()
foreach ($pkg in $ordered) {
    $id = "$namespace-$($pkg.name)-$($pkg.version)"
    $zip = Join-Path $PackagesDir $pkg.zip
    if (-not (Test-Path $zip)) { throw "Missing $zip" }

    # Recorded whatever happens, so the release notes can link every package,
    # not only the ones this run uploaded.
    $result = [ordered]@{
        name    = $pkg.name
        version = $pkg.version
        url     = "$Repository/c/$Community/p/$namespace/$($pkg.name)/"
        status  = 'dry-run'
    }
    $results += $result

    if (-not $DryRun -and (Test-Published $namespace $pkg.name $pkg.version)) {
        Write-Host "$id is already on Thunderstore; skipping."
        $skipped += $id
        $result.status = 'already-published'
        continue
    }

    $config = Join-Path $configRoot "$($pkg.name).toml"
    Write-TcliConfig $pkg $config

    Write-Host "::group::Publishing $id"
    Write-Host "tcli publish --file $zip --config-path $config"
    if ($DryRun) {
        Get-Content $config | Write-Host
        Write-Host '::endgroup::'
        continue
    }

    & tcli publish --file $zip --config-path $config
    if ($LASTEXITCODE -ne 0) { throw "tcli publish failed for $id" }
    Write-Host '::endgroup::'
    $published += $id
    $result.status = 'published'
}

# Read by Add-ThunderstoreReleaseNotes.ps1.
$summary = [ordered]@{ namespace = $namespace; community = $Community; packages = $results }
[IO.File]::WriteAllText((Join-Path $PackagesDir 'publish-result.json'), ($summary | ConvertTo-Json -Depth 4), (New-Object System.Text.UTF8Encoding $false))

Write-Host ''
Write-Host "Published: $(if ($published) { $published -join ', ' } else { 'nothing' })"
Write-Host "Skipped (already on Thunderstore): $(if ($skipped) { $skipped -join ', ' } else { 'nothing' })"
if (-not $DryRun -and $published.Count -eq 0) {
    Write-Host '::notice::No Thunderstore package had a new version. Bump the BepInPlugin version of a mod to publish it.'
}
