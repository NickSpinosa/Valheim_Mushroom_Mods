<#
.SYNOPSIS
    Converts a downloaded horn recording into the Assets/horn.wav that ships.

.DESCRIPTION
    Ticket 07 replaces the synthesised placeholder with a real recording, and
    ffmpeg is not installed on the build machine (and must not be), so the whole
    conversion the ticket's ffmpeg one-liner would have done is here in .NET:

        mono mix-down -> resample to 44.1 kHz -> trim head and tail silence ->
        fade in/out -> normalise peak to -1 dBFS -> canonical 16-bit PCM WAV

    Only 16-bit PCM sources are accepted, mono or stereo, at any sample rate.
    That is not laziness: src/Audio/WavLoader.cs accepts exactly that and nothing
    else, so a converter that swallowed 24-bit or IEEE-float input would only be
    able to produce something the mod then refuses to load. Anything else exits
    non-zero and names what it found.

    The RIFF container is chunk-walked by id and size, the same way WavLoader
    does, because editors and download services routinely write LIST/INFO, `fact`
    or `bext` between `fmt ` and `data`; a reader that assumed `data` came
    straight after `fmt ` would read metadata as audio.

    The output is a bare 44-byte-header WAV - fmt then data, nothing else. The
    placeholder generator deliberately writes a LIST chunk to keep WavLoader's
    chunk walk honest; this script does not need to repeat that, and every byte
    saved is a byte off an embedded resource.

    Everything is a plain PowerShell loop over a byte[]. On a five-second clip
    that is a few seconds of work, which is cheaper than a dependency.

.PARAMETER Source
    The downloaded recording. 16-bit PCM WAV, mono or stereo, any sample rate.

.PARAMETER Destination
    Where to write. Defaults to audible-horn/Assets/horn.wav, overwriting the
    placeholder. Point it somewhere else to audition a conversion first.

.PARAMETER MaxSeconds
    Hard cap on the trimmed clip. If the recording is longer it is cut here and
    the fade-out lands on the cut, so a long take never ships a click.

