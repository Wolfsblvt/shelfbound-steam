#requires -Version 7
<#
.SYNOPSIS
  Runs the open-core test suite with coverage collection and generates an HTML + text summary.
  Covers Core / Query / Steam / Storage business logic.

  ReportGenerator is installed automatically if absent:
    dotnet tool install --global dotnet-reportgenerator-globaltool
#>

$ErrorActionPreference = 'Stop'
$root    = Split-Path -Parent $PSScriptRoot
$outDir  = Join-Path $root 'coverage'
$htmlDir = Join-Path $outDir 'report'

# Ensure ReportGenerator is available.
if (-not (Get-Command 'reportgenerator' -ErrorAction SilentlyContinue)) {
    Write-Host 'Installing ReportGenerator (global dotnet tool)...' -ForegroundColor Yellow
    dotnet tool install --global dotnet-reportgenerator-globaltool
    if ($LASTEXITCODE -ne 0) { Write-Error 'Failed to install ReportGenerator.'; exit 1 }
}

Write-Host 'Collecting coverage...' -ForegroundColor Cyan
dotnet test (Join-Path $root 'Shelfbound.slnx') `
    --verbosity normal `
    '--collect:XPlat Code Coverage' `
    --results-directory $outDir

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ''
Write-Host 'Generating report...' -ForegroundColor Cyan
reportgenerator `
    "-reports:$outDir/**/coverage.cobertura.xml" `
    "-targetdir:$htmlDir" `
    '-reporttypes:Html;TextSummary'

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$summary = Join-Path $htmlDir 'Summary.txt'
if (Test-Path $summary) {
    Write-Host ''
    Get-Content $summary
}

Write-Host ''
Write-Host "HTML report: file:///$($htmlDir -replace '\\', '/')/index.html" -ForegroundColor Green
