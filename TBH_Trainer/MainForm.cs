#pragma warning disable CA1416

using System.Text;

namespace TBH_Trainer;

internal sealed class MainForm : Form
{
    private readonly GameMemory _mem = new();
    private AntiCheatPatcher? _patcher;
    private readonly TrainerBridge _bridge = new();
    private int _pid = -1;
    private bool _injected;

    // Controls
    private Label _lblStatus = null!;
    private Button _btnAttach = null!;
    private Button _btnBypass = null!;
    private Label _lblBypassStatus = null!;

    private CheckBox _chkSpeed = null!;
    private Panel _pnlSpeed = null!;
    private TrackBar _trkSpeed = null!;
    private Label _lblSpeedValue = null!;
    private TextBox _txtSpeed = null!;
    private Label _lblSpeedStatus = null!;
    private bool _suppressSlider;

    private Button _btnScanHeroes = null!;
    private HeroScanBridge? _heroScanBridge;
    private TextBox _txtHeroReport = null!;
    private ComboBox _cmbHeroIndex = null!;
    private ComboBox _cmbHeroStat = null!;
    private TextBox _txtHeroValue = null!;
    private Button _btnWriteHeroStat = null!;
    private Button _btnMaxHeroStats = null!;
    private Button _btnSelfCheck = null!;
    private CheckBox _chkOneHitKill = null!;
    private CheckBox _chkGodMode = null!;
    private CheckBox _chkAutoOpenBoxes = null!;

    private TextBox _txtItemKey = null!;
    private ComboBox _cmbGrade = null!;
    private TextBox _txtSpawnCount = null!;
    private Button _btnSpawnItem = null!;
    private Button _btnScanItems = null!;
    private Button _btnExportCatalog = null!;
    private Label _lblSpawnStatus = null!;
    private ItemScanBridge? _scanBridge;
    private DateTime _lastSpawnClick = DateTime.MinValue;

    private TextBox _txtLog = null!;
    private Panel _header = null!;
    private TabControl _tabs = null!;

    // Detected game build
    private GameBuildProfile _buildProfile = GameBuildProfile.V1008;
    private bool _buildDetected;

    // === DENOISER theme palette ===
    private static readonly Color BgForm     = Color.FromArgb(16, 18, 24);
    private static readonly Color BgInput    = Color.FromArgb(34, 37, 46);
    private static readonly Color BtnBg      = Color.FromArgb(30, 33, 42);
    private static readonly Color AccentCyan = Color.FromArgb(60, 220, 225);
    private static readonly Color AccentPurp = Color.FromArgb(155, 110, 255);
    private static readonly Color AccentDim  = Color.FromArgb(40, 70, 80);
    private static readonly Color TextMain   = Color.FromArgb(224, 228, 236);
    private static readonly Color TextDim    = Color.FromArgb(135, 140, 156);

    private static readonly Bitmap? Logo = LoadEmbedded<Bitmap>("logo.png");

