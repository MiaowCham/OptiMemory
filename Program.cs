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

    [STAThread]
    static void Main(string[] args)
    {
        var opts = ParseArgs(args);

        // 提权子进程模式——静默执行，无 GUI，无单例锁，必须在一切其他分支之前处理
        if (opts.Elevated)
        {
            if (opts.PipeName is { Length: > 0 } pipe)
                ElevationService.RunWorker(pipe);
            return;
        }

        if (opts.ShowHelp)
        {
            NativeInterop.AttachParentConsole();
            PrintHelp();
            return;
        }

        if (opts.NoGui)
        {
            RunNoGui(opts);
        }
        else
        {
            // Single instance for GUI mode
            _instanceMutex = new Mutex(true, "OptiMemory_SingleInstance", out bool isFirst);
            if (!isFirst)
            {
                MessageBox.Show("OptiMemory 已在运行中。", "OptiMemory",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        NativeInterop.AttachParentConsole();

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
            Info($"触发阈值: {thresholdPct}%（内存占用低于此值时跳过清理）");

        if (opts.Auto)
        {
            Info($"自动清理模式  间隔: {intervalMin} 分钟  按 Ctrl+C 退出");

            bool cancelled = false;
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancelled = true; };

            while (!cancelled)
            {
                int waited = 0;
                while (!cancelled && waited < intervalMin * 60)
                {
                    Thread.Sleep(1000);
                    waited++;
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
            Dbg($"当前占用 {usedPct}%，阈值 {thresholdPct}%");
            if (usedPct < thresholdPct)
            {
                Info($"跳过  内存占用 {usedPct}% 未达阈值 {thresholdPct}%  可用 {Fmt(avail)} / {Fmt(total)}");
                return;
            }
        }

        Info($"开始优化  内存占用 {usedPct}%  可用 {Fmt(avail)} / {Fmt(total)}");

        OptimizeResult? result = null;
        Exception? optimizeError = null;
        var optimizeTask = Task.Run(() =>
        {
            try   { result = MemoryOptimizer.Optimize(); }
            catch (Exception ex) { optimizeError = ex; }
        });

        // 每秒刷新一次内存状态（覆盖当前行）
        string progressLine = "";
        while (!optimizeTask.IsCompleted)
        {
            Thread.Sleep(1000);
            if (optimizeTask.IsCompleted) break;
            try
            {
                var (t2, a2) = MemoryOptimizer.GetMemoryStatus();
                int p2 = t2 > 0 ? (int)((double)(t2 - a2) / t2 * 100) : 0;
                string line = $"[{Ts()}] [INFO] 优化中  占用 {p2}%  可用 {Fmt(a2)} / {Fmt(t2)}";
                if (progressLine.Length > 0)
                    Console.Write($"\r{new string(' ', progressLine.Length)}\r");
                Console.Write(line);
                progressLine = line;
            }
            catch { }
        }
        if (progressLine.Length > 0) Console.WriteLine();
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
    private static void Info(string msg) => Console.WriteLine($"[{Ts()}] [INFO] {msg}");
    private static void Warn(string msg) => Console.WriteLine($"[{Ts()}] [WARN] {msg}");
    private static void Err(string msg)  => Console.WriteLine($"[{Ts()}] [ERR ] {msg}");
    private static void Dbg(string msg)  { if (_debug) Console.WriteLine($"[{Ts()}] [DBG ] {msg}"); }
    private static string Fmt(ulong bytes) => OptimizeResult.FormatBytes(bytes);
}
