using Microsoft.Win32;
using System.Diagnostics;

namespace OptiMemory;

public sealed class MainForm : Form
{
    private enum OptimizeTrigger
    {
        ManualButton,
        TrayMenu,
        Startup,
        AutoTimer
    }

    // Theme colors
    private static readonly Color DarkBg = Color.FromArgb(28, 28, 28);
    private static readonly Color DarkSurface = Color.FromArgb(42, 42, 42);
    private static readonly Color DarkBorder = Color.FromArgb(60, 60, 60);
    private static readonly Color DarkText = Color.FromArgb(240, 240, 240);
    private static readonly Color DarkSubText = Color.FromArgb(160, 160, 160);
    private static readonly Color LightBg = Color.FromArgb(243, 243, 243);
    private static readonly Color LightSurface = Color.White;
    private static readonly Color LightBorder = Color.FromArgb(210, 210, 210);
    private static readonly Color LightText = Color.FromArgb(26, 26, 26);
    private static readonly Color LightSubText = Color.FromArgb(100, 100, 100);
    private static readonly Color Accent = Color.FromArgb(0, 120, 212);
    private static readonly Color AccentHover = Color.FromArgb(0, 102, 180);

    private bool _isDark;
    private AppSettings _settings;

    // Controls
    private Label _lblAvailable = null!;
    private Label _lblTotal = null!;
    private Panel _pbOuter = null!;
    private Panel _pbFill = null!;
    private Button _btnOptimize = null!;
    private CheckBox _chkAutoClean = null!;
    private NumericUpDown _nudInterval = null!;
    private Label _lblInterval = null!;
    private Label _lblIntervalUnit = null!;
    private NumericUpDown _nudThreshold = null!;
    private Label _lblThresholdUnit = null!;
    private Label _lblStatus = null!;
    private LinkLabel _lblVersion = null!;
    private Label _lblAdminWarn = null!;
    private Panel _panelMain = null!;

    // Tray
    private NotifyIcon _tray = null!;
    private ToolStripMenuItem _trayShowItem = null!;
    private ToolStripMenuItem _trayOptimizeItem = null!;
    private ToolStripMenuItem _trayAutoCleanItem = null!;
    private ToolStripMenuItem _trayExitItem = null!;

    // Timers
    private System.Windows.Forms.Timer _refreshTimer = null!;
    private System.Windows.Forms.Timer _autoCleanTimer = null!;

    private bool _isOptimizing;
    private bool _syncingAutoCleanState;
    private bool _autoCleanDeferredWhileBusy;
    private OptimizeTrigger _activeTrigger = OptimizeTrigger.ManualButton;
    private DateTime _optimizeStartedAt;
    private bool _closing;
    private Icon? _autoCleanTrayIcon;
    private ElevationService.ElevatedSession? _elevatedAutoCleanSession;
    private Task? _autoCleanSessionTask;
    private bool _autoCleanSessionStarting;

    public MainForm(AppSettings settings)
    {
        _settings = settings;
        _isDark = IsSystemDarkMode();

        InitializeComponent();
        ApplyTheme();
        RefreshMemoryDisplay();
        SetupAutoClean();
    }

