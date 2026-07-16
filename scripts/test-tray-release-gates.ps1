#requires -Version 7

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$entryPoint = Join-Path $PSScriptRoot 'assert-tray-release.ps1'
$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) "shelfbound-tray-release-gates-$([guid]::NewGuid())"

function Assert-Throws {
    param(
        [Parameter(Mandatory)][scriptblock]$Action,
        [Parameter(Mandatory)][string]$MessageFragment
    )

    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -notlike "*$MessageFragment*") {
            throw "Expected an error containing '$MessageFragment', got: $($_.Exception.Message)"
        }
        return
    }

    throw "Expected an error containing '$MessageFragment', but the tray gate accepted the input."
}

function New-FixtureFile {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Content
    )

    $path = Join-Path $fixtureRoot $Name
    Set-Content -LiteralPath $path -Value $Content -NoNewline -Encoding utf8
    return $path
}

function Invoke-TrayGate {
    param(
        [Parameter(Mandatory)][string]$EventName,
        [Parameter(Mandatory)][string]$RefType,
        [Parameter(Mandatory)][string]$RefName,
        [Parameter(Mandatory)][string]$PropsPath,
        [Parameter(Mandatory)][string]$ChangelogPath,
        [switch]$WriteNotes
    )

    $outputPath = Join-Path $fixtureRoot "output-$([guid]::NewGuid()).txt"
    $arguments = @{
        EventName = $EventName
        RefType = $RefType
        RefName = $RefName
        PropsPath = $PropsPath
        ChangelogPath = $ChangelogPath
        OutputPath = $outputPath
    }
    if ($WriteNotes) {
        $arguments.ReleaseNotesPath = Join-Path $fixtureRoot "notes-$([guid]::NewGuid()).md"
    }

    & $entryPoint @arguments
    $outputs = @{}
    Get-Content -LiteralPath $outputPath | ForEach-Object {
        $name, $value = $_ -split '=', 2
        $outputs[$name] = $value
    }
    if ($WriteNotes) {
        $outputs['notes'] = Get-Content -LiteralPath $arguments.ReleaseNotesPath -Raw
    }
    return $outputs
}

