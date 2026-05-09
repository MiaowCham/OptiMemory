<div align="center">
<img src="OptiMemory.ico" width="20%" alt="icon" style="margin-bottom: -20px;"/>

# OptiMemory
[![MIT](https://img.shields.io/badge/License-Apache%202.0-orange.svg)](https://github.com/MiaowCham/OptiMemory/blob/main/LICENSE)
[![Static Badge](https://img.shields.io/badge/Languages-C%23-blue.svg)](https://github.com/search?q=repo%3AMiaowCham%2FOptiMemory++language%3AC%23&type=code)
[![Github Release](https://img.shields.io/github/v/release/MiaowCham/OptiMemory)](https://github.com/MiaowCham/OptiMemory/releases)
[![GitHub Actions](https://img.shields.io/github/actions/workflow/status/MiaowCham/OptiMemory/.github/workflows/build.yml)](https://github.com/MiaowCham/OptiMemory/actions/workflows/build.yml)
[![GitHub last commit](https://img.shields.io/github/last-commit/MiaowCham/OptiMemory)](https://github.com/MiaowCham/OptiMemory/commits/main)

轻量级 Windows 内存优化工具，提供图形界面与命令行两种使用方式。

</div>

>[!note]
>本项目使用AI生成。  
>This project uses AI generation.

## 功能

- **一键优化**：清理工作集、刷新文件缓存、清理修改页列表、清理待机列表等
- **非管理员支持**：通过 UAC 弹窗临时提权执行完整优化，无需以管理员身份启动程序
- **自动清理**：可配置间隔与内存占用阈值，后台定时执行；开启时托盘图标自动切换以示区分
- **系统托盘**：最小化到托盘持续运行，右键菜单支持显示/隐藏主界面及静默手动优化
- **深色/浅色主题**：随系统设置自动切换
- **命令行模式**：支持脚本调用与自动化

## 使用

可以自行构建，也可以从 [Releases](https://github.com/MiaowCham/OptiMemory/releases/) 下载安装程序或单文件版本。

### 图形界面

直接运行 `OptiMemory.exe` 即可启动。

- 点击"立即优化"执行一次优化（非管理员时会弹出 UAC 授权）
- 关闭窗口会最小化到系统托盘，托盘右键菜单可退出
- 可配置自动清理间隔和触发阈值

### 命令行

```
OptiMemory [选项]

选项:
  (无参数)            启动图形界面
  -n, --nogui         命令行模式，执行一次优化后退出
  -a, --auto          持续自动清理（需配合 -n，且需管理员权限）
  -i, --interval <分> 自动清理间隔（分钟，默认读取保存设置，否则 30）
  -t, --threshold <%> 触发阈值（auto 模式下内存占用超过此值才清理，0=始终）
  -d, --debug         显示完整调试日志
  -h, --help          显示帮助
```

示例：

```bat
# 立即优化一次（非管理员时通过 UAC 提权）
OptiMemory -n

# 每 15 分钟、内存占用超 80% 时清理（需管理员）
OptiMemory -n -a -i 15 -t 80
```

## 构建

**依赖项**

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Windows 10 或更高版本

**构建**

```powershell
dotnet build
```

**发布单文件**

```powershell
dotnet publish -c Release
```

输出位于 `bin\Release\net9.0-windows\publish\OptiMemory.exe`。

## 鸣谢

内存优化逻辑参考自 [PCL-CE](https://github.com/PCL-Community/PCL-CE) 项目中的 `MemSwapService`，使用 `ntdll.dll` / `kernel32.dll` 原生 API 实现以下操作：

- `NtSetSystemInformation`：清理修改页列表、待机列表（含低优先级）、合并物理内存
- `RtlAdjustPrivilege`：启用 `SeProfileSingleProcessPrivilege` 和 `SeIncreaseQuotaPrivilege`
- `GlobalMemoryStatusEx`：获取物理内存状态

提权方案：非管理员进程通过 `ShellExecute "runas"` 启动自身的提权副本，双方经命名管道交换优化结果，提权副本执行完后静默退出。

## 许可

本项目由 VSCode 及 Github Copilot 强力驱动。  
本项目使用 Apache License 2.0 许可证，详见 LICENSE 文件。
