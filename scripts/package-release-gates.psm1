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

Export-ModuleMember -Function @(
    'ConvertTo-ReleaseVersion',
    'Compare-ReleaseVersion',
    'Assert-PackageVersionNotReused',
    'Assert-SchemaReleasePolicy',
    'Assert-BreakingChangeReleasePolicy',
    'Assert-CloudPackagePin'
)
