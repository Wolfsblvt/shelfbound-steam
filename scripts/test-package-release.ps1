#requires -Version 7
<#
.SYNOPSIS
  Verifies the immutable Shelfbound library package contract before CI or a v* publish.
.DESCRIPTION
  Binds package version, snapshot schema, source commit, public API compatibility, and (when supplied)
  the cloud consumer pin. -CheckPublishedVersion and -RequireReleaseTag are the fail-closed publish gates.
#>
[CmdletBinding()]
param(
    [string]$CloudRepo,
    [switch]$CheckPublishedVersion,
    [switch]$RequireReleaseTag,
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$libraryProjects = [ordered]@{
    'Shelfbound.Core'  = 'src/Shelfbound.Core/Shelfbound.Core.csproj'
    'Shelfbound.Query' = 'src/Shelfbound.Query/Shelfbound.Query.csproj'
    'Shelfbound.Steam' = 'src/Shelfbound.Steam/Shelfbound.Steam.csproj'
}

Import-Module (Join-Path $PSScriptRoot 'package-release-gates.psm1') -Force

function Get-XmlProperty {
    param(
        [Parameter(Mandatory)][string]$XmlText,
        [Parameter(Mandatory)][string]$PropertyName
    )

    [xml]$document = $XmlText
    $node = $document.SelectSingleNode("//*[local-name()='$PropertyName']")
    if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) {
        throw "Property <$PropertyName> was not found."
    }
    return $node.InnerText.Trim()
}

function Get-GitFile {
    param(
        [Parameter(Mandatory)][string]$Ref,
        [Parameter(Mandatory)][string]$Path,
        [switch]$Optional
    )

    $content = (& git -C $root show "$($Ref):$Path" 2>$null) -join [Environment]::NewLine
    if ($LASTEXITCODE -ne 0) {
        if ($Optional) { return $null }
        throw "Could not read '$Path' at '$Ref'."
    }
    return $content
}

function Get-SnapshotSchemaVersion {
    param([Parameter(Mandatory)][string]$Source)

    $match = [regex]::Match($Source, 'public const string Version\s*=\s*"(?<version>\d+\.\d+\.\d+)";')
    if (-not $match.Success) { throw 'Could not read SnapshotSchema.Version.' }
    return $match.Groups['version'].Value
}

function Get-BaselineTag {
    param(
        [Parameter(Mandatory)][string]$CurrentVersion,
        [Parameter(Mandatory)][bool]$StrictlyOlder
    )

    foreach ($tag in (& git -C $root tag --list 'v[0-9]*' --sort=-version:refname)) {
        if ($tag -notmatch '^v(?<version>\d+\.\d+\.\d+)$') { continue }
        $tagVersion = $Matches.version
        $comparison = Compare-ReleaseVersion $tagVersion $CurrentVersion
        if ($comparison -lt 0 -or (-not $StrictlyOlder -and $comparison -eq 0)) { return $tag }
    }
    throw "No prior v* library release tag was found for package '$CurrentVersion'."
}

function Get-SuppressionKeys {
    param([AllowNull()][string]$XmlText)

    if ([string]::IsNullOrWhiteSpace($XmlText)) { return @() }
    [xml]$document = $XmlText
    return @($document.SelectNodes("//*[local-name()='Suppression']") | ForEach-Object {
        $diagnostic = $_.SelectSingleNode("./*[local-name()='DiagnosticId']").InnerText
        $target = $_.SelectSingleNode("./*[local-name()='Target']").InnerText
        "$diagnostic|$target"
    })
}

function Get-MSBuildProperty {
    param(
        [Parameter(Mandatory)][string]$Project,
        [Parameter(Mandatory)][string]$PropertyName
    )

    $output = @(& dotnet msbuild $Project -nologo "-getProperty:$PropertyName" 2>&1)
    if ($LASTEXITCODE -ne 0) { throw ($output -join [Environment]::NewLine) }
    $value = ($output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Last 1).ToString().Trim()
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "MSBuild property '$PropertyName' is empty for '$Project'."
    }
    return $value
}

function Get-NuspecMetadata {
    param([Parameter(Mandatory)][string]$PackagePath)

    Add-Type -AssemblyName System.IO.Compression
    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $entry = $archive.Entries | Where-Object FullName -Like '*.nuspec' | Select-Object -First 1
        if ($null -eq $entry) { throw "Package '$PackagePath' has no nuspec." }
        $reader = [System.IO.StreamReader]::new($entry.Open())
        try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally {
        $archive.Dispose()
    }

    $metadata = $nuspec.SelectSingleNode("//*[local-name()='metadata']")
    $repository = $metadata.SelectSingleNode("./*[local-name()='repository']")
    $releaseNotes = $metadata.SelectSingleNode("./*[local-name()='releaseNotes']")
    return [pscustomobject]@{
        Id           = $metadata.SelectSingleNode("./*[local-name()='id']").InnerText
        Version      = $metadata.SelectSingleNode("./*[local-name()='version']").InnerText
        ReleaseNotes = if ($null -eq $releaseNotes) { '' } else { $releaseNotes.InnerText }
        Commit       = if ($null -eq $repository) { '' } else { $repository.GetAttribute('commit') }
    }
}

