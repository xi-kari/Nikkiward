[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Root,

    [string]$ManifestPath,

    [switch]$AllowInstallerArtifacts,

    [string]$Label = 'release payload',

    [string[]]$AdditionalBlockedHash = @()
)

$ErrorActionPreference = 'Stop'

function Get-RelativePath {
    param(
        [string]$BasePath,
        [string]$Path
    )

    return $Path.Substring($BasePath.Length).TrimStart('\', '/') -replace '/', '\'
}

function Get-FileHashText {
    param([string]$Path)

    return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToUpperInvariant()
}

$resolvedRoot = (Resolve-Path -LiteralPath $Root -ErrorAction Stop).Path.TrimEnd('\', '/')
if (-not (Test-Path -LiteralPath $resolvedRoot -PathType Container)) {
    throw "Release payload root is not a directory: $resolvedRoot"
}

$allEntries = @(Get-ChildItem -LiteralPath $resolvedRoot -Recurse -Force)
$reparseEntries = @($allEntries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })
if ($reparseEntries.Count -gt 0) {
    $paths = $reparseEntries | ForEach-Object { Get-RelativePath $resolvedRoot $_.FullName }
    throw "Release payload contains reparse points: $($paths -join ', ')"
}

$files = @($allEntries | Where-Object { -not $_.PSIsContainer })
$records = @{}
foreach ($file in $files) {
    $relative = Get-RelativePath $resolvedRoot $file.FullName
    $records[$relative] = [pscustomobject]@{
        RelativePath = $relative
        FullPath = $file.FullName
        Length = [long]$file.Length
        Hash = Get-FileHashText $file.FullName
    }
}

$blockedDirectoryNames = @(
    'Plugins',
    'ProtectedFavorites',
    'ArtCache',
    'Settings',
    'Cache',
    'Caches',
    'Backup',
    'Backups',
    'Temp',
    'Tmp',
    'Cookie',
    'Logs',
    'Log',
    'Cookies',
    'Token',
    'Tokens'
)
$blockedFileNames = @(
    'plugin.json'
)
$blockedExtensionPattern = '(?i)^(\.pdb|\.log|\.cache|\.cookie|\.token|\.sqlite|\.sqlite3|\.db|\.db3|\.bak|\.backup|\.old|\.orig|\.temp|\.tmp|\.user|\.suo)$'
$blockedPathRecords = @(
    $records.Values | Where-Object {
        $parts = $_.RelativePath -split '\\'
        ($parts | Where-Object { $blockedDirectoryNames -contains $_ }).Count -gt 0 -or
        $blockedFileNames -contains ([IO.Path]::GetFileName($_.RelativePath).ToLowerInvariant()) -or
        ([IO.Path]::GetExtension($_.RelativePath) -match $blockedExtensionPattern) -or
        ([IO.Path]::GetFileName($_.RelativePath) -match '(?i)^(settings?|logs?|caches?|cookies?|tokens?|backups?|temp|tmp)([-_.].*)?$')
    }
)
if ($AllowInstallerArtifacts) {
    $blockedPathRecords = @($blockedPathRecords | Where-Object {
        $_.RelativePath -notmatch '(?i)^unins\d{3}\.(exe|dat)$'
    })
}
if ($blockedPathRecords.Count -gt 0) {
    throw "Release payload privacy paths rejected: $(($blockedPathRecords | ForEach-Object RelativePath | Sort-Object) -join ', ')"
}

$allowedExecutablePaths = @(
    'Nikkiward.exe',
    'createdump.exe',
    'RestartAgent.exe',
    'avifdec.exe',
    'avifenc.exe',
    'avifgainmaputil.exe',
    'cjxl.exe',
    'djxl.exe',
    'jxlinfo.exe'
)
if ($AllowInstallerArtifacts) {
    $allowedExecutablePaths += 'unins000.exe'
}
$unexpectedExecutablePaths = @(
    $records.Values |
        Where-Object { [IO.Path]::GetExtension($_.RelativePath) -ieq '.exe' } |
        Where-Object { $allowedExecutablePaths -notcontains $_.RelativePath } |
        ForEach-Object RelativePath |
        Sort-Object
)
if ($unexpectedExecutablePaths.Count -gt 0) {
    throw "Release payload executable allowlist rejected: $($unexpectedExecutablePaths -join ', ')"
}

$allowedMediaPaths = @(
    'Assets\NikkiDefaultBackground.jpg',
    'Assets\NikkiPresetBackground2.jpg',
    'Assets\NikkiDefaultBackgroundBlur.jpg',
    'Assets\NikkiGameIcon.png',
    'Assets\NikkiwardIcon.ico',
    'Assets\XikariAvatar.jpg',
    'Assets\DefaultFavorites\01.jpg',
    'Assets\DefaultFavorites\02.jpg',
    'Assets\DefaultFavorites\03.jpg',
    'Assets\DefaultFavorites\04.jpg',
    'Assets\DefaultFavorites\05.jpg',
    'Microsoft.UI.Xaml\Assets\NoiseAsset_256x256_PNG.png'
)
$mediaExtensions = @(
    '.jpg', '.jpeg', '.png', '.webp', '.bmp', '.gif', '.tif', '.tiff', '.ico', '.avif', '.heic', '.heif',
    '.mp4', '.webm', '.mkv', '.mov', '.avi', '.wmv', '.m4v', '.flv',
    '.wav', '.mp3', '.flac', '.ogg', '.m4a', '.aac'
)
$unexpectedMediaPaths = @(
    $records.Values |
        Where-Object { $mediaExtensions -contains ([IO.Path]::GetExtension($_.RelativePath).ToLowerInvariant()) } |
        Where-Object { $allowedMediaPaths -notcontains $_.RelativePath } |
        ForEach-Object RelativePath |
        Sort-Object
)
if ($unexpectedMediaPaths.Count -gt 0) {
    throw "Release payload media allowlist rejected: $($unexpectedMediaPaths -join ', ')"
}

$unexpectedBinaryPaths = @(
    $records.Values |
        Where-Object { [IO.Path]::GetExtension($_.RelativePath) -ieq '.bin' } |
        Where-Object { $_.RelativePath -ne 'Shaders\LauncherNebula.bin' } |
        ForEach-Object RelativePath |
        Sort-Object
)
if ($unexpectedBinaryPaths.Count -gt 0) {
    throw "Release payload binary allowlist rejected: $($unexpectedBinaryPaths -join ', ')"
}

$requiredHashes = [ordered]@{
    'Assets\NikkiDefaultBackground.jpg' = '79E98642EC260C9CA8F4A89A12D8294B0474B78658DAB6DE330BFCB192514880'
    'Assets\NikkiPresetBackground2.jpg' = '7462F1C59F5DFAF23A850ADBF25D81C3163C37187735052249A690F9AADEB68B'
    'Assets\NikkiDefaultBackgroundBlur.jpg' = 'E4279123900181ED11C0C4249EFA1A881E20D4AF6FEF29D283D314281DBD9108'
    'Assets\NikkiGameIcon.png' = '58F6FF748453DF0509C050D35FF1B109D96DFCBB2910F8076FA604E1D1A4E103'
    'Assets\NikkiwardIcon.ico' = '984927AC315620ED7F3668B157C5CB11D5127C4E19217CAADEAC17B7EC3280BE'
    'Assets\XikariAvatar.jpg' = '56AD9D85D6AAB0828BA7BF279AFE1D0AE4271DCD3550C835C7BB0394E46CEEED'
    'Assets\DefaultFavorites\01.jpg' = '21093DD12A21385F76AD57819FF1EB2A80AF751579CB50EAF3C598BC0768F902'
    'Assets\DefaultFavorites\02.jpg' = '0FC974EE740B09D5E620F2AC34EB23126D56E8E957422BC580CB35C5AADBBB22'
    'Assets\DefaultFavorites\03.jpg' = '79E98642EC260C9CA8F4A89A12D8294B0474B78658DAB6DE330BFCB192514880'
    'Assets\DefaultFavorites\04.jpg' = 'C2ADB227F963C6C46F98874A04027E8169DEEF425AE87FC8437BA810F68E275D'
    'Assets\DefaultFavorites\05.jpg' = 'EC0C9FFE241C771256CE4B8500079850DC4FCCECA9F42B8F2D644A12DD672072'
    'Shaders\LauncherNebula.bin' = '50BBEC06E2C387675A83311458001154415D3F544C5CB06F39277A35B2482A6F'
    'runtimes\win-x64\native\nuan5_decryption.dll' = '3F0D88A2510106FF8E66A4730A77EF9F7FFC27C89411F81FA223CC3E1170E601'
}
$requiredPaths = @(
    'Nikkiward.exe',
    'Nikkiward.dll',
    'Nikkiward.deps.json',
    'Nikkiward.runtimeconfig.json',
    'Nikkiward.pri',
    'createdump.exe',
    'RestartAgent.exe',
    'avifdec.exe',
    'avifenc.exe',
    'avifgainmaputil.exe',
    'cjxl.exe',
    'djxl.exe',
    'jxlinfo.exe',
    'LICENSE',
    'PRIVACY.md',
    'THIRD-PARTY-NOTICES.md'
) + @($requiredHashes.Keys)
foreach ($relative in $requiredPaths) {
    if (-not $records.ContainsKey($relative)) {
        throw "Release payload file is missing: $relative"
    }
}
foreach ($entry in $requiredHashes.GetEnumerator()) {
    if ($records[$entry.Key].Hash -ne $entry.Value) {
        throw "Release payload hash mismatch: $($entry.Key) expected=$($entry.Value) actual=$($records[$entry.Key].Hash)"
    }
}

$defaultFavoritePaths = @($records.Keys | Where-Object { $_ -like 'Assets\DefaultFavorites\*.jpg' } | Sort-Object)
if ($defaultFavoritePaths.Count -ne 5 -or
    (Compare-Object $defaultFavoritePaths $requiredHashes.Keys.Where({ $_ -like 'Assets\DefaultFavorites\*.jpg' })) ) {
    throw "Release payload default favorite set rejected: $($defaultFavoritePaths -join ', ')"
}

$nativeHashCount = @($records.Values | Where-Object Hash -eq $requiredHashes['runtimes\win-x64\native\nuan5_decryption.dll']).Count
if ($nativeHashCount -ne 1) {
    throw "Release payload native dependency count rejected: $nativeHashCount"
}
$blockedHashes = @(
    '48A54DA85DA2570AAE87F76F0D773A47DD01011ACE7AFE66AABA831FACD2E069'
) + $AdditionalBlockedHash
$externalPluginMatches = @($records.Values | Where-Object { $blockedHashes -contains $_.Hash })
if ($externalPluginMatches.Count -ne 0) {
    throw "Release payload external gallery plugin detected: $(($externalPluginMatches | ForEach-Object RelativePath) -join ', ')"
}

$sensitivePatterns = @(
    '(?i)[A-Z]:\\Users\\[^\\\x00\r\n]+\\',
    '(?i)[A-Z]:\\[^\x00\r\n]*\\OneDrive\\',
    '(?i)/Users/[^/\x00\r\n]+/',
    '(?i)/home/[^/\x00\r\n]+/',
    '(?i)github_pat_[A-Za-z0-9_]+',
    '(?i)ghp_[A-Za-z0-9]+',
    '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'
)
$sensitiveScanRecords = @($records.Values)
if ($AllowInstallerArtifacts) {
    $sensitiveScanRecords = @($sensitiveScanRecords | Where-Object {
        $_.RelativePath -notmatch '(?i)^unins\d{3}\.(exe|dat)$'
    })
}
foreach ($record in $sensitiveScanRecords) {
    $bytes = [IO.File]::ReadAllBytes($record.FullPath)
    $singleByteText = [Text.Encoding]::Latin1.GetString($bytes)
    $wideText = [Text.Encoding]::Unicode.GetString($bytes)
    foreach ($pattern in $sensitivePatterns) {
        if ($singleByteText -match $pattern -or $wideText -match $pattern) {
            throw "Release payload sensitive content rejected: $($record.RelativePath)"
        }
    }
}

if (-not [string]::IsNullOrWhiteSpace($ManifestPath)) {
    $manifestLines = $records.Values |
        Sort-Object RelativePath |
        ForEach-Object { "$($_.RelativePath)|$($_.Length)|$($_.Hash)" }
    $manifestDirectory = Split-Path -Parent $ManifestPath
    if ($manifestDirectory) {
        New-Item -ItemType Directory -Force -Path $manifestDirectory | Out-Null
    }
    [IO.File]::WriteAllLines($ManifestPath, $manifestLines, [Text.UTF8Encoding]::new($false))
}

$executableCount = @($records.Values | Where-Object { [IO.Path]::GetExtension($_.RelativePath) -ieq '.exe' }).Count
Write-Output "PAYLOAD_VERIFY=PASS label=$Label files=$($records.Count) executables=$executableCount default_favorites=$($defaultFavoritePaths.Count)"
