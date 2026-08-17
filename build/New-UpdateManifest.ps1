[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Version,

    [Parameter(Mandatory)]
    [ValidateSet('stable', 'preview')]
    [string]$Channel,

    [Parameter(Mandatory)]
    [string]$PackagePath,

    [Parameter(Mandatory)]
    [string]$Repository,

    [Parameter(Mandatory)]
    [string]$Tag,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9A-Fa-f]{40}$')]
    [string]$CommitSha,

    [string]$MinimumSupportedVersion = '0.1.0-preview.1',

    [Parameter(Mandatory)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$semanticVersionPattern = '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$'

if ($Version -notmatch $semanticVersionPattern) {
    throw "Version is not valid semantic version text: $Version"
}
if ($Tag -ne "v$Version") {
    throw "Tag must equal v<Version>. Received $Tag for $Version."
}
if ($MinimumSupportedVersion -notmatch $semanticVersionPattern) {
    throw "MinimumSupportedVersion is not valid semantic version text: $MinimumSupportedVersion"
}
if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
    throw "Repository must use owner/name form: $Repository"
}

$resolvedPackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
$package = Get-Item -LiteralPath $resolvedPackagePath
if ($package.Length -le 0) {
    throw "Package is empty: $resolvedPackagePath"
}
if ($package.Name -ne 'Nikkiward-win-x64.zip') {
    throw "Package file name must be Nikkiward-win-x64.zip. Received $($package.Name)."
}

$sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $resolvedPackagePath).Hash

$manifest = [ordered]@{
    schemaVersion = 1
    product = 'Nikkiward'
    channel = $Channel
    version = $Version
    tag = $Tag
    commitSha = $CommitSha.ToLowerInvariant()
    minimumSupportedVersion = $MinimumSupportedVersion
    publishedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    package = [ordered]@{
        fileName = $package.Name
        sha256 = $sha256
        size = $package.Length
        runtimeIdentifier = 'win-x64'
        format = 'zip'
    }
    signature = $null
}

$resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($resolvedOutputPath)) | Out-Null
$json = $manifest | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText(
    $resolvedOutputPath,
    "$json`n",
    [System.Text.UTF8Encoding]::new($false))

Write-Output "UPDATE_MANIFEST_OK path=$resolvedOutputPath version=$Version channel=$Channel sha256=$sha256 size=$($package.Length)"