function Assert-SymbolPackageSourceLink {
    param(
        [Parameter(Mandatory)][string]$SymbolPackagePath,
        [Parameter(Mandatory)][string]$PackageId,
        [Parameter(Mandatory)][string]$Commit
    )

    Add-Type -AssemblyName System.IO.Compression
    $archive = [System.IO.Compression.ZipFile]::OpenRead($SymbolPackagePath)
    try {
        $pdb = $archive.Entries | Where-Object FullName -EQ "lib/net10.0/$PackageId.pdb" | Select-Object -First 1
        if ($null -eq $pdb) { throw "Symbol package '$SymbolPackagePath' has no portable PDB for '$PackageId'." }
        $memory = [System.IO.MemoryStream]::new()
        $stream = $pdb.Open()
        try { $stream.CopyTo($memory) } finally { $stream.Dispose() }
        try { $pdbText = [System.Text.Encoding]::UTF8.GetString($memory.ToArray()) } finally { $memory.Dispose() }
    }
    finally {
        $archive.Dispose()
    }

    $expectedUrl = "https://raw.githubusercontent.com/Wolfsblvt/shelfbound-steam/$Commit/"
    if (-not $pdbText.Contains($expectedUrl)) {
        throw "Portable PDB for '$PackageId' does not contain Source Link for commit '$Commit'."
    }
}

function Assert-CloudReferencesUseCentralPin {
    param(
        [Parameter(Mandatory)][string]$Repo,
        [Parameter(Mandatory)][string]$ExpectedVersion
    )

    $apiProject = Join-Path $Repo 'src/Shelfbound.Cloud.Api/Shelfbound.Cloud.Api.csproj'
    $harnessProject = Join-Path $Repo 'tools/Shelfbound.LlmHarness/Shelfbound.LlmHarness.csproj'
    foreach ($project in @($apiProject, $harnessProject)) {
        if (-not (Test-Path -LiteralPath $project)) { throw "Cloud project not found: '$project'." }
        [xml]$document = Get-Content -Raw -LiteralPath $project
        $references = @($document.SelectNodes("//PackageReference[starts-with(@Include, 'Shelfbound.')]"))
        if ($references.Count -eq 0) { throw "No Shelfbound package references found in '$project'." }
        foreach ($reference in $references) {
            if ($reference.Version -ne '$(ShelfboundOpenCoreVersion)') {
                throw "Package '$($reference.Include)' in '$project' bypasses the central ShelfboundOpenCoreVersion pin."
            }
        }
    }

    $cloudVersion = Get-MSBuildProperty -Project $apiProject -PropertyName 'ShelfboundOpenCoreVersion'
    Assert-CloudPackagePin -ProducerPackageVersion $ExpectedVersion -CloudPackageVersion $cloudVersion
}

$props = Get-Content -Raw -LiteralPath (Join-Path $root 'Directory.Build.props')
$packageVersion = Get-XmlProperty -XmlText $props -PropertyName 'Version'
$declaredSchemaVersion = Get-XmlProperty -XmlText $props -PropertyName 'SnapshotSchemaVersion'
$schemaSource = Get-Content -Raw -LiteralPath (Join-Path $root 'src/Shelfbound.Core/SnapshotSchema.cs')
$schemaVersion = Get-SnapshotSchemaVersion $schemaSource
if ($declaredSchemaVersion -ne $schemaVersion) {
    throw "Directory.Build.props maps schema '$declaredSchemaVersion', but SnapshotSchema.Version is '$schemaVersion'."
}

if (-not $AllowDirty -and (& git -C $root status --porcelain)) {
    throw 'The steam worktree is dirty. Commit first so package payload and repository commit identify the same source.'
}

if ($RequireReleaseTag) {
    $expectedTag = "v$packageVersion"
    $pointingTags = @(& git -C $root tag --points-at HEAD)
    if ($pointingTags -notcontains $expectedTag) {
        throw "Release tag '$expectedTag' must point at HEAD before publishing."
    }
}

if ($CheckPublishedVersion) {
    foreach ($packageId in $libraryProjects.Keys) {
        $uri = "https://api.nuget.org/v3-flatcontainer/$($packageId.ToLowerInvariant())/index.json"
        try { $published = (Invoke-RestMethod -Uri $uri -TimeoutSec 30).versions }
        catch { throw "Could not verify published versions for '$packageId': $($_.Exception.Message)" }
        Assert-PackageVersionNotReused -PackageId $packageId -PackageVersion $packageVersion -PublishedVersions $published
    }
}

