#pragma warning disable CA1416

using System.Diagnostics;
using System.Runtime.InteropServices;
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
    private CheckBox _chkBagToStash = null!;
    private Button _btnBagToStashNow = null!;
    private Button _btnChestUpgrades = null!;
    private ComboBox _cmbChestChance = null!;
    private Button _btnChestLog = null!;

    private ItemScanBridge? _scanBridge;

    private TextBox _txtLog = null!;
    private Panel _header = null!;
    private TabControl _tabs = null!;

    // Detected game build
    private GameBuildProfile _buildProfile = GameBuildProfile.V1008;
    private bool _buildDetected;

    // === theme palette ===
    private static readonly Color BgForm     = Color.FromArgb(16, 18, 24);
    private static readonly Color BgInput    = Color.FromArgb(34, 37, 46);
    private static readonly Color BtnBg      = Color.FromArgb(30, 33, 42);
    private static readonly Color AccentCyan = Color.FromArgb(60, 220, 225);
    private static readonly Color AccentPurp = Color.FromArgb(155, 110, 255);
    private static readonly Color AccentDim  = Color.FromArgb(40, 70, 80);
    private static readonly Color TextMain   = Color.FromArgb(224, 228, 236);
    private static readonly Color TextDim    = Color.FromArgb(135, 140, 156);

    private static readonly Bitmap? Logo = LoadEmbedded<Bitmap>("logo.png");

    /// <summary>
    /// itemKey -> display name, extracted once from the game's localisation bundles (see
    /// item_names.tsv). The item database only carries the key "ItemName_&lt;itemKey&gt;", and
    /// resolving that in the game would mean driving Unity's async localisation API for a
    /// table that does not change between patches.
    /// </summary>
    private static Dictionary<int, string> ItemNames => _itemNames ??= LoadItemNames();
    private static Dictionary<int, string>? _itemNames;

    private static Dictionary<int, string> LoadItemNames()
    {
        var map = new Dictionary<int, string>();
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            string? name = asm.GetManifestResourceNames()
                              .FirstOrDefault(n => n.EndsWith("item_names.tsv", StringComparison.Ordinal));
            if (name == null) return map;
            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (reader.ReadLine() is { } line)
            {
                if (line.Length == 0 || line[0] == '#') continue;
                int tab = line.IndexOf('\t');
                if (tab > 0 && int.TryParse(line[..tab], out int key))
                    map[key] = line[(tab + 1)..].Trim();
            }
        }
        catch { }
        return map;
    }

    /// <summary>Items whose name contains <paramref name="query"/>, or the one with that key.</summary>
    private static List<(int Key, string Name)> FindItemsByName(string query)
    {
        var hits = new List<(int, string)>();
        if (int.TryParse(query, out int exact))
        {
            if (ItemNames.TryGetValue(exact, out string? only)) hits.Add((exact, only));
            return hits;
        }
        foreach (var (key, name) in ItemNames)
            if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
                hits.Add((key, name));
        hits.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return hits;
    }

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
    // 20x stalled stages and got heroes killed on 1.3.0 even with god mode on; 5x is the
    // fastest that stays stable, and stranded monsters stop happening there at all.
    private const float MaxSpeed = 5f;

    public MainForm()
    {
        InitializeComponent();
        InitializeTrayIcon();
        Shown += async (_, _) =>
        {
            // Offer a newer release before the version warning.
            await Updater.CheckAsync(this, Log, interactive: false);
            if (_startupDllPath != null)
                WarnIfUnsupportedBuild(_startupDiskBuild, _startupDllPath);

            // Startup allocates far more than the trainer needs to sit idle; give it back once.
            var settle = new System.Windows.Forms.Timer { Interval = 60_000 };
            settle.Tick += (_, _) => { settle.Stop(); settle.Dispose(); TrimMemory(); };
            settle.Start();
        };
    }

    // Minimize hides the window to the notification area instead of the taskbar.
    private NotifyIcon _trayIcon = null!;

    /// <summary>
    /// Keeps the trainer out of Alt+Tab (and off the taskbar - Windows ties the two together;
    /// a window cannot have a taskbar button without also appearing in the switcher). The tray
    /// icon is always visible to make up for it.
    ///
    /// An invisible owner window does the same job and keeps the normal caption, but it made
    /// the form come up minimised and hidden, so this takes the plain route instead: Windows
    /// draws a tool window with a shorter title bar and no icon in it.
    /// </summary>
    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOOLWINDOW = 0x00000080;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    private void InitializeTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show trainer", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Hide to tray", null, (_, _) => HideToTray());
        menu.Items.Add("Check for updates", null, async (_, _) =>
        {
            RestoreFromTray();
            await Updater.CheckAsync(this, Log, interactive: true);
        });
        menu.Items.Add("Exit", null, (_, _) => Close());

        _trayIcon = new NotifyIcon
        {
            Icon = Icon ?? SystemIcons.Application,
            Text = "KEWI_TBH",
            ContextMenuStrip = menu,
            Visible = true,   // the window has no taskbar button now, so this is the way back
        };
        _trayIcon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) RestoreFromTray(); };

        Resize += (_, _) =>
        {
            if (WindowState != FormWindowState.Minimized) return;
            HideToTray();
        };
    }

    /// <summary>Out of sight, still running. The tray icon is how it comes back.</summary>
    private void HideToTray()
    {
        Hide();
        TrimMemory();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
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
        Text = $"KEWI_TBH v{AppInfo.Version}";
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

        // Support link, top right of the banner. Opens in the default browser.
        var lnkKofi = new LinkLabel
        {
            Text = "☕ Support on Ko-fi",
            Location = new Point(424, 52), AutoSize = true,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            LinkColor = AccentCyan, ActiveLinkColor = AccentPurp, VisitedLinkColor = AccentCyan,
            LinkBehavior = LinkBehavior.HoverUnderline,
            BackColor = BgForm, Cursor = Cursors.Hand
        };
        lnkKofi.LinkClicked += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://ko-fi.com/kewiv") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log($"could not open the Ko-fi page: {ex.Message}");
            }
        };
        _header.Controls.Add(lnkKofi);

        // A tool window gets no minimize button from Windows (that is the price of staying out
        // of Alt+Tab), so hiding to the tray needs a control of its own.
        var btnHide = new Button
        {
            Text = "Hide to tray",
            Location = new Point(448, 10), Size = new Size(120, 26),
            FlatStyle = FlatStyle.Flat, BackColor = BtnBg, ForeColor = TextMain,
            Font = new Font("Segoe UI", 8.5f), Cursor = Cursors.Hand
        };
        btnHide.FlatAppearance.BorderColor = AccentDim;
        btnHide.Click += (_, _) => HideToTray();
        _header.Controls.Add(btnHide);
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
            MaxLength = 0,   // default is 32767 chars, after which a TextBox silently drops new text
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

        // Local test channel. Starts on one machine only; silent everywhere else.
        _debug = new DebugServer(HandleDebugCommand);
        if (_debug.Start()) Log($"Debug channel open at {DebugServer.ChannelDir}.");
        else { _debug.Dispose(); _debug = null; }
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

        // === 4. Boxes & Stash (TBHHook + il2cpp) ===
        var grp6 = MakeGroup(page, "4. Boxes & Stash", ref y, 125);

        // Opens stage boxes one at a time: box found -> wait 2s -> open -> 2s -> next.
        _chkAutoOpenBoxes = new CheckBox
        {
            Text = "Auto open boxes (every 2s)", Location = new Point(15, 26), AutoSize = true,
            ForeColor = TextMain, Checked = true
        };
        _chkAutoOpenBoxes.CheckedChanged += (_, _) => ApplyCombatToggle(16, _chkAutoOpenBoxes.Checked);

        // Empties the bag into the stash once it fills up, so boxes keep opening.
        // Materials go to the Material Stash tab when that DLC is owned; gear is only
        // kept at level 90+ and lower gear stays in the bag.
        _chkBagToStash = new CheckBox
        {
            Text = "Bag → stash when full (materials → Material Stash, gear 90+)", Location = new Point(230, 26), AutoSize = true,
            ForeColor = TextMain, Checked = true
        };
        _chkBagToStash.CheckedChanged += (_, _) => ApplyCombatToggle(33, _chkBagToStash.Checked);

        _btnBagToStashNow = MakeBtn("Empty bag now", 15, 56, 150);
        _btnBagToStashNow.Enabled = false;
        _btnBagToStashNow.Click += (_, _) => OnBagToStashNow();
        // What to write into the two chest drop-chance upgrades, in tenths of a percent.
        // The account sits at 4910 and 8850 (+491% and +885%) before anything is written.
        _cmbChestChance = new ComboBox
        {
            Location = new Point(175, 88), Size = new Size(130, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = BgInput, ForeColor = TextMain, FlatStyle = FlatStyle.Flat,
        };
        _cmbChestChance.Items.AddRange(new object[] { "+1,000%", "+10,000%", "+100,000%", "+1,000,000%" });
        _cmbChestChance.SelectedIndex = 0;

        _btnChestUpgrades = MakeBtn("Chest upgrades", 175, 56, 140);
        _btnChestUpgrades.Enabled = false;
        _btnChestUpgrades.Click += (_, _) => OnChestUpgrades();
        _btnChestLog = MakeBtn("Chest log", 325, 56, 120);
        _btnChestLog.Enabled = false;
        _btnChestLog.Click += (_, _) => OnChestLog();

        grp6.Controls.AddRange(new Control[] { _chkAutoOpenBoxes, _chkBagToStash, _btnBagToStashNow, _btnChestUpgrades, _btnChestLog, _cmbChestChance });
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
            Text = "Atk Dmg/Spd, Crit, CDR, Armor, Move, Cast, AoE, Skill Range, Projectiles, Multistrike,"
                   + Environment.NewLine
                   + "Damage Reduction, Fire / Cold / Lightning / Chaos Resistance",
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
            g.DrawString("KEWI", titleFont, tb, tx, 6);

        float dw = g.MeasureString("KEWI", titleFont).Width;
        using var subFont = new Font("Segoe UI", 11, FontStyle.Bold);
        // Carries the wordmark's gradient on, picking up where KEWI leaves off.
        var subRect = new Rectangle((int)(tx + dw - 10), 18, 70, 22);
        using (var db = new System.Drawing.Drawing2D.LinearGradientBrush(
            subRect, AccentPurp, AccentCyan, 0f))
            g.DrawString("_TBH", subFont, db, tx + dw - 10, 20);

        using var tagFont = new Font("Segoe UI", 8.5f);
        using (var tg = new SolidBrush(Color.FromArgb(120, 125, 142)))
            g.DrawString($"Taskbar Hero 1.00.08–1.00.09, 1.2.4–1.3.1   •   v{AppInfo.Version}   •   ACTk + Speed + Hero + Stash",
                tagFont, tg, tx, 50);

        using var pen = new Pen(AccentCyan, 2);
        g.DrawLine(pen, 0, rect.Height - 2, rect.Width, rect.Height - 2);
    }

    // The log used to grow for the whole session - a trainer left open overnight ends up with
    // a TextBox holding megabytes of text that it still has to lay out and scroll. The oldest
    // half is dropped once it gets long. (MaxLength is set to 0 as well, but only for good
    // measure: AppendText is not subject to it, so the old log was never actually truncated.)
    private const int LogTrimAt = 24000;

    private DebugServer? _debug;

    /// <summary>
    /// One line in, one block of text out. Everything that touches the UI or the bridge is
    /// marshalled onto the UI thread, because the channel runs on a background task.
    /// </summary>
    private string HandleDebugCommand(string request)
    {
        string[] parts = request.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        string cmd = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
        string arg = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        switch (cmd)
        {
            case "ping":
                return "pong " + AppInfo.Version;

            case "log":
            {
                int want = int.TryParse(arg, out int n) && n > 0 ? n : 80;
                lock (_logRing)
                    return string.Join("\n", _logRing.Reverse().Take(want).Reverse());
            }

            case "status":
                return RunOnUi(DescribeStatus);

            case "do":
                return RunOnUi(() => RunDebugAction(arg));

            case "help":
            default:
                return "commands: ping | log [lines] | status | do <action> | help\n" +
                       "actions:  attach bypass bagstash chestlog chestupgrades scanheroes " +
                       "maxstats clearmax selfcheck herolayout invscan stashlist\n" +
                       "          finditem <name> | spawn <key> [n] | speedwatch [secs]";
        }
    }

    private string RunOnUi(Func<string> fn)
    {
        if (!InvokeRequired) return fn();
        string result = string.Empty;
        Invoke(() => { try { result = fn(); } catch (Exception ex) { result = "ERROR " + ex.Message; } });
        return result;
    }

    private string DescribeStatus()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"version      {AppInfo.Version}");
        sb.AppendLine($"build        {(_buildDetected ? _buildProfile.VersionLabel : "not detected")}");
        sb.AppendLine($"game api     {(GameApiAllowed ? "allowed" : "off for this build")}");
        sb.AppendLine($"bridge       {(_bridge.IsConnected ? "connected" : "not connected")}");
        sb.AppendLine($"stash api    {(_bridge.StashResolved ? "resolved" : "not resolved")}");
        sb.AppendLine($"matstash dlc rva 0x{_buildProfile.MatStashDlcRva:X}");
        return sb.ToString().TrimEnd();
    }

    /// <summary>Presses one of the trainer's own buttons. The log carries the result.</summary>
    private string RunDebugAction(string action)
    {
        string[] bits = action.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        string verb = bits.Length > 0 ? bits[0].ToLowerInvariant() : string.Empty;
        string arg = bits.Length > 1 ? bits[1].Trim() : string.Empty;
        switch (verb)
        {
            case "attach":        OnAttach(this, EventArgs.Empty); return "attach requested";
            case "bypass":        OnBypass(this, EventArgs.Empty); return "bypass requested";
            case "bagstash":      OnBagToStashNow();      return "bag -> stash requested";
            case "chestlog":      OnChestLog();           return "chest log requested";
            case "chestupgrades": OnChestUpgrades();      return "chest upgrades requested";
            case "scanheroes":    OnScanHeroes(this, EventArgs.Empty); return "hero scan requested";
            case "maxstats":      OnMaxHeroStats(true);   return "max stats requested";
            case "clearmax":      OnMaxHeroStats(false);  return "clear max requested";
            case "selfcheck":     OnSelfCheck();          return "self check requested";
            case "herolayout":    OnDumpHeroLayout();     return "hero layout dump requested";
            case "invscan":       RunInventoryScan();     return "inventory scan requested";
            case "matadd":
            {
                // Command 61: offer one stash slot to a Material Stash adder candidate.
                // "do matadd <stash slot> [1|2]" - 1 = kbx, 2 = kby.
                string[] a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (a.Length < 1 || !int.TryParse(a[0], out int mslot) || mslot < 0)
                    return "usage: do matadd <stash slot> [1=kbx|2=kby]";
                int which = a.Length > 1 && int.TryParse(a[1], out int w) ? w : 1;
                _heroScanBridge ??= new HeroScanBridge();
                if (!_heroScanBridge.TryConnect(3000)) return "hook channel missing";
                // 61 = the slot is in the stash, 62 = it is in the bag.
                bool fromBag = a.Length > 2 && a[2].Equals("bag", StringComparison.OrdinalIgnoreCase);
                return _heroScanBridge.WriteValue(fromBag ? 62 : 61, mslot, which, timeoutMs: 30000)?.Trim()
                       ?? "material stash adder test timed out";
            }
            case "finditem":
            {
                // Name lookup, done in the trainer against the extracted name table.
                if (arg.Length == 0) return "usage: do finditem <name or key>";
                var hits = FindItemsByName(arg);
                if (hits.Count == 0) return $"no item matches \"{arg}\" ({ItemNames.Count} names loaded)";
                var sb = new System.Text.StringBuilder();
                foreach (var (key, name) in hits.Take(40)) sb.AppendLine($"{key}\t{name}");
                if (hits.Count > 40) sb.AppendLine($"... {hits.Count} matches, showing 40");
                return sb.ToString().TrimEnd();
            }
            case "spawn":
            {
                // Command 25. A spawned item is flagged by the game and cannot be sold.
                string[] sp = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (sp.Length < 1 || !int.TryParse(sp[0], out int spawnKey) || spawnKey <= 0)
                    return "usage: do spawn <itemKey> [count]";
                int count = sp.Length > 1 && int.TryParse(sp[1], out int c) ? c : 1;
                _heroScanBridge ??= new HeroScanBridge();
                if (!_heroScanBridge.TryConnect(3000)) return "hook channel missing";
                return _heroScanBridge.WriteValue(65, spawnKey, count, timeoutMs: 30000)?.Trim()
                       ?? "spawn timed out";
            }
            case "speedwatch":
            {
                // Command 66: sample the heroes for N seconds while the speed is up.
                // "do speedwatch <secs> [speed]". A speed given here is driven by the hook for
                // the watch window only, so the 20x failure can be reproduced on purpose.
                string[] sw = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                int secs = sw.Length > 0 && int.TryParse(sw[0], out int sv) && sv >= 3 ? sv : 15;
                int speed = sw.Length > 1 && int.TryParse(sw[1], out int sp) && sp > 0 ? sp : 0;
                _heroScanBridge ??= new HeroScanBridge();
                if (!_heroScanBridge.TryConnect(3000)) return "hook channel missing";
                return _heroScanBridge.WriteValue(66, speed, secs, timeoutMs: (secs + 25) * 1000)?.Trim()
                       ?? "speed watch timed out";
            }
            case "itemsearch":
            {
                // Command 64: search the game's item database by name or key.
                if (arg.Length == 0) return "usage: do itemsearch <name or key>";
                _heroScanBridge ??= new HeroScanBridge();
                if (!_heroScanBridge.TryConnect(3000)) return "hook channel missing";
                return _heroScanBridge.RunQuery(64, arg)?.Trim() ?? "item search timed out";
            }
            case "onehit":
            case "godmode":
            {
                // Commands 12 and 13. Needed to tell whether a symptom belongs to a feature:
                // turning one off and watching is the only honest way to attribute it.
                bool want = !arg.Equals("off", StringComparison.OrdinalIgnoreCase);
                _heroScanBridge ??= new HeroScanBridge();
                if (!_heroScanBridge.TryConnect(3000)) return "hook channel missing";
                int cmd = verb == "onehit" ? 12 : 13;
                return _heroScanBridge.WriteValue(cmd, 0, want ? 1f : 0f, timeoutMs: 15000)?.Trim()
                       ?? "toggle timed out";
            }
            case "pull":
            {
                // Command 63: move one stash slot into the first free bag slot.
                if (!int.TryParse(arg, out int pslot) || pslot < 0) return "usage: do pull <stash slot>";
                _heroScanBridge ??= new HeroScanBridge();
                if (!_heroScanBridge.TryConnect(3000)) return "hook channel missing";
                return _heroScanBridge.WriteValue(63, pslot, 0f, timeoutMs: 30000)?.Trim()
                       ?? "pull timed out";
            }
            case "mattest":
            {
                // Command 60: move one stash slot into the Material Stash and straight back.
                if (!int.TryParse(arg, out int slot) || slot < 0) return "usage: do mattest <stash slot>";
                _heroScanBridge ??= new HeroScanBridge();
                if (!_heroScanBridge.TryConnect(3000)) return "hook channel missing";
                return _heroScanBridge.WriteValue(60, slot, 0f, timeoutMs: 30000)?.Trim()
                       ?? "material stash test timed out";
            }
            case "stashlist":
            {
                // Command 19 is the hook's read-only stash listing; it has no button of its own.
                _heroScanBridge ??= new HeroScanBridge();
                if (!_heroScanBridge.TryConnect(3000)) return "hook channel missing";
                return _heroScanBridge.WriteValue(19, 0, 0f, timeoutMs: 20000)?.Trim()
                       ?? "stash list timed out";
            }
            default:              return "unknown action: " + action;
        }
    }

    private void Log(string msg)
    {
        if (_txtLog == null) return;
        string line = $"[{DateTime.Now:HH:mm:ss}] {msg}\r\n";
        if (_txtLog.InvokeRequired) _txtLog.Invoke(() => AppendLog(line));
        else AppendLog(line);
    }

    // The TextBox is trimmed to keep the trainer's footprint down, and its handle does not
    // even exist until its tab is shown, so the debug channel reads this ring instead.
    private readonly Queue<string> _logRing = new();
    private const int LogRingLines = 600;

    private void AppendLog(string line)
    {
        lock (_logRing)
        {
            _logRing.Enqueue(line.TrimEnd('\r', '\n'));
            while (_logRing.Count > LogRingLines) _logRing.Dequeue();
        }

        if (_txtLog.TextLength + line.Length > LogTrimAt)
        {
            string text = _txtLog.Text;
            int cut = text.IndexOf('\n', text.Length / 2);
            _txtLog.Text = cut >= 0 ? text[(cut + 1)..] : string.Empty;
        }
        _txtLog.AppendText(line);
    }

    /// <summary>
    /// Hands the working set back to Windows. The pages are still ours and come back on the
    /// next touch - this only stops an idle trainer sitting on ~100 MB of resident memory
    /// while the game and everything else fight over what is left.
    /// </summary>
    private static void TrimMemory()
    {
        // Off the UI thread, and without WaitForPendingFinalizers. Both callers (the tray
        // minimize handler and the startup timer) run on the UI thread, and a finalizer that
        // needs to marshal back to that thread would then deadlock it - a WinForms hang, not a
        // crash, which is what the first version risked. Neither GC.Collect nor EmptyWorkingSet
        // needs the UI thread, so none of this belongs there.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                GC.Collect();
                EmptyWorkingSet(Process.GetCurrentProcess().Handle);
            }
            catch { }
        });
    }

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

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
        SetGameApiControlsEnabled(false);
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
            SetGameApiControlsEnabled(GameApiAllowed);
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
        if (GameApiAllowed && !_bridge.StashResolved)
            Log("Stash API not resolved yet; bag → stash starts working once the game is fully loaded.");
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

    private void SetGameApiControlsEnabled(bool enabled)
    {
        _btnBagToStashNow.Enabled = enabled && _buildProfile.SlotMoveRva > 0;
        _btnChestUpgrades.Enabled = enabled && _buildProfile.AccStatusGetRva > 0;
        _btnChestLog.Enabled = enabled && _buildProfile.BoxCountRva > 0;
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

    // The five values the auto setup used to write by hand are gone: it applies Max stats now,
    // which covers them and the stats they left out. The hook owns those numbers (kMaxAttackDamage
    // and friends in dllmain.cpp), so there is one place to change them instead of two.
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

        Log("--- Auto hero lock: Max stats on every hero ---");
        OnScanHeroes(null, EventArgs.Empty);

        // Max stats rather than the five locks this used to write. Those left Skill Range at 0,
        // and without reach the heroes never land the killing blow: the stranded-monster handler
        // does it for them, thousands of times an hour. Measured overnight with Max stats on,
        // 90% of 393,171 kills were the heroes' own; the night before it was none of them.
        OnMaxHeroStats(true);
        Log("Auto hero lock done. Locks re-apply every ~0.2s (also to heroes that spawn or revive later); use \"Clear Hero Locks\" to release.");

        if (_chkOneHitKill.Checked) ApplyCombatToggle(12, true);
        if (_chkGodMode.Checked) ApplyCombatToggle(13, true);
        if (_chkAutoOpenBoxes.Checked && _buildProfile.BoxCountRva > 0) ApplyCombatToggle(16, true);
        if (_chkBagToStash.Checked && _buildProfile.SlotMoveRva > 0) ApplyCombatToggle(33, true);
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
    /// <summary>
    /// Command 33 with value 2: moves everything out of the bag right now. Materials go to
    /// the Material Stash tab when the DLC is owned, gear only at level 90+, everything else
    /// to the first free stash slot; the toggle keeps its own state.
    /// </summary>
    private void OnBagToStashNow()
    {
        if (!EnsureGameApiAllowed("Bag to stash")) return;
        if (!EnsureSpeedReady()) return;
        _heroScanBridge ??= new HeroScanBridge();
        if (!_heroScanBridge.TryConnect(3000))
        {
            Log("ERROR: hook channel missing - restart the game with the newest TBHHook.dll.");
            return;
        }
        _btnBagToStashNow.Enabled = false;
        try
        {
            Log("--- Emptying the bag into the stash (materials → Material Stash, gear 90+) ---");
            string? result = _heroScanBridge.WriteValue(33, 0, 2f, timeoutMs: 70000);
            Log(result?.Trim() ?? "ERROR: bag to stash timed out.");
            RunInventoryScan();
        }
        finally
        {
            _btnBagToStashNow.Enabled = true;
        }
    }

    /// <summary>
    /// Reads the game's own chest-drop and Plague-chest account upgrades. It only ever reports
    /// until a design maximum is filled in for an upgrade, so nothing is written by surprise.
    /// </summary>
    private void OnChestUpgrades()
    {
        _heroScanBridge ??= new HeroScanBridge();
        if (!_heroScanBridge.TryConnect(3000))
        {
            Log("ERROR: hook channel missing - restart the game with the newest TBHHook.dll.");
            return;
        }
        _btnChestUpgrades.Enabled = false;
        try
        {
            Log("--- Chest upgrades ---");
            // The picker is tenths of a percent: +1,000% is 10,000.
        int perMille = _cmbChestChance.SelectedIndex switch
        {
            1 => 100_000,
            2 => 1_000_000,
            3 => 10_000_000,
            _ => 10_000,
        };
        // Say what is being written: a silent number here is the difference between +1,000%
        // and +1,000,000%, and the log is the only record of which one the account got.
        Log($"--- Chest upgrades: writing drop chance {perMille} (= +{perMille / 10.0:N0}%) ---");
        string? result = _heroScanBridge.WriteValue(34, perMille, 1f, timeoutMs: 15000);
            Log(result?.Trim() ?? "ERROR: the hook did not answer.");
        }
        finally
        {
            _btnChestUpgrades.Enabled = true;
        }
    }

    /// <summary>
    /// Read-only timeline of chests landing and being opened, built from the counts the game
    /// keeps. Hold Shift while clicking to clear it and start a fresh run.
    /// </summary>
    private void OnChestLog()
    {
        _heroScanBridge ??= new HeroScanBridge();
        if (!_heroScanBridge.TryConnect(3000))
        {
            Log("ERROR: hook channel missing - restart the game with the newest TBHHook.dll.");
            return;
        }
        bool clear = (ModifierKeys & Keys.Shift) == Keys.Shift;
        string? result = _heroScanBridge.WriteValue(37, 0, clear ? 1f : 0f, timeoutMs: 15000);
        Log(result?.Trim() ?? "ERROR: the hook did not answer.");
    }


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
    /// Command 64: sets every hero stat in the list to its maximum stable value on all
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
        _debug?.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _bridge.Dispose();
        _mem.Dispose();
        base.OnFormClosing(e);
    }
}
