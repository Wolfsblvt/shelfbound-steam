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

function Assert-WorkflowMatch {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$Pattern,
        [Parameter(Mandatory)][string]$Message
    )

    if ($Text -notmatch $Pattern) {
        throw $Message
    }
}

function Get-WorkflowJobBlocks {
    param([Parameter(Mandatory)][string[]]$Lines)

    $jobsStart = -1
    for ($index = 0; $index -lt $Lines.Count; $index++) {
        if ($Lines[$index] -match '^jobs:\s*(?:#.*)?$') {
            $jobsStart = $index
            break
        }
    }
    if ($jobsStart -lt 0) {
        throw 'Release workflow has no jobs mapping.'
    }

    $starts = @()
    for ($index = $jobsStart + 1; $index -lt $Lines.Count; $index++) {
        if ($Lines[$index] -match '^  (?<name>[A-Za-z][A-Za-z0-9_-]*):\s*(?:#.*)?$') {
            $starts += [pscustomobject]@{ Name = $Matches.name; Index = $index }
        }
    }

    $jobs = @{}
    for ($index = 0; $index -lt $starts.Count; $index++) {
        $end = if ($index + 1 -lt $starts.Count) { $starts[$index + 1].Index } else { $Lines.Count }
        $jobs[$starts[$index].Name] = $Lines[$starts[$index].Index..($end - 1)] -join "`n"
    }
    return $jobs
}

function Get-JobPermissionLines {
    param([Parameter(Mandatory)][string]$Job)

    $lines = $Job -split '\r?\n'
    $start = -1
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match '^    permissions:\s*(?:#.*)?$') {
            $start = $index
            break
        }
        if ($lines[$index] -match '^    permissions:') {
            return @($lines[$index].Trim())
        }
    }
    if ($start -lt 0) {
        return @()
    }

    $permissionLines = @()
    for ($index = $start + 1; $index -lt $lines.Count; $index++) {
        $line = $lines[$index]
        if ($line.Trim().Length -gt 0 -and $line -notmatch '^\s*#' -and $line -match '^\s{0,4}\S') {
            break
        }
        if ($line -match '^      \S') {
            $permissionLines += $line.Trim()
        }
    }
    return $permissionLines
}

