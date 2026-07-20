[CmdletBinding()]
param()

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$root = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $PSCommandPath }
$rar = 'C:\Program Files\WinRAR\Rar.exe'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$releaseRoot = Join-Path $root 'release'
$oldDir = Join-Path $releaseRoot '旧版勿发'
$workRoot = Join-Path $releaseRoot "package-$stamp"
$sourceStage = Join-Path $workRoot '流氓软件克星-源码'
$exeStage = Join-Path $workRoot '流氓软件克星-EXE透明发布版'

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
Get-ChildItem -LiteralPath $releaseRoot -Filter '*.rar' -File | ForEach-Object {
    Move-Item -LiteralPath $_.FullName -Destination (Join-Path $oldDir $_.Name) -Force
}
if (Test-Path -LiteralPath $workRoot) {
    Remove-Item -LiteralPath $workRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $sourceStage -Force | Out-Null
New-Item -ItemType Directory -Path $exeStage -Force | Out-Null

foreach ($file in @(
    'README.md',
    'LICENSE',
    'Build-Exe.ps1',
    'Build-OneClick-Sfx.ps1',
    'Build-Release-Packages.ps1',
    'RogueCleaner.ps1',
    '.gitignore',
    '先解压整个文件夹再运行.txt'
)) {
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $sourceStage -Force
}
Copy-Item -LiteralPath (Join-Path $root 'src') -Destination $sourceStage -Recurse -Force
foreach ($dir in @('docs', 'rules')) {
    $dirPath = Join-Path $root $dir
    if (Test-Path -LiteralPath $dirPath -PathType Container) {
        Copy-Item -LiteralPath $dirPath -Destination $sourceStage -Recurse -Force
    }
}

foreach ($file in @(
    '流氓软件克星.exe',
    'README.md'
)) {
    Copy-Item -LiteralPath (Join-Path $root "dist\流氓软件克星\$file") -Destination $exeStage -Force
}
foreach ($file in @('52pojie发布文案.md', '火绒误报回复.md', 'Win10Win11报错回复.md')) {
    $releaseFile = Join-Path $releaseRoot $file
    if (Test-Path -LiteralPath $releaseFile -PathType Leaf) {
        Copy-Item -LiteralPath $releaseFile -Destination $exeStage -Force
    }
}

$sourceArchive = Join-Path $releaseRoot "流氓软件克星_源码_$stamp.rar"
$exeArchive = Join-Path $releaseRoot "流氓软件克星_EXE透明发布版_$stamp.rar"
Push-Location $workRoot
try {
    & $rar a -r -m5 -idq $sourceArchive '流氓软件克星-源码'
    if ($LASTEXITCODE -ne 0) { throw "源码 RAR 打包失败，退出码 $LASTEXITCODE" }
    & $rar a -r -m5 -idq $exeArchive '流氓软件克星-EXE透明发布版'
    if ($LASTEXITCODE -ne 0) { throw "EXE RAR 打包失败，退出码 $LASTEXITCODE" }
}
finally {
    Pop-Location
}
Move-Item -LiteralPath $workRoot -Destination (Join-Path $oldDir (Split-Path -Leaf $workRoot)) -Force

Write-Host "SOURCE=$sourceArchive"
Write-Host "TRANSPARENT=$exeArchive"
