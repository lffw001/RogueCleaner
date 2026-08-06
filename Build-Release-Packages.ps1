[CmdletBinding()]
param()

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$root = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $PSCommandPath }
$rar = 'C:\Program Files\WinRAR\Rar.exe'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$version = '2.0.14'
$releaseRoot = Join-Path $root 'release'
$oldDir = Join-Path $releaseRoot '_old'
$workRoot = Join-Path $releaseRoot "package-$stamp"
$sourceStage = Join-Path $workRoot 'RogueCleaner-Source'
$exeStage = Join-Path $workRoot 'RogueCleaner-Transparent'

$resolvedRelease = [System.IO.Path]::GetFullPath($releaseRoot)
$resolvedOld = [System.IO.Path]::GetFullPath($oldDir)
$resolvedWork = [System.IO.Path]::GetFullPath($workRoot)
if (!$resolvedOld.StartsWith($resolvedRelease, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "旧版目录不在 release 目录下：$resolvedOld"
}
if (!$resolvedWork.StartsWith($resolvedRelease, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "打包临时目录不在 release 目录下：$resolvedWork"
}
if (!(Test-Path -LiteralPath $rar -PathType Leaf)) {
    throw "缺少 WinRAR 命令行：$rar"
}

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
New-Item -ItemType Directory -Path $oldDir -Force | Out-Null
$staleDataDir = Join-Path $releaseRoot '流氓软件克星数据'
if (Test-Path -LiteralPath $staleDataDir -PathType Container) {
    Move-Item -LiteralPath $staleDataDir -Destination (Join-Path $oldDir "stale-runtime-data-$stamp") -Force
}
Get-ChildItem -LiteralPath $releaseRoot -File | Where-Object { $_.Extension -in @('.rar', '.exe') -or $_.Name -like 'SHA256SUMS-v*.txt' } | ForEach-Object {
    Move-Item -LiteralPath $_.FullName -Destination (Join-Path $oldDir $_.Name) -Force
}
if (Test-Path -LiteralPath $workRoot) {
    Remove-Item -LiteralPath $workRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $sourceStage -Force | Out-Null
New-Item -ItemType Directory -Path $exeStage -Force | Out-Null

foreach ($file in @(
    'LICENSE',
    'Build-Exe.ps1',
    'Build-Release-Packages.ps1',
    '.gitignore'
)) {
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $sourceStage -Force
}
$sourceSrc = Join-Path $sourceStage 'src'
New-Item -ItemType Directory -Path $sourceSrc -Force | Out-Null
foreach ($file in @(
    'app.manifest',
    'app.ico',
    'app-icon.png'
)) {
    $path = Join-Path $root "src\$file"
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        Copy-Item -LiteralPath $path -Destination $sourceSrc -Force
    }
}
Copy-Item -LiteralPath (Join-Path $root 'src\v2') -Destination $sourceSrc -Recurse -Force

foreach ($file in @(
    '流氓软件克星.exe'
)) {
    Copy-Item -LiteralPath (Join-Path $root "dist\流氓软件克星\$file") -Destination $exeStage -Force
}

$sourceArchive = Join-Path $releaseRoot "RogueCleaner-Source-v$version-$stamp.rar"
$exeArchive = Join-Path $releaseRoot "RogueCleaner-Transparent-v$version-$stamp.rar"
$directExe = Join-Path $releaseRoot "RogueCleaner-v$version-$stamp.exe"
$hashFile = Join-Path $releaseRoot "SHA256SUMS-v$version-$stamp.txt"
Push-Location $workRoot
try {
    & $rar a -r -m5 -idq $sourceArchive 'RogueCleaner-Source'
    if ($LASTEXITCODE -ne 0) { throw "源码 RAR 打包失败，退出码 $LASTEXITCODE" }
    & $rar a -r -m5 -idq $exeArchive 'RogueCleaner-Transparent'
    if ($LASTEXITCODE -ne 0) { throw "EXE RAR 打包失败，退出码 $LASTEXITCODE" }
}
finally {
    Pop-Location
}
Copy-Item -LiteralPath (Join-Path $root 'dist\流氓软件克星\流氓软件克星.exe') -Destination $directExe -Force
$hashLines = @()
foreach ($path in @($directExe, $exeArchive, $sourceArchive)) {
    $hash = Get-FileHash -LiteralPath $path -Algorithm SHA256
    $hashLines += "$($hash.Hash.ToLowerInvariant())  $(Split-Path -Leaf $path)"
}
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllLines($hashFile, [string[]]$hashLines, $utf8NoBom)
Move-Item -LiteralPath $workRoot -Destination (Join-Path $oldDir (Split-Path -Leaf $workRoot)) -Force

Write-Host "SOURCE=$sourceArchive"
Write-Host "TRANSPARENT=$exeArchive"
Write-Host "DIRECT_EXE=$directExe"
Write-Host "SHA256SUMS=$hashFile"
