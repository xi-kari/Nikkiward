[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PublishDir,

    [Parameter(Mandatory)]
    [string]$OutputDir,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$')]
    [string]$VersionInfoVersion,

    [string]$IsccPath
)

$ErrorActionPreference = 'Stop'

function Resolve-IsccPath {
    param([string]$RequestedPath)

    if ($RequestedPath) {
        $resolved = (Resolve-Path -LiteralPath $RequestedPath -ErrorAction Stop).Path
        return $resolved
    }

    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

    if (-not $candidates) {
        throw 'ISCC.exe was not found. Install Inno Setup 6 or pass -IsccPath.'
    }

    return (Resolve-Path -LiteralPath $candidates[0]).Path
}

$resolvedPublishDir = (Resolve-Path -LiteralPath $PublishDir -ErrorAction Stop).Path
$resolvedOutputDir = [System.IO.Path]::GetFullPath($OutputDir)
$resolvedIsccPath = Resolve-IsccPath $IsccPath
$versionInfo = if ($VersionInfoVersion) { $VersionInfoVersion } else { ($Version -split '-', 2)[0] + '.0' }

$payloadValidatorPath = Join-Path $PSScriptRoot 'Test-ReleasePayload.ps1'
if (-not (Test-Path -LiteralPath $payloadValidatorPath -PathType Leaf)) {
    throw "Release payload validator is missing: $payloadValidatorPath"
}
& $payloadValidatorPath -Root $resolvedPublishDir -Label 'installer publish input'

New-Item -ItemType Directory -Force -Path $resolvedOutputDir | Out-Null
$issPath = Join-Path $PSScriptRoot 'Nikkiward.iss'
$arguments = @(
    "/DMyAppVersion=$Version",
    "/DMyVersionInfoVersion=$versionInfo",
    "/DPublishDir=$resolvedPublishDir",
    "/DOutputDir=$resolvedOutputDir",
    $issPath
)
& $resolvedIsccPath @arguments
if ($LASTEXITCODE -ne 0) {
    throw "ISCC.exe failed with exit code $LASTEXITCODE."
}

$setupPath = Join-Path $resolvedOutputDir 'Nikkiward-Setup-win-x64.exe'
if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
    throw "Installer output is missing: $setupPath"
}

$setup = Get-Item -LiteralPath $setupPath
Write-Output "INSTALLER_OK path=$($setup.FullName) size=$($setup.Length) version=$Version sha256=$((Get-FileHash -Algorithm SHA256 -LiteralPath $setup.FullName).Hash)"
