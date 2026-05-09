using Microsoft.Win32;
using System.Diagnostics;

namespace OptiMemory;

public sealed class MainForm : Form
{
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
    private bool _closing;

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
            BeginInvoke(() => OnOptimizeClick(null, EventArgs.Empty));
        };

        // Listen for system theme changes
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        ResumeLayout(true);
    }

    private void SetupTray()
    {
        _trayShowItem = new ToolStripMenuItem("显示窗口");
        _trayShowItem.Click += (_, _) => ShowWindow();

        _trayOptimizeItem = new ToolStripMenuItem("立即优化");
        _trayOptimizeItem.Click += (_, _) => { ShowWindow(); OnOptimizeClick(null, EventArgs.Empty); };

        _trayAutoCleanItem = new ToolStripMenuItem("自动清理")
        {
            Checked = _settings.AutoClean,
            CheckOnClick = true
        };
        _trayAutoCleanItem.Click += (_, _) =>
        {
            _settings.AutoClean = _trayAutoCleanItem.Checked;
            _chkAutoClean.Checked = _trayAutoCleanItem.Checked;
            _settings.Save();
            SetupAutoClean();
        };

        _trayExitItem = new ToolStripMenuItem("退出");
        _trayExitItem.Click += (_, _) => ExitApp();

        var menu = new ContextMenuStrip();
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
    }

    private static Icon LoadIcon()
    {
        // Extract from the embedded application icon in the exe itself
        try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; }
        catch { return SystemIcons.Application; }
    }

    private void ShowWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
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
            _tray.Visible = false;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }
    }

    private void ExitApp()
    {
        _closing = true;
        _settings.Save();
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

    private async void OnOptimizeClick(object? sender, EventArgs e)
    {
        if (_isOptimizing) return;
        _isOptimizing = true;
        _btnOptimize.Enabled = false;
        _btnOptimize.Text = "优化中…";

        try
        {
            if (MemoryOptimizer.IsAdmin)
            {
                _lblStatus.Text = "正在优化内存…";
                var result = await Task.Run(() => MemoryOptimizer.Optimize());
                _lblStatus.Text = result.Errors.Count == 0
                    ? $"完成：释放 {result.FreedText}，可用 {result.AfterText}"
                    : $"完成（部分操作失败）：可用 {result.AfterText}";
                RefreshMemoryDisplay();
            }
            else
            {
                _lblStatus.Text = "正在请求管理员权限…";
                var result = await ElevationService.RequestOptimizeAsync();
                if (result == null)
                {
                    _lblStatus.Text = "已取消";
                }
                else if (!result.Success)
                {
                    _lblStatus.Text = $"失败：{result.ErrorMessage}";
                }
                else
                {
                    _lblStatus.Text = result.Errors.Count == 0
                        ? $"完成：释放 {result.FreedText}，可用 {result.AfterText}"
                        : $"完成（部分操作失败）：可用 {result.AfterText}";
                    RefreshMemoryDisplay();
                }
            }
        }
        catch (Exception ex)
        {
            _lblStatus.Text = $"失败：{ex.Message}";
        }
        finally
        {
            _isOptimizing = false;
            _btnOptimize.Enabled = true;
            _btnOptimize.Text = "立即优化";
        }
    }

    private void OnAutoCleanChanged(object? sender, EventArgs e)
    {
        _settings.AutoClean = _chkAutoClean.Checked;
        _trayAutoCleanItem.Checked = _chkAutoClean.Checked;
        _settings.Save();
        SetupAutoClean();
    }

    private void OnIntervalChanged(object? sender, EventArgs e)
    {
        _settings.AutoCleanIntervalMinutes = (int)_nudInterval.Value;
        _settings.Save();
        SetupAutoClean();
    }

    private void OnThresholdChanged(object? sender, EventArgs e)
    {
        _settings.AutoCleanThresholdPercent = (int)_nudThreshold.Value;
        _settings.Save();
    }

    private void SetupAutoClean()
    {
        _autoCleanTimer.Stop();
        if (_settings.AutoClean)
        {
            _autoCleanTimer.Interval = _settings.AutoCleanIntervalMinutes * 60 * 1000;
            _autoCleanTimer.Start();
        }
    }

    private void RunAutoClean()
    {
        if (_isOptimizing) return;
        int threshold = _settings.AutoCleanThresholdPercent;
        if (threshold > 0)
        {
            try
            {
                var (total, avail) = MemoryOptimizer.GetMemoryStatus();
                int usedPct = total > 0 ? (int)((double)(total - avail) / total * 100) : 0;
                if (usedPct < threshold) return;
            }
            catch { /* fall through and optimize */ }
        }

        if (MemoryOptimizer.IsAdmin)
        {
            Task.Run(() =>
            {
                try { MemoryOptimizer.Optimize(); }
                catch { /* ignore auto-clean errors */ }
            });
        }
        else
        {
            // 非管理员：通过 ElevationService 提权执行
            _ = ElevationService.RequestOptimizeAsync();
        }
    }
}
