#requires -Version 7
<#
.SYNOPSIS
  Exports and validates the committed Shelfbound tray/application icons.
.DESCRIPTION
  assets/icon.svg is the locked color source. Export uses ImageMagick for SVG rasterization and for
  Windows/Linux formats. On macOS, iconutil builds the ICNS from a standard iconset. Non-Mac runs preserve
  and structurally validate the committed ICNS because Apple's encoder is not cross-platform.

  ImageMagick is a build-only prerequisite, not a runtime dependency. Use -MagickPath for a portable or
  otherwise non-PATH executable.
.PARAMETER Check
  Verify source hashes, required files, PNG dimensions, ICO frames, and ICNS embedded raster sizes without
  regenerating anything. This mode does not require ImageMagick.
.PARAMETER MagickPath
  ImageMagick executable or command name. Defaults to magick.
.EXAMPLE
  ./scripts/icons.ps1 -MagickPath C:/tools/ImageMagick/magick.exe
.EXAMPLE
  ./scripts/icons.ps1 -Check
#>
[CmdletBinding()]
param(
    [switch] $Check,
    [string] $MagickPath = 'magick'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$colorSource = Join-Path $root 'assets/icon.svg'
$monoSource = Join-Path $root 'assets/icon-mono.svg'
$trayPng = Join-Path $root 'src/Shelfbound.Tray/Assets/tray.png'
$linuxPng = Join-Path $root 'assets/icon-256.png'
$windowsIco = Join-Path $root 'assets/icon.ico'
$macosIcns = Join-Path $root 'assets/icon.icns'

$expectedSourceHashes = [ordered]@{
    $colorSource = 'ba2afc3c68128f50c6f8c49ac64bffe79df6a61bc6dfe87aa6cb6f847451cf9d'
    $monoSource = '0c862d1b679d43be68cb56d603e3ce76375932afb12dcd1f6b8fe8ab2260d644'
}
$expectedIcoSizes = @(16, 24, 32, 48, 64, 128, 256)
$expectedIcnsSizes = @(16, 32, 64, 128, 256, 512, 1024)

function Get-BigEndianUInt32 {
    param(
        [byte[]] $Bytes,
        [int] $Offset
    )

    return [uint32](
        ([uint64]$Bytes[$Offset] * 16777216) +
        ([uint64]$Bytes[$Offset + 1] * 65536) +
        ([uint64]$Bytes[$Offset + 2] * 256) +
        $Bytes[$Offset + 3]
    )
}

function Get-PngDimensions {
    param([string] $Path)

    $bytes = [IO.File]::ReadAllBytes($Path)
    $signature = @(137, 80, 78, 71, 13, 10, 26, 10)
    if ($bytes.Length -lt 24) {
        throw "PNG is truncated: $Path"
    }
    for ($index = 0; $index -lt $signature.Count; $index++) {
        if ($bytes[$index] -ne $signature[$index]) {
            throw "Invalid PNG signature: $Path"
        }
    }
    if ([Text.Encoding]::ASCII.GetString($bytes, 12, 4) -ne 'IHDR') {
        throw "PNG does not start with an IHDR chunk: $Path"
    }
    $colorType = $bytes[25]
    $hasTransparencyChunk = $false
    $chunkOffset = 8
    while ($chunkOffset -lt $bytes.Length) {
        if ($chunkOffset + 12 -gt $bytes.Length) {
            throw "PNG chunk header is truncated: $Path"
        }
        $chunkLength = Get-BigEndianUInt32 -Bytes $bytes -Offset $chunkOffset
        if ($chunkOffset + 12 + $chunkLength -gt $bytes.Length) {
            throw "PNG chunk is truncated at byte ${chunkOffset}: $Path"
        }
        $chunkType = [Text.Encoding]::ASCII.GetString($bytes, $chunkOffset + 4, 4)
        if ($chunkType -eq 'tRNS') {
            $hasTransparencyChunk = $true
        }
        $chunkOffset += 12 + $chunkLength
        if ($chunkType -eq 'IEND') {
            break
        }
    }
    if ($colorType -notin @(4, 6) -and -not ($colorType -eq 3 -and $hasTransparencyChunk)) {
        throw "PNG has no alpha channel or transparency chunk (color type $colorType): $Path"
    }

    return @(
        (Get-BigEndianUInt32 -Bytes $bytes -Offset 16),
        (Get-BigEndianUInt32 -Bytes $bytes -Offset 20)
    )
}

function Get-IcoSizes {
    param([string] $Path)

    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 6 -or [BitConverter]::ToUInt16($bytes, 0) -ne 0 -or [BitConverter]::ToUInt16($bytes, 2) -ne 1) {
        throw "Invalid ICO header: $Path"
    }

    $count = [BitConverter]::ToUInt16($bytes, 4)
    if ($bytes.Length -lt 6 + ($count * 16)) {
        throw "ICO directory is truncated: $Path"
    }

    $sizes = for ($index = 0; $index -lt $count; $index++) {
        $offset = 6 + ($index * 16)
        $width = if ($bytes[$offset] -eq 0) { 256 } else { $bytes[$offset] }
        $height = if ($bytes[$offset + 1] -eq 0) { 256 } else { $bytes[$offset + 1] }
        if ($width -ne $height) {
            throw "ICO contains a non-square frame ($width x $height): $Path"
        }
        $width
    }

    return @($sizes | Sort-Object -Unique)
}

