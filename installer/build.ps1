param(
    [ValidateSet('1', '2', '3')]
    [string]$Choice
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $repoRoot 'OptiMemory.csproj'

if (-not $Choice) {
    Write-Host '请选择构建类型:' -ForegroundColor Cyan
    Write-Host '  1) Debug 构建' -ForegroundColor Yellow
    Write-Host '  2) Release 构建（框架依赖，非单文件，安装包使用）' -ForegroundColor Yellow
    Write-Host '  3) Release 单文件发布（框架依赖）' -ForegroundColor Yellow
    $Choice = Read-Host '输入数字 1/2/3'
}

Set-Location $repoRoot

switch ($Choice) {
    '1' {
        Write-Host '开始 Debug 构建...' -ForegroundColor Green
        dotnet build $projectPath -c Debug
        Write-Host '完成: bin/Debug' -ForegroundColor Green
    }
    '2' {
        $outDir = Join-Path $repoRoot 'publish\release'
        Write-Host '开始 Release 构建（非单文件）...' -ForegroundColor Green
        dotnet publish $projectPath -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -o $outDir
        Write-Host "完成: $outDir" -ForegroundColor Green
    }
    '3' {
        $outDir = Join-Path $repoRoot 'publish\single-file'
        Write-Host '开始 Release 单文件发布...' -ForegroundColor Green
        dotnet publish $projectPath -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $outDir
        Write-Host "完成: $outDir" -ForegroundColor Green
    }
    default {
        throw '无效输入。请输入 1、2 或 3。'
    }
}