function Assert-TrayReleaseWorkflowContract {
    param([Parameter(Mandatory)][string]$WorkflowPath)

    $workflow = Get-Content -LiteralPath $WorkflowPath -Raw
    $lines = @($workflow -split '\r?\n' | Where-Object { $_.Length -gt 0 })
    Assert-WorkflowMatch -Text $workflow -Pattern '(?m)^permissions:\s*(?:#.*)?$' -Message 'Release workflow must declare top-level default permissions.'

    $rootPermissionStart = [array]::IndexOf($lines, ($lines | Where-Object { $_ -match '^permissions:\s*(?:#.*)?$' } | Select-Object -First 1))
    $rootPermissionLines = @()
    for ($index = $rootPermissionStart + 1; $index -lt $lines.Count; $index++) {
        $line = $lines[$index]
        if ($line.Trim().Length -gt 0 -and $line -notmatch '^\s*#' -and $line -match '^\S') {
            break
        }
        if ($line -match '^  \S') {
            $rootPermissionLines += $line.Trim()
        }
    }
    if (@($rootPermissionLines).Count -ne 1 -or $rootPermissionLines[0] -notmatch '^contents:\s+read(?:\s+#.*)?$') {
        throw 'Release workflow must default to exactly contents: read, not workflow-wide write authority.'
    }

    $jobs = Get-WorkflowJobBlocks -Lines $lines
    $expectedJobs = @('identity', 'quality', 'windows', 'linux', 'macos')
    $actualJobNames = ($jobs.Keys | Sort-Object) -join ','
    $expectedJobNames = ($expectedJobs | Sort-Object) -join ','
    if ($actualJobNames -ne $expectedJobNames) {
        throw 'Release workflow job graph changed; update the permission contract deliberately.'
    }

    foreach ($publisher in @('windows', 'linux')) {
        $permissionLines = @(Get-JobPermissionLines -Job $jobs[$publisher])
        if ($permissionLines.Count -ne 1 -or $permissionLines[0] -notmatch '^contents:\s+write(?:\s+#.*)?$') {
            throw "Publishing job '$publisher' must have exactly contents: write."
        }
    }
    foreach ($nonPublisher in @('identity', 'quality', 'macos')) {
        if (@(Get-JobPermissionLines -Job $jobs[$nonPublisher]).Count -ne 0) {
            throw "Non-publishing job '$nonPublisher' must inherit the read-only workflow default."
        }
    }

    Assert-WorkflowMatch -Text $workflow -Pattern '(?m)^      - "tray-v\*"\s*$' -Message 'Release workflow must keep the tray-v* push-tag trigger.'
    Assert-WorkflowMatch -Text $workflow -Pattern '(?m)^  workflow_dispatch:\s*$' -Message 'Release workflow must retain workflow_dispatch artifact rehearsals.'
    Assert-WorkflowMatch -Text $jobs.windows -Pattern "(?ms)^      - name: Publish to GitHub Releases\s*\r?\n        if: needs\.identity\.outputs\.is_release == 'true'\s*\r?\n        run:" -Message 'Windows publication must remain gated by the fail-closed release identity output.'
    Assert-WorkflowMatch -Text $jobs.windows -Pattern "(?ms)^      - name: Set release notes from CHANGELOG\s*\r?\n        if: needs\.identity\.outputs\.is_release == 'true'\s*\r?\n        env:" -Message 'Windows release-note mutation must remain gated by the fail-closed release identity output.'
    Assert-WorkflowMatch -Text $jobs.linux -Pattern "(?ms)^      - name: Publish to GitHub Releases\s*\r?\n        if: needs\.identity\.outputs\.is_release == 'true'\s*\r?\n        run:" -Message 'Linux publication must remain gated by the fail-closed release identity output.'
    Assert-WorkflowMatch -Text $jobs.windows -Pattern '(?m)^    needs: \[identity, quality\]\s*$' -Message 'Windows must require identity and quality before publishing.'
    Assert-WorkflowMatch -Text $jobs.linux -Pattern '(?m)^    needs: \[identity, quality, windows\]\s*$' -Message 'Linux must wait for Windows to create the Release.'
    Assert-WorkflowMatch -Text $jobs.macos -Pattern '(?m)^    needs: \[identity, quality\]\s*$' -Message 'macOS must require identity and quality before artifact packaging.'
    if ($jobs.macos -match 'vpk upload github|gh release edit') {
        throw 'macOS must remain artifact-only and never mutate a GitHub Release.'
    }
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
    $workflow = Get-Content -LiteralPath (Join-Path $root '.github/workflows/release-tray.yml') -Raw
    $workflowPath = New-FixtureFile 'release-tray.yml' $workflow
    Assert-TrayReleaseWorkflowContract -WorkflowPath $workflowPath

    Assert-Throws {
        Assert-TrayReleaseWorkflowContract -WorkflowPath (New-FixtureFile 'workflow-wide-write.yml' ($workflow -replace '(?m)^  contents: read.*$', '  contents: write'))
    } 'default to exactly contents: read'
    Assert-Throws {
        Assert-TrayReleaseWorkflowContract -WorkflowPath (New-FixtureFile 'missing-windows-write.yml' ($workflow -replace '(?m)^      contents: write # creates.*$', '      contents: read'))
    } "Publishing job 'windows'"

    $macosWithWrite = [regex]::Replace(
        $workflow,
        '(?m)^(  macos:\r?\n(?:    .*\r?\n)*?    runs-on: macos-latest\r?\n)',
        { param($match) "$($match.Value)    permissions: write-all`n" }
    )
    if ($macosWithWrite -eq $workflow) {
        throw 'The workflow permission rejection fixture did not add macOS write access.'
    }
    Assert-Throws {
        Assert-TrayReleaseWorkflowContract -WorkflowPath (New-FixtureFile 'macos-write.yml' $macosWithWrite)
    } "Non-publishing job 'macos'"
    Assert-Throws {
        Assert-TrayReleaseWorkflowContract -WorkflowPath (New-FixtureFile 'unguarded-publish.yml' ([regex]::Replace($workflow, "if: needs\.identity\.outputs\.is_release == 'true'", 'if: always()', 1)))
    } 'must remain gated'

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

Write-Host 'Tray release gate tests passed (tag identity, changelog, props, dispatch, and workflow permissions).' -ForegroundColor Green
