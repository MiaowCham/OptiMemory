param(
    [ValidateSet('1', '2', '3', '4')]
    [string]$Choice,

    # 版本号，不传则自动从 csproj 读取
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$repoRoot   = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $repoRoot 'OptiMemory.csproj'

# 解析版本号
if (-not $Version) {
    $xml     = [xml](Get-Content $projectPath)
    $raw     = $xml.Project.PropertyGroup.InformationalVersion |
               Where-Object { $_ } | Select-Object -First 1
    $Version = $raw -replace '^v', ''
}
Write-Host "使用版本号: $Version" -ForegroundColor DarkCyan

if (-not $Choice) {
    Write-Host '请选择构建类型:' -ForegroundColor Cyan
    Write-Host '  1) Debug 构建' -ForegroundColor Yellow
    Write-Host '  2) Release 构建（框架依赖，非单文件，安装包使用）' -ForegroundColor Yellow
    Write-Host '  3) Release 单文件发布（框架依赖）' -ForegroundColor Yellow
    Write-Host '  4) 构建安装包（需先运行选项 2）' -ForegroundColor Yellow
    $Choice = Read-Host '输入数字 1/2/3/4'
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
        dotnet publish $projectPath -c Release -r win-x64 --self-contained false `
            -p:PublishSingleFile=false `
            -p:InformationalVersion="v$Version" `
            -p:AssemblyVersion="$Version.0" `
            -p:FileVersion="$Version.0" `
            -o $outDir
        Write-Host "完成: $outDir" -ForegroundColor Green
    }
    '3' {
        $outDir = Join-Path $repoRoot 'publish\single-file'
        Write-Host '开始 Release 单文件发布...' -ForegroundColor Green
        dotnet publish $projectPath -c Release -r win-x64 --self-contained false `
            -p:PublishSingleFile=true `
            -p:InformationalVersion="v$Version" `
            -p:AssemblyVersion="$Version.0" `
            -p:FileVersion="$Version.0" `
            -o $outDir
        Write-Host "完成: $outDir" -ForegroundColor Green
    }
    '4' {
        # 检测 Inno Setup 是否安装
        $iscc = Get-Command iscc -ErrorAction SilentlyContinue
        if (-not $iscc) {
            # 尝试默认安装路径
            $candidate = 'C:\Program Files (x86)\Inno Setup 6\iscc.exe'
            if (Test-Path $candidate) { $iscc = $candidate } else {
                throw 'iscc.exe 未找到，请安装 Inno Setup 6。'
            }
        } else {
            $iscc = $iscc.Source
        }

        # 检测中文语言包是否存在
        $innoDir    = Split-Path $iscc
        $zhLangFile = Join-Path $innoDir 'Languages\ChineseSimplified.isl'
        if (Test-Path $zhLangFile) {
            $issFile = Join-Path $PSScriptRoot 'OptiMemory.iss'
            Write-Host '检测到中文语言包，使用 OptiMemory.iss' -ForegroundColor DarkCyan
        } else {
            $issFile = Join-Path $PSScriptRoot 'OptiMemory.CI.iss'
            Write-Host '未找到中文语言包，回退到 OptiMemory.CI.iss（英文界面）' -ForegroundColor Yellow
        }

        Write-Host "开始构建安装包 v$Version..." -ForegroundColor Green
        & $iscc "/DMyAppVersion=$Version" $issFile
        Write-Host "完成: $(Join-Path $repoRoot 'dist')" -ForegroundColor Green
    }
    default {
        throw '无效输入。请输入 1、2、3 或 4。'
    }
}