.EXAMPLE
    powershell -NoProfile -File audible-horn/tools/convert-horn.ps1 `
        -Source audible-horn/Assets/hunting-horn-source.wav
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Source,

    # Defaults to audible-horn/Assets/horn.wav, next to this script's parent.
    [string] $Destination,

    [double] $MaxSeconds = 4.0
)

$ErrorActionPreference = 'Stop'

# --- constants -------------------------------------------------------------

$TargetRate = 44100
# -50 dBFS. Quiet enough that room tone and the encoder's noise floor count as
# silence, loud enough that the first breath of the horn does not.
$SilenceThreshold = [math]::Pow(10.0, -50.0 / 20.0)
# Kept in front of the onset so the attack is not clipped into a click.
$LeadInSeconds = 0.020
$TailSeconds = 0.020
$FadeInSeconds = 0.015
$FadeOutSeconds = 0.150
# -1 dBFS, the headroom the ticket asks for.
$PeakTarget = [math]::Pow(10.0, -1.0 / 20.0)

function Stop-WithError([string] $message) {
    $Host.UI.WriteErrorLine("convert-horn: $message")
    exit 1
}

# Join-Path is wrong here: given an already-rooted path it happily returns
# "C:\cwd\C:\other", which GetFullPath then rejects. Path.Combine drops the left
# side when the right side is rooted, which is the behaviour wanted.
function Resolve-FullPath([string] $path) {
    return [System.IO.Path]::GetFullPath(
        [System.IO.Path]::Combine((Get-Location).ProviderPath, $path))
}

function Format-Dbfs([double] $linear) {
    if ($linear -le 0.0) { return '-inf dBFS' }
    return ('{0:0.00} dBFS' -f (20.0 * [math]::Log10($linear)))
}

# --- read and validate the source -----------------------------------------

if (-not $Destination) {
    $Destination = Join-Path (Split-Path -Parent $PSCommandPath) '..\Assets\horn.wav'
}

$sourceFull = Resolve-FullPath $Source
if (-not (Test-Path -LiteralPath $sourceFull -PathType Leaf)) {
    # Test-Path first: [IO.File]::ReadAllBytes on a directory throws something
    # far less useful than the path that was actually wanted.
    Stop-WithError "source file not found: $sourceFull"
}

$bytes = [System.IO.File]::ReadAllBytes($sourceFull)
Write-Host "Reading $sourceFull ($($bytes.Length) bytes)..."

function Test-ChunkId([byte[]] $buffer, [int] $offset, [string] $id) {
    if ($offset + 4 -gt $buffer.Length) { return $false }
    for ($k = 0; $k -lt 4; $k++) {
        if ($buffer[$offset + $k] -ne [byte][char]$id[$k]) { return $false }
    }
    return $true
}

if ($bytes.Length -lt 12 -or
    -not (Test-ChunkId $bytes 0 'RIFF') -or
    -not (Test-ChunkId $bytes 8 'WAVE')) {
    Stop-WithError "not a RIFF/WAVE file. Only 16-bit PCM WAV is accepted."
}

$formatTag = -1
$channels = 0
$sampleRate = 0
$bitsPerSample = 0
$dataOffset = -1
$dataLength = 0
$otherChunks = @()

# Same walk as WavLoader.Build: by id and size, word-aligned, bounds-checked so
# a corrupt size field cannot spin the cursor backwards forever.
$position = 12
while ($position + 8 -le $bytes.Length) {
    $size = [System.BitConverter]::ToUInt32($bytes, $position + 4)
    $body = $position + 8
    if ($body -gt $bytes.Length) { break }

    $id = [System.Text.Encoding]::ASCII.GetString($bytes, $position, 4)

    if ((Test-ChunkId $bytes $position 'fmt ') -and $size -ge 16 -and ($body + 16) -le $bytes.Length) {
        $formatTag = [System.BitConverter]::ToUInt16($bytes, $body)
        $channels = [System.BitConverter]::ToUInt16($bytes, $body + 2)
        $sampleRate = [int][System.BitConverter]::ToUInt32($bytes, $body + 4)
        $bitsPerSample = [System.BitConverter]::ToUInt16($bytes, $body + 14)
    }
    elseif (Test-ChunkId $bytes $position 'data') {
        $dataOffset = $body
        $dataLength = [int][math]::Min([double]$size, [double]($bytes.Length - $body))
    }
    else {
        $otherChunks += $id
    }

    $next = [long]$body + [long]$size + ([long]$size -band 1L)
    if ($next -le $position -or $next -gt $bytes.Length) { break }
    $position = [int]$next
}

if ($otherChunks.Count -gt 0) {
    Write-Host "  chunks skipped: $($otherChunks -join ', ')"
}

if ($formatTag -ne 1) {
    $hint = ''
    if ($formatTag -eq 0xFFFE) {
        $hint = ' (WAVE_FORMAT_EXTENSIBLE; re-export as plain PCM)'
    }
    elseif ($formatTag -eq 3) {
        $hint = ' (IEEE float; re-export as 16-bit PCM)'
    }
    Stop-WithError "format tag $formatTag is not WAVE_FORMAT_PCM (1)$hint. WavLoader accepts 16-bit PCM only."
}
if ($bitsPerSample -ne 16) {
    Stop-WithError "$bitsPerSample-bit samples; only 16-bit PCM is accepted. Re-export as 16-bit."
}
if ($channels -ne 1 -and $channels -ne 2) {
    Stop-WithError "$channels channel(s); only mono or stereo is accepted."
}
if ($sampleRate -le 0) {
    Stop-WithError "sample rate $sampleRate is not usable."
}
if ($dataOffset -lt 0 -or $dataLength -le 0) {
    Stop-WithError "no usable 'data' chunk."
}

$srcFrames = [int]([math]::Floor($dataLength / 2 / $channels))
if ($srcFrames -le 0) {
    Stop-WithError "the 'data' chunk holds no audio frames."
}

Write-Host ("  source: {0} frames, {1} channel(s), {2} Hz, {3}-bit PCM ({4:0.000} s)" -f `
    $srcFrames, $channels, $sampleRate, $bitsPerSample, ($srcFrames / [double]$sampleRate))

# --- decode to mono --------------------------------------------------------

