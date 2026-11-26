$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '../Nikkiward')).Path
foreach ($set in 'Fonts','Skins') {
    $folders = if ($set -eq 'Fonts') { 'fonts' } else { 'shared','skins' }
    $paths = foreach ($folder in $folders) {
        Get-ChildItem (Join-Path $project "Web/$folder") -File -Recurse | ForEach-Object {
            [IO.Path]::GetRelativePath($project, $_.FullName).Replace('\','/')
        }
    }
    [Array]::Sort($paths, [StringComparer]::Ordinal)
    $paths | ForEach-Object { "$( (Get-FileHash (Join-Path $project $_)).Hash )  $_" } |
        Set-Content (Join-Path $PSScriptRoot "Web$set.sha256") -Encoding utf8NoBOM
}
