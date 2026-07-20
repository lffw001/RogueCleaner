[CmdletBinding()]
param()

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$root = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $PSCommandPath }
$releaseRoot = Join-Path $root 'release'
$distRoot = Join-Path $root 'dist\流氓软件克星'
$objRoot = Join-Path $root 'obj'
$sfxRoot = Join-Path $objRoot 'sfx-oneclick'
$sfxConfig = Join-Path $objRoot 'oneclick-sfx.txt'
$publishStamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$sfxExe = Join-Path $releaseRoot "流氓软件克星_一键运行版_$publishStamp.exe"
$stableSfxExe = Join-Path $releaseRoot '流氓软件克星_一键运行版.exe'
$oldDir = Join-Path $releaseRoot '旧版勿发'
$sfxIcon = Join-Path $root 'src\app.ico'
$winrar = 'C:\Program Files\WinRAR\WinRAR.exe'
$sfxModule = 'C:\Program Files\WinRAR\Default.SFX'

$resolvedRoot = [System.IO.Path]::GetFullPath($root)
$resolvedObj = [System.IO.Path]::GetFullPath($objRoot)
$resolvedSfxRoot = [System.IO.Path]::GetFullPath($sfxRoot)
$resolvedRelease = [System.IO.Path]::GetFullPath($releaseRoot)
$resolvedOld = [System.IO.Path]::GetFullPath($oldDir)

if (!$resolvedObj.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "obj 目录不在项目目录下：$resolvedObj"
}
if (!$resolvedSfxRoot.StartsWith($resolvedObj, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "SFX 临时目录不在 obj 目录下：$resolvedSfxRoot"
}
if (!$resolvedOld.StartsWith($resolvedRelease, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "旧版目录不在 release 目录下：$resolvedOld"
}
if (!(Test-Path -LiteralPath $winrar -PathType Leaf)) {
    throw "缺少 WinRAR 程序：$winrar"
}
if (!(Test-Path -LiteralPath $sfxModule -PathType Leaf)) {
    throw "缺少 WinRAR SFX 模块：$sfxModule"
}
if (!(Test-Path -LiteralPath $sfxIcon -PathType Leaf)) {
    throw "缺少一键运行版图标：$sfxIcon"
}
if (!(Test-Path -LiteralPath (Join-Path $distRoot '流氓软件克星.exe') -PathType Leaf)) {
    throw "请先运行 Build-Exe.ps1 生成透明发布目录。"
}

New-Item -ItemType Directory -Path $objRoot -Force | Out-Null
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
New-Item -ItemType Directory -Path $oldDir -Force | Out-Null
if (Test-Path -LiteralPath $sfxRoot) {
    Remove-Item -LiteralPath $sfxRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $sfxRoot -Force | Out-Null

Copy-Item -LiteralPath (Join-Path $distRoot '流氓软件克星.exe') -Destination (Join-Path $sfxRoot 'RogueCleaner.exe') -Force
Copy-Item -LiteralPath (Join-Path $distRoot 'README.md') -Destination (Join-Path $sfxRoot 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $distRoot 'RogueCleaner.ps1') -Destination (Join-Path $sfxRoot 'RogueCleaner.ps1') -Force
Copy-Item -LiteralPath (Join-Path $distRoot 'rules') -Destination $sfxRoot -Recurse -Force
Copy-Item -LiteralPath (Join-Path $distRoot '先解压整个文件夹再运行.txt') -Destination (Join-Path $sfxRoot '先解压整个文件夹再运行.txt') -Force

$configLines = @(
    ';The comment below contains SFX script commands',
    'Path=%LOCALAPPDATA%\RogueCleaner\OneClick',
    'SavePath',
    'Setup=RogueCleaner.exe',
    'Silent=1',
    'Overwrite=1',
    'Title=RogueCleaner OneClick'
)
[System.IO.File]::WriteAllLines($sfxConfig, $configLines, [System.Text.Encoding]::ASCII)

function Quote-ProcessArgument {
    param([Parameter(Mandatory = $true)][string]$Value)
    if ($Value -notmatch '[\s"]') {
        return $Value
    }
    return '"' + $Value.Replace('"', '\"') + '"'
}

if (Test-Path -LiteralPath $stableSfxExe) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    Move-Item -LiteralPath $stableSfxExe -Destination (Join-Path $oldDir "流氓软件克星_一键运行版_旧固定名_$stamp.exe") -Force
}
Get-ChildItem -LiteralPath $releaseRoot -Filter '流氓软件克星_一键运行版_*.exe' -File -ErrorAction SilentlyContinue | ForEach-Object {
    Move-Item -LiteralPath $_.FullName -Destination (Join-Path $oldDir $_.Name) -Force
}

Push-Location $sfxRoot
try {
    $winrarArgs = @(
        'a',
        '-r',
        '-m5',
        '-inul',
        '-ibck',
        '-y',
        "-sfx$sfxModule",
        "-iicon$sfxIcon",
        "-z$sfxConfig",
        $sfxExe,
        '*'
    )
    $argumentLine = ($winrarArgs | ForEach-Object { Quote-ProcessArgument $_ }) -join ' '
    $process = Start-Process -FilePath $winrar -ArgumentList $argumentLine -WorkingDirectory $sfxRoot -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "SFX 打包失败，退出码 $($process.ExitCode)"
    }
    if (!(Test-Path -LiteralPath $sfxExe -PathType Leaf)) {
        throw "SFX 打包没有生成目标文件：$sfxExe"
    }
}
finally {
    Pop-Location
}

Write-Host "ONECLICK_PUBLISH=$sfxExe"
