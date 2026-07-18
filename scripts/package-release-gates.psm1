Set-StrictMode -Version Latest

function ConvertTo-ReleaseVersion {
    param([Parameter(Mandatory)][string]$Version)

    if ($Version -notmatch '^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)$') {
        throw "Release version '$Version' must be stable semver in x.y.z form."
    }

    return [pscustomobject]@{
        Raw   = $Version
        Major = [int]$Matches.major
        Minor = [int]$Matches.minor
        Patch = [int]$Matches.patch
    }
}

function Compare-ReleaseVersion {
    param(
        [Parameter(Mandatory)][string]$Left,
        [Parameter(Mandatory)][string]$Right
    )

    $leftVersion = ConvertTo-ReleaseVersion $Left
    $rightVersion = ConvertTo-ReleaseVersion $Right
    foreach ($property in @('Major', 'Minor', 'Patch')) {
        if ($leftVersion.$property -lt $rightVersion.$property) { return -1 }
        if ($leftVersion.$property -gt $rightVersion.$property) { return 1 }
    }
    return 0
}

function Assert-PackageVersionNotReused {
    param(
        [Parameter(Mandatory)][string]$PackageId,
        [Parameter(Mandatory)][string]$PackageVersion,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$PublishedVersions
    )

    [void](ConvertTo-ReleaseVersion $PackageVersion)
    if ($PublishedVersions -contains $PackageVersion) {
        throw "Package '$PackageId' version '$PackageVersion' is already published. Package identities are immutable."
    }
}

function Assert-SchemaReleasePolicy {
    param(
        [Parameter(Mandatory)][string]$BaselinePackageVersion,
        [Parameter(Mandatory)][string]$CurrentPackageVersion,
        [Parameter(Mandatory)][string]$BaselineSchemaVersion,
        [Parameter(Mandatory)][string]$CurrentSchemaVersion,
        [Parameter(Mandatory)][bool]$SchemaContractChanged
    )

    $packageComparison = Compare-ReleaseVersion $CurrentPackageVersion $BaselinePackageVersion
    if ($packageComparison -lt 0) {
        throw "Package version regressed from '$BaselinePackageVersion' to '$CurrentPackageVersion'."
    }

    $schemaVersionChanged = $CurrentSchemaVersion -ne $BaselineSchemaVersion
    if ($SchemaContractChanged -and -not $schemaVersionChanged) {
        throw "The snapshot schema changed without bumping SnapshotSchema.Version ('$CurrentSchemaVersion')."
    }

    if (($SchemaContractChanged -or $schemaVersionChanged) -and $packageComparison -le 0) {
        throw "Snapshot schema '$BaselineSchemaVersion' -> '$CurrentSchemaVersion' requires a new package version; '$CurrentPackageVersion' reuses '$BaselinePackageVersion'."
    }
}

function Assert-BreakingChangeReleasePolicy {
    param(
        [Parameter(Mandatory)][string]$BaselinePackageVersion,
        [Parameter(Mandatory)][string]$CurrentPackageVersion,
        [Parameter(Mandatory)][bool]$HasNewApiCompatSuppressions
    )

    if (-not $HasNewApiCompatSuppressions) { return }

    $baseline = ConvertTo-ReleaseVersion $BaselinePackageVersion
    $current = ConvertTo-ReleaseVersion $CurrentPackageVersion
    $breakingChangeAllowed = if ($baseline.Major -eq 0) {
        $current.Major -gt $baseline.Major -or
            ($current.Major -eq 0 -and $current.Minor -gt $baseline.Minor)
    }
    else {
        $current.Major -gt $baseline.Major
    }

    if (-not $breakingChangeAllowed) {
        $policy = if ($baseline.Major -eq 0) { 'a pre-1.0 minor bump' } else { 'a major bump' }
        throw "New API compatibility suppressions require $policy; '$BaselinePackageVersion' -> '$CurrentPackageVersion' is not sufficient."
    }
}

function Test-UseProjectApiCompatSuppressions {
    param(
        [Parameter(Mandatory)][string]$BaselinePackageVersion,
        [Parameter(Mandatory)][string]$CurrentPackageVersion
    )

    # Suppressions describe intentional breaks from an older release. Once the current version is published,
    # CI must compare against that exact package without letting those historical suppressions mask new breaks.
    return (Compare-ReleaseVersion $CurrentPackageVersion $BaselinePackageVersion) -gt 0
}

function Assert-CloudPackagePin {
    param(
        [Parameter(Mandatory)][string]$ProducerPackageVersion,
        [Parameter(Mandatory)][string]$CloudPackageVersion
    )

    [void](ConvertTo-ReleaseVersion $ProducerPackageVersion)
    [void](ConvertTo-ReleaseVersion $CloudPackageVersion)
    if ($ProducerPackageVersion -ne $CloudPackageVersion) {
        throw "Cloud pins Shelfbound packages '$CloudPackageVersion', but the producer declares '$ProducerPackageVersion'."
    }
}

function Test-ContractContentChanged {
    param(
        [Parameter(Mandatory)][string]$Baseline,
        [Parameter(Mandatory)][string]$Current
    )

    # PowerShell materializes native command output with the host OS newline. Compare repository text
    # semantically so an LF checkout does not look changed when the gate runs on Windows.
    $normalizedBaseline = ($Baseline -replace "`r`n?", "`n").TrimEnd()
    $normalizedCurrent = ($Current -replace "`r`n?", "`n").TrimEnd()
    return $normalizedBaseline -ne $normalizedCurrent
}

function Remove-TextByteOrderMark {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)

    # Native git output preserves a committed UTF-8 BOM as U+FEFF on some hosts. Text readers normally consume it,
    # so normalize git-loaded repository text before XML/JSON parsing or semantic comparison.
    return $Text.TrimStart([char]0xFEFF)
}

function ConvertTo-GitPath {
    param([Parameter(Mandatory)][string]$Path)

    return $Path.Replace('\', '/')
}

Export-ModuleMember -Function @(
    'ConvertTo-ReleaseVersion',
    'Compare-ReleaseVersion',
    'Assert-PackageVersionNotReused',
    'Assert-SchemaReleasePolicy',
    'Assert-BreakingChangeReleasePolicy',
    'Test-UseProjectApiCompatSuppressions',
    'Assert-CloudPackagePin',
    'Test-ContractContentChanged',
    'Remove-TextByteOrderMark',
    'ConvertTo-GitPath'
)
