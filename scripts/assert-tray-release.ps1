#requires -Version 7
<#!
.SYNOPSIS
  Resolves the tray artifact version and fails closed unless a tag release has matching source identity and notes.
.DESCRIPTION
  Called by Release Tray CI before quality/packing and again to prepare the already-validated GitHub Release notes.
  workflow_dispatch is always artifact-only, regardless of the chosen ref name.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EventName,
    [Parameter(Mandatory)][string]$RefType,
    [Parameter(Mandatory)][string]$RefName,
    [string]$PropsPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Directory.Build.props'),
    [string]$ChangelogPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'CHANGELOG.md'),
    [string]$OutputPath,
    [string]$ReleaseNotesPath
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'tray-release-gates.psm1') -Force

$identityArguments = @{
    EventName = $EventName
    RefType = $RefType
    RefName = $RefName
    PropsPath = $PropsPath
    ChangelogPath = $ChangelogPath
}
$identity = Resolve-TrayReleaseIdentity @identityArguments

if ($OutputPath) {
    Add-Content -LiteralPath $OutputPath -Value "version=$($identity.Version)" -Encoding utf8
    Add-Content -LiteralPath $OutputPath -Value "is_release=$($identity.IsRelease.ToString().ToLowerInvariant())" -Encoding utf8
}

if ($ReleaseNotesPath) {
    if (-not $identity.IsRelease) {
        throw 'Release notes can only be prepared for an actual tray release tag.'
    }

    Set-Content -LiteralPath $ReleaseNotesPath -Value $identity.ReleaseNotes -NoNewline -Encoding utf8
}

$mode = if ($identity.IsRelease) { 'tag release' } else { 'artifact-only dispatch' }
Write-Host "Resolved tray version $($identity.Version) for $mode." -ForegroundColor Green
