#requires -Version 7
<#
.SYNOPSIS
  Checks every GitHub Actions workflow with the repository-pinned actionlint version.

.DESCRIPTION
  Uses an exact matching actionlint executable from PATH when available. Otherwise it runs the official
  versioned Docker image against a read-only repository mount, so local and CI validation do not depend on
  an untracked global installation.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$actionlintVersion = '1.7.12'
$actionlintImage = 'rhysd/actionlint:1.7.12@sha256:b1934ee5f1c509618f2508e6eb47ee0d3520686341fec936f3b79331f9315667'

$actionlint = Get-Command actionlint -ErrorAction SilentlyContinue
if ($null -ne $actionlint) {
    $reportedVersion = (& $actionlint.Source -version | Select-Object -First 1).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Could not query actionlint at '$($actionlint.Source)'."
    }

    if ($reportedVersion -eq $actionlintVersion) {
        Push-Location $root
        try {
            & $actionlint.Source
            exit $LASTEXITCODE
        }
        finally {
            Pop-Location
        }
    }

    Write-Host "Ignoring actionlint $reportedVersion on PATH; this repository pins $actionlintVersion." -ForegroundColor Yellow
}

$docker = Get-Command docker -ErrorAction SilentlyContinue
if ($null -eq $docker) {
    throw "actionlint $actionlintVersion is not on PATH and Docker is unavailable. Install that exact actionlint version or start Docker."
}

$dockerArguments = @(
    'run',
    '--rm',
    '--volume', "${root}:/repo:ro",
    '--workdir', '/repo',
    $actionlintImage
)
& $docker.Source @dockerArguments
exit $LASTEXITCODE
