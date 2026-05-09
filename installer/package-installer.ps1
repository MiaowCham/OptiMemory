param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$issPath = Join-Path $PSScriptRoot 'OptiMemory.iss'

if (-not $SkipBuild) {
    Write-Host '先执行 Release 非单文件发布（安装包输入）...' -ForegroundColor Cyan
    dotnet publish (Join-Path $repoRoot 'OptiMemory.csproj') -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -o (Join-Path $repoRoot 'publish\release')
}

$iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $candidate = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
    if (Test-Path $candidate) {
        $iscc = $candidate
    }
}

if (-not $iscc) {
    throw '未找到 ISCC.exe。请先安装 Inno Setup 6，或将 iscc 加入 PATH。'
}

Write-Host "使用 ISCC: $iscc" -ForegroundColor Green
Write-Host '开始打包安装程序...' -ForegroundColor Cyan
& $iscc $issPath

Write-Host '完成: dist 目录下已生成安装包。' -ForegroundColor Green