# Averaged rather than summed: a stereo recording of one instrument is close to
# correlated, so summing would clip most of it. The normalise at the end puts
# the level back where it belongs either way.
$mono = New-Object double[] $srcFrames
$sourcePeak = 0.0
for ($i = 0; $i -lt $srcFrames; $i++) {
    $at = $dataOffset + $i * 2 * $channels
    $sum = [double][System.BitConverter]::ToInt16($bytes, $at)
    if ($channels -eq 2) {
        $sum = ($sum + [double][System.BitConverter]::ToInt16($bytes, $at + 2)) * 0.5
    }
    # 32768, not 32767: int16 is asymmetric, and dividing by the magnitude of
    # the most negative sample is what keeps the result inside [-1, 1].
    $value = $sum / 32768.0
    $mono[$i] = $value
    $magnitude = [math]::Abs($value)
    if ($magnitude -gt $sourcePeak) { $sourcePeak = $magnitude }
}

if ($channels -eq 2) { Write-Host '  mixed stereo down to mono.' }

# --- resample --------------------------------------------------------------

$resampled = $false
if ($sampleRate -ne $TargetRate) {
    $ratio = $sampleRate / [double]$TargetRate
    $outFrames = [int][math]::Floor($srcFrames / $ratio)
    if ($outFrames -le 1) {
        Stop-WithError "resampling $sampleRate Hz to $TargetRate Hz leaves $outFrames frame(s); the source is too short."
    }

    # Linear interpolation. It is not a windowed-sinc resampler and it does not
    # need to be: this is one horn blast played at a distance through the game's
    # own reverb, and the alias products of linear interpolation sit far below
    # anything a Listener could pick out.
    $out = New-Object double[] $outFrames
    for ($j = 0; $j -lt $outFrames; $j++) {
        $srcPosition = $j * $ratio
        $i0 = [int][math]::Floor($srcPosition)
        $frac = $srcPosition - $i0
        $i1 = $i0 + 1
        if ($i1 -ge $srcFrames) { $i1 = $srcFrames - 1 }
        $out[$j] = $mono[$i0] * (1.0 - $frac) + $mono[$i1] * $frac
    }
    $mono = $out
    $resampled = $true
    Write-Host ("  resampled {0} Hz -> {1} Hz ({2} frames)." -f $sampleRate, $TargetRate, $outFrames)
}
else {
    Write-Host "  already $TargetRate Hz; no resampling."
}

$frames = $mono.Length

# --- trim silence ----------------------------------------------------------

$first = -1
for ($i = 0; $i -lt $frames; $i++) {
    if ([math]::Abs($mono[$i]) -ge $SilenceThreshold) { $first = $i; break }
}
if ($first -lt 0) {
    Stop-WithError ("nothing in this file rises above {0} - it is silence as far as the trimmer can tell." -f (Format-Dbfs $SilenceThreshold))
}

$last = $frames - 1
for ($i = $frames - 1; $i -ge 0; $i--) {
    if ([math]::Abs($mono[$i]) -ge $SilenceThreshold) { $last = $i; break }
}

$leadIn = [int][math]::Round($LeadInSeconds * $TargetRate)
$tail = [int][math]::Round($TailSeconds * $TargetRate)
$start = [math]::Max(0, $first - $leadIn)
$end = [math]::Min($frames - 1, $last + $tail)
$trimmed = $end - $start + 1

Write-Host ("  trimmed {0:0.000} s from the head and {1:0.000} s from the tail." -f `
    ($start / [double]$TargetRate), (($frames - 1 - $end) / [double]$TargetRate))

# --- length cap ------------------------------------------------------------

$maxFrames = [int][math]::Round($MaxSeconds * $TargetRate)
$truncated = $false
if ($trimmed -gt $maxFrames) {
    $truncated = $true
    Write-Host ("  clip is {0:0.000} s; cutting at MaxSeconds ({1:0.000} s), fade-out lands on the cut." -f `
        ($trimmed / [double]$TargetRate), $MaxSeconds)
    $trimmed = $maxFrames
}

$clip = New-Object double[] $trimmed
[System.Array]::Copy($mono, $start, $clip, 0, $trimmed)

# --- fades -----------------------------------------------------------------

$fadeIn = [int][math]::Round($FadeInSeconds * $TargetRate)
$fadeOut = [int][math]::Round($FadeOutSeconds * $TargetRate)
if ($fadeIn + $fadeOut -gt $trimmed) {
    # A clip shorter than the two fades put together would have them overlap and
    # scoop the middle out. Shrink both to fit rather than produce that.
    $scale = $trimmed / [double]($fadeIn + $fadeOut)
    $fadeIn = [int][math]::Floor($fadeIn * $scale)
    $fadeOut = [int][math]::Floor($fadeOut * $scale)
    Write-Host "  clip is shorter than the fades; shrank them to $fadeIn / $fadeOut frames."
}

