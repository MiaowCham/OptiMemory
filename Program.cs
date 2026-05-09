using System.Diagnostics;
using System.Reflection;

namespace OptiMemory;

static class Program
{
    /// <summary>从程序集 InformationalVersion 读取，单一数据源在 .csproj 的 &lt;InformationalVersion&gt;。</summary>
    public static string Version { get; } =
        Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? "v?.?.?";

    private static Mutex? _instanceMutex;
    private static bool _debug;
    private static readonly object _logSync = new();
    private static readonly string _logFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptiMemory", "logs", "latest.log");
    private static bool _logFileReady;
    private static bool _logFileInitTried;

    [STAThread]
    static void Main(string[] args)
    {
        // 对 CLI 以外的模式（GUI 启动、提权子进程）立刻隐藏并脱离控制台，
        // 彻底解决双击启动时短暂出现黑色控制台窗口的问题。
        // 使用 Exe 子系统（而非 WinExe）是为了让 PowerShell/CMD 能正确等待
        // 命令行模式结束，彻底解决提示符错位和按 Enter 乱出提示符的问题。
        bool isCliMode = args.Any(a =>
            a.Equals("--nogui", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-n",      StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--help",  StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-h",      StringComparison.OrdinalIgnoreCase));
        if (!isCliMode)
            NativeInterop.HideConsoleWindow();

        var opts = ParseArgs(args);
        if (_debug)
            Info("已启用调试日志（--debug）");
        Dbg($"参数解析完成: {string.Join(' ', args)}");
        Dbg($"运行选项 => nogui={opts.NoGui}, auto={opts.Auto}, debug={opts.Debug}, interval={opts.IntervalMinutes?.ToString() ?? "(默认)"}, threshold={opts.ThresholdPercent?.ToString() ?? "(默认)"}, elevated={opts.Elevated}");

        // 提权子进程模式——静默执行，无 GUI，无单例锁，必须在一切其他分支之前处理
        if (opts.Elevated)
        {
            Dbg("进入提权子进程模式");
            if (opts.PipeName is { Length: > 0 } pipe)
                ElevationService.RunWorker(pipe);
            return;
        }

        if (opts.ShowHelp)
        {
            Dbg("显示帮助并退出");
            PrintHelp();
            return;
        }

        if (opts.NoGui)
        {
            Dbg("进入命令行模式");
            RunNoGui(opts);
        }
        else
        {
            Dbg("进入 GUI 模式");

            // Single instance for GUI mode
            _instanceMutex = new Mutex(true, "OptiMemory_SingleInstance", out bool isFirst);
            if (!isFirst)
            {
                MessageBox.Show("OptiMemory 已在运行中。", "OptiMemory",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                Dbg("检测到已有实例运行，已取消本次启动");
                return;
            }
            RunGui();
        }
    }

    // ─── Argument model ───────────────────────────────────────────────────────

    private sealed class CliOptions
    {
        public bool ShowHelp;
        public bool NoGui;
        public bool Auto;
        public bool Debug;
        public bool Elevated;       // 提权子进程模式
        public string? PipeName;    // 命名管道名称
        public int? IntervalMinutes;
        public int? ThresholdPercent;
    }

    private static CliOptions ParseArgs(string[] args)
    {
        var o = new CliOptions();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--help":  case "-h": o.ShowHelp = true; break;
                case "--nogui": case "-n": o.NoGui    = true; break;
                case "--auto":  case "-a": o.Auto     = true; break;
                case "--debug": case "-d": o.Debug    = true; break;
                case "--interval": case "-i":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int iv) && iv > 0)
                        o.IntervalMinutes = iv;
                    break;
                case "--threshold": case "-t":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int tv) && tv is >= 0 and <= 99)
                        o.ThresholdPercent = tv;
                    break;
                case "--elevated": o.Elevated = true; break;
                case "--pipe":
                    if (i + 1 < args.Length) o.PipeName = args[++i];
                    break;
            }
        }
        _debug = o.Debug;
        return o;
    }

    // ─── Help ─────────────────────────────────────────────────────────────────

    private static void PrintHelp()
    {
        Console.WriteLine($"""
            OptiMemory {Version} - 内存优化工具

            用法:
              OptiMemory [选项]

            选项:
              (无参数)            启动图形界面
              -n, --nogui         命令行模式，执行一次优化后退出
              -a, --auto          持续自动清理（需配合 -n）
              -i, --interval <分> 自动清理间隔（分钟，默认读取保存设置，否则 30）
              -t, --threshold <%> 触发阈值（内存占用超过此百分比才清理，不指定则始终清理）
              -d, --debug         显示完整调试日志
              -h, --help          显示此帮助

            示例:
              OptiMemory                     启动 GUI
              OptiMemory -n                  立即优化一次
              OptiMemory -n -a               按保存间隔自动清理
              OptiMemory -n -a -i 15 -t 80  每 15 分钟、占用超 80% 时清理
            """);
    }

    // ─── GUI mode ─────────────────────────────────────────────────────────────

    private static void RunGui()
    {
        Info($"OptiMemory {Version}  GUI 模式启动  管理员: {(MemoryOptimizer.IsAdmin ? "是" : "否")}");
        Dbg("GUI 模式，日志写入文件");

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
#pragma warning disable WFO5001
        Application.SetColorMode(SystemColorMode.System);
#pragma warning restore WFO5001

        var settings = AppSettings.Load();
        Application.Run(new MainForm(settings));
    }

    // ─── CLI mode ─────────────────────────────────────────────────────────────

    private static void RunNoGui(CliOptions opts)
    {
        // 使用 Exe 子系统后控制台已自动就绪，无需 AttachConsole
        Dbg("命令行模式控制台已就绪");

        // 启动信息 + 当前内存状态
        try
        {
            var (total, avail) = MemoryOptimizer.GetMemoryStatus();
            int usedPct = total > 0 ? (int)((double)(total - avail) / total * 100) : 0;
            Info($"OptiMemory {Version}  管理员: {(MemoryOptimizer.IsAdmin ? "是" : "否")}  " +
                 $"内存占用: {usedPct}%  可用: {Fmt(avail)} / {Fmt(total)}");
        }
        catch (Exception ex)
        {
            Info($"OptiMemory {Version}  管理员: {(MemoryOptimizer.IsAdmin ? "是" : "否")}");
            Dbg($"获取内存状态失败: {ex.Message}");
        }

        // 非管理员：单次执行通过 UAC 提权；自动清理模式需要管理员权限
        if (!MemoryOptimizer.IsAdmin)
        {
            if (opts.Auto)
            {
                Err("自动清理模式需要管理员权限，请以管理员身份启动程序");
                Environment.ExitCode = 1;
                return;
            }
            RunElevatedCli();
            return;
        }

        // 管理员路径
        var settings = AppSettings.Load();
        int intervalMin  = opts.IntervalMinutes ?? settings.AutoCleanIntervalMinutes;
        int thresholdPct = opts.Auto ? (opts.ThresholdPercent ?? 0) : 0;

        if (opts.Auto && thresholdPct > 0)
            Dbg($"触发阈值: {thresholdPct}%（内存占用低于此值时跳过清理）");

        if (opts.Auto)
        {
            Info($"自动清理模式  间隔: {intervalMin} 分钟  按 Ctrl+C 退出");
            Dbg("自动清理循环已启动");

            bool cancelled = false;
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancelled = true; };

            while (!cancelled)
            {
                int waited = 0;
                while (!cancelled && waited < intervalMin * 60)
                {
                    Thread.Sleep(1000);
                    waited++;
                    if (_debug && waited % 30 == 0)
                        Dbg($"自动清理等待中: {waited}/{intervalMin * 60} 秒");
                }
                if (cancelled) break;
                RunOnce(thresholdPct);
            }
            Info("已退出");
        }
        else
        {
            RunOnce(thresholdPct);
        }
    }

    private static void RunElevatedCli()
    {
        Info("当前非管理员，将通过 UAC 请求提权...");
        Dbg("开始等待提权优化结果");
        var elevated = ElevationService.RequestOptimizeAsync().GetAwaiter().GetResult();
        if (elevated == null)
        {
            Err("提权被取消或通信超时");
            Environment.ExitCode = 1;
            return;
        }
        if (!elevated.Success)
        {
            Err($"优化失败: {elevated.ErrorMessage}");
            Environment.ExitCode = 1;
            return;
        }
        Info($"优化完成  释放 {elevated.FreedText}  占用 {elevated.UsagePct}%  可用 {elevated.AfterText} / {elevated.TotalText}");
        foreach (var e in elevated.Errors)
            Warn($"操作提示: {e}");
    }

    private static void RunOnce(int thresholdPct)
    {
        ulong total = 0, avail = 0;
        int usedPct = 0;
        try
        {
            (total, avail) = MemoryOptimizer.GetMemoryStatus();
            usedPct = total > 0 ? (int)((double)(total - avail) / total * 100) : 0;
        }
        catch (Exception ex) { Dbg($"获取内存状态失败: {ex.Message}"); }

        if (thresholdPct > 0)
        {
            Dbg($"阈值判断: 当前占用 {usedPct}%，阈值 {thresholdPct}%");
            if (usedPct < thresholdPct)
            {
                Info($"跳过  内存占用 {usedPct}% 未达阈值 {thresholdPct}%  可用 {Fmt(avail)} / {Fmt(total)}");
                return;
            }
        }

        Info($"开始优化  内存占用 {usedPct}%  可用 {Fmt(avail)} / {Fmt(total)}");
        Dbg("创建后台优化任务");

        OptimizeResult? result = null;
        Exception? optimizeError = null;
        var optimizeTask = Task.Run(() =>
        {
            try   { result = MemoryOptimizer.Optimize(); }
            catch (Exception ex) { optimizeError = ex; }
        });

        // 每秒采样一次内存状态（使用 DBG 行日志，避免覆盖写入影响 PowerShell 提示符）
        int progressSeconds = 0;
        while (!optimizeTask.IsCompleted)
        {
            Thread.Sleep(1000);
            if (optimizeTask.IsCompleted) break;
            try
            {
                var (t2, a2) = MemoryOptimizer.GetMemoryStatus();
                int p2 = t2 > 0 ? (int)((double)(t2 - a2) / t2 * 100) : 0;
                progressSeconds++;
                Dbg($"优化进行中({progressSeconds}s): 占用 {p2}% 可用 {Fmt(a2)} / {Fmt(t2)}");
            }
            catch (Exception ex)
            {
                Dbg($"优化进行中采样失败: {ex.Message}");
            }
        }
        optimizeTask.Wait();

        if (optimizeError != null)
        {
            Err($"优化失败: {optimizeError.Message}");
            Environment.ExitCode = 1;
            return;
        }
        int afterPct = (int)Math.Round(result!.UsagePercent);
        Info($"优化完成  释放 {result.FreedText}  占用 {afterPct}%  可用 {result.AfterText} / {result.TotalText}");
        foreach (var e in result.Errors)
            Warn($"操作提示: {e}");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static string Ts() => DateTime.Now.ToString("HH:mm:ss");
    internal static void Info(string msg) => WriteLog("INFO", msg, debugOnly: false);
    internal static void Warn(string msg) => WriteLog("WARN", msg, debugOnly: false);
    internal static void Err(string msg)  => WriteLog("ERR ", msg, debugOnly: false);
    internal static void Dbg(string msg)  => WriteLog("DBG ", msg, debugOnly: true);
    private static string Fmt(ulong bytes) => OptimizeResult.FormatBytes(bytes);

    private static void WriteLog(string level, string msg, bool debugOnly)
    {
        if (debugOnly && !_debug) return;

        string line = $"[{Ts()}] [{level}] [T{Environment.CurrentManagedThreadId}] {msg}";

        try { Console.WriteLine(line); }
        catch { /* ignore console write errors */ }

        try
        {
            EnsureLogFileReady();
            if (!_logFileReady) return;
            lock (_logSync)
            {
                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
        }
        catch { /* ignore file write errors */ }
    }

    private static void EnsureLogFileReady()
    {
        if (_logFileReady || _logFileInitTried) return;

        lock (_logSync)
        {
            if (_logFileReady || _logFileInitTried) return;
            _logFileInitTried = true;
            try
            {
                string? dir = Path.GetDirectoryName(_logFilePath);
                if (string.IsNullOrWhiteSpace(dir)) return;
                Directory.CreateDirectory(dir);

                // Rotate when the latest file is too large.
                if (File.Exists(_logFilePath))
                {
                    var info = new FileInfo(_logFilePath);
                    if (info.Length > 2 * 1024 * 1024)
                    {
                        string backup = _logFilePath + ".1";
                        if (File.Exists(backup)) File.Delete(backup);
                        File.Move(_logFilePath, backup);
                    }
                }

                _logFileReady = true;
            }
            catch
            {
                _logFileReady = false;
            }
        }
    }
}