    private static bool IsSystemDarkMode()
    {
        try
        {
            var val = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1);
            return val is 0;
        }
        catch { return false; }
    }

    private void InitializeComponent()
    {
        const int pad = 12;
        const int cw = 296; // content width

        SuspendLayout();

        Text = "OptiMemory";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Icon = LoadIcon();
        ShowInTaskbar = true;

        // Main panel
        _panelMain = new Panel { Dock = DockStyle.Fill };
        Controls.Add(_panelMain);

        int y = 10;

        // Available memory (large)
        _lblAvailable = new Label
        {
            Text = "可用: —",
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(pad, y)
        };
        _panelMain.Controls.Add(_lblAvailable);
        y += 22;

        _lblTotal = new Label
        {
            Text = "/ —",
            Font = new Font("Segoe UI", 9f),
            AutoSize = true,
            Location = new Point(pad, y)
        };
        _panelMain.Controls.Add(_lblTotal);
        y += 20;

        // Progress bar (custom colored)
        _pbOuter = new Panel
        {
            Location = new Point(pad, y),
            Size = new Size(cw, 6),
            Tag = "pbouter"
        };
        _pbFill = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(0, 6),
            BackColor = Accent
        };
        _pbOuter.Controls.Add(_pbFill);
        _panelMain.Controls.Add(_pbOuter);
        y += 12;

        // Separator
        var sep = new Panel
        {
            Location = new Point(pad, y),
            Size = new Size(cw, 1),
            Tag = "separator"
        };
        _panelMain.Controls.Add(sep);
        y += 8;

        // Admin warning (only when not admin)
        _lblAdminWarn = new Label
        {
            Text = "⚠ 优化时将通过 UAC 请求管理员权限",
            Font = new Font("Segoe UI", 8.5f),
            AutoSize = true,
            ForeColor = Color.FromArgb(200, 140, 0),
            Location = new Point(pad, y),
            Visible = !MemoryOptimizer.IsAdmin
        };
        _panelMain.Controls.Add(_lblAdminWarn);
        if (!MemoryOptimizer.IsAdmin) y += 18;

        // Optimize button
        _btnOptimize = new Button
        {
            Text = "立即优化",
            Location = new Point(pad, y),
            Size = new Size(cw, 32),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f),
            Cursor = Cursors.Hand
        };
        _btnOptimize.FlatAppearance.BorderSize = 0;
        _btnOptimize.Click += OnOptimizeClick;
        _btnOptimize.MouseEnter += (_, _) => _btnOptimize.BackColor = AccentHover;
        _btnOptimize.MouseLeave += (_, _) => _btnOptimize.BackColor = Accent;
        _panelMain.Controls.Add(_btnOptimize);
        y += 36;

        // Auto clean row (available to all users)
        y += 4;
        _chkAutoClean = new CheckBox
        {
            Text = "自动清理",
            AutoSize = true,
            Font = new Font("Segoe UI", 9f),
            Location = new Point(pad, y + 2),
            Checked = _settings.AutoClean
        };
        _chkAutoClean.CheckedChanged += OnAutoCleanChanged;
        _panelMain.Controls.Add(_chkAutoClean);

        // "每" + interval NUD + "分钟" — right-aligned
        _lblInterval = new Label
        {
            Text = "每",
            AutoSize = true,
            Font = new Font("Segoe UI", 9f),
            Location = new Point(195, y + 3)
        };
        _panelMain.Controls.Add(_lblInterval);

        _nudInterval = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 1440,
            Value = _settings.AutoCleanIntervalMinutes,
            Location = new Point(220, y),
            Size = new Size(54, 22),
            Font = new Font("Segoe UI", 9f)
        };
        _nudInterval.ValueChanged += OnIntervalChanged;
        _panelMain.Controls.Add(_nudInterval);

        _lblIntervalUnit = new Label
        {
            Text = "分钟",
            AutoSize = true,
            Font = new Font("Segoe UI", 9f),
            Location = new Point(278, y + 3)
        };
        _panelMain.Controls.Add(_lblIntervalUnit);
        y += 24;

        // Threshold row — NUD aligned with interval row
        var lblThreshold = new Label
        {
            Text = "触发阈值",
            AutoSize = true,
            Font = new Font("Segoe UI", 9f),
            Location = new Point(pad + 17, y + 3),
            Tag = "thrlabel"
        };
        _panelMain.Controls.Add(lblThreshold);

        _nudThreshold = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 99,
            Value = Math.Clamp(_settings.AutoCleanThresholdPercent, 0, 99),
            Location = new Point(220, y),
            Size = new Size(54, 22),
            Font = new Font("Segoe UI", 9f)
        };
        _nudThreshold.ValueChanged += OnThresholdChanged;
        _panelMain.Controls.Add(_nudThreshold);

        _lblThresholdUnit = new Label
        {
            Text = "%",
            AutoSize = true,
            Font = new Font("Segoe UI", 9f),
            Location = new Point(278, y + 3)
        };
        _panelMain.Controls.Add(_lblThresholdUnit);
        y += 24;

        // Status label (left)
        _lblStatus = new Label
        {
            Text = "就绪",
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(pad, y)
        };
        _panelMain.Controls.Add(_lblStatus);

        // Version link (right-aligned, same row as status)
        _lblVersion = new LinkLabel
        {
            Text = Program.Version,
            Font = new Font("Segoe UI", 7.5f),
            TextAlign = ContentAlignment.MiddleRight,
            Size = new Size(cw, 18),
            Location = new Point(pad, y),
            LinkBehavior = LinkBehavior.HoverUnderline,
            TabStop = false
        };
        _lblVersion.LinkClicked += (_, _) =>
            Process.Start(new ProcessStartInfo("https://github.com/MiaowCham/OptiMemory")
                { UseShellExecute = true });
        _panelMain.Controls.Add(_lblVersion);
        y += 20;

        // Fix window size to content
        ClientSize = new Size(cw + pad * 2, y);

        // Refresh timer (every 3 seconds)
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _refreshTimer.Tick += (_, _) => RefreshMemoryDisplay();
        _refreshTimer.Start();

        // Auto-clean timer
        _autoCleanTimer = new System.Windows.Forms.Timer();
        _autoCleanTimer.Tick += (_, _) => RunAutoClean();

        // Tray
        SetupTray();

        // Form events
        FormClosing += OnFormClosing;
        Load += (_, _) =>
        {
            RefreshMemoryDisplay();
            // Run one optimization immediately on startup
            BeginInvoke(() => BeginOptimize(OptimizeTrigger.Startup));
        };

        // Listen for system theme changes
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        ResumeLayout(true);
    }

    private void SetupTray()
    {
        _trayShowItem = new ToolStripMenuItem("显示主界面");
        _trayShowItem.Click += (_, _) =>
        {
            if (Visible) Hide();
            else ShowWindow();
        };

        _trayOptimizeItem = new ToolStripMenuItem("立即优化");
        _trayOptimizeItem.Click += (_, _) => BeginOptimize(OptimizeTrigger.TrayMenu);

        _trayAutoCleanItem = new ToolStripMenuItem("自动清理")
        {
            Checked = _settings.AutoClean,
            CheckOnClick = true
        };
        _trayAutoCleanItem.Click += (_, _) =>
        {
            SetAutoCleanEnabled(_trayAutoCleanItem.Checked, "托盘菜单");
        };

        _trayExitItem = new ToolStripMenuItem("退出");
        _trayExitItem.Click += (_, _) => ExitApp();

        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) =>
            _trayShowItem.Text = Visible ? "隐藏主界面" : "显示主界面";
        menu.Items.Add(_trayShowItem);
        menu.Items.Add(_trayOptimizeItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_trayAutoCleanItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_trayExitItem);

        _tray = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "OptiMemory",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => ShowWindow();

        // 预加载自动清理托盘图标
        _autoCleanTrayIcon = LoadAutoCleanIcon();
        if (_settings.AutoClean && _autoCleanTrayIcon is not null)
            _tray.Icon = _autoCleanTrayIcon;
    }

    private static Icon LoadIcon()
    {
        // Extract from the embedded application icon in the exe itself
        try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; }
        catch { return SystemIcons.Application; }
    }

    private static Icon? LoadAutoCleanIcon()
    {
        try
        {
            var stream = typeof(MainForm).Assembly.GetManifestResourceStream("OptiMemory.Auto.ico");
            return stream is null ? null : new Icon(stream);
        }
        catch { return null; }
    }

    private void ShowWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        ApplyOptimizeUiState();
        Activate();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        // 用户关闭时始终最小化到托盘（管理员和非管理员均支持）
        if (e.CloseReason == CloseReason.UserClosing && !_closing)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            _closing = true;
            _autoCleanTimer.Stop();
            _tray.Visible = false;
            _ = DisposeAutoCleanElevationSessionAsync();
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }
    }

    private async void ExitApp()
    {
        _closing = true;
        _settings.Save();
        _autoCleanTimer.Stop();
        await DisposeAutoCleanElevationSessionAsync();
        Application.Exit();
    }

    private void OnUserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category == Microsoft.Win32.UserPreferenceCategory.General)
        {
            var dark = IsSystemDarkMode();
            if (dark != _isDark)
            {
                _isDark = dark;
                BeginInvoke(ApplyTheme);
            }
        }
    }

    private void ApplyTheme()
    {
        var bg = _isDark ? DarkBg : LightBg;
        var surface = _isDark ? DarkSurface : LightSurface;
        var border = _isDark ? DarkBorder : LightBorder;
        var text = _isDark ? DarkText : LightText;
        var subText = _isDark ? DarkSubText : LightSubText;

        BackColor = bg;
        _panelMain.BackColor = bg;

        _lblAvailable.ForeColor = text;
        _lblTotal.ForeColor = subText;
        _lblStatus.ForeColor = subText;
        _lblVersion.ForeColor = subText;
        _lblVersion.LinkColor = subText;
        _lblVersion.ActiveLinkColor = Accent;
        _lblVersion.VisitedLinkColor = subText;

        _btnOptimize.BackColor = Accent;
        _btnOptimize.ForeColor = Color.White;

        _chkAutoClean.ForeColor = text;
        _chkAutoClean.BackColor = bg;
        _nudInterval.BackColor = surface;
        _nudInterval.ForeColor = text;
        _lblInterval.ForeColor = text;
        _lblIntervalUnit.ForeColor = text;
        _nudThreshold.BackColor = surface;
        _nudThreshold.ForeColor = text;
        _lblThresholdUnit.ForeColor = text;

        // Separator / progress bar outer / theme labels
        foreach (Control c in _panelMain.Controls)
        {
            if (c.Tag is "separator") c.BackColor = border;
            if (c.Tag is "pbouter") c.BackColor = _isDark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(200, 200, 200);
            if (c.Tag is "thrlabel") c.ForeColor = text;
        }

        if (!MemoryOptimizer.IsAdmin)
            _lblAdminWarn.ForeColor = _isDark
                ? Color.FromArgb(255, 185, 50)
                : Color.FromArgb(160, 100, 0);
    }

    private void SetProgress(int pct)
    {
        pct = Math.Clamp(pct, 0, 100);
        _pbFill.Width = (int)(_pbOuter.Width * pct / 100.0);
        // Color: green→yellow→red based on usage
        _pbFill.BackColor = pct < 60
            ? Color.FromArgb(0, 188, 100)
            : pct < 80 ? Color.FromArgb(255, 165, 0) : Color.FromArgb(220, 50, 50);
    }

    private void RefreshMemoryDisplay()
    {
        try
        {
            var (total, avail) = MemoryOptimizer.GetMemoryStatus();
            var usedPct = total > 0 ? (int)((double)(total - avail) / total * 100) : 0;
            _lblAvailable.Text = $"可用: {OptimizeResult.FormatBytes(avail)}";
            _lblTotal.Text = $"/ {OptimizeResult.FormatBytes(total)}  ({usedPct}% 已用)";
            SetProgress(usedPct);
        }
        catch { /* ignore */ }
    }

    private void OnOptimizeClick(object? sender, EventArgs e)
    {
        BeginOptimize(OptimizeTrigger.ManualButton);
    }

    private void OnAutoCleanChanged(object? sender, EventArgs e)
    {
        if (_syncingAutoCleanState) return;
        SetAutoCleanEnabled(_chkAutoClean.Checked, "主界面开关");
    }

    private void OnIntervalChanged(object? sender, EventArgs e)
    {
        _settings.AutoCleanIntervalMinutes = (int)_nudInterval.Value;
        _settings.Save();
        SetStatus($"自动清理间隔已更新为 {_settings.AutoCleanIntervalMinutes} 分钟");
        Program.Dbg($"自动清理间隔更新: {_settings.AutoCleanIntervalMinutes} 分钟");
        SetupAutoClean();
    }

    private void OnThresholdChanged(object? sender, EventArgs e)
    {
        _settings.AutoCleanThresholdPercent = (int)_nudThreshold.Value;
        _settings.Save();
        SetStatus($"自动清理阈值已更新为 {_settings.AutoCleanThresholdPercent}%");
        Program.Dbg($"自动清理阈值更新: {_settings.AutoCleanThresholdPercent}%");
    }

    private void SetupAutoClean()
    {
        _autoCleanTimer.Stop();
        if (_settings.AutoClean)
        {
            _autoCleanTimer.Interval = _settings.AutoCleanIntervalMinutes * 60 * 1000;
            EnsureAutoCleanElevationSession();
            _autoCleanTimer.Start();
            Program.Dbg($"自动清理定时器启动: 间隔 {_settings.AutoCleanIntervalMinutes} 分钟，阈值 {_settings.AutoCleanThresholdPercent}%");
        }
        else
        {
            _ = DisposeAutoCleanElevationSessionAsync();
            Program.Dbg("自动清理定时器已停止");
        }
    }

    private void EnsureAutoCleanElevationSession()
    {
        if (MemoryOptimizer.IsAdmin || !_settings.AutoClean || _elevatedAutoCleanSession is not null || _autoCleanSessionStarting)
            return;

        _autoCleanSessionTask = StartAutoCleanElevationSessionAsync();
    }

    private async Task StartAutoCleanElevationSessionAsync()
    {
        _autoCleanSessionStarting = true;
        SetStatus("自动清理需要管理员权限，正在请求 UAC 授权…");
        Program.Info("自动清理启用，正在建立持久提权会话");

        try
        {
            var session = await ElevationService.StartSessionAsync();
            if (!_settings.AutoClean)
            {
                if (session is not null)
                    await session.DisposeAsync();
                return;
            }

            if (session is null)
            {
                Program.Warn("自动清理持久提权会话未建立");
                SetStatus("自动清理未获得管理员权限，已关闭");
                SetAutoCleanEnabled(false, "提权失败");
                return;
            }

            _elevatedAutoCleanSession = session;
            SetStatus($"自动清理已就绪：每 {_settings.AutoCleanIntervalMinutes} 分钟，阈值 {_settings.AutoCleanThresholdPercent}%");
            Program.Info("自动清理持久提权会话已建立");
        }
        catch (Exception ex)
        {
            Program.Err($"自动清理提权会话异常: {ex.Message}");
            SetStatus($"自动清理提权失败：{ex.Message}");
            SetAutoCleanEnabled(false, "提权异常");
        }
        finally
        {
            _autoCleanSessionStarting = false;
        }
    }

    private async Task DisposeAutoCleanElevationSessionAsync()
    {
        var session = _elevatedAutoCleanSession;
        _elevatedAutoCleanSession = null;
        if (session is not null)
        {
            await session.DisposeAsync();
            Program.Dbg("自动清理持久提权会话已释放");
        }
    }

    private void RunAutoClean()
    {
        if (!_settings.AutoClean)
        {
            Program.Dbg("收到自动清理 Tick，但自动清理已关闭，忽略");
            return;
        }

        Program.Dbg("自动清理 Tick 触发，开始评估是否执行优化");

        if (_isOptimizing)
        {
            if (!_autoCleanDeferredWhileBusy)
            {
                _autoCleanDeferredWhileBusy = true;
                SetStatus("自动清理排队中：当前有优化任务正在执行");
                Program.Info("自动清理触发时检测到优化进行中，已排队等待");
            }
            return;
        }

        if (!MemoryOptimizer.IsAdmin && _elevatedAutoCleanSession is null)
        {
            EnsureAutoCleanElevationSession();
            SetStatus(_autoCleanSessionStarting
                ? "自动清理等待管理员授权…"
                : "自动清理等待提权会话就绪…");
            Program.Dbg("自动清理等待持久提权会话，跳过本次 Tick");
            return;
        }

        int threshold = _settings.AutoCleanThresholdPercent;
        if (threshold > 0)
        {
            try
            {
                var (total, avail) = MemoryOptimizer.GetMemoryStatus();
                int usedPct = total > 0 ? (int)((double)(total - avail) / total * 100) : 0;
                if (usedPct < threshold)
                {
                    Program.Dbg($"自动清理跳过：当前占用 {usedPct}% 未达到阈值 {threshold}%");
                    SetStatus($"自动清理跳过：占用 {usedPct}% 低于阈值 {threshold}%");
                    return;
                }
            }
            catch (Exception ex)
            {
                Program.Dbg($"自动清理读取内存状态失败，将继续尝试优化: {ex.Message}");
            }
        }

        SetStatus("自动清理触发，准备开始优化…");
        BeginOptimize(OptimizeTrigger.AutoTimer);
    }

    private void BeginOptimize(OptimizeTrigger trigger)
    {
        if (_isOptimizing)
        {
            if (trigger == OptimizeTrigger.AutoTimer)
            {
                if (!_autoCleanDeferredWhileBusy)
                {
                    _autoCleanDeferredWhileBusy = true;
                    SetStatus("自动清理排队中：当前有优化任务正在执行");
                    Program.Info("自动清理请求已排队，等待当前优化结束");
                }
                return;
            }

            Program.Dbg($"忽略重复优化请求: trigger={trigger}");
            return;
        }

        _ = OptimizeAsync(trigger);
    }

    private async Task OptimizeAsync(OptimizeTrigger trigger)
    {
        _activeTrigger = trigger;
        _optimizeStartedAt = DateTime.Now;
        _isOptimizing = true;
        ApplyOptimizeUiState();

        string triggerText = TriggerToText(trigger);
        Program.Info($"{triggerText}开始");
        Program.Dbg($"优化上下文: trigger={trigger}, admin={MemoryOptimizer.IsAdmin}, autoEnabled={_settings.AutoClean}, interval={_settings.AutoCleanIntervalMinutes}, threshold={_settings.AutoCleanThresholdPercent}");

        try
        {
            if (MemoryOptimizer.IsAdmin)
            {
                SetStatus(trigger == OptimizeTrigger.AutoTimer ? "自动清理中…" : "正在优化内存…");

                OptimizeResult? result = null;
                Exception? optimizeError = null;
                var optimizeTask = Task.Run(() =>
                {
                    try { result = MemoryOptimizer.Optimize(); }
                    catch (Exception ex) { optimizeError = ex; }
                });

                int elapsed = 0;
                while (!optimizeTask.IsCompleted)
                {
                    await Task.Delay(1000);
                    if (optimizeTask.IsCompleted) break;

                    elapsed++;
                    try
                    {
                        var (t, a) = MemoryOptimizer.GetMemoryStatus();
                        int p = t > 0 ? (int)((double)(t - a) / t * 100) : 0;
                        string live = $"优化中… 占用 {p}% 可用 {OptimizeResult.FormatBytes(a)} / {OptimizeResult.FormatBytes(t)}";
                        SetStatus(live);
                        Program.Dbg($"优化进行中({elapsed}s): trigger={trigger}, used={p}%, avail={OptimizeResult.FormatBytes(a)}, total={OptimizeResult.FormatBytes(t)}");
                    }
                    catch (Exception ex)
                    {
                        Program.Dbg($"优化进行中刷新状态失败({elapsed}s): {ex.Message}");
                    }
                }

                await optimizeTask;

                if (optimizeError != null)
                    throw optimizeError;

                var done = result!;
                SetStatus(done.Errors.Count == 0
                    ? $"完成：释放 {done.FreedText}，可用 {done.AfterText}"
                    : $"完成（部分操作失败）：可用 {done.AfterText}");
                Program.Info($"优化完成  来源={triggerText}  释放 {done.FreedText}  占用 {(int)Math.Round(done.UsagePercent)}%  可用 {done.AfterText} / {done.TotalText}");
                foreach (var warn in done.Errors)
                    Program.Warn($"操作提示: {warn}");
                RefreshMemoryDisplay();
            }
            else
            {
                SetStatus(trigger == OptimizeTrigger.AutoTimer ? "自动清理中…" : "正在请求管理员权限…");
                Program.Dbg($"等待 UAC 提权结果: trigger={trigger}");
                var result = await RunElevatedOptimizeAsync(trigger);
                if (result == null)
                {
                    SetStatus(trigger == OptimizeTrigger.AutoTimer ? "自动清理提权会话已失效，等待重新授权" : "已取消");
                    Program.Warn(trigger == OptimizeTrigger.AutoTimer
                        ? "自动清理持久提权会话已失效"
                        : $"{triggerText}已取消（用户未完成提权或通信超时）");
                    if (trigger == OptimizeTrigger.AutoTimer)
                    {
                        await DisposeAutoCleanElevationSessionAsync();
                        EnsureAutoCleanElevationSession();
                    }
                }
                else if (!result.Success)
                {
                    SetStatus($"失败：{result.ErrorMessage}");
                    Program.Err($"优化失败: {result.ErrorMessage}");
                }
                else
                {
                    SetStatus(result.Errors.Count == 0
                        ? $"完成：释放 {result.FreedText}，可用 {result.AfterText}"
                        : $"完成（部分操作失败）：可用 {result.AfterText}");
                    Program.Info($"优化完成  来源={triggerText}  释放 {result.FreedText}  占用 {result.UsagePct}%  可用 {result.AfterText} / {result.TotalText}");
                    foreach (var warn in result.Errors)
                        Program.Warn($"操作提示: {warn}");
                    RefreshMemoryDisplay();
                }
            }
        }
        catch (Exception ex)
        {
            SetStatus($"失败：{ex.Message}");
            Program.Err($"优化异常: {ex.Message}");
        }
        finally
        {
            int elapsedMs = (int)(DateTime.Now - _optimizeStartedAt).TotalMilliseconds;
            Program.Dbg($"优化任务结束: trigger={trigger}, durationMs={elapsedMs}, autoDeferred={_autoCleanDeferredWhileBusy}");

            _isOptimizing = false;
            ApplyOptimizeUiState();

            if (_autoCleanDeferredWhileBusy && _settings.AutoClean)
            {
                _autoCleanDeferredWhileBusy = false;
                Program.Dbg("执行排队中的自动清理检查");
                RunAutoClean();
            }
        }
    }

    private async Task<ElevatedResult?> RunElevatedOptimizeAsync(OptimizeTrigger trigger)
    {
        if (_elevatedAutoCleanSession is not null)
            return await _elevatedAutoCleanSession.OptimizeAsync();

        if (_settings.AutoClean && (trigger == OptimizeTrigger.AutoTimer || trigger == OptimizeTrigger.Startup))
        {
            EnsureAutoCleanElevationSession();
            if (_autoCleanSessionTask is not null)
            {
                SetStatus("等待自动清理管理员授权完成…");
                await _autoCleanSessionTask;
            }

            if (_elevatedAutoCleanSession is not null)
                return await _elevatedAutoCleanSession.OptimizeAsync();
        }

        return await ElevationService.RequestOptimizeAsync();
    }

    private void SetAutoCleanEnabled(bool enabled, string source)
    {
        _settings.AutoClean = enabled;
        _settings.Save();

        _syncingAutoCleanState = true;
        try
        {
            _chkAutoClean.Checked = enabled;
            _trayAutoCleanItem.Checked = enabled;
        }
        finally
        {
            _syncingAutoCleanState = false;
        }

        SetupAutoClean();

        // 根据自动清理开关状态切换托盘图标
        _tray.Icon = (enabled && _autoCleanTrayIcon is not null)
            ? _autoCleanTrayIcon
            : LoadIcon();

        string status = enabled
            ? $"自动清理已开启：每 {_settings.AutoCleanIntervalMinutes} 分钟，阈值 {_settings.AutoCleanThresholdPercent}%"
            : "自动清理已关闭";
        SetStatus(status);
        Program.Info($"{source} -> {status}");
    }

    private void SetStatus(string text)
    {
        _lblStatus.Text = text;
    }

    private void ApplyOptimizeUiState()
    {
        if (_isOptimizing)
        {
            _btnOptimize.Enabled = false;
            _btnOptimize.Text = _activeTrigger == OptimizeTrigger.AutoTimer ? "自动优化中…" : "优化中…";
        }
        else
        {
            _btnOptimize.Enabled = true;
            _btnOptimize.Text = "立即优化";
        }
    }

    private static string TriggerToText(OptimizeTrigger trigger)
        => trigger switch
        {
            OptimizeTrigger.AutoTimer => "自动清理",
            OptimizeTrigger.Startup => "启动自检优化",
            OptimizeTrigger.TrayMenu => "托盘手动优化",
            _ => "手动优化"
        };
}
