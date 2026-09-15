<#
.SYNOPSIS
  Builds one Thunderstore package zip per mod from the built DLLs.

.DESCRIPTION
  Every directory under thunderstore/ that holds a package.json is a package.
  The version comes from the DLL's BepInPlugin attribute (read by
  .github/tools/PluginInfo), so the number Thunderstore shows is the one BepInEx
  logs. Dependencies between the repo's own mods are derived from their
  BepInDependency attributes; BepInExPack is pinned to whatever Thunderstore
  currently lists as latest, the same way the build action fetches it.

  Writes <OutDir>/<Namespace>-<Name>-<Version>.zip for each package and
  <OutDir>/packages.json, the index Publish-ThunderstorePackages.ps1 consumes.

  Runs under Windows PowerShell 5.1 and PowerShell 7.
#>
[CmdletBinding()]
param(
    # Directory of built plugin DLLs (the build action's release-artifacts/).
    [Parameter(Mandatory)] [string] $ArtifactsDir,
    [Parameter(Mandatory)] [string] $OutDir,
    # Thunderstore team the packages publish under. Empty falls back to a
    # placeholder so CI still exercises packaging before the team exists.
    [string] $Namespace = '',
    [string] $PackagesDir = '',
    [string] $WebsiteUrl = 'https://github.com/NickSpinosa/Valheim_Mushroom_Mods',
    # Leave empty to resolve the current BepInExPack_Valheim version from Thunderstore.
    [string] $BepInExPackVersion = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $PackagesDir) { $PackagesDir = Join-Path $repoRoot 'thunderstore' }
if (-not $Namespace) {
    $Namespace = 'MushroomMods'
    Write-Warning "No Thunderstore namespace given; packaging under placeholder '$Namespace'. Set the THUNDERSTORE_NAMESPACE repository variable before publishing."
}
if ($Namespace -notmatch '^[A-Za-z0-9_]{1,64}$') { throw "Namespace '$Namespace' is not a valid Thunderstore team name (letters, digits, underscore)." }

# --- Plugin metadata from the DLLs -------------------------------------------
$helper = Join-Path $repoRoot '.github\tools\PluginInfo'
$pluginJson = & dotnet run --project $helper -c Release -- (Resolve-Path $ArtifactsDir).Path
if ($LASTEXITCODE -ne 0) { throw 'PluginInfo helper failed; see output above.' }
# ForEach-Object unrolls the array Windows PowerShell otherwise hands back as one object.
$plugins = @(($pluginJson -join "`n") | ConvertFrom-Json | ForEach-Object { $_ })
if ($plugins.Count -eq 0) { throw "No BepInEx plugins found in $ArtifactsDir." }

# --- BepInExPack version -----------------------------------------------------
if (-not $BepInExPackVersion) {
    $pack = Invoke-RestMethod -Uri 'https://thunderstore.io/api/experimental/package/denikson/BepInExPack_Valheim/'
    $BepInExPackVersion = $pack.latest.version_number
}
if ($BepInExPackVersion -notmatch '^\d+\.\d+\.\d+$') { throw "BepInExPack version '$BepInExPackVersion' is not Major.Minor.Patch." }
Write-Host "Depending on denikson-BepInExPack_Valheim-$BepInExPackVersion"

# --- Package definitions -----------------------------------------------------
function Read-PngSize([string] $path) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $sig = [byte[]](0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)
    for ($i = 0; $i -lt 8; $i++) { if ($bytes[$i] -ne $sig[$i]) { throw "$path is not a PNG." } }
    # IHDR: width and height are big-endian at offsets 16 and 20.
    $w = ([int]$bytes[16] -shl 24) -bor ([int]$bytes[17] -shl 16) -bor ([int]$bytes[18] -shl 8) -bor [int]$bytes[19]
    $h = ([int]$bytes[20] -shl 24) -bor ([int]$bytes[21] -shl 16) -bor ([int]$bytes[22] -shl 8) -bor [int]$bytes[23]
    return @($w, $h)
}