function Get-IcnsSizes {
    param([string] $Path)

    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 8 -or [Text.Encoding]::ASCII.GetString($bytes, 0, 4) -ne 'icns') {
        throw "Invalid ICNS header: $Path"
    }

    $declaredLength = Get-BigEndianUInt32 -Bytes $bytes -Offset 4
    if ($declaredLength -ne $bytes.Length) {
        throw "ICNS length mismatch in ${Path}: header says $declaredLength bytes, file has $($bytes.Length)"
    }

    $sizeByType = @{
        'is32' = 16
        'icp4' = 16
        'il32' = 32
        'icp5' = 32
        'ic11' = 32
        'icp6' = 64
        'ic12' = 64
        'ic07' = 128
        'ic08' = 256
        'ic13' = 256
        'ic09' = 512
        'ic14' = 512
        'ic10' = 1024
    }
    $sizes = [Collections.Generic.List[int]]::new()
    $offset = 8
    while ($offset -lt $bytes.Length) {
        if ($offset + 8 -gt $bytes.Length) {
            throw "ICNS chunk header is truncated: $Path"
        }

        $chunkLength = Get-BigEndianUInt32 -Bytes $bytes -Offset ($offset + 4)
        if ($chunkLength -lt 8 -or $offset + $chunkLength -gt $bytes.Length) {
            throw "ICNS chunk length is invalid at byte ${offset}: $Path"
        }

        $chunkType = [Text.Encoding]::ASCII.GetString($bytes, $offset, 4)
        $expectedChunkSize = $sizeByType[$chunkType]
        if ($expectedChunkSize) {
            $sizes.Add($expectedChunkSize)
        }

        $payloadOffset = $offset + 8
        if ($chunkLength -ge 32 -and
            $bytes[$payloadOffset] -eq 137 -and
            $bytes[$payloadOffset + 1] -eq 80 -and
            $bytes[$payloadOffset + 2] -eq 78 -and
            $bytes[$payloadOffset + 3] -eq 71) {
            $width = Get-BigEndianUInt32 -Bytes $bytes -Offset ($payloadOffset + 16)
            $height = Get-BigEndianUInt32 -Bytes $bytes -Offset ($payloadOffset + 20)
            if ($width -ne $height) {
                throw "ICNS contains a non-square PNG ($width x $height): $Path"
            }
            if ($expectedChunkSize -and $width -ne $expectedChunkSize) {
                throw "ICNS chunk $chunkType contains ${width}px PNG; expected ${expectedChunkSize}px: $Path"
            }
            $sizes.Add($width)
        }

        $offset += $chunkLength
    }

    return @($sizes | Sort-Object -Unique)
}

function Assert-SequenceEqual {
    param(
        [int[]] $Expected,
        [int[]] $Actual,
        [string] $Label
    )

    $expectedText = $Expected -join ','
    $actualText = $Actual -join ','
    if ($actualText -ne $expectedText) {
        throw "$Label mismatch: expected [$expectedText], got [$actualText]"
    }
}

function Assert-IconAssets {
    foreach ($entry in $expectedSourceHashes.GetEnumerator()) {
        if (-not (Test-Path -LiteralPath $entry.Key -PathType Leaf)) {
            throw "Icon source is missing: $($entry.Key)"
        }
        $actualHash = (Get-FileHash -LiteralPath $entry.Key -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $entry.Value) {
            throw "Locked icon source hash mismatch for $($entry.Key): expected $($entry.Value), got $actualHash"
        }
    }

    foreach ($path in @($trayPng, $linuxPng, $windowsIco, $macosIcns)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Generated icon is missing: $path"
        }
    }

    Assert-SequenceEqual -Expected @(32, 32) -Actual (Get-PngDimensions -Path $trayPng) -Label 'Tray PNG dimensions'
    Assert-SequenceEqual -Expected @(256, 256) -Actual (Get-PngDimensions -Path $linuxPng) -Label 'Linux PNG dimensions'
    Assert-SequenceEqual -Expected $expectedIcoSizes -Actual (Get-IcoSizes -Path $windowsIco) -Label 'ICO frames'
    Assert-SequenceEqual -Expected $expectedIcnsSizes -Actual (Get-IcnsSizes -Path $macosIcns) -Label 'ICNS representations'
}

function Invoke-NativeCommand {
    param(
        [string] $Command,
        [string[]] $Arguments
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $Command $($Arguments -join ' ')"
    }
}

