#requires -Version 7

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'package-release-gates.psm1') -Force

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

    throw "Expected an error containing '$MessageFragment', but the gate accepted the input."
}

# T-16 rejection 1: an already-published immutable package version is never reusable.
Assert-Throws {
    Assert-PackageVersionNotReused -PackageId 'Shelfbound.Core' -PackageVersion '0.8.0' -PublishedVersions @('0.7.0', '0.8.0')
} 'already published'

# T-16 rejection 2: changing the schema contract requires both schema and package version bumps.
Assert-Throws {
    $arguments = @{
        BaselinePackageVersion = '0.8.0'
        CurrentPackageVersion = '0.8.0'
        BaselineSchemaVersion = '0.5.0'
        CurrentSchemaVersion = '0.6.0'
        SchemaContractChanged = $true
    }
    Assert-SchemaReleasePolicy @arguments
} 'requires a new package version'

# T-16 rejection 3: a new APICompat suppression is an explicit breaking change and needs the policy bump.
Assert-Throws {
    Assert-BreakingChangeReleasePolicy -BaselinePackageVersion '0.8.0' -CurrentPackageVersion '0.8.1' -HasNewApiCompatSuppressions $true
} 'pre-1.0 minor bump'

# T-16 rejection 4: the cloud consumer must pin the producer's immutable package version.
Assert-Throws {
    Assert-CloudPackagePin -ProducerPackageVersion '0.8.0' -CloudPackageVersion '0.7.0'
} 'Cloud pins'

# Positive controls keep the rejection fixtures honest.
Assert-PackageVersionNotReused -PackageId 'Shelfbound.Core' -PackageVersion '0.8.0' -PublishedVersions @('0.7.0')
$schemaArguments = @{
    BaselinePackageVersion = '0.7.0'
    CurrentPackageVersion = '0.8.0'
    BaselineSchemaVersion = '0.5.0'
    CurrentSchemaVersion = '0.6.0'
    SchemaContractChanged = $true
}
Assert-SchemaReleasePolicy @schemaArguments
Assert-BreakingChangeReleasePolicy -BaselinePackageVersion '0.7.0' -CurrentPackageVersion '0.8.0' -HasNewApiCompatSuppressions $true
Assert-CloudPackagePin -ProducerPackageVersion '0.8.0' -CloudPackageVersion '0.8.0'

$lfContract = "{`n  `"version`": 1`n}`n"
$crlfContract = $lfContract -replace "`n", "`r`n"
if (Test-ContractContentChanged -Baseline $crlfContract -Current $lfContract) {
    throw 'Equivalent LF and CRLF contracts must compare equal.'
}
if (-not (Test-ContractContentChanged -Baseline $lfContract -Current ($lfContract -replace '1', '2'))) {
    throw 'A semantic contract edit must compare changed.'
}
$bomPrefixedXml = "$([char]0xFEFF)<Suppressions />"
[xml]$bomDocument = Remove-TextByteOrderMark -Text $bomPrefixedXml
if ($bomDocument.DocumentElement.Name -ne 'Suppressions') {
    throw 'Git-loaded repository XML must parse after a leading byte-order mark is removed.'
}
if ((ConvertTo-GitPath -Path 'src\Shelfbound.Core\CompatibilitySuppressions.xml') -ne
    'src/Shelfbound.Core/CompatibilitySuppressions.xml') {
    throw 'Repository paths must use Git separators on every host OS.'
}

Write-Host 'Package release gate tests passed (4 rejection fixtures + text/platform controls).' -ForegroundColor Green