$packages = @()
foreach ($dir in (Get-ChildItem -Path $PackagesDir -Directory | Sort-Object Name)) {
    $defPath = Join-Path $dir.FullName 'package.json'
    if (-not (Test-Path $defPath)) { continue }
    $def = Get-Content -Raw -Path $defPath -Encoding UTF8 | ConvertFrom-Json

    $name = $dir.Name
    if ($name -notmatch '^[A-Za-z0-9_]{1,128}$') { throw "Package directory '$name' is not a valid Thunderstore package name (letters, digits, underscore)." }
    if (-not $def.dll) { throw "$defPath has no 'dll'." }
    if (-not $def.description) { throw "$defPath has no 'description'." }
    if ($def.description.Length -gt 250) { throw "$name description is $($def.description.Length) characters; Thunderstore allows 250." }
    if (-not $def.readme) { throw "$defPath has no 'readme'." }
    if (-not $def.categories -or @($def.categories).Count -eq 0) { throw "$defPath has no 'categories'." }

    $icon = Join-Path $dir.FullName 'icon.png'
    if (-not (Test-Path $icon)) { throw "$name has no icon.png." }
    $size = Read-PngSize $icon
    if ($size[0] -ne 256 -or $size[1] -ne 256) { throw "$name icon.png is $($size[0])x$($size[1]); Thunderstore requires 256x256." }

    $readme = Join-Path $repoRoot $def.readme
    if (-not (Test-Path $readme)) { throw "$name readme '$($def.readme)' not found." }

    $dll = Join-Path (Resolve-Path $ArtifactsDir).Path $def.dll
    if (-not (Test-Path $dll)) { throw "$name expects '$($def.dll)' in $ArtifactsDir but it was not built." }

    $plugin = $plugins | Where-Object { $_.file -ieq $def.dll } | Select-Object -First 1
    if (-not $plugin) { throw "$($def.dll) carries no BepInPlugin attribute." }
    if ($plugin.version -notmatch '^\d+\.\d+\.\d+$') { throw "${name}: BepInPlugin version '$($plugin.version)' is not Major.Minor.Patch, which Thunderstore requires." }

    $packages += [pscustomobject]@{
        name        = $name
        dll         = $def.dll
        dllPath     = $dll
        guid        = $plugin.guid
        version     = $plugin.version
        description = $def.description
        readme      = $readme
        icon        = $icon
        categories  = @($def.categories)
        # Dependencies outside this repo, already in team-Package-1.2.3 form.
        extraDeps   = @(if ($def.dependencies) { $def.dependencies } else { @() })
        pluginDeps  = @($plugin.dependencies)
    }
}
if ($packages.Count -eq 0) { throw "No package definitions under $PackagesDir." }

# Every shipped plugin should have a package, or a release silently leaves one
# mod off Thunderstore.
foreach ($plugin in $plugins) {
    if (-not ($packages | Where-Object { $_.dll -ieq $plugin.file })) {
        throw "Built plugin $($plugin.file) has no thunderstore/<Name>/package.json. Add one, or exclude the DLL from the build."
    }
}

# --- Resolve dependencies ----------------------------------------------------
foreach ($pkg in $packages) {
    $deps = @("denikson-BepInExPack_Valheim-$BepInExPackVersion") + $pkg.extraDeps
    $internal = @()
    foreach ($guid in $pkg.pluginDeps) {
        $target = $packages | Where-Object { $_.guid -eq $guid } | Select-Object -First 1
        if (-not $target) {
            throw "$($pkg.name) declares BepInDependency on '$guid', which no package in this repo provides. List its Thunderstore id under 'dependencies' in package.json."
        }
        $deps += "$Namespace-$($target.name)-$($target.version)"
        $internal += $target.name
    }
    $pkg | Add-Member -NotePropertyName dependencies -NotePropertyValue $deps
    $pkg | Add-Member -NotePropertyName internalDependencies -NotePropertyValue $internal
}

# --- Write zips --------------------------------------------------------------
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$utf8NoBom = New-Object System.Text.UTF8Encoding $false

$index = @()
foreach ($pkg in $packages) {
    $manifest = [ordered]@{
        name           = $pkg.name
        version_number = $pkg.version
        website_url    = $WebsiteUrl
        description    = $pkg.description
        dependencies   = $pkg.dependencies
    }
    $manifestJson = $manifest | ConvertTo-Json -Depth 3

    $zipName = "$Namespace-$($pkg.name)-$($pkg.version).zip"
    $zipPath = Join-Path $OutDir $zipName
    if (Test-Path $zipPath) { Remove-Item $zipPath }

    # Entries are written by name at the zip root, so no path separator can go
    # wrong; the same concern the plugins zip documents.
    $zip = [IO.Compression.ZipFile]::Open($zipPath, 'Create')
    try {
        $entry = $zip.CreateEntry('manifest.json')
        $writer = New-Object IO.StreamWriter($entry.Open(), $utf8NoBom)
        try { $writer.Write($manifestJson) } finally { $writer.Dispose() }

        $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $pkg.icon, 'icon.png')
        $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $pkg.readme, 'README.md')
        $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $pkg.dllPath, $pkg.dll)
    } finally { $zip.Dispose() }

    $index += [ordered]@{
        name                 = $pkg.name
        version              = $pkg.version
        zip                  = $zipName
        description          = $pkg.description
        websiteUrl           = $WebsiteUrl
        categories           = $pkg.categories
        dependencies         = $pkg.dependencies
        internalDependencies = $pkg.internalDependencies
    }
}

$indexDoc = [ordered]@{ namespace = $Namespace; packages = $index }
[IO.File]::WriteAllText((Join-Path $OutDir 'packages.json'), ($indexDoc | ConvertTo-Json -Depth 5), $utf8NoBom)

Write-Host "Thunderstore packages in $OutDir (namespace $Namespace):"
$packages | Select-Object name, version, dll, @{ n = 'depends on'; e = { $_.internalDependencies -join ', ' } } | Format-Table -AutoSize | Out-String | Write-Host
