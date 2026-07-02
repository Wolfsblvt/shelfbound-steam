#requires -Version 7
<#
.SYNOPSIS
  Rasterizes assets/icon.svg into the app + installer icon formats and commits them alongside the SVG.
.DESCRIPTION
  The SVG is the single source of truth; run this whenever it changes (placeholder today, branded later).
  Generates the raster assets the app and Velopack installers actually consume, so builds don't need an
  SVG toolchain. Requires ImageMagick (`magick`) on PATH. macOS .icns is best generated on a Mac (iconutil)
  — see docs/project/releasing.md.

  Outputs:
    src/Shelfbound.Tray/Assets/tray.png  (32x32) — in-app tray/window icon
    assets/icon-256.png                  (256x256) — Linux AppImage icon
    assets/icon.ico                      (16..256) — Windows installer icon (auto-wired in release-tray.yml)
    assets/icon.icns                     (best effort; prefer iconutil on macOS)
.EXAMPLE
  ./scripts/icons.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$svg = Join-Path $root 'assets/icon.svg'

if (-not (Get-Command magick -ErrorAction SilentlyContinue)) {
    throw 'ImageMagick (magick) not found on PATH. Install it (https://imagemagick.org) and re-run, or generate the icons manually — see docs/project/releasing.md.'
}
if (-not (Test-Path $svg)) { throw "Source not found: $svg" }

Write-Host "Rasterizing $svg ..." -ForegroundColor Cyan
$trayPng = Join-Path $root 'src/Shelfbound.Tray/Assets/tray.png'
$png256 = Join-Path $root 'assets/icon-256.png'
$ico = Join-Path $root 'assets/icon.ico'
$icns = Join-Path $root 'assets/icon.icns'

magick -background none $svg -resize 32x32 $trayPng
magick -background none $svg -resize 256x256 $png256
magick -background none $svg -define icon:auto-resize=256,128,64,48,32,16 $ico
# .icns: ImageMagick can emit it, but macOS `iconutil` gives crisper results — treat this as best-effort.
try { magick -background none $svg -resize 512x512 $icns } catch { Write-Warning "Could not write $icns (generate on macOS with iconutil for best results)." }

Write-Host 'Generated:' -ForegroundColor Green
foreach ($f in @($trayPng, $png256, $ico, $icns)) { if (Test-Path $f) { Write-Host "  $f" } }
Write-Host 'Review and commit the regenerated icons.' -ForegroundColor Yellow