for ($i = 0; $i -lt $fadeIn; $i++) {
    $clip[$i] = $clip[$i] * ($i / [double]$fadeIn)
}
for ($i = 0; $i -lt $fadeOut; $i++) {
    $clip[$trimmed - 1 - $i] = $clip[$trimmed - 1 - $i] * ($i / [double]$fadeOut)
}

# --- normalise -------------------------------------------------------------

$peakAfterFades = 0.0
for ($i = 0; $i -lt $trimmed; $i++) {
    $magnitude = [math]::Abs($clip[$i])
    if ($magnitude -gt $peakAfterFades) { $peakAfterFades = $magnitude }
}
if ($peakAfterFades -le 0.0) {
    Stop-WithError 'the clip is silent after trimming and fading; nothing to normalise.'
}
$gain = $PeakTarget / $peakAfterFades

$pcm = New-Object byte[] ($trimmed * 2)
$finalPeak = 0.0
for ($i = 0; $i -lt $trimmed; $i++) {
    $value = $clip[$i] * $gain
    $magnitude = [math]::Abs($value)
    if ($magnitude -gt $finalPeak) { $finalPeak = $magnitude }

    $sample = [int][math]::Round($value * 32767.0)
    if ($sample -gt 32767) { $sample = 32767 }
    elseif ($sample -lt -32768) { $sample = -32768 }

    # Two's complement little-endian int16.
    $bits = $sample -band 0xFFFF
    $pcm[$i * 2] = [byte]($bits -band 0xFF)
    $pcm[$i * 2 + 1] = [byte](($bits -shr 8) -band 0xFF)
}

# --- write -----------------------------------------------------------------

$ascii = [System.Text.Encoding]::ASCII
$dataSize = $pcm.Length
$fmtSize = 16
$riffSize = 4 + (8 + $fmtSize) + (8 + $dataSize)

$stream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($stream)
try {
    $writer.Write($ascii.GetBytes('RIFF'))
    $writer.Write([uint32]$riffSize)
    $writer.Write($ascii.GetBytes('WAVE'))

    $writer.Write($ascii.GetBytes('fmt '))
    $writer.Write([uint32]$fmtSize)
    $writer.Write([uint16]1)                        # WAVE_FORMAT_PCM
    $writer.Write([uint16]1)                        # mono
    $writer.Write([uint32]$TargetRate)
    $writer.Write([uint32]($TargetRate * 2))        # byte rate
    $writer.Write([uint16]2)                        # block align
    $writer.Write([uint16]16)                       # bits per sample

    $writer.Write($ascii.GetBytes('data'))
    $writer.Write([uint32]$dataSize)
    $writer.Write($pcm)
    $writer.Flush()

    $outBytes = $stream.ToArray()
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}

$destinationFull = Resolve-FullPath $Destination
$destinationDir = Split-Path -Parent $destinationFull
if (-not (Test-Path -LiteralPath $destinationDir)) {
    [void](New-Item -ItemType Directory -Path $destinationDir)
}
[System.IO.File]::WriteAllBytes($destinationFull, $outBytes)

# --- summary ---------------------------------------------------------------

Write-Host ''
Write-Host 'Converted:'
Write-Host ("  frames        : {0} mono @ {1} Hz" -f $trimmed, $TargetRate)
Write-Host ("  seconds       : {0:0.000}{1}" -f ($trimmed / [double]$TargetRate), $(if ($truncated) { " (cut at MaxSeconds $MaxSeconds)" } else { '' }))
Write-Host ("  resampled     : {0}" -f $(if ($resampled) { "yes, $sampleRate Hz -> $TargetRate Hz" } else { 'no' }))
Write-Host ("  peak before   : {0:0.0000} ({1})" -f $sourcePeak, (Format-Dbfs $sourcePeak))
Write-Host ("  peak after    : {0:0.0000} ({1})" -f $finalPeak, (Format-Dbfs $finalPeak))
Write-Host ("  bytes written : {0} ({1:0.0} KB)" -f $outBytes.Length, ($outBytes.Length / 1024.0))
Write-Host ("  destination   : {0}" -f $destinationFull)
