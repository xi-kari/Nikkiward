[CmdletBinding()]
param(
    [string]$Destination = (Join-Path $PSScriptRoot '..\Nikkiward\runtimes\win-x64\native\nuan5_decryption.dll'),
    [string]$SourcePath
)

$ErrorActionPreference = 'Stop'

$sourceUri = 'https://raw.githubusercontent.com/QianQianLuLu1/NikkiGallery/ca8ac9fbc97d449ebc8dc8d08997c93b00a882e9/resources/nuan5_decryption.dll'
$expectedSha256 = '3F0D88A2510106FF8E66A4730A77EF9F7FFC27C89411F81FA223CC3E1170E601'
$destinationPath = [System.IO.Path]::GetFullPath($Destination)
$destinationDirectory = [System.IO.Path]::GetDirectoryName($destinationPath)
$temporaryPath = "$destinationPath.download-$([Guid]::NewGuid().ToString('N'))"

[System.IO.Directory]::CreateDirectory($destinationDirectory) | Out-Null

try {
    $downloaded = $false
    $workspaceSource = Join-Path $PSScriptRoot '..\InfinityNikki\NikkiGallery\resources\nuan5_decryption.dll'
    $resolvedSourcePath = if (-not [string]::IsNullOrWhiteSpace($SourcePath)) {
        (Resolve-Path -LiteralPath $SourcePath).Path
    }
    elseif (Test-Path -LiteralPath $workspaceSource) {
        (Resolve-Path -LiteralPath $workspaceSource).Path
    }
    else {
        $null
    }

    if ($resolvedSourcePath) {
        Copy-Item -LiteralPath $resolvedSourcePath -Destination $temporaryPath
        $downloaded = $true
    }
    else {
        for ($attempt = 1; $attempt -le 3; $attempt++) {
            try {
                Invoke-WebRequest -Uri $sourceUri -OutFile $temporaryPath
                $downloaded = $true
                break
            }
            catch {
                if ($attempt -eq 3) {
                    throw
                }
                Start-Sleep -Seconds (2 * $attempt)
            }
        }
    }
    if (-not $downloaded) {
        throw 'Native dependency download did not complete.'
    }
    $actualSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $temporaryPath).Hash
    if (-not [string]::Equals($actualSha256, $expectedSha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "nuan5_decryption.dll SHA-256 mismatch. Expected $expectedSha256, received $actualSha256."
    }

    Move-Item -LiteralPath $temporaryPath -Destination $destinationPath -Force
    Write-Output "NATIVE_DEPENDENCY_OK path=$destinationPath sha256=$actualSha256"
}
finally {
    if (Test-Path -LiteralPath $temporaryPath) {
        Remove-Item -LiteralPath $temporaryPath -Force
    }
}
