#requires -Version 7
<#
.SYNOPSIS
  Reports C# formatting/style and checks Decky ESLint/Prettier.

.DESCRIPTION
  C# style remains report-only: this command reports drift through `dotnet format`, but CI does not gate it.
  Decky lint and format are required gates once the tree is clean.

.PARAMETER Fix
  Apply C# formatting plus safe Decky ESLint/Prettier fixes.

.PARAMETER Cs
  Run only the C# report. This mode does not require Decky dependencies.

.PARAMETER Decky
  Run only the Decky lint and format checks.

.EXAMPLE
  pwsh scripts/lint.ps1
  pwsh scripts/lint.ps1 -Cs
  pwsh scripts/lint.ps1 -Decky -Fix
#>
[CmdletBinding()]
param(
    [switch]$Fix,
    [switch]$Cs,
    [switch]$Decky
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$runCs = $Cs -or -not $Decky
$runDecky = $Decky -or -not $Cs
$failed = @()

function Invoke-Step {
    param(
        [Parameter(Mandatory)]
        [string]$Label,

        [Parameter(Mandatory)]
        [scriptblock]$Action
    )

    Write-Host ''
    Write-Host "==> $Label" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) {
        $script:failed += $Label
        Write-Host "    drift/errors reported ($Label)" -ForegroundColor Yellow
    }
}

if ($runCs) {
    $solution = Join-Path $root 'Shelfbound.slnx'
    if ($Fix) {
        Invoke-Step 'C# dotnet format (apply)' { dotnet format $solution }
    }
    else {
        Invoke-Step 'C# dotnet format (report only)' { dotnet format $solution --verify-no-changes }
    }
}

if ($runDecky) {
    $deckyPath = Join-Path $root 'decky'
    $nodeModulesPath = Join-Path $deckyPath 'node_modules'
    if (-not (Test-Path $nodeModulesPath)) {
        Write-Host ''
        Write-Host "==> Decky dependencies missing. Run 'corepack pnpm install --frozen-lockfile' in decky/." -ForegroundColor Yellow
        $failed += 'Decky dependencies missing'
    }
    else {
        Push-Location $deckyPath
        try {
            if ($Fix) {
                Invoke-Step 'Decky ESLint (apply)' { corepack pnpm run lint:fix }
                Invoke-Step 'Decky Prettier (apply)' { corepack pnpm run format }
            }
            else {
                Invoke-Step 'Decky ESLint' { corepack pnpm run lint }
                Invoke-Step 'Decky Prettier' { corepack pnpm run format:check }
            }
        }
        finally {
            Pop-Location
        }
    }
}

Write-Host ''
if ($failed.Count -gt 0) {
    Write-Host 'Code-quality checks reported drift/errors in:' -ForegroundColor Yellow
    $failed | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
    exit 1
}

Write-Host 'All requested code-quality checks are clean.' -ForegroundColor Green
