#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$UnityEditorPath
)
$ErrorActionPreference = 'Stop'
$releaseRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $releaseRoot 'package'
$manifest = Get-Content -LiteralPath (Join-Path $source 'package.json') -Raw | ConvertFrom-Json
if ($manifest.version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw 'Invalid package version.' }
$unity = (Get-Item -LiteralPath $UnityEditorPath).FullName
$project = Join-Path ([IO.Path]::GetTempPath()) ('utt-export-' + [Guid]::NewGuid())
$root = Join-Path $project 'Assets\YGTextTranslator'
$packageDirectory = Join-Path $project 'Packages'
$settingsDirectory = Join-Path $project 'ProjectSettings'
New-Item -ItemType Directory -Path $root, $packageDirectory, $settingsDirectory | Out-Null

$packageItems = @(
    'Editor', 'lexicons', 'package.json', 'README.md', 'CHANGELOG.md', 'LICENSE.md', 'THIRD PARTY NOTICES.md',
    'Editor.meta', 'lexicons.meta', 'package.json.meta', 'README.md.meta', 'CHANGELOG.md.meta',
    'LICENSE.md.meta', 'THIRD PARTY NOTICES.md.meta'
)
foreach ($item in $packageItems) {
    Copy-Item -LiteralPath (Join-Path $source $item) -Destination $root -Recurse
}
$dependencies = Join-Path $root 'Editor\Dependencies'
New-Item -ItemType Directory -Path $dependencies | Out-Null
Copy-Item -LiteralPath (Join-Path $source 'Dependencies~\0Harmony.dll') -Destination (Join-Path $dependencies '0Harmony.dll.bytes')

$folderMetas = @{
    (Join-Path $project 'Assets\YGTextTranslator.meta') = '705918afcb94444da6262d8d968c5198'
    (Join-Path $root 'Editor\Dependencies.meta') = '840d480175174565876c347c37089bb3'
}
foreach ($path in $folderMetas.Keys) {
    @"
fileFormatVersion: 2
guid: $($folderMetas[$path])
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
"@ | Set-Content -LiteralPath $path -Encoding utf8NoBOM
}
@"
fileFormatVersion: 2
guid: 67ede76e24884e85afc6a71b916da13e
TextScriptImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
"@ | Set-Content -LiteralPath (Join-Path $dependencies '0Harmony.dll.bytes.meta') -Encoding utf8NoBOM

'{"dependencies":{"com.unity.modules.imgui":"1.0.0","com.unity.modules.uielements":"1.0.0"}}' |
    Set-Content -LiteralPath (Join-Path $packageDirectory 'manifest.json') -Encoding utf8NoBOM
'm_EditorVersion: 2022.3.22f1' |
    Set-Content -LiteralPath (Join-Path $settingsDirectory 'ProjectVersion.txt') -Encoding utf8NoBOM

$stagedArtifact = Join-Path $project 'export.unitypackage'
$logPath = Join-Path $project 'export.log'
$launch = [Diagnostics.ProcessStartInfo]::new($unity)
$launch.UseShellExecute = $false
$launch.CreateNoWindow = $true
$launch.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
foreach ($argument in '-batchmode','-nographics','-quit','-projectPath',$project,'-logFile',$logPath,
    '-exportPackage','Assets/YGTextTranslator',$stagedArtifact) { $launch.ArgumentList.Add($argument) }
$process = [Diagnostics.Process]::Start($launch)
$process.WaitForExit()
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $stagedArtifact -PathType Leaf) -or
    (Get-Item -LiteralPath $stagedArtifact).Length -eq 0) { throw "Unity export failed. Log: $logPath" }

$artifact = Join-Path $releaseRoot "DangoImpact-$($manifest.version).unitypackage"
[IO.File]::Copy($stagedArtifact, $artifact, $true)
Write-Output $artifact
Write-Output "Export log $logPath"
