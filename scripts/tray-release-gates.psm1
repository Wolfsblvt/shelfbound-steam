Set-StrictMode -Version Latest

function ConvertTo-TrayReleaseVersion {
    param([Parameter(Mandatory)][string]$Version)

    if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.]+)?$') {
        throw "Tray release version '$Version' must be x.y.z, optionally followed by a prerelease suffix."
    }

    return $Version
}

function Get-DirectoryBuildVersion {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Directory.Build.props was not found at '$Path'."
    }

    try {
        [xml]$props = Get-Content -LiteralPath $Path -Raw
    }
    catch {
        throw "Directory.Build.props at '$Path' is not valid XML: $($_.Exception.Message)"
    }

    $versions = @($props.SelectNodes('//*[local-name() = "Version"]'))
    if ($versions.Count -eq 0) {
        throw "Directory.Build.props at '$Path' has no <Version> value."
    }
    if ($versions.Count -ne 1) {
        throw "Directory.Build.props at '$Path' has $($versions.Count) <Version> values; tray release identity is ambiguous."
    }

    $version = $versions[0].InnerText
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "Directory.Build.props at '$Path' has a blank <Version> value."
    }

    return ConvertTo-TrayReleaseVersion $version
}

function Get-TrayReleaseNotes {
    param(
        [Parameter(Mandatory)][string]$ChangelogPath,
        [Parameter(Mandatory)][string]$Version
    )

    if (-not (Test-Path -LiteralPath $ChangelogPath -PathType Leaf)) {
        throw "CHANGELOG.md was not found at '$ChangelogPath'."
    }

    $changelog = Get-Content -LiteralPath $ChangelogPath -Raw
    $headers = [regex]::Matches($changelog, '(?m)^## \[(?<version>[^\]\r\n]+)\][^\r\n]*(?:\r?\n|$)')
    $matches = @($headers | Where-Object { $_.Groups['version'].Value -eq $Version })

    if ($matches.Count -eq 0) {
        throw "CHANGELOG.md has no section for [$Version]."
    }
    if ($matches.Count -ne 1) {
        throw "CHANGELOG.md has $($matches.Count) sections for [$Version]; tray release notes are ambiguous."
    }

    $header = $matches[0]
    $nextHeader = $headers | Where-Object { $_.Index -gt $header.Index } | Select-Object -First 1
    $sectionEnd = if ($nextHeader) { $nextHeader.Index } else { $changelog.Length }
    $notes = $changelog.Substring($header.Index + $header.Length, $sectionEnd - ($header.Index + $header.Length)).Trim()
    $contentLines = @($notes -split '\r?\n' | Where-Object {
            $line = $_.Trim()
            $line.Length -gt 0 -and -not $line.StartsWith('#')
        })

    if ($contentLines.Count -eq 0) {
        throw "CHANGELOG.md section [$Version] has no release-note content."
    }

    return $notes
}

function Resolve-TrayReleaseIdentity {
    param(
        [Parameter(Mandatory)][string]$EventName,
        [Parameter(Mandatory)][string]$RefType,
        [Parameter(Mandatory)][string]$RefName,
        [Parameter(Mandatory)][string]$PropsPath,
        [Parameter(Mandatory)][string]$ChangelogPath
    )

    $version = Get-DirectoryBuildVersion $PropsPath

    # A dispatch is an artifact rehearsal even if its selected branch happens to look like a tray tag.
    if ($EventName -eq 'workflow_dispatch') {
        return [pscustomobject]@{
            Version      = $version
            IsRelease    = $false
            ReleaseTag   = ''
            ReleaseNotes = $null
        }
    }

    if ($EventName -ne 'push' -or $RefType -ne 'tag') {
        throw "Tray release identity requires workflow_dispatch or a push tag event; got event '$EventName' with ref type '$RefType'."
    }

    $tagMatch = [regex]::Match($RefName, '^tray-v(?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z.]+)?)$')
    if (-not $tagMatch.Success) {
        throw "Tag '$RefName' must be an exact tray-v<version> release tag."
    }

    $tagVersion = ConvertTo-TrayReleaseVersion $tagMatch.Groups['version'].Value
    if ($tagVersion -ne $version) {
        throw "Tag version '$tagVersion' does not match committed Directory.Build.props version '$version'."
    }

    return [pscustomobject]@{
        Version      = $version
        IsRelease    = $true
        ReleaseTag   = $tagMatch.Value
        ReleaseNotes = Get-TrayReleaseNotes -ChangelogPath $ChangelogPath -Version $version
    }
}

Export-ModuleMember -Function @(
    'ConvertTo-TrayReleaseVersion',
    'Get-DirectoryBuildVersion',
    'Get-TrayReleaseNotes',
    'Resolve-TrayReleaseIdentity'
)