    private static T? LoadEmbedded<T>(string endsWith) where T : class
    {
        var asm = typeof(MainForm).Assembly;
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(endsWith, StringComparison.OrdinalIgnoreCase));
        if (name == null) return null;
        using var s = asm.GetManifestResourceStream(name);
        if (s == null) return null;
        if (typeof(T) == typeof(Bitmap)) return new Bitmap(s) as T;
        if (typeof(T) == typeof(Icon)) return new Icon(s) as T;
        return null;
    }

    private static string DllPath => Path.Combine(AppContext.BaseDirectory, "TBHHook.dll");

    /// <summary>Speedhack ceiling; TBHHook clamps timeScale to the same value.</summary>
    private const float MaxSpeed = 20f;

    public MainForm()
    {
        InitializeComponent();
        InitializeTrayIcon();
        Shown += async (_, _) =>
        {
            // Offer a newer release (invited users only) before the version warning.
            await Updater.CheckAsync(this, Log, interactive: false);
            if (_startupDllPath != null)
                WarnIfUnsupportedBuild(_startupDiskBuild, _startupDllPath);
        };
    }

    // Minimize hides the window to the notification area instead of the taskbar.
    private NotifyIcon _trayIcon = null!;

    private void InitializeTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show trainer", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Check for updates", null, async (_, _) =>
        {
            RestoreFromTray();
            await Updater.CheckAsync(this, Log, interactive: true);
        });
        menu.Items.Add("Exit", null, (_, _) => Close());

        _trayIcon = new NotifyIcon
        {
            Icon = Icon ?? SystemIcons.Application,
            Text = "TBH Trainer",
            ContextMenuStrip = menu,
            Visible = false,
        };
        _trayIcon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) RestoreFromTray(); };

        Resize += (_, _) =>
        {
            if (WindowState != FormWindowState.Minimized) return;
            Hide();
            ShowInTaskbar = false;
            _trayIcon.Visible = true;
        };
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        _trayIcon.Visible = false;
    }

    // Installed-build detection at startup, shown as a pop-up once the window is visible.
    private string? _startupDllPath;
    private GameBuildProfile? _startupDiskBuild;
    private bool _warnedBuild;

    /// <summary>
    /// Pop-up (once per session) when the installed game is not a fully supported build,
    /// so the trainer gets updated before anything is patched.
    /// </summary>
    private void WarnIfUnsupportedBuild(GameBuildProfile? build, string? dllPath)
    {
        if (_warnedBuild) return;
        List<string> disabled = build == null ? new() : DisabledFeatures(build);
        if (build != null && disabled.Count == 0) return;
        _warnedBuild = true;

        if (build == null)
        {
            string gameVersion = ReadGameVersion(dllPath) ?? "unknown";
            string supported = string.Join(", ", GameBuildProfile.All.Select(p => p.VersionLabel));
            MessageBox.Show(this,
                $"Taskbar Hero version {gameVersion} is not supported by this trainer.\n\n" +
                $"Supported builds: {supported}\n\n" +
                "The game was probably updated. Download the latest trainer release:\n" +
                Updater.ReleasesUrl + "\n\n" +
                "Until then, ACTk bypass and all item / hero features will refuse to run.",
                "Unsupported game version", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        else if (build.FromScan)
        {
            string gameVersion = ReadGameVersion(dllPath) ?? "unknown";
            MessageBox.Show(this,
                $"Taskbar Hero {gameVersion} is newer than this trainer, but the six ACTk detectors " +
                "were located by scanning the game, so:\n\n" +
                "  • ACTk bypass and Speedhack work\n" +
                "  • Hero stats, item spawn and the scans stay off until the offsets for this build " +
                "are verified\n\n" +
                "Check for a trainer update to get the rest back:\n" + Updater.ReleasesUrl,
                "New game version — partial support", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show(this,
                $"Taskbar Hero {build.VersionLabel} is supported except:\n\n" +
                string.Join("\n", disabled.Select(d => "  • " + d)),
                "Partially supported game version", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    /// <summary>Features that stay disabled on a detected build until its offsets are mapped.</summary>
    private static List<string> DisabledFeatures(GameBuildProfile build)
    {
        var list = new List<string>();
        if (!build.GameApiVerified) list.Add("Stash item spawn / export catalog");
        else if (!build.ItemScanSupported && !build.InventoryScanSupported) list.Add("Scan items (use Export catalog for itemKeys)");
        if (!build.HeroStatsVerified) list.Add("Hero stats (HP / Attack Speed lock)");
        return list;
    }

    /// <summary>Reads the game's Version.txt next to GameAssembly.dll (e.g. "1.2.4").</summary>
    private static string? ReadGameVersion(string? dllPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(dllPath);
            if (dir == null) return null;
            var file = Path.Combine(dir, "Version.txt");
            return File.Exists(file) ? File.ReadAllText(file).Trim() : null;
        }
        catch { return null; }
    }

    private void InitializeComponent()
    {
        Text = $"TBH Trainer v{AppInfo.Version}";
        Size = new Size(584, 920);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = BgForm;
        ForeColor = TextMain;
        Font = new Font("Segoe UI", 9.5f);
        try { Icon = LoadEmbedded<Icon>("icon.ico"); } catch { }

        _header = new Panel { Location = new Point(0, 0), Size = new Size(584, 76), BackColor = BgForm };
        _header.Paint += PaintHeader;
        Controls.Add(_header);

        // === Tabs ===
        _tabs = new TabControl
        {
            Location = new Point(8, 84), Size = new Size(560, 618),
            DrawMode = TabDrawMode.OwnerDrawFixed, SizeMode = TabSizeMode.Fixed,
            ItemSize = new Size(270, 30), Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };
        _tabs.DrawItem += OnDrawTab;
        var tabRuntime = new TabPage("ACTk bypass")   { BackColor = BgForm, ForeColor = TextMain };
        var tabHeroes  = new TabPage("Hero Stats") { BackColor = BgForm, ForeColor = TextMain };
        _tabs.TabPages.Add(tabRuntime);
        _tabs.TabPages.Add(tabHeroes);
        Controls.Add(_tabs);

        // Log must exist before the tab builders and startup detection call Log().
        var grpLog = new GroupBox
        {
            Text = "Log", Location = new Point(8, 710), Size = new Size(560, 150),
            ForeColor = AccentCyan, Font = new Font("Segoe UI", 9, FontStyle.Bold), BackColor = BgForm
        };
        _txtLog = new TextBox
        {
            Location = new Point(12, 20), Size = new Size(540, 118),
            Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true,
            BackColor = Color.FromArgb(12, 13, 18), ForeColor = Color.FromArgb(120, 220, 200),
            Font = new Font("Consolas", 8.5f), BorderStyle = BorderStyle.FixedSingle
        };
        grpLog.Controls.Add(_txtLog);
        Controls.Add(grpLog);

        BuildRuntimeTab(tabRuntime);
        BuildHeroStatsTab(tabHeroes);
        DetectInstalledBuild();

        Log("Trainer ready.");
        Log("ACTK BYPASS tab: offsets per build — see README.md if a hotfix broke features.");
    }

    private void BuildRuntimeTab(TabPage page)
    {
        int y = 8;

        // === 1. Connect ===
        var grp1 = MakeGroup(page, "1. Connect to Game", ref y, 74);
        _btnAttach = MakeBtn("Connect", 15, 22, 110);
        _btnAttach.Click += OnAttach;
        var btnLaunch = MakeBtn("Launch Game", 132, 22, 110);
        btnLaunch.Click += OnLaunchGame;
        _lblStatus = new Label { Text = "Disconnected", ForeColor = Color.FromArgb(255, 100, 100), Location = new Point(252, 27), AutoSize = true };
        // Known issue: closing the game while the hook is active can crash it on exit.
        var lblCloseNote = new Label
        {
            Text = "⚠ Press Disconnect before closing the game (otherwise the game may crash on exit).",
            ForeColor = Color.FromArgb(255, 200, 50), Location = new Point(15, 52), AutoSize = true,
            Font = new Font("Segoe UI", 8.5f)
        };
        grp1.Controls.AddRange(new Control[] { _btnAttach, btnLaunch, _lblStatus, lblCloseNote });

        // === 2. Anti-Cheat Bypass ===
        var grp2 = MakeGroup(page, "2. Anti-Cheat Bypass (ACTk)", ref y, 55);
        _btnBypass = MakeBtn("Disable ACTk", 15, 22, 150);
        _btnBypass.Click += OnBypass;
        _btnBypass.Enabled = false;
        _lblBypassStatus = new Label { Text = "ACTk active", ForeColor = Color.FromArgb(255, 200, 50), Location = new Point(175, 27), AutoSize = true };
        grp2.Controls.AddRange(new Control[] { _btnBypass, _lblBypassStatus });

        // === 3. Game Speed (Speedhack) ===
        var grp3 = MakeGroup(page, "3. Game Speed (Speedhack)", ref y, 140);
        _chkSpeed = new CheckBox
        {
            Text = "Enable Speedhack", Location = new Point(15, 24), AutoSize = true,
            ForeColor = TextMain
        };
        _chkSpeed.CheckedChanged += OnSpeedToggle;
        grp3.Controls.Add(_chkSpeed);

        // All speed controls live in a panel that is hidden until Speedhack is enabled.
        _pnlSpeed = new Panel
        {
            Location = new Point(3, 48), Size = new Size(536, 88),
            BackColor = BgForm, Visible = false
        };

        _trkSpeed = new TrackBar
        {
            Location = new Point(6, 0), Size = new Size(522, 45),
            Minimum = 0, Maximum = (int)MaxSpeed, Value = 1, TickFrequency = 2
        };
        _trkSpeed.ValueChanged += OnSpeedSlider;

        _pnlSpeed.Controls.Add(new Label { Text = "Speed:", Location = new Point(12, 55), AutoSize = true });
        _txtSpeed = MakeTxt(67, 52, 80);
        _txtSpeed.Text = "1.0";
        _txtSpeed.KeyDown += OnSpeedKeyDown;
        _txtSpeed.Leave += (_, _) => CommitTypedSpeed();
        _lblSpeedValue = new Label { Text = "Speed  1.00", Location = new Point(160, 55), AutoSize = true, ForeColor = AccentCyan, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
        _lblSpeedStatus = new Label { Text = $"(0x - {MaxSpeed:0}x  -  type and press Enter, or drag)", Location = new Point(265, 56), AutoSize = true, ForeColor = Color.FromArgb(150, 150, 150), Font = new Font("Segoe UI", 8.5f) };

        _pnlSpeed.Controls.AddRange(new Control[] { _trkSpeed, _txtSpeed, _lblSpeedValue, _lblSpeedStatus });
        grp3.Controls.Add(_pnlSpeed);

        // === 4. Stash Item Spawn (TBHHook + il2cpp) ===
        var grp6 = MakeGroup(page, "4. Stash Item Spawn", ref y, 226);
        grp6.Controls.Add(new Label
        {
            Text = "Each ItemKey has a fixed rarity in the catalog. Grade picks another row (e.g. 304061 Immortal → Cosmic uses 309161). Export catalog to browse IDs.",
            Font = new Font("Segoe UI", 8.5f), ForeColor = Color.FromArgb(150, 150, 150),
            Location = new Point(15, 20), AutoSize = true, MaximumSize = new Size(515, 0)
        });
        grp6.Controls.Add(new Label { Text = "ItemKey:", Location = new Point(15, 62), AutoSize = true });
        _txtItemKey = MakeTxt(75, 59, 100);
        _txtItemKey.Text = "1001";
        _txtItemKey.Enabled = false;

        grp6.Controls.Add(new Label { Text = "Grade:", Location = new Point(190, 62), AutoSize = true });
        _cmbGrade = new SafeComboBox
        {
            Location = new Point(235, 58), Width = 145, DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = BgInput, ForeColor = AccentCyan, FlatStyle = FlatStyle.Flat, Enabled = false
        };
        foreach (TrainerBridge.GradeType g in Enum.GetValues<TrainerBridge.GradeType>())
            _cmbGrade.Items.Add(TrainerBridge.GradeLabel(g));
        _cmbGrade.SelectedIndex = (int)TrainerBridge.GradeType.Legendary;

        grp6.Controls.Add(new Label { Text = "Count:", Location = new Point(395, 62), AutoSize = true });
        _txtSpawnCount = MakeTxt(443, 59, 55);
        _txtSpawnCount.Text = "1";
        _txtSpawnCount.Enabled = false;

        _btnSpawnItem = MakeBtn("Spawn", 15, 94, 105);
        _btnSpawnItem.Enabled = false;
        _btnSpawnItem.Click += OnSpawnItem;

        _btnScanItems = MakeBtn("Scan items", 130, 94, 105);
        _btnScanItems.Enabled = false;
        _btnScanItems.Click += OnScanItems;

        _btnExportCatalog = MakeBtn("Export catalog", 245, 94, 120);
        _btnExportCatalog.Enabled = false;
        _btnExportCatalog.Click += OnExportCatalog;

        _lblSpawnStatus = new Label
        {
            Text = "Hook required", Location = new Point(378, 101), AutoSize = true,
            MaximumSize = new Size(150, 34),
            ForeColor = TextDim, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
        };
        // Opens stage boxes one at a time: box found -> wait 2s -> open -> 2s -> next.
        _chkAutoOpenBoxes = new CheckBox
        {
            Text = "Auto open boxes (every 2s)", Location = new Point(15, 136), AutoSize = true,
            ForeColor = TextMain, Checked = true
        };
        _chkAutoOpenBoxes.CheckedChanged += (_, _) => ApplyCombatToggle(16, _chkAutoOpenBoxes.Checked);

        grp6.Controls.AddRange(new Control[] { _txtItemKey, _cmbGrade, _txtSpawnCount, _btnSpawnItem, _btnScanItems, _btnExportCatalog, _chkAutoOpenBoxes, _lblSpawnStatus });
    }

    /// <summary>Finds the installed GameAssembly.dll and its build for the startup version pop-up.</summary>
    private void DetectInstalledBuild()
    {
        _startupDllPath = GameBuildProfile.FindInstalledGameAssembly();
        if (_startupDllPath == null)
        {
            Log("GameAssembly.dll not found in any Steam library — version check runs on Connect.");
            return;
        }
        _startupDiskBuild = GameBuildProfile.DetectFromDisk(_startupDllPath);
        if (_startupDiskBuild != null)
        {
            Log($"Installed game build: {_startupDiskBuild.VersionLabel}.");
            return;
        }
        // Newer than this trainer: say up front whether the detector scan will carry it.
        var scan = ActkScanner.Scan(_startupDllPath);
        Log(scan.Ok
            ? $"Installed game build: unknown, but the ACTk detectors were found by scanning ({scan.Detail}) — " +
              "ACTk bypass and Speedhack will work on Connect."
            : $"Installed game build: unknown and the detector scan did not find them ({scan.Detail}).");
    }

    private void BuildHeroStatsTab(TabPage page)
    {
        page.Controls.Add(new Label
        {
            Text = "Enter a run with active heroes, connect to the game, then scan or apply a persistent lock.",
            Location = new Point(14, 14), AutoSize = true, ForeColor = TextDim,
            MaximumSize = new Size(515, 0)
        });

        _btnScanHeroes = MakeBtn("Scan active heroes", 14, 45, 160);
        _btnScanHeroes.Enabled = false;
        _btnScanHeroes.Click += OnScanHeroes;
        page.Controls.Add(_btnScanHeroes);

        _cmbHeroIndex = new SafeComboBox
        {
            Location = new Point(185, 48), Size = new Size(90, 25),
            DropDownStyle = ComboBoxStyle.DropDownList, BackColor = BgInput, ForeColor = TextMain
        };
        _cmbHeroIndex.Items.AddRange(["Hero 1", "Hero 2", "Hero 3"]);
        _cmbHeroIndex.SelectedIndex = 0;

        _cmbHeroStat = new SafeComboBox
        {
            Location = new Point(285, 48), Size = new Size(120, 25),
            DropDownStyle = ComboBoxStyle.DropDownList, BackColor = BgInput, ForeColor = TextMain
        };
        _cmbHeroStat.Items.AddRange(["HP Lock", "Attack Speed Lock", "Attack Dmg Lock", "Crit Chance Lock",
            "Crit Dmg Lock", "CDR Lock", "Armor Lock", "Move Speed Lock", "Cast Speed Lock",
            "Area of Effect Lock", "Skill Range Lock", "Projectile Count Lock", "Multistrike Lock",
            "Clear Hero Locks"]);
        _cmbHeroStat.SelectedIndex = 0;

        _txtHeroValue = MakeTxt(415, 48, 70);
        _txtHeroValue.Text = "2000000000";
        _btnWriteHeroStat = MakeBtn("Apply", 490, 45, 48);
        _btnWriteHeroStat.Enabled = false;
        _btnWriteHeroStat.Click += OnWriteHeroStat;
        page.Controls.AddRange(new Control[] { _cmbHeroIndex, _cmbHeroStat, _txtHeroValue, _btnWriteHeroStat });

        // Combat toggles: on by default, applied automatically on Connect.
        _chkOneHitKill = new CheckBox
        {
            Text = "One-hit kill (monsters at 1 HP)", Location = new Point(14, 84), AutoSize = true,
            ForeColor = TextMain, Checked = true
        };
        _chkOneHitKill.CheckedChanged += (_, _) => ApplyCombatToggle(12, _chkOneHitKill.Checked);
        _chkGodMode = new CheckBox
        {
            Text = "God mode (HP refill 33ms + Armor)", Location = new Point(280, 84), AutoSize = true,
            ForeColor = TextMain, Checked = true
        };
        _chkGodMode.CheckedChanged += (_, _) => ApplyCombatToggle(13, _chkGodMode.Checked);
        page.Controls.AddRange(new Control[] { _chkOneHitKill, _chkGodMode });

        _btnMaxHeroStats = MakeBtn("Max stats (all heroes)", 14, 108, 180);
        _btnMaxHeroStats.Enabled = false;
        _btnMaxHeroStats.Click += (_, _) => OnMaxHeroStats(true);
        Button btnClearMax = MakeBtn("Release", 200, 108, 70);
        btnClearMax.Click += (_, _) => OnMaxHeroStats(false);
        _btnSelfCheck = MakeBtn("Self-check", 278, 108, 92);
        _btnSelfCheck.Enabled = false;
        _btnSelfCheck.Click += (_, _) => OnSelfCheck();
        Label lblMax = new()
        {
            Text = "Atk Dmg/Spd, Crit, CDR, Armor, Move, Cast, AoE, Skill Range, Projectiles, Multistrike",
            Location = new Point(14, 140), AutoSize = true, ForeColor = TextDim,
            Font = new Font("Segoe UI", 8f)
        };
        page.Controls.AddRange(new Control[] { _btnMaxHeroStats, btnClearMax, _btnSelfCheck, lblMax });

        _txtHeroReport = new TextBox
        {
            Location = new Point(14, 162), Size = new Size(514, 410),
            Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, ReadOnly = true,
            BackColor = Color.FromArgb(12, 13, 18), ForeColor = Color.FromArgb(120, 220, 200),
            Font = new Font("Consolas", 8.5f), BorderStyle = BorderStyle.FixedSingle
        };
        page.Controls.Add(_txtHeroReport);
    }

    private void OnDrawTab(object? sender, DrawItemEventArgs e)
    {
        var page = _tabs.TabPages[e.Index];
        bool selected = e.Index == _tabs.SelectedIndex;
        using (var b = new SolidBrush(selected ? AccentDim : BtnBg))
            e.Graphics.FillRectangle(b, e.Bounds);
        TextRenderer.DrawText(
            e.Graphics, page.Text, _tabs.Font, e.Bounds,
            selected ? AccentCyan : TextDim,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private GroupBox MakeGroup(Control parent, string text, ref int y, int height)
    {
        var g = new GroupBox
        {
            Text = text, Location = new Point(4, y), Size = new Size(544, height),
            ForeColor = AccentCyan, Font = new Font("Segoe UI", 9, FontStyle.Bold),
            BackColor = BgForm
        };
        parent.Controls.Add(g);
        y += height + 6;
        return g;
    }

    private Button MakeBtn(string text, int x, int y, int w)
    {
        var b = new ThemedButton
        {
            Text = text, Location = new Point(x, y), Size = new Size(w, 30),
            FlatStyle = FlatStyle.Flat, BackColor = BtnBg,
            ForeColor = TextMain, Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderColor = AccentCyan;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = AccentDim;
        b.FlatAppearance.MouseDownBackColor = AccentCyan;
        return b;
    }

    private TextBox MakeTxt(int x, int y, int w) => new()
    {
        Location = new Point(x, y), Width = w,
        BackColor = BgInput, ForeColor = AccentCyan,
        BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 10, FontStyle.Bold)
    };

    private void PaintHeader(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var rect = _header.ClientRectangle;

        using (var bg = new System.Drawing.Drawing2D.LinearGradientBrush(
            rect, Color.FromArgb(18, 20, 30), Color.FromArgb(36, 26, 54), 8f))
            g.FillRectangle(bg, rect);

        int tx = 14;
        if (Logo != null)
        {
            const int sz = 58;
            g.DrawImage(Logo, new Rectangle(12, 9, sz, sz));
            tx = 12 + sz + 10;
        }

        using var titleFont = new Font("Segoe UI", 22, FontStyle.Bold);
        var titleRect = new Rectangle(tx, 4, 360, 44);
        using (var tb = new System.Drawing.Drawing2D.LinearGradientBrush(
            titleRect, AccentCyan, AccentPurp, 0f))
            g.DrawString("DENOISER", titleFont, tb, tx, 6);

        float dw = g.MeasureString("DENOISER", titleFont).Width;
        using var subFont = new Font("Segoe UI", 11, FontStyle.Bold);
        using (var db = new SolidBrush(TextDim))
            g.DrawString("TRAINER", subFont, db, tx + dw - 10, 20);

        using var tagFont = new Font("Segoe UI", 8.5f);
        using (var tg = new SolidBrush(Color.FromArgb(120, 125, 142)))
            g.DrawString($"Taskbar Hero 1.00.08–1.00.09, 1.2.4–1.2.8   •   v{AppInfo.Version}   •   ACTk + Speed + Hero + Spawn",
                tagFont, tg, tx, 50);

        using var pen = new Pen(AccentCyan, 2);
        g.DrawLine(pen, 0, rect.Height - 2, rect.Width, rect.Height - 2);
    }

    private void Log(string msg)
    {
        if (_txtLog == null) return;
        string line = $"[{DateTime.Now:HH:mm:ss}] {msg}\r\n";
        if (_txtLog.InvokeRequired) _txtLog.Invoke(() => _txtLog.AppendText(line));
        else _txtLog.AppendText(line);
    }

    private void OnAttach(object? sender, EventArgs e)
    {
        if (_mem.IsAttached)
        {
            _bridge.SetSpeed(false, 1.0f);
            _bridge.Pause();
            Thread.Sleep(150);   // let the hook finish its current step before we let go
            _bridge.Dispose();
            _scanBridge?.Dispose();
            _scanBridge = null;
            _heroScanBridge?.Dispose();
            _heroScanBridge = null;
            _mem.Detach();
            _patcher = null;
            _pid = -1;
            _injected = false;
            _btnAttach.Text = "Connect";
            _lblStatus.Text = "Disconnected";
            _lblStatus.ForeColor = Color.FromArgb(255, 100, 100);
            _btnBypass.Enabled = false;
            _chkSpeed.Checked = false;
            _pnlSpeed.Visible = false;
            _btnScanHeroes.Enabled = false;
            _btnWriteHeroStat.Enabled = false;
            _btnMaxHeroStats.Enabled = false;
            _btnSelfCheck.Enabled = false;
            SetSpawnControlsEnabled(false);
            Log("Disconnected. The hook is idle now — it is safe to close the game.");
            return;
        }

        // Auto-select the game when exactly one TaskBarHero process is running;
        // otherwise (none, several, or attach failed) fall back to the manual picker.
        int pid = FindGamePid();
        if (pid >= 0 && _mem.AttachToPid(pid))
        {
            Log($"Auto-selected TaskBarHero (PID {pid}).");
        }
        else
        {
            if (pid < 0) Log("TaskBarHero not found automatically — pick the game process.");
            using var picker = new ProcessPickerForm();
            if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedPid < 0)
            {
                Log("Process selection cancelled.");
                return;
            }
            pid = picker.SelectedPid;
            if (!_mem.AttachToPid(pid)) pid = -1;
        }

        if (pid >= 0)
        {
            _pid = pid;

            var gaPath = GameBuildProfile.ResolveDllPath(_mem, _startupDllPath);
            var detected = GameBuildProfile.DetectForAttach(_mem, gaPath);
            _buildDetected = detected != null;
            if (detected != null)
            {
                _buildProfile = detected;
                if (_buildProfile.FromScan)
                {
                    Log("Unknown game build — located the ACTk detectors by scanning GameAssembly.dll " +
                        $"({GameBuildProfile.LastScanDetail}).");
                    Log("  ACTk bypass and Speedhack work. Hero stats, item spawn and scans stay off " +
                        "until the offsets for this build are verified.");
                }
                else
                {
                    Log($"Detected game build: {_buildProfile.VersionLabel}.");
                }
                foreach (var feature in DisabledFeatures(_buildProfile))
                    Log($"  Not available on {_buildProfile.VersionLabel}: {feature}.");
            }
            else
            {
                _buildProfile = GameBuildProfile.V1008;
                Log($"WARNING: Unknown game build — none of {string.Join(" / ", GameBuildProfile.All.Select(p => p.VersionLabel))} matched.");
                Log("  ACTk will refuse to patch (signature check). Game-API features are disabled.");
                Log("  Update GameBuildProfile from an Il2CppDumper dump (see README.md).");
            }
            WarnIfUnsupportedBuild(detected, gaPath);

            _patcher = new AntiCheatPatcher(_mem, _buildProfile, gaPath);
            _btnAttach.Text = "Disconnect";
            _lblStatus.Text = $"Connected: {_mem.ProcessName} | GA @ 0x{_mem.GameAssemblyBase.ToInt64():X}";
            _lblStatus.ForeColor = Color.FromArgb(100, 255, 100);
            _btnBypass.Enabled = true;
            // Unverified builds get a metadata-only layout dump instead of the live hero scan.
            _btnScanHeroes.Enabled = true;
            _btnScanHeroes.Text = HeroApiAllowed ? "Scan active heroes" : "Dump hero layout";
            _btnWriteHeroStat.Enabled = HeroApiAllowed;
            _btnMaxHeroStats.Enabled = HeroApiAllowed;
            _btnSelfCheck.Enabled = true;
            SetSpawnControlsEnabled(GameApiAllowed);
            _lblSpawnStatus.Text = GameApiAllowed
                ? "Connect hook (enable speed or spawn)"
                : $"Not supported on {(_buildDetected ? _buildProfile.VersionLabel : "unknown build")}";
            _lblSpawnStatus.ForeColor = TextDim;
            Log($"Connected! PID {_pid} | GameAssembly.dll base: 0x{_mem.GameAssemblyBase.ToInt64():X}");
            AutoSetupAfterConnect();
        }
        else
        {
            Log("ERROR: could not attach (process has no GameAssembly.dll?).");
            Log("  Make sure the trainer runs as Administrator.");
        }
    }

    /// <summary>PID of the single running TaskBarHero process, or -1 if none / ambiguous.</summary>
    private const string SteamAppId = "3678970";

    /// <summary>Starts the game through Steam (keeps Steam overlay / cloud saves working).</summary>
    private void OnLaunchGame(object? sender, EventArgs e)
    {
        if (FindGamePid() >= 0 || System.Diagnostics.Process.GetProcessesByName("TaskBarHero").Length > 0)
        {
            Log("Taskbar Hero is already running — press Connect.");
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"steam://rungameid/{SteamAppId}")
            {
                UseShellExecute = true,
            });
            Log("Launching Taskbar Hero through Steam… press Connect once you are in the game.");
        }
        catch (Exception ex)
        {
            Log($"Could not launch the game through Steam: {ex.Message}");
        }
    }

    private static int FindGamePid()
    {
        var procs = System.Diagnostics.Process.GetProcessesByName("TaskBarHero");
        try { return procs.Length == 1 ? procs[0].Id : -1; }
        finally { foreach (var p in procs) p.Dispose(); }
    }

    /// <summary>
    /// One-click setup on Connect: ACTk bypass → inject hook → immortal heroes
    /// (HP + Attack Speed locks, re-applied by the hook every ~0.2s).
    /// Only on a detected build with verified hero offsets; ACTk must be fully bypassed first.
    /// </summary>
    private void AutoSetupAfterConnect()
    {
        if (!HeroApiAllowed) return;
        Log("--- Auto setup: ACTk bypass + hero locks ---");
        if (!RunBypass())
        {
            Log("Auto setup stopped: ACTk bypass incomplete.");
            return;
        }
        if (!EnsureSpeedReady()) return;
        AutoApplyHeroLocks();
    }

    private void OnBypass(object? sender, EventArgs e) => RunBypass();

    /// <returns>True when every ACTk detector is neutralized.</returns>
    private bool RunBypass()
    {
        if (_patcher == null) return false;

        var report = _patcher.PatchDetectors();
        int ok = report.Count(r => r.ok);
        Log($"--- ACTk Bypass: {ok}/{report.Count} detectors neutralized ---");
        foreach (var (name, success, detail) in report)
            Log($"  [{(success ? "OK" : "SKIP")}] {name} - {detail}");

        if (ok == report.Count)
        {
            _lblBypassStatus.Text = "ACTk DISABLED (verified)";
            _lblBypassStatus.ForeColor = Color.FromArgb(100, 255, 100);
            Log("All detectors neutralized, including SpeedHackDetector.");
            return true;
        }
        else if (ok > 0)
        {
            _lblBypassStatus.Text = $"ACTk PARTIAL ({ok}/{report.Count})";
            _lblBypassStatus.ForeColor = Color.FromArgb(255, 200, 50);
            Log("WARNING: some patches failed. Run as Administrator.");
        }
        else
        {
            _lblBypassStatus.Text = "ACTk active (failed)";
            _lblBypassStatus.ForeColor = Color.FromArgb(255, 100, 100);
            Log("ERROR: no patch applied.");
        }
        return false;
    }

    /// <summary>Injects TBHHook.dll (once) and connects the shared-memory bridge.</summary>
    private bool EnsureSpeedReady()
    {
        if (_bridge.IsConnected) return true;
        if (_pid < 0) { Log("Connect to the game first."); return false; }

        if (!_injected)
        {
            Log($"Injecting TBHHook.dll into PID {_pid}...");
            var (ok, msg) = Injector.Inject(_pid, DllPath);
            Log($"  {msg}");
            if (!ok) return false;
            _injected = true;
        }

        if (!_bridge.Connect())
        {
            Log("ERROR: injected DLL did not create the shared channel.");
            return false;
        }

        if (!_bridge.WaitHeartbeat())
        {
            Log("ERROR: DLL is loaded but its worker thread is not responding.");
            return false;
        }

        _bridge.PublishBuildProfile(_buildProfile, allowGameApi: GameApiAllowed);

        Log($"Bridge connected. build={_buildProfile.VersionLabel} il2cpp={_bridge.Il2CppReady}, Time={_bridge.TimeResolved}, Stash={_bridge.StashResolved}.");
        if (!_bridge.TimeResolved)
            Log("WARNING: UnityEngine.Time::set_timeScale not resolved; speed may not apply.");
        if (!GameApiAllowed)
        {
            // Stash API is intentionally not published for this build — nothing to wait for.
            _lblSpawnStatus.Text = $"Not supported on {(_buildDetected ? _buildProfile.VersionLabel : "unknown build")}";
            _lblSpawnStatus.ForeColor = TextDim;
        }
        else if (!_bridge.StashResolved)
        {
            Log("WARNING: stash API not resolved; item spawn will fail until the game is fully loaded.");
            _lblSpawnStatus.Text = "Stash API not ready";
            _lblSpawnStatus.ForeColor = Color.FromArgb(255, 200, 50);
        }
        else
        {
            _lblSpawnStatus.Text = "Ready to spawn";
            _lblSpawnStatus.ForeColor = Color.FromArgb(100, 255, 100);
        }
        return true;
    }

    /// <summary>Stash / item / hero features need a detected build with verified game-API offsets.</summary>
    private bool GameApiAllowed => _buildDetected && _buildProfile.GameApiVerified;

    /// <summary>Hero Stats need only the verified Unit / health-controller offsets.</summary>
    private bool HeroApiAllowed => _buildDetected && _buildProfile.HeroStatsVerified;

    private bool EnsureGameApiAllowed(string feature, bool heroOnly = false)
    {
        if (heroOnly ? HeroApiAllowed : GameApiAllowed) return true;
        Log(_buildDetected
            ? $"{feature}: not supported on game build {_buildProfile.VersionLabel} yet (offsets not verified)."
            : $"{feature}: disabled — game build was not recognised.");
        return false;
    }

    private void SetSpawnControlsEnabled(bool enabled)
    {
        _txtItemKey.Enabled = enabled;
        _cmbGrade.Enabled = enabled;
        _txtSpawnCount.Enabled = enabled;
        _btnSpawnItem.Enabled = enabled;
        _btnScanItems.Enabled = enabled && (_buildProfile.ItemScanSupported || _buildProfile.InventoryScanSupported);
        _btnExportCatalog.Enabled = enabled;
    }

    private void OnExportCatalog(object? sender, EventArgs e)
    {
        if (!EnsureGameApiAllowed("Export catalog")) return;
        if (!EnsureSpeedReady())
        {
            Log("Export aborted: inject TBHHook.dll first.");
            return;
        }

        _scanBridge ??= new ItemScanBridge();
        if (!_scanBridge.TryConnect(3000))
        {
            Log("ERROR: scan channel missing — rebuild TBHHook.dll and reinject.");
            return;
        }

        _btnExportCatalog.Enabled = false;
        try
        {
            Log("Dumping full item catalog from game data (yq)...");
            string? report = _scanBridge.RunCatalogDump(20000);
            if (report == null)
            {
                Log("ERROR: catalog dump failed (timeout). Stay in-game on main menu or stash and retry.");
                return;
            }

            using var dlg = new SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = $"TBH_item_catalog_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                Title = "Save item catalog"
            };
            if (dlg.ShowDialog(this) != DialogResult.OK)
            {
                Log("Export cancelled — showing first lines in log only.");
                LogCatalogPreview(report, 30);
                return;
            }

            File.WriteAllText(dlg.FileName, report, Encoding.UTF8);
            int lineCount = report.Split('\n').Length;
            Log($"Catalog saved: {dlg.FileName} ({lineCount} lines).");
            LogCatalogPreview(report, 15);
        }
        catch (Exception ex)
        {
            Log($"Export error: {ex.Message}");
        }
        finally
        {
            _btnExportCatalog.Enabled = true;
        }
    }

    private void LogCatalogPreview(string report, int maxLines)
    {
        int n = 0;
        foreach (string raw in report.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("===")) continue;
            if (line.StartsWith("ITEM "))
            {
                Log(line);
                if (++n >= maxLines) break;
            }
        }
        if (n >= maxLines)
            Log("  … (see saved file for full list)");
    }

    // HP stays exact as a float up to 2^24; Attack Speed above ~50 gains nothing at 60 fps.
    private const float AutoHeroHpValue = 2_000_000_000f;
    private const float AutoHeroAttackSpeedValue = 50f;
    // Unit stats are stored as ratios: live heroes show crit chance 0.03-0.37 (1.0 = 100%),
    // crit damage 3-8x and cooldown reduction up to ~1.8. No cap exists in the data tables,
    // so these stay well above live values without pushing cooldowns towards zero.
    private const float AutoHeroCritChanceValue = 1f;
    private const float AutoHeroCritDamageValue = 100f;
    private const float AutoHeroCdrValue = 10f;
    // Live armor is ~9k-90k; stay under the hook's 1e7 re-apply sanity limit.
    private const float GodModeArmorValue = 5_000_000f;

    /// <summary>
    /// Runs when Speedhack is enabled: scans the active heroes, then applies persistent
    /// HP (current + max) and Attack Speed locks to every hero slot.
    /// </summary>
    private void AutoApplyHeroLocks()
    {
        if (!HeroApiAllowed) return;

        _heroScanBridge ??= new HeroScanBridge();
        if (!_heroScanBridge.TryConnect(3000))
        {
            Log("Auto hero lock skipped: hero channel missing (restart game with the newest hook).");
            return;
        }

        Log($"--- Auto hero lock: HP = {AutoHeroHpValue:N0}, Attack Speed = {Fmt(AutoHeroAttackSpeedValue)}, " +
            $"Crit Chance = {Fmt(AutoHeroCritChanceValue)}, Crit Damage = {Fmt(AutoHeroCritDamageValue)}, CDR = {Fmt(AutoHeroCdrValue)} ---");
        OnScanHeroes(null, EventArgs.Empty);

        for (int hero = 0; hero < _cmbHeroIndex.Items.Count; hero++)
        {
            // 5 = HP, 4 = Attack Speed, 8 = Crit Chance, 9 = Crit Damage, 10 = CDR
            foreach (var (command, value) in new[]
                     {
                         (5, AutoHeroHpValue), (4, AutoHeroAttackSpeedValue),
                         (8, AutoHeroCritChanceValue), (9, AutoHeroCritDamageValue), (10, AutoHeroCdrValue),
                     })
            {
                string? result = _heroScanBridge.WriteValue(command, hero, value);
                Log("  " + (result?.Trim() ?? $"ERROR: Hero {hero + 1} write timed out."));
            }
        }
        Log("Auto hero lock done. Locks re-apply every ~0.2s (also to heroes that spawn or revive later); use \"Clear Hero Locks\" to release.");

        if (_chkOneHitKill.Checked) ApplyCombatToggle(12, true);
        if (_chkGodMode.Checked) ApplyCombatToggle(13, true);
        if (_chkAutoOpenBoxes.Checked && _buildProfile.BoxCountRva > 0) ApplyCombatToggle(16, true);
    }

    /// <summary>
    /// Command 12 = one-hit kill, 13 = god mode (plus an Armor lock on every hero slot,
    /// released again when god mode is turned off), 16 = auto open boxes.
    /// </summary>
    private void ApplyCombatToggle(int command, bool on)
    {
        if (!HeroApiAllowed || !_bridge.IsConnected) return;
        _heroScanBridge ??= new HeroScanBridge();
        if (!_heroScanBridge.TryConnect(3000))
        {
            Log("Combat toggle skipped: hero channel missing (restart game with the newest hook).");
            return;
        }

        string? result = _heroScanBridge.WriteValue(command, 0, on ? 1f : 0f);
        Log(result?.Trim() ?? "ERROR: combat toggle timed out.");
        if (command != 13) return;

        for (int hero = 0; hero < _cmbHeroIndex.Items.Count; hero++)
        {
            string? armor = _heroScanBridge.WriteValue(11, hero, on ? GodModeArmorValue : -1f);
            Log("  " + (armor?.Trim() ?? $"ERROR: Hero {hero + 1} armor lock timed out."));
        }
    }

    /// <summary>
    /// Command 31: one page answering "is everything actually working?" — hook alive, game
    /// running frames, which game APIs resolved, heroes and locks, boxes, stash.
    /// </summary>
    private void OnSelfCheck()
    {
        Log("--- Self-check ---");
        Log($"  trainer v{AppInfo.Version} | game build: {(_buildDetected ? _buildProfile.VersionLabel : "unknown")}" +
            $"{(_buildProfile.FromScan ? " (detectors found by scan)" : "")}");
        Log($"  attached: {(_mem.IsAttached ? $"PID {_pid}" : "no")} | bridge: {(_bridge.IsConnected ? "connected" : "not connected")}");
        if (!_bridge.IsConnected)
        {
            Log("  Press Connect first — the rest of the check needs the hook.");
            return;
        }
        Log($"  hook heartbeat: {_bridge.Heartbeat} | il2cpp {_bridge.Il2CppReady} | Time {_bridge.TimeResolved} | Stash {_bridge.StashResolved}");

        _heroScanBridge ??= new HeroScanBridge();
        if (!_heroScanBridge.TryConnect(3000))
        {
            Log("  ERROR: hero channel missing — restart the game with the newest TBHHook.dll.");
            return;
        }
        string? report = _heroScanBridge.WriteValue(31, 0, 0f, timeoutMs: 10000);
        if (report == null)
        {
            Log("  ERROR: the hook did not answer (is the game running frames?).");
            return;
        }
        foreach (string line in report.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            Log("  " + line.TrimEnd());
    }

    /// <summary>
    /// Command 24: sets every hero stat in the list to its maximum stable value on all
    /// three hero slots and keeps it locked (the hook re-applies it every ~0.2s, so heroes
    /// that spawn or revive later get it too). <paramref name="on"/> false releases them.
    /// </summary>
    private void OnMaxHeroStats(bool on)
    {
        if (!EnsureGameApiAllowed("Hero stats", heroOnly: true)) return;
        if (!EnsureSpeedReady()) return;

        _heroScanBridge ??= new HeroScanBridge();
        if (!_heroScanBridge.TryConnect(3000))
        {
            Log("ERROR: hero control channel missing. Restart game with the newest hook.");
            return;
        }

        string? result = _heroScanBridge.WriteValue(24, 0, on ? 1f : 0f);
        Log(result?.Trim() ?? "ERROR: max stats write timed out.");
    }

    private void OnWriteHeroStat(object? sender, EventArgs e)
    {
        if (!float.TryParse(_txtHeroValue.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float value))
        {
            Log("Invalid hero stat value. Use dot as decimal separator.");
            return;
        }
        if (!EnsureGameApiAllowed("Hero stats", heroOnly: true)) return;
        if (!EnsureSpeedReady()) return;

        _heroScanBridge ??= new HeroScanBridge();
        if (!_heroScanBridge.TryConnect(3000))
        {
            Log("ERROR: hero control channel missing. Restart game with the newest hook.");
            return;
        }

        int command = _cmbHeroStat.SelectedIndex switch
        {
            0 => 5,   // Current + Max HP
            1 => 4,   // Attack Speed
            2 => 3,   // Attack Damage
            3 => 8,   // Critical Chance
            4 => 9,   // Critical Damage
            5 => 10,  // Cooldown Reduction
            6 => 11,  // Armor
            7 => 22,  // Move Speed
            8 => 23,  // Cast Speed
            9 => 25,  // Area of Effect
            10 => 26, // Skill Range
            11 => 27, // Projectile Count
            12 => 28, // Multistrike
            13 => 6,  // Clear locks
            _ => 0
        };
        string? result = _heroScanBridge.WriteValue(command, _cmbHeroIndex.SelectedIndex, value);
        Log(result?.Trim() ?? "ERROR: hero stat write timed out.");
        OnScanHeroes(null, EventArgs.Empty);
    }

    private void OnScanHeroes(object? sender, EventArgs e)
    {
        if (!HeroApiAllowed)
        {
            if (sender != null) OnDumpHeroLayout();
            return;
        }
        if (!EnsureSpeedReady())
        {
            Log("Hero scan aborted: inject TBHHook.dll first.");
            return;
        }

        _heroScanBridge ??= new HeroScanBridge();
        if (!_heroScanBridge.TryConnect(3000))
        {
            Log("ERROR: hero scan channel missing. Restart the game after rebuilding TBHHook.dll.");
            return;
        }

        _btnScanHeroes.Enabled = false;
        try
        {
            Log("--- Hero diagnostics scan ---");
            string? report = _heroScanBridge.RunScan(10000);
            if (report == null)
            {
                _txtHeroReport.Text = "ERROR: hero scan timed out.";
                Log("ERROR: hero scan timed out.");
                return;
            }

            _txtHeroReport.Text = report;
            _txtHeroReport.SelectionStart = 0;
            _txtHeroReport.SelectionLength = 0;
            foreach (string raw in report.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                Log(raw.TrimEnd());
        }
        finally
        {
            _btnScanHeroes.Enabled = _mem.IsAttached && HeroApiAllowed;
        }
    }

    /// <summary>
    /// Metadata-only dump of StageManager / Hero / Unit / health-controller / ObscuredFloat
    /// layouts, saved next to the trainer so offsets can be re-mapped for a new game build.
    /// </summary>
    private void OnDumpHeroLayout()
    {
        if (!EnsureSpeedReady())
        {
            Log("Layout dump aborted: inject TBHHook.dll first.");
            return;
        }

        _heroScanBridge ??= new HeroScanBridge();
        if (!_heroScanBridge.TryConnect(3000))
        {
            Log("ERROR: hero channel missing. Restart the game after rebuilding TBHHook.dll.");
            return;
        }

        _btnScanHeroes.Enabled = false;
        try
        {
            string? report = _heroScanBridge.RunLayoutDump(10000);
            if (report == null)
            {
                Log("ERROR: layout dump timed out.");
                return;
            }

            _txtHeroReport.Text = report;
            _txtHeroReport.SelectionStart = 0;
            _txtHeroReport.SelectionLength = 0;

            string label = _buildDetected ? _buildProfile.VersionLabel : "unknown";
            string file = Path.Combine(AppContext.BaseDirectory, $"hero_layout_{label}.txt");
            try
            {
                File.WriteAllText(file, report);
                Log($"Hero layout dump saved: {file}");
            }
            catch (Exception ex)
            {
                Log($"Hero layout dump shown below (could not save file: {ex.Message}).");
            }
        }
        finally
        {
            _btnScanHeroes.Enabled = _mem.IsAttached;
        }
    }

    private void OnScanItems(object? sender, EventArgs e)
    {
        if (!EnsureGameApiAllowed("Item scan")) return;
        if (!_buildProfile.ItemScanSupported && _buildProfile.InventoryScanSupported)
        {
            RunInventoryScan();
            return;
        }
        if (!_buildProfile.ItemScanSupported)
        {
            Log($"Item scan is not supported on {_buildProfile.VersionLabel}. Use Export catalog to find itemKeys.");
            return;
        }
        if (!EnsureSpeedReady())
        {
            Log("Scan aborted: inject TBHHook.dll first.");
            return;
        }

        Log("Waiting for stash API (open in-game stash)...");
        if (!_bridge.WaitForStash(10000))
        {
            Log("ERROR: Stash API not ready — open the stash screen and try Scan again.");
            return;
        }

        _scanBridge ??= new ItemScanBridge();
        if (!_scanBridge.TryConnect(3000))
        {
            Log("ERROR: scan channel missing. Rebuild TBHHook.dll, copy to publish folder, reinject.");
            return;
        }

        _btnScanItems.Enabled = false;
        try
        {
            Log("--- Item scan (stash slots + inventory UIDs) ---");
            string? report = _scanBridge.RunScan(10000);
            if (report == null)
            {
                Log("ERROR: scan failed (timeout or hook exception).");
                return;
            }

            int? firstKey = null;
            TrainerBridge.GradeType? firstGrade = null;
            foreach (string raw in report.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("===") || line.StartsWith("grade =") ||
                    line.StartsWith("src:") || line.StartsWith("Debug:") ||
                    line.StartsWith("--") || line.StartsWith("Summary:"))
                    continue;
                Log(line);

                if (firstKey.HasValue) continue;
                int k = line.IndexOf("itemKey=", StringComparison.Ordinal);
                if (k < 0) continue;
                string tail = line[(k + 8)..];
                int sp = tail.IndexOf(' ');
                string num = sp > 0 ? tail[..sp] : tail;
                if (int.TryParse(num, out int ik) && ik > 0)
                {
                    firstKey = ik;
                    int g = line.IndexOf("grade=", StringComparison.Ordinal);
                    if (g >= 0)
                    {
                        string gt = line[(g + 6)..];
                        int gsp = gt.IndexOf(' ');
                        string gnum = gsp > 0 ? gt[..gsp] : gt;
                        int giEnd = 0;
                        while (giEnd < gnum.Length && char.IsDigit(gnum[giEnd])) giEnd++;
                        if (giEnd > 0 && int.TryParse(gnum[..giEnd], out int gi) &&
                            TrainerBridge.TryGradeFromInt(gi, out var gr))
                            firstGrade = gr;
                    }
                }
            }

            if (firstKey.HasValue)
            {
                _txtItemKey.Text = firstKey.Value.ToString();
                if (firstGrade.HasValue)
                    _cmbGrade.SelectedIndex = (int)firstGrade.Value;
                Log(firstGrade.HasValue
                    ? $"→ Filled spawn: ItemKey={firstKey}, {TrainerBridge.GradeLabel(firstGrade.Value)}"
                    : $"→ Filled ItemKey={firstKey} (grade unchanged)");
            }
            else
                Log("No itemKey found — stash empty or obfuscated names mismatch (rebuild hook).");
        }
        catch (Exception ex)
        {
            Log($"Scan error: {ex.Message}");
        }
        finally
        {
            _btnScanItems.Enabled = true;
        }
    }

    /// <summary>1.2.6+: read-only inventory listing through the game's slot API.</summary>
    private void RunInventoryScan()
    {
        if (!EnsureSpeedReady()) return;
        _heroScanBridge ??= new HeroScanBridge();
        if (!_heroScanBridge.TryConnect(3000))
        {
            Log("ERROR: hook channel missing — restart the game with the newest TBHHook.dll.");
            return;
        }
        string? report = _heroScanBridge.RunInventoryScan();
        if (report == null) { Log("ERROR: inventory scan timed out."); return; }
        foreach (string line in report.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            Log(line);
    }

    private void OnSpawnItem(object? sender, EventArgs e)
    {
        if (!EnsureGameApiAllowed("Item spawn")) return;
        if (!EnsureSpeedReady())
        {
            Log("Spawn aborted: inject TBHHook.dll first (enable speedhack or retry after connect).");
            return;
        }

        var sinceLast = DateTime.UtcNow - _lastSpawnClick;
        if (_lastSpawnClick != DateTime.MinValue && sinceLast.TotalMilliseconds < 800)
        {
            Log("Wait ~1 second between spawns (inventory sync).");
            return;
        }
        _lastSpawnClick = DateTime.UtcNow;

        Log("Waiting for stash API (open in-game stash if this takes long)...");
        if (!_bridge.WaitForStash(10000))
        {
            Log("ERROR: Stash API still not resolved after 10s.");
            Log("  Open the stash screen in-game, wait 2s, then Spawn again.");
            Log($"  If it persists, the add-item RVAs in GameBuildProfile.{_buildProfile.VersionLabel} need re-mapping (see README.md).");
            _lblSpawnStatus.Text = "Stash API not ready";
            _lblSpawnStatus.ForeColor = Color.FromArgb(255, 100, 100);
            return;
        }
        _lblSpawnStatus.Text = "Ready to spawn";
        _lblSpawnStatus.ForeColor = Color.FromArgb(100, 255, 100);

        if (!int.TryParse(_txtItemKey.Text.Trim(), out int itemKey) || itemKey <= 0)
        {
            Log("ERROR: invalid ItemKey (positive integer required).");
            return;
        }

        if (!int.TryParse(_txtSpawnCount.Text.Trim(), out int count) || count < 1 || count > 99)
        {
            Log("ERROR: spawn count must be 1..99.");
            return;
        }
        if (count > 15)
            Log("WARNING: high spawn count can lag or crash the game — use 1–5 for testing.");

        TrainerBridge.GradeType grade = TrainerBridge.GradeType.Legendary;
        if (_cmbGrade.SelectedIndex >= 0 && _cmbGrade.SelectedIndex <= (int)TrainerBridge.GradeType.None)
            grade = (TrainerBridge.GradeType)_cmbGrade.SelectedIndex;

        int gradeDigit = (itemKey / 1000) % 10;
        if (itemKey >= 100000 && gradeDigit == (int)grade)
            Log($"ItemKey {itemKey} already matches {TrainerBridge.GradeLabel(grade)} — no grade remap.");
        else if (itemKey >= 100000)
            Log($"Note: for gear IDs use catalog row with digit {(int)grade} (e.g. 31{(int)grade}xxx) or remap from lower grade.");

        _btnSpawnItem.Enabled = false;
        try
        {
            Log($"Spawning {count}x ItemKey={itemKey} {TrainerBridge.GradeLabel(grade)}...");
            int ok = _bridge.SpawnItems(itemKey, grade, count);
            if (ok == count)
            {
                Log($"Spawn OK: {ok}/{count} items.");
                int detail = _bridge.LastSpawnDetail;
                if (detail > 1000)
                    Log($"  Grade remap: used ItemKey {detail} (catalog row for {grade}).");
                _lblSpawnStatus.Text = $"Last: {ok}/{count} OK";
                _lblSpawnStatus.ForeColor = Color.FromArgb(100, 255, 100);
            }
            else
            {
                Log($"Spawn partial/failed: {ok}/{count} succeeded.");
                int err = _bridge.LastSpawnErrorCode;
                int detail = _bridge.LastSpawnDetail;
                if (err < 0)
                    Log($"  {TrainerBridge.DescribeSpawnError(err)}");
                else if (detail < 0)
                    Log($"  {TrainerBridge.DescribeSpawnError(detail)}");
                else if (err == 0)
                    Log("  Timeout — hook did not finish (reinject TBHHook or retry).");
                if (detail > 1000 && detail != itemKey)
                    Log($"  Tried remapped ItemKey {detail} for {grade}.");
                Log("  If AddItemResult code 2+: clear inventory/stash space, wait 1s, retry.");
                _lblSpawnStatus.Text = $"Last: {ok}/{count}";
                _lblSpawnStatus.ForeColor = Color.FromArgb(255, 200, 50);
            }
        }
        catch (Exception ex)
        {
            Log($"Spawn error: {ex.Message}");
            _lblSpawnStatus.Text = "Spawn failed";
            _lblSpawnStatus.ForeColor = Color.FromArgb(255, 100, 100);
        }
        finally
        {
            _btnSpawnItem.Enabled = true;
        }
    }

    private void OnSpeedToggle(object? sender, EventArgs e)
    {
        if (_chkSpeed.Checked)
        {
            if (!EnsureSpeedReady())
            {
                _chkSpeed.Checked = false; // re-enters this handler on the unchecked branch
                return;
            }
            _pnlSpeed.Visible = true;
            float v = ParseSpeed();
            _bridge.SetSpeed(true, v);
            Log($"Speedhack ENABLED at {Fmt(v)}x.");
        }
        else
        {
            _pnlSpeed.Visible = false;
            if (_bridge.IsConnected)
            {
                _bridge.SetSpeed(false, 1.0f);
                Log("Speedhack disabled (speed restored to 1.00x).");
            }
        }
    }

    // Fires for both user drags and programmatic Value changes; _suppressSlider
    // guards the programmatic update we do from Apply to avoid redundant pushes.
    private void OnSpeedSlider(object? sender, EventArgs e)
    {
        float v = _trkSpeed.Value;
        if (!_suppressSlider)
            _txtSpeed.Text = Fmt(v);
        _lblSpeedValue.Text = $"Speed  {Fmt(v)}";
        if (!_suppressSlider && _chkSpeed.Checked && _bridge.IsConnected)
            _bridge.SetSpeed(true, v);
    }

    private void OnSpeedKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;
        e.SuppressKeyPress = true; // no ding
        CommitTypedSpeed();
    }

    // Applies a value typed in the box (supports fractions like 2.5 that the
    // integer slider can't reach). Pushes the exact typed value when enabled.
    private void CommitTypedSpeed()
    {
        float v = ParseSpeed();
        _txtSpeed.Text = Fmt(v);
        _lblSpeedValue.Text = $"Speed  {Fmt(v)}";

        _suppressSlider = true;
        _trkSpeed.Value = (int)Math.Clamp(Math.Round(v), 0, MaxSpeed);
        _suppressSlider = false;

        if (_chkSpeed.Checked && _bridge.IsConnected)
        {
            _bridge.SetSpeed(true, v);
            Log($"Applied speed {Fmt(v)}x.");
        }
    }

    // Parse tolerant of both '.' and ',' decimal separators (pt-BR types comma).
    private float ParseSpeed()
    {
        string s = _txtSpeed.Text.Trim().Replace(',', '.');
        if (!float.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v))
            v = 1.0f;
        return Math.Clamp(v, 0f, MaxSpeed);
    }

    private static string Fmt(float v) =>
        v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    // Parse tolerant of both '.' and ',' decimal separators, no clamp.
    private static bool TryParseStat(string text, out float value)
    {
        string s = text.Trim().Replace(',', '.');
        return float.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

#if false
    private void OnApplyHp(object? sender, EventArgs e)
    {
        if (_hero == null) { Log("Connect to the game first."); return; }
        if (!TryParseStat(_txtHp.Text, out float v)) { Log("Invalid HP value."); return; }

        float? before = _hero.ReadHp();
        if (_hero.SetHp(v))
        {
            float? after = _hero.ReadHp();
            Log($"HP set to {Fmt(v)} (was {(before.HasValue ? Fmt(before.Value) : "?")}, now {(after.HasValue ? Fmt(after.Value) : "?")}).");
        }
        else if (_buildProfile.HpStatic == 0)
        {
            Log("HP not configured for 1.00.09 — GameBuildProfile.V1009.HpStatic is 0.");
            Log("  Use Cheat Engine pointer scan (see README.md).");
        }
        else
        {
            Log("Could not write HP. Enter a run/battle first so the hero exists.");
        }
    }

    private void OnApplyAtk(object? sender, EventArgs e)
    {
        if (_hero == null) { Log("Connect to the game first."); return; }
        if (!TryParseStat(_txtAtk.Text, out float v)) { Log("Invalid ATK value."); return; }

        float? before = _hero.ReadAtk();
        if (_hero.SetAtk(v))
        {
            float? after = _hero.ReadAtk();
            Log($"ATK set to {Fmt(v)} (was {(before.HasValue ? Fmt(before.Value) : "?")}, now {(after.HasValue ? Fmt(after.Value) : "?")}).");
        }
        else if (_buildProfile.AtkStatic == 0)
        {
            Log("ATK not configured for 1.00.09 — GameBuildProfile.V1009.AtkStatic is 0.");
            Log("  Use Cheat Engine pointer scan (see README.md).");
        }
        else
        {
            Log("Could not write ATK. Enter a run/battle first so the hero exists.");
        }
    }

#endif

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        try
        {
            if (_bridge.IsConnected)
            {
                _bridge.SetSpeed(false, 1.0f);
                _bridge.Pause();   // closing the trainer also leaves the hook idle
            }
        }
        catch { }
        _heroScanBridge?.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _bridge.Dispose();
        _mem.Dispose();
        base.OnFormClosing(e);
    }
}