New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
try {
    $props = New-FixtureFile 'valid.props' '<Project><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>'
    $prereleaseProps = New-FixtureFile 'prerelease.props' '<Project><PropertyGroup><Version>1.2.3-rc.1</Version></PropertyGroup></Project>'
    $changelog = New-FixtureFile 'valid.md' @'
# Changelog

## [1.2.3] - 2026-07-16
### Added
- Stable release notes.

## [1.2.3-rc.1] - 2026-07-16
### Fixed
- Prerelease notes.

## [1.2.2] - 2026-07-01
- Previous release.
'@

    $stable = Invoke-TrayGate -EventName 'push' -RefType 'tag' -RefName 'tray-v1.2.3' -PropsPath $props -ChangelogPath $changelog -WriteNotes
    if ($stable.version -ne '1.2.3' -or $stable.is_release -ne 'true' -or $stable.notes -notmatch 'Stable release notes') {
        throw 'The valid stable tag fixture did not preserve version, release mode, and extracted notes.'
    }

    $prerelease = Invoke-TrayGate -EventName 'push' -RefType 'tag' -RefName 'tray-v1.2.3-rc.1' -PropsPath $prereleaseProps -ChangelogPath $changelog
    if ($prerelease.version -ne '1.2.3-rc.1' -or $prerelease.is_release -ne 'true') {
        throw 'The admitted prerelease tag fixture was rejected or resolved incorrectly.'
    }

    Assert-Throws {
        Invoke-TrayGate -EventName 'push' -RefType 'tag' -RefName 'tray-v1.2.4' -PropsPath $props -ChangelogPath $changelog
    } 'does not match committed'
    Assert-Throws {
        Invoke-TrayGate -EventName 'push' -RefType 'tag' -RefName 'tray-v1.2' -PropsPath $props -ChangelogPath $changelog
    } 'exact tray-v<version>'

    $missingProps = New-FixtureFile 'missing.props' '<Project><PropertyGroup><Authors>Shelfbound</Authors></PropertyGroup></Project>'
    $duplicateProps = New-FixtureFile 'duplicate.props' '<Project><PropertyGroup><Version>1.2.3</Version><Version>1.2.4</Version></PropertyGroup></Project>'
    $blankProps = New-FixtureFile 'blank.props' '<Project><PropertyGroup><Version> </Version></PropertyGroup></Project>'
    $invalidProps = New-FixtureFile 'invalid.props' '<Project><PropertyGroup><Version>1.2</Version></PropertyGroup></Project>'
    $malformedProps = New-FixtureFile 'malformed.props' '<Project><PropertyGroup><Version>1.2.3</Version></Project>'
    Assert-Throws { Invoke-TrayGate -EventName 'workflow_dispatch' -RefType 'branch' -RefName 'main' -PropsPath $missingProps -ChangelogPath $changelog } 'no <Version>'
    Assert-Throws { Invoke-TrayGate -EventName 'workflow_dispatch' -RefType 'branch' -RefName 'main' -PropsPath $duplicateProps -ChangelogPath $changelog } 'ambiguous'
    Assert-Throws { Invoke-TrayGate -EventName 'workflow_dispatch' -RefType 'branch' -RefName 'main' -PropsPath $blankProps -ChangelogPath $changelog } 'blank <Version>'
    Assert-Throws { Invoke-TrayGate -EventName 'workflow_dispatch' -RefType 'branch' -RefName 'main' -PropsPath $invalidProps -ChangelogPath $changelog } 'must be x.y.z'
    Assert-Throws { Invoke-TrayGate -EventName 'workflow_dispatch' -RefType 'branch' -RefName 'main' -PropsPath $malformedProps -ChangelogPath $changelog } 'not valid XML'

    $absentChangelog = New-FixtureFile 'absent.md' "## [1.2.2]`n- Previous release."
    $duplicateChangelog = New-FixtureFile 'duplicate.md' "## [1.2.3]`n- First.`n`n## [1.2.3]`n- Second."
    $emptyChangelog = New-FixtureFile 'empty.md' "## [1.2.3]`n### Added`n`n## [1.2.2]`n- Previous release."
    Assert-Throws { Invoke-TrayGate -EventName 'push' -RefType 'tag' -RefName 'tray-v1.2.3' -PropsPath $props -ChangelogPath $absentChangelog } 'has no section'
    Assert-Throws { Invoke-TrayGate -EventName 'push' -RefType 'tag' -RefName 'tray-v1.2.3' -PropsPath $props -ChangelogPath $duplicateChangelog } 'ambiguous'
    Assert-Throws { Invoke-TrayGate -EventName 'push' -RefType 'tag' -RefName 'tray-v1.2.3' -PropsPath $props -ChangelogPath $emptyChangelog } 'no release-note content'

    $dispatch = Invoke-TrayGate -EventName 'workflow_dispatch' -RefType 'branch' -RefName 'main' -PropsPath $props -ChangelogPath (Join-Path $fixtureRoot 'not-needed.md')
    if ($dispatch.version -ne '1.2.3' -or $dispatch.is_release -ne 'false') {
        throw 'An ordinary dispatch must use committed props and remain artifact-only.'
    }

    $trayNamedDispatch = Invoke-TrayGate -EventName 'workflow_dispatch' -RefType 'branch' -RefName 'tray-v1.2.3' -PropsPath $props -ChangelogPath (Join-Path $fixtureRoot 'also-not-needed.md')
    if ($trayNamedDispatch.version -ne '1.2.3' -or $trayNamedDispatch.is_release -ne 'false') {
        throw 'A dispatch from a tray-v-looking branch must remain artifact-only.'
    }
}
finally {
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Tray release gate tests passed (tag identity, changelog, props, and dispatch fixtures).' -ForegroundColor Green
