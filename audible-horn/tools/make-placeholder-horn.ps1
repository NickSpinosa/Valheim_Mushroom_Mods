<#
.SYNOPSIS
    Generates the placeholder Assets/horn.wav for Audible Horn.

.DESCRIPTION
    Ticket 03 needs a horn clip before ticket 07 sources a real recording, and
    ffmpeg is not installed on the build machine, so the WAV bytes are written
    here by hand: a 2.5 s, 44.1 kHz, 16-bit mono additive tone at 220 Hz with
    its 2nd and 3rd harmonics, a 100 ms linear attack and a 600 ms linear
    release.

    It is meant to sound obviously synthetic. Three pure sines with no noise,
    no vibrato and no body resonance read as a test tone, not as a horn, which
    is the point: nobody should be able to mistake this for the final asset.

    The file deliberately carries a LIST/INFO chunk between `fmt ` and `data`.
    src/Audio/WavLoader.cs walks the chunk list by id and size rather than
    assuming `data` comes straight after `fmt `, and this is what keeps that
    path honest - a placeholder written as a bare 44-byte header would let the
    walker rot until a real recording from some editor broke it.

    This script lives under tools/, which docs/devops.md excludes from CI
    project discovery. It is committed because the placeholder is
    reproducible, not because CI runs it.

.EXAMPLE
    pwsh -File audible-horn/tools/make-placeholder-horn.ps1
#>
[CmdletBinding()]
param(
    # Defaults to audible-horn/Assets/horn.wav, next to this script's parent.
    [string] $OutputPath,
    [double] $Seconds = 2.5,
    [int]    $SampleRate = 44100,
    [double] $Fundamental = 220.0,
    [double] $AttackSeconds = 0.100,
    [double] $ReleaseSeconds = 0.600,
    # Headroom below full scale, so the clip never clips on playback.
    [double] $PeakAmplitude = 0.89
)

$ErrorActionPreference = 'Stop'

if (-not $OutputPath) {
    $OutputPath = Join-Path (Split-Path -Parent $PSCommandPath) '..\Assets\horn.wav'
}

$frames = [int][math]::Round($Seconds * $SampleRate)
$attackFrames = [int][math]::Round($AttackSeconds * $SampleRate)
$releaseFrames = [int][math]::Round($ReleaseSeconds * $SampleRate)

if ($attackFrames + $releaseFrames -gt $frames) {
    throw "Attack ($AttackSeconds s) and release ($ReleaseSeconds s) do not fit in $Seconds s."
}
$releaseStart = $frames - $releaseFrames

# Fundamental loudest, harmonics falling off - enough to give the tone an edge
# without letting it pass for an instrument.
$h1 = 1.0
$h2 = 0.5
$h3 = 0.25
$gain = $PeakAmplitude / ($h1 + $h2 + $h3)

$radiansPerFrame = 2.0 * [math]::PI * $Fundamental / $SampleRate

Write-Host "Synthesising $frames frames ($Seconds s @ $SampleRate Hz, 16-bit mono)..."

$pcm = New-Object byte[] ($frames * 2)
for ($i = 0; $i -lt $frames; $i++) {
    $phase = $radiansPerFrame * $i
    $sample = $h1 * [math]::Sin($phase) +
              $h2 * [math]::Sin(2.0 * $phase) +
              $h3 * [math]::Sin(3.0 * $phase)

    if ($i -lt $attackFrames) {
        $envelope = $i / [double]$attackFrames
    }
    elseif ($i -ge $releaseStart) {
        $envelope = ($frames - $i) / [double]$releaseFrames
    }
    else {
        $envelope = 1.0
    }

    $value = [int][math]::Round($sample * $gain * $envelope * 32767.0)
    if ($value -gt 32767) { $value = 32767 }
    elseif ($value -lt -32768) { $value = -32768 }

    # Two's complement little-endian int16.
    $bits = $value -band 0xFFFF
    $pcm[$i * 2] = [byte]($bits -band 0xFF)
    $pcm[$i * 2 + 1] = [byte](($bits -shr 8) -band 0xFF)
}

$ascii = [System.Text.Encoding]::ASCII

# LIST/INFO/ISFT, padded to an even length so no chunk needs a pad byte.
$softwareText = 'AudibleHorn placeholder generator'
$software = New-Object byte[] 34
[void]$ascii.GetBytes($softwareText, 0, $softwareText.Length, $software, 0)
$listSize = 4 + 8 + $software.Length          # "INFO" + ISFT header + payload

$dataSize = $pcm.Length
$fmtSize = 16
$riffSize = 4 + (8 + $fmtSize) + (8 + $listSize) + (8 + $dataSize)

$stream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($stream)
try {
    $writer.Write($ascii.GetBytes('RIFF'))
    $writer.Write([uint32]$riffSize)
    $writer.Write($ascii.GetBytes('WAVE'))

    $writer.Write($ascii.GetBytes('fmt '))
    $writer.Write([uint32]$fmtSize)
    $writer.Write([uint16]1)                                   # PCM
    $writer.Write([uint16]1)                                   # channels
    $writer.Write([uint32]$SampleRate)
    $writer.Write([uint32]($SampleRate * 2))                   # byte rate
    $writer.Write([uint16]2)                                   # block align
    $writer.Write([uint16]16)                                  # bits per sample

    $writer.Write($ascii.GetBytes('LIST'))
    $writer.Write([uint32]$listSize)
    $writer.Write($ascii.GetBytes('INFO'))
    $writer.Write($ascii.GetBytes('ISFT'))
    $writer.Write([uint32]$software.Length)
    $writer.Write($software)

    $writer.Write($ascii.GetBytes('data'))
    $writer.Write([uint32]$dataSize)
    $writer.Write($pcm)
    $writer.Flush()

    $bytes = $stream.ToArray()
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}

$full = [System.IO.Path]::GetFullPath($OutputPath)
$dir = Split-Path -Parent $full
if (-not (Test-Path -LiteralPath $dir)) {
    [void](New-Item -ItemType Directory -Path $dir)
}
[System.IO.File]::WriteAllBytes($full, $bytes)

Write-Host "Wrote $full ($($bytes.Length) bytes)."
