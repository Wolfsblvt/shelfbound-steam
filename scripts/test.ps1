#requires -Version 7
<#
.SYNOPSIS
  Runs the Shelfbound open-core test suite.
#>

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

Write-Host 'Running steam core tests...' -ForegroundColor Cyan
dotnet test (Join-Path $root 'Shelfbound.slnx') --verbosity normal
exit $LASTEXITCODE