$baselineTag = Get-BaselineTag -CurrentVersion $packageVersion -StrictlyOlder:$CheckPublishedVersion
$baselineProps = Get-GitFile -Ref $baselineTag -Path 'Directory.Build.props'
$baselinePackageVersion = Get-XmlProperty -XmlText $baselineProps -PropertyName 'Version'
$baselineSchemaSource = Get-GitFile -Ref $baselineTag -Path 'src/Shelfbound.Core/SnapshotSchema.cs'
$baselineSchemaVersion = Get-SnapshotSchemaVersion $baselineSchemaSource
$baselineSchemaContract = (Get-GitFile -Ref $baselineTag -Path 'schema/snapshot.v0.schema.json').TrimEnd()
$currentSchemaContract = (Get-Content -Raw -LiteralPath (Join-Path $root 'schema/snapshot.v0.schema.json')).TrimEnd()
$schemaContractChanged = $currentSchemaContract -ne $baselineSchemaContract

$schemaArguments = @{
    BaselinePackageVersion = $baselinePackageVersion
    CurrentPackageVersion = $packageVersion
    BaselineSchemaVersion = $baselineSchemaVersion
    CurrentSchemaVersion = $schemaVersion
    SchemaContractChanged = $schemaContractChanged
}
Assert-SchemaReleasePolicy @schemaArguments

$newSuppressionFound = $false
foreach ($projectPath in $libraryProjects.Values) {
    $projectDirectory = Split-Path -Parent $projectPath
    $relativeSuppression = "$projectDirectory/CompatibilitySuppressions.xml"
    $currentSuppressionPath = Join-Path $root $relativeSuppression
    $currentKeys = if (Test-Path -LiteralPath $currentSuppressionPath) {
        Get-SuppressionKeys (Get-Content -Raw -LiteralPath $currentSuppressionPath)
    }
    else {
        @()
    }
    $baselineKeys = Get-SuppressionKeys (Get-GitFile -Ref $baselineTag -Path $relativeSuppression -Optional)
    if (@($currentKeys | Where-Object { $baselineKeys -notcontains $_ }).Count -gt 0) {
        $newSuppressionFound = $true
    }
}
Assert-BreakingChangeReleasePolicy -BaselinePackageVersion $baselinePackageVersion -CurrentPackageVersion $packageVersion -HasNewApiCompatSuppressions $newSuppressionFound

if ($CloudRepo) {
    $resolvedCloudRepo = (Resolve-Path -LiteralPath $CloudRepo).Path
    Assert-CloudReferencesUseCentralPin -Repo $resolvedCloudRepo -ExpectedVersion $packageVersion
}

foreach ($entry in $libraryProjects.GetEnumerator()) {
    $packable = Get-MSBuildProperty -Project (Join-Path $root $entry.Value) -PropertyName 'IsPackable'
    if ($packable -ne 'true') { throw "Library package '$($entry.Key)' is not packable." }
}
$storageProject = Join-Path $root 'src/Shelfbound.Storage/Shelfbound.Storage.csproj'
if ((Get-MSBuildProperty -Project $storageProject -PropertyName 'IsPackable') -ne 'false') {
    throw 'Shelfbound.Storage must remain non-packable; SnapshotStorage belongs to Shelfbound.Core.'
}

$runRoot = Join-Path ([System.IO.Path]::GetTempPath()) "shelfbound-package-release-$([Guid]::NewGuid().ToString('N'))"
$artifacts = Join-Path $runRoot 'packages'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
try {
    foreach ($project in $libraryProjects.Values) {
        $arguments = @(
            'pack', (Join-Path $root $project),
            '-c', 'Release',
            '-o', $artifacts,
            '-warnaserror',
            "-p:PackageVersion=$packageVersion",
            '-p:EnablePackageValidation=true',
            "-p:PackageValidationBaselineVersion=$baselinePackageVersion"
        )
        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) { throw "Package validation failed for '$project'." }
    }

    $commit = (& git -C $root rev-parse HEAD).Trim()
    foreach ($packageId in $libraryProjects.Keys) {
        $packagePath = Join-Path $artifacts "$packageId.$packageVersion.nupkg"
        $symbolsPath = Join-Path $artifacts "$packageId.$packageVersion.snupkg"
        if (-not (Test-Path -LiteralPath $packagePath)) { throw "Expected package missing: '$packagePath'." }
        if (-not (Test-Path -LiteralPath $symbolsPath)) { throw "Source Link symbol package missing: '$symbolsPath'." }

        $metadata = Get-NuspecMetadata $packagePath
        if ($metadata.Id -ne $packageId -or $metadata.Version -ne $packageVersion) {
            throw "Nuspec identity mismatch for '$packageId': '$($metadata.Id)' '$($metadata.Version)'."
        }
        if ($metadata.Commit -ne $commit) {
            throw "Nuspec commit '$($metadata.Commit)' does not match HEAD '$commit' for '$packageId'."
        }
        if ($metadata.ReleaseNotes -notlike "*Snapshot schema: $schemaVersion.*") {
            throw "Nuspec for '$packageId' does not embed snapshot schema '$schemaVersion'."
        }
        Assert-SymbolPackageSourceLink -SymbolPackagePath $symbolsPath -PackageId $packageId -Commit $commit
    }
}
finally {
    if (Test-Path -LiteralPath $runRoot) { Remove-Item -LiteralPath $runRoot -Recurse -Force }
}

Write-Host "Package release contract passed: packages $packageVersion, schema $schemaVersion, baseline $baselinePackageVersion ($baselineTag)." -ForegroundColor Green
