#Requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$releaseRoot = Split-Path -Parent $PSScriptRoot
$releaseSource = Join-Path $releaseRoot 'package'
$releaseManifest = Get-Content -LiteralPath (Join-Path $releaseSource 'package.json') -Raw -Encoding UTF8 | ConvertFrom-Json -AsHashtable
$releaseName = $releaseManifest.name
$releaseVersion = $releaseManifest.version
if ($releaseName -notmatch '^[a-z0-9]+(?:[.-][a-z0-9]+)+$' -or $releaseVersion -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw 'Invalid package name or version in package/package.json.'
}
$releaseArchive = "$releaseName-$releaseVersion.zip"
$releaseArchivePath = Join-Path $releaseRoot $releaseArchive
$releaseTemp = Join-Path $releaseRoot ('.release-' + [Guid]::NewGuid().ToString('N') + '.zip')
$releaseFiles = Get-ChildItem -LiteralPath $releaseSource -Recurse -Force -File | Sort-Object -Property FullName
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
try {
    $releaseZip = [IO.Compression.ZipFile]::Open($releaseTemp, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($releaseFile in $releaseFiles) {
            $releaseEntryName = [IO.Path]::GetRelativePath($releaseSource, $releaseFile.FullName).Replace('\', '/')
            $releaseEntry = $releaseZip.CreateEntry($releaseEntryName, [IO.Compression.CompressionLevel]::Optimal)
            $releaseEntry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $releaseInput = [IO.File]::OpenRead($releaseFile.FullName)
            try {
                $releaseOutput = $releaseEntry.Open()
                try { $releaseInput.CopyTo($releaseOutput) }
                finally { $releaseOutput.Dispose() }
            }
            finally { $releaseInput.Dispose() }
        }
    }
    finally {
        $releaseZip.Dispose()
    }
    [IO.File]::Move($releaseTemp, $releaseArchivePath, $true)
}
finally {
    if (Test-Path -LiteralPath $releaseTemp) { Remove-Item -LiteralPath $releaseTemp }
}

$releaseHash = (Get-FileHash -LiteralPath $releaseArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$releaseBaseUrl = 'https://raw.githubusercontent.com/xinling464/unity-text-translator/main/'
$releaseIndexPath = Join-Path $releaseRoot 'index.json'
$releaseIndex = Get-Content -LiteralPath $releaseIndexPath -Raw -Encoding UTF8 | ConvertFrom-Json -AsHashtable
$releaseVersions = $releaseIndex.packages[$releaseName].versions
$releaseIndex.author = $releaseManifest.author
$releaseManifest.url = $releaseBaseUrl + $releaseArchive
$releaseManifest.zipSHA256 = $releaseHash
if (!$releaseManifest.Contains('dependencies')) { $releaseManifest.dependencies = @{} }
if (!$releaseManifest.Contains('vpmDependencies')) { $releaseManifest.vpmDependencies = @{} }
$releaseVersions[$releaseVersion] = $releaseManifest
[IO.File]::WriteAllText($releaseIndexPath, (($releaseIndex | ConvertTo-Json -Depth 20) -replace "\r\n", [string][char]10) + [char]10)

$releaseProxy = 'https://ghproxy.net/'
$releaseMirrorPath = Join-Path $releaseRoot 'index-ghproxy.json'
$releaseMirror = Get-Content -LiteralPath $releaseMirrorPath -Raw -Encoding UTF8 | ConvertFrom-Json -AsHashtable
$releaseMirror.url = $releaseProxy + $releaseBaseUrl + 'index-ghproxy.json'
$releaseMirror.author = $releaseManifest.author
$releaseMirrorVersions = $releaseMirror.packages[$releaseName].versions
foreach ($releaseVersionKey in $releaseVersions.Keys) {
    $releaseVersionInfo = $releaseVersions[$releaseVersionKey] | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
    $releaseVersionInfo.url = $releaseProxy + $releaseVersionInfo.url
    $releaseMirrorVersions[$releaseVersionKey] = $releaseVersionInfo
}
[IO.File]::WriteAllText($releaseMirrorPath, (($releaseMirror | ConvertTo-Json -Depth 20) -replace "\r\n", [string][char]10) + [char]10)
Write-Output "$releaseArchivePath"
Write-Output "SHA-256 $releaseHash"
