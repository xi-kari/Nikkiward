[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SetupPath,

    [Parameter(Mandatory)]
    [string]$PublishDir,

    [Parameter(Mandatory)]
    [string]$TestRoot,

    [switch]$UseDefaultInstallPath
)

$ErrorActionPreference = 'Stop'

function Invoke-Installer {
    param(
        [string]$FilePath,
        [string[]]$ArgumentList
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.UseShellExecute = $false
    foreach ($argument in $ArgumentList) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        throw "Installer command failed: $FilePath exit=$($process.ExitCode)"
    }
}

function Get-TreeDigest {
    param([string]$Root)

    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        return 'ABSENT'
    }

    $lines = Get-ChildItem -LiteralPath $Root -Recurse -File -Force |
        ForEach-Object {
            $relative = $_.FullName.Substring($Root.Length).TrimStart('\')
            $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash
            "$relative|$hash|$($_.Length)"
        } |
        Sort-Object
    if (-not $lines) {
        return 'EMPTY'
    }

    $bytes = [System.Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))
    return [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes))
}

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -ne $Actual) {
        throw "$Message expected '$Expected' actual '$Actual'"
    }
}

function Assert-InstalledPayload {
    param(
        [string]$PublishRoot,
        [string]$InstallRoot,
        [string]$ValidatorPath
    )

    $sourceFiles = @(Get-ChildItem -LiteralPath $PublishRoot -Recurse -File | Sort-Object FullName)
    $sourcePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($source in $sourceFiles) {
        $relative = $source.FullName.Substring($PublishRoot.Length).TrimStart('\')
        $sourcePaths.Add($relative) | Out-Null
        $installed = Join-Path $InstallRoot $relative
        if (-not (Test-Path -LiteralPath $installed -PathType Leaf)) {
            throw "Installed file is missing: $relative"
        }
        Assert-Equal (Get-FileHash -Algorithm SHA256 -LiteralPath $source.FullName).Hash (Get-FileHash -Algorithm SHA256 -LiteralPath $installed).Hash "Installed file hash: $relative"
    }

    $unexpected = @(
        Get-ChildItem -LiteralPath $InstallRoot -Recurse -File -Force |
            ForEach-Object { $_.FullName.Substring($InstallRoot.Length).TrimStart('\') } |
            Where-Object {
                -not $sourcePaths.Contains($_) -and
                $_ -notmatch '(?i)^unins\d{3}\.(exe|dat)$'
            } |
            Sort-Object
    )
    if ($unexpected.Count -gt 0) {
        throw "Installed payload contains unexpected files: $($unexpected -join ', ')"
    }

    $null = & $ValidatorPath -Root $InstallRoot -AllowInstallerArtifacts -Label 'installed application payload'
    return $sourceFiles.Count
}

function Get-InstallRegistration {
    param([string]$InstallPath)

    $registrationPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{D5DBD5C6-0E4C-4BA8-B6D8-2B2E497E1AF3}_is1'
    if (-not (Test-Path -LiteralPath $registrationPath)) {
        return @()
    }

    $entry = Get-ItemProperty -LiteralPath $registrationPath -ErrorAction Stop
    if ($entry.InstallLocation -and
        [System.IO.Path]::GetFullPath($entry.InstallLocation).TrimEnd('\') -eq $InstallPath.TrimEnd('\')) {
        return @($entry)
    }

    return @()
}

function Wait-ForCondition {
    param(
        [scriptblock]$Condition,
        [string]$FailureMessage,
        [int]$TimeoutSeconds = 10
    )

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    do {
        if (& $Condition) {
            return
        }
        Start-Sleep -Milliseconds 100
    } while ($stopwatch.Elapsed.TotalSeconds -lt $TimeoutSeconds)

    throw $FailureMessage
}

function Wait-ForWindow {
    param([System.Diagnostics.Process]$Process, [int]$TimeoutSeconds = 20)
    for ($i = 0; $i -lt ($TimeoutSeconds * 10); $i++) {
        Start-Sleep -Milliseconds 100
        $Process.Refresh()
        if ($Process.HasExited) {
            throw "Installed app exited during startup: exit=$($Process.ExitCode)"
        }
        if ($Process.MainWindowHandle -ne 0) {
            return
        }
    }
    throw 'Installed app did not expose a window within the startup timeout.'
}

$resolvedSetupPath = (Resolve-Path -LiteralPath $SetupPath -ErrorAction Stop).Path
$resolvedPublishDir = (Resolve-Path -LiteralPath $PublishDir -ErrorAction Stop).Path
$resolvedTestRoot = [System.IO.Path]::GetFullPath($TestRoot)
$payloadValidatorPath = Join-Path $PSScriptRoot 'Test-ReleasePayload.ps1'
if (-not (Test-Path -LiteralPath $payloadValidatorPath -PathType Leaf)) {
    throw "Release payload validator is missing: $payloadValidatorPath"
}
$installDir = if ($UseDefaultInstallPath) {
    Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'Programs\Nikkiward'
} else {
    Join-Path $resolvedTestRoot 'Nikkiward 安装 验证'
}
$localDataDir = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'Nikkiward'
$shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Nikkiward\Nikkiward.lnk'
$installArguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-')
if (-not $UseDefaultInstallPath) {
    $installArguments += "/DIR=$installDir"
}

New-Item -ItemType Directory -Force -Path $resolvedTestRoot | Out-Null
if (Test-Path -LiteralPath $installDir) {
    if ($UseDefaultInstallPath) {
        throw "Default install path must be absent before the test: $installDir"
    }
    Remove-Item -LiteralPath $installDir -Recurse -Force
}

$dataBeforeInstall = Get-TreeDigest $localDataDir
Invoke-Installer $resolvedSetupPath $installArguments
Write-Output 'INSTALL_EXIT=0'

$publishFileCount = Assert-InstalledPayload $resolvedPublishDir $installDir $payloadValidatorPath
if (-not (Test-Path -LiteralPath (Join-Path $installDir 'unins000.exe') -PathType Leaf)) {
    throw 'Uninstaller is missing.'
}
if (-not (Test-Path -LiteralPath $shortcut -PathType Leaf)) {
    throw 'Start menu shortcut is missing.'
}
Assert-Equal 1 @(Get-InstallRegistration $installDir).Count 'Installer registration count'
Assert-Equal $dataBeforeInstall (Get-TreeDigest $localDataDir) 'Installer user-data preservation'
Write-Output "INSTALL_VERIFY=PASS files=$publishFileCount"

$executable = Join-Path $installDir 'Nikkiward.exe'
$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $executable
$startInfo.WorkingDirectory = $installDir
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.Environment['PATH'] = "$env:SystemRoot\System32;$env:SystemRoot"
$appProcess = [System.Diagnostics.Process]::Start($startInfo)
try {
    Wait-ForWindow $appProcess
    $appProcess.Refresh()
    Write-Output "LAUNCH_VERIFY=PASS pid=$($appProcess.Id) title=$($appProcess.MainWindowTitle)"
}
finally {
    if (-not $appProcess.HasExited) {
        $appProcess.CloseMainWindow() | Out-Null
        if (-not $appProcess.WaitForExit(5000)) {
            $appProcess.Kill()
            $appProcess.WaitForExit()
        }
    }
}

$dataBeforeRepair = Get-TreeDigest $localDataDir
$repairMissingPath = Join-Path $installDir 'Assets\NikkiGameIcon.png'
$repairChangedPath = Join-Path $installDir 'Nikkiward.dll'
Remove-Item -LiteralPath $repairMissingPath -Force
$repairStream = [IO.File]::Open($repairChangedPath, [IO.FileMode]::Append, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $repairStream.WriteByte(0xA5)
}
finally {
    $repairStream.Dispose()
}
Invoke-Installer $resolvedSetupPath $installArguments
$repairFileCount = Assert-InstalledPayload $resolvedPublishDir $installDir $payloadValidatorPath
Assert-Equal $dataBeforeRepair (Get-TreeDigest $localDataDir) 'Repair installer user-data preservation'
Write-Output "REPAIR_VERIFY=PASS files=$repairFileCount missing_resource_restored=True changed_dll_restored=True"

$dataBeforeUninstall = Get-TreeDigest $localDataDir
$uninstaller = Join-Path $installDir 'unins000.exe'
Invoke-Installer $uninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
Wait-ForCondition { -not (Test-Path -LiteralPath $installDir) } 'Uninstall left the install directory.'
Wait-ForCondition { -not (Test-Path -LiteralPath $shortcut) } 'Uninstall left the Start menu shortcut.'
Wait-ForCondition { @(Get-InstallRegistration $installDir).Count -eq 0 } 'Uninstall left its registration.'
Assert-Equal $dataBeforeUninstall (Get-TreeDigest $localDataDir) 'Uninstaller user-data preservation'
Write-Output 'UNINSTALL_VERIFY=PASS user_data_preserved=True'
Write-Output "RESULT install=PASS launch=PASS repair=PASS uninstall=PASS test_root=$resolvedTestRoot"
