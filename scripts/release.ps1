#requires -Version 7
<#
.SYNOPSIS
  Prepares a tray release locally: bumps the version, promotes CHANGELOG.md, commits, and creates the
  `tray-v<version>` tag. Does NOT push — review, then `git push origin tray-v<version>` to trigger CI.
.DESCRIPTION
  See docs/project/releasing.md for the full flow. Requires a clean working tree so the release commit
  contains only the version bump + changelog. Supports -WhatIf to preview without changing anything.
.EXAMPLE
  ./scripts/release.ps1 -Version 0.6.1
.EXAMPLE
  ./scripts/release.ps1 -Version 0.6.1 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$') {
    throw "Version '$Version' is not semver (expected x.y.z, optionally -suffix)."
}

$tag = "tray-v$Version"
$propsPath = Join-Path $root 'Directory.Build.props'
$changelogPath = Join-Path $root 'CHANGELOG.md'

# Clean tree so the release commit is only the version bump + changelog.
if ((git -C $root status --porcelain)) {
    throw 'Working tree is not clean. Commit or stash your changes before releasing.'
}
if (git -C $root tag --list $tag) {
    throw "Tag $tag already exists."
}

# 1) Bump the solution version (literal replace of the current value, no regex-replacement pitfalls).
$props = Get-Content $propsPath -Raw
$current = [regex]::Match($props, '<Version>(.*?)</Version>').Groups[1].Value
if (-not $current) { throw "No <Version> found in $propsPath." }
Write-Host "Version: $current -> $Version" -ForegroundColor Cyan
$newProps = $props.Replace("<Version>$current</Version>", "<Version>$Version</Version>")

# 2) Promote CHANGELOG: keep an empty [Unreleased] on top, add a dated section for this version below it.
$changelog = Get-Content $changelogPath -Raw
if ($changelog -notmatch '(?m)^## \[Unreleased\]') { throw "No '## [Unreleased]' section in CHANGELOG.md." }
if ($changelog -match "(?m)^## \[$([regex]::Escape($Version))\]") { throw "CHANGELOG already has a [$Version] section." }
$date = Get-Date -Format 'yyyy-MM-dd'
$newChangelog = $changelog -replace '(?m)^## \[Unreleased\]$', "## [Unreleased]`n`n## [$Version] - $date"

if ($PSCmdlet.ShouldProcess($propsPath, "Set <Version> to $Version")) {
    Set-Content -Path $propsPath -Value $newProps -NoNewline -Encoding utf8
}
if ($PSCmdlet.ShouldProcess($changelogPath, "Add [$Version] - $date section")) {
    Set-Content -Path $changelogPath -Value $newChangelog -NoNewline -Encoding utf8
}
if ($PSCmdlet.ShouldProcess("git", "commit release + tag $tag")) {
    git -C $root add -- Directory.Build.props CHANGELOG.md
    git -C $root commit -m "Release tray v$Version"
    git -C $root tag $tag
    Write-Host "`nCommitted and tagged $tag." -ForegroundColor Green
    Write-Host "Next: review, then push to trigger CI:" -ForegroundColor Yellow
    Write-Host "  git push && git push origin $tag"
}
