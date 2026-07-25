[CmdletBinding()]
param(
    [switch]$PackageOnly,
    [switch]$ValidationBuild
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$root = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $PSCommandPath }
$v2Src = Join-Path $root 'src\v2'
$manifest = Join-Path $root 'src\app.manifest'
$icon = Join-Path $root 'src\app.ico'
$obj = Join-Path $root 'obj'
$exe = Join-Path $root '流氓软件克星.exe'
$dist = Join-Path $root 'dist\流氓软件克星'
$resolvedRoot = [System.IO.Path]::GetFullPath($root)
$resolvedDist = [System.IO.Path]::GetFullPath($dist)

if (!$PackageOnly) {
    New-Item -ItemType Directory -Path $obj -Force | Out-Null

    if (!(Test-Path -LiteralPath $v2Src -PathType Container)) {
        throw "缺少 v2 源码目录：$v2Src"
    }
    $sources = @(
        Get-ChildItem -LiteralPath $v2Src -Filter '*.cs' -File |
            Sort-Object FullName |
            ForEach-Object { $_.FullName }
    )
    if ($sources.Count -eq 0) {
        throw "没有找到 C# 源码文件。"
    }

    $cscCandidates = @(
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
        "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    )
    $csc = $cscCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (!$csc) {
        throw '没有找到 .NET Framework csc.exe，无法构建 exe。'
    }
    if (!(Test-Path -LiteralPath $icon -PathType Leaf)) {
        throw "缺少图标文件：$icon"
    }

    $cscArgs = @(
        '/nologo',
        '/target:winexe',
        '/platform:anycpu',
        "/win32manifest:$manifest",
        "/win32icon:$icon",
        '/reference:System.Windows.Forms.dll',
        '/reference:System.Drawing.dll',
        '/reference:System.Web.Extensions.dll',
        '/reference:System.Management.dll',
        '/reference:System.ServiceProcess.dll',
        '/reference:Microsoft.CSharp.dll',
        "/out:$exe"
    ) + $sources
    if ($ValidationBuild) {
        $cscArgs = @('/define:VALIDATION') + $cscArgs
    }
    & $csc @cscArgs
    if ($LASTEXITCODE -ne 0) {
        throw "csc 构建失败，退出码 $LASTEXITCODE"
    }
}

if (!$resolvedDist.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "发布目录不在项目目录下，拒绝清理：$resolvedDist"
}

if (Test-Path -LiteralPath $resolvedDist) {
    Remove-Item -LiteralPath $resolvedDist -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedDist -Force | Out-Null

Copy-Item -LiteralPath $exe -Destination (Join-Path $resolvedDist '流氓软件克星.exe') -Force

Write-Host "EXE=$exe"
Write-Host "PACKAGE=$resolvedDist"
