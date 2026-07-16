#requires -Version 7
<#
.SYNOPSIS
  Runs the Shelfbound open-core .NET and Decky Python test suites.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$WarnAsError
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$deckyPath = Join-Path $root 'decky'

function Resolve-Python {
    $venvPython = if ($IsWindows) {
        Join-Path $deckyPath '.venv/Scripts/python.exe'
    }
    else {
        Join-Path $deckyPath '.venv/bin/python'
    }

    if (Test-Path $venvPython) {
        return $venvPython
    }

    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($null -eq $python) {
        throw "Python is required for the Decky tests. Install Python 3, then run 'python -m venv decky/.venv' and install decky/requirements-dev.txt."
    }

    return $python.Source
}

Write-Host "Running steam core tests ($Configuration)..." -ForegroundColor Cyan
$dotnetArguments = @(
    (Join-Path $root 'Shelfbound.slnx'),
    '--configuration', $Configuration,
    '--verbosity', 'normal'
)
if ($WarnAsError) {
    $dotnetArguments += '-warnaserror'
}
dotnet test @dotnetArguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$python = Resolve-Python
& $python -c 'import jsonschema, pytest, rfc3339_validator' 2>$null
if ($LASTEXITCODE -ne 0) {
    throw "Decky test dependencies are missing. Install them with '$python -m pip install -r decky/requirements-dev.txt'."
}

Write-Host ''
Write-Host 'Running Decky backend tests...' -ForegroundColor Cyan
Push-Location $deckyPath
try {
    & $python -m pytest tests
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