function Export-Png {
    param(
        [string] $Destination,
        [int] $Size
    )

    Invoke-NativeCommand -Command $resolvedMagick -Arguments @(
        '-density', '384',
        '-background', 'none',
        $colorSource,
        '-resize', "${Size}x${Size}",
        '-strip',
        '-define', 'png:exclude-chunk=date,time',
        $Destination
    )
}

function Copy-IfChanged {
    param(
        [string] $Source,
        [string] $Destination
    )

    $unchanged = (Test-Path -LiteralPath $Destination -PathType Leaf) -and
        ((Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash -eq
         (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash)
    if (-not $unchanged) {
        Copy-Item -LiteralPath $Source -Destination $Destination -Force
    }
}

if ($Check) {
    Assert-IconAssets
    Write-Host 'Locked icon sources and generated application assets verified.' -ForegroundColor Green
    return
}

$resolvedMagick = if (Test-Path -LiteralPath $MagickPath -PathType Leaf) {
    (Resolve-Path -LiteralPath $MagickPath).Path
} else {
    (Get-Command $MagickPath -ErrorAction SilentlyContinue)?.Source
}
if (-not $resolvedMagick) {
    throw 'ImageMagick (magick) was not found. Install it as a build-only prerequisite or pass a portable executable with -MagickPath.'
}

$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$tempRoot = [IO.Path]::GetFullPath((Join-Path $tempBase "shelfbound-icons-$([guid]::NewGuid().ToString('N'))"))
$tempPrefix = $tempBase.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $tempRoot.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($tempRoot) -notmatch '^shelfbound-icons-[a-f0-9]{32}$') {
    throw "Refusing to use unsafe temporary icon path: $tempRoot"
}
$iconset = Join-Path $tempRoot 'Shelfbound.iconset'
try {
    New-Item -ItemType Directory -Path $iconset -Force | Out-Null
    $tempTrayPng = Join-Path $tempRoot 'tray.png'
    $tempLinuxPng = Join-Path $tempRoot 'icon-256.png'
    $tempWindowsIco = Join-Path $tempRoot 'icon.ico'
    $tempMacosIcns = Join-Path $tempRoot 'icon.icns'

    Export-Png -Destination $tempTrayPng -Size 32
    Export-Png -Destination $tempLinuxPng -Size 256

    Invoke-NativeCommand -Command $resolvedMagick -Arguments @(
        '-density', '384',
        '-background', 'none',
        $colorSource,
        '-define', "icon:auto-resize=$($expectedIcoSizes[($expectedIcoSizes.Count - 1)..0] -join ',')",
        '-strip',
        $tempWindowsIco
    )

    $iconsetFiles = [ordered]@{
        'icon_16x16.png' = 16
        'icon_16x16@2x.png' = 32
        'icon_32x32.png' = 32
        'icon_32x32@2x.png' = 64
        'icon_128x128.png' = 128
        'icon_128x128@2x.png' = 256
        'icon_256x256.png' = 256
        'icon_256x256@2x.png' = 512
        'icon_512x512.png' = 512
        'icon_512x512@2x.png' = 1024
    }
    foreach ($entry in $iconsetFiles.GetEnumerator()) {
        Export-Png -Destination (Join-Path $iconset $entry.Key) -Size $entry.Value
    }

    if ($IsMacOS) {
        $iconutil = (Get-Command iconutil -ErrorAction SilentlyContinue)?.Source
        if (-not $iconutil) {
            throw 'iconutil is required to generate assets/icon.icns on macOS.'
        }
        Invoke-NativeCommand -Command $iconutil -Arguments @('-c', 'icns', $iconset, '-o', $tempMacosIcns)
    } else {
        if (-not (Test-Path -LiteralPath $macosIcns -PathType Leaf)) {
            throw 'assets/icon.icns is missing. Regenerate it on macOS with iconutil before exporting on another platform.'
        }
        Copy-Item -LiteralPath $macosIcns -Destination $tempMacosIcns
    }

    $previousTrayPng = $trayPng
    $previousLinuxPng = $linuxPng
    $previousWindowsIco = $windowsIco
    $previousMacosIcns = $macosIcns
    $trayPng = $tempTrayPng
    $linuxPng = $tempLinuxPng
    $windowsIco = $tempWindowsIco
    $macosIcns = $tempMacosIcns
    Assert-IconAssets
    $trayPng = $previousTrayPng
    $linuxPng = $previousLinuxPng
    $windowsIco = $previousWindowsIco
    $macosIcns = $previousMacosIcns

    Copy-IfChanged -Source $tempTrayPng -Destination $trayPng
    Copy-IfChanged -Source $tempLinuxPng -Destination $linuxPng
    Copy-IfChanged -Source $tempWindowsIco -Destination $windowsIco
    if ($IsMacOS) {
        Copy-IfChanged -Source $tempMacosIcns -Destination $macosIcns
    }
    Assert-IconAssets
} finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}

Write-Host 'Exported and verified:' -ForegroundColor Green
foreach ($path in @($trayPng, $linuxPng, $windowsIco, $macosIcns)) {
    Write-Host "  $path"
}
