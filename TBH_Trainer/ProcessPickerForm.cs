using System.ComponentModel;

#pragma warning disable CA1416

namespace TBH_Trainer;

internal sealed class ProcessPickerForm : Form
{
    public int SelectedPid { get; private set; } = -1;

    private readonly ListView _list;
    private readonly TextBox _filter;
    private List<GameMemory.ProcessEntry> _all = new();

    // Shared DENOISER theme palette (kept in sync with MainForm).
    private static readonly Color BgForm     = Color.FromArgb(16, 18, 24);
    private static readonly Color BgInput    = Color.FromArgb(34, 37, 46);
    private static readonly Color BtnBg      = Color.FromArgb(30, 33, 42);
    private static readonly Color AccentCyan = Color.FromArgb(60, 220, 225);
    private static readonly Color AccentDim  = Color.FromArgb(40, 70, 80);
    private static readonly Color TextMain   = Color.FromArgb(224, 228, 236);
    private static readonly Color TextDim    = Color.FromArgb(135, 140, 156);

    public ProcessPickerForm()
    {
        Text = "Select Game Process";
        Size = new Size(560, 520);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = BgForm;
        ForeColor = TextMain;
        Font = new Font("Segoe UI", 9.5f);

        Controls.Add(new Label
        {
            Text = "Choose the process (games with GameAssembly.dll appear at the top):",
            Location = new Point(12, 10), AutoSize = true, ForeColor = TextDim
        });

        _filter = new TextBox
        {
            Location = new Point(12, 34), Width = 410,
            BackColor = BgInput, ForeColor = TextMain,
            BorderStyle = BorderStyle.FixedSingle
        };
        _filter.TextChanged += (_, _) => ApplyFilter();
        var btnRefresh = MakeBtn("Refresh", 430, 33, 100, 25);
        btnRefresh.Click += (_, _) => Reload();
        Controls.Add(_filter);
        Controls.Add(btnRefresh);

        _list = new ListView
        {
            Location = new Point(12, 66), Size = new Size(518, 360),
            View = View.Details, FullRowSelect = true, MultiSelect = false,
            BackColor = Color.FromArgb(24, 26, 34), ForeColor = TextMain,
            Font = new Font("Consolas", 9), BorderStyle = BorderStyle.FixedSingle, HideSelection = false
        };
        _list.Columns.Add("PID", 70);
        _list.Columns.Add("Process", 180);
        _list.Columns.Add("GameAssembly", 100);
        _list.Columns.Add("Window", 150);
        _list.DoubleClick += (_, _) => Accept();
        Controls.Add(_list);

        var btnOk = MakeBtn("Select", 330, 436, 100, 30);
        btnOk.FlatAppearance.BorderColor = Color.FromArgb(80, 220, 130);
        btnOk.DialogResult = DialogResult.None;
        btnOk.Click += (_, _) => Accept();
        var btnCancel = MakeBtn("Cancel", 436, 436, 94, 30);
        btnCancel.DialogResult = DialogResult.Cancel;
        Controls.Add(btnOk);
        Controls.Add(btnCancel);
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        Reload();
    }

    private static Button MakeBtn(string text, int x, int y, int w, int h)
    {
        var b = new Button
        {
            Text = text, Location = new Point(x, y), Size = new Size(w, h),
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

    private void Reload()
    {
        _all = GameMemory.ListProcesses();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string f = _filter.Text.Trim();
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var e in _all)
        {
            if (f.Length > 0 &&
                !e.Name.Contains(f, StringComparison.OrdinalIgnoreCase) &&
                !e.Title.Contains(f, StringComparison.OrdinalIgnoreCase) &&
                !e.Pid.ToString().Contains(f))
                continue;

            var item = new ListViewItem(e.Pid.ToString()) { Tag = e.Pid };
            item.SubItems.Add(e.Name);
            item.SubItems.Add(e.HasGameAssembly ? "YES" : "");
            item.SubItems.Add(e.Title);
            if (e.HasGameAssembly) item.ForeColor = Color.FromArgb(120, 255, 160);
            _list.Items.Add(item);
        }
        _list.EndUpdate();
    }

    private void Accept()
    {
        if (_list.SelectedItems.Count == 0) return;
        SelectedPid = (int)_list.SelectedItems[0].Tag!;
        DialogResult = DialogResult.OK;
        Close();
    }
}
