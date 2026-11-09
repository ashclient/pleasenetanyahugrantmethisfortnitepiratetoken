using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace TreasureLedger;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--self-test"))
        {
            try
            {
                using var overlay = new OverlayWindow();
                using var bitmap = new Bitmap(overlay.Width, overlay.Height);
                overlay.Show();
                overlay.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save("overlay-preview.png");
                using var sample = new Bitmap(1600, 900);
                using (var g = Graphics.FromImage(sample))
                { g.Clear(Color.Black); using var font = new Font("Segoe UI", 40); g.DrawString("CHEST SEARCHED", font, Brushes.White, 520, 380); }
                var capture = new GameCapture("en-US");
                var lines = Task.Run(() => capture.Read(sample)).GetAwaiter().GetResult();
                var ledger = new Ledger();
                new Tracker(ledger).Process(lines, DateTimeOffset.UtcNow);
                if (ledger.Totals.GetValueOrDefault("Chest") != 1) throw new Exception("Synthetic OCR integration failed: " + string.Join(" | ", lines.Select(x => x.Text)));
                File.WriteAllText("smoke-test.txt", "PASS: overlay rendered; packaged exe loaded Windows OCR; synthetic chest recognized. Live Fortnite accuracy not tested.");
                return 0;
            }
            catch (Exception e) { File.WriteAllText("smoke-test.txt", e.ToString()); return 1; }
        }
        using var mutex = new Mutex(true, "TreasureLedger.SingleInstance", out bool created);
        if (!created) { MessageBox.Show("Treasure Ledger is already running. Open it from the system tray."); return 0; }
        try { Application.Run(new LedgerApplication()); return 0; }
        catch (Exception e) { MessageBox.Show(e.Message, "Treasure Ledger", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
    }
}

public sealed class Preferences
{
    public string Language { get; set; } = "ja-JP";
    public int X { get; set; } = 24;
    public int Y { get; set; } = 70;
    public bool ClickThrough { get; set; }
    public double Opacity { get; set; } = .9;
}

public sealed class LedgerApplication : ApplicationContext
{
    readonly LedgerStore store = new();
    readonly Ledger ledger;
    readonly Tracker tracker;
    readonly Preferences preferences;
    readonly OverlayWindow overlay = new();
    readonly NotifyIcon tray;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 400 };
    readonly string preferencesPath;
    GameCapture? capture;
    Dashboard? dashboard;
    bool busy, paused, exiting;
    string status = "Starting recognition…";
    List<Observation> latest = [];

    public LedgerApplication()
    {
        ledger = store.Load();
        tracker = new Tracker(ledger);
        preferencesPath = Path.Combine(store.DirectoryPath, "preferences.json");
        try { preferences = File.Exists(preferencesPath) ? JsonSerializer.Deserialize<Preferences>(File.ReadAllText(preferencesPath)) ?? new() : new(); }
        catch { preferences = new(); }
        var menu = new ContextMenuStrip();
        menu.Items.Add("History & settings", null, (_, _) => OpenDashboard());
        menu.Items.Add("Pause / resume", null, (_, _) => { paused = !paused; Refresh(); });
        var languages = new ToolStripMenuItem("HUD language");
        foreach (var (label, code) in new[] { ("日本語 · Japanese", "ja-JP"), ("English", "en-US") })
            languages.DropDownItems.Add(label, null, (_, _) => SetLanguage(code));
        menu.Items.Add(languages);
        var click = new ToolStripMenuItem("Click-through overlay") { CheckOnClick = true, Checked = preferences.ClickThrough };
        click.CheckedChanged += (_, _) => { preferences.ClickThrough = click.Checked; overlay.SetClickThrough(click.Checked); SavePreferences(); };
        menu.Items.Add(click);
        menu.Items.Add("Show overlay", null, (_, _) => overlay.Show());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "Treasure Ledger", Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => OpenDashboard();
        overlay.ContextMenuStrip = menu;
        overlay.DetailsRequested += (_, _) => OpenDashboard();
        overlay.Move += (_, _) => { if (overlay.Visible) { preferences.X = overlay.Left; preferences.Y = overlay.Top; } };
        overlay.FormClosing += (_, e) => { if (!exiting) { e.Cancel = true; overlay.Hide(); } };
        var requested = new Rectangle(preferences.X, preferences.Y, overlay.Width, overlay.Height);
        var area = Screen.AllScreens.FirstOrDefault(s => s.WorkingArea.IntersectsWith(requested))?.WorkingArea ?? Screen.PrimaryScreen!.WorkingArea;
        overlay.Location = new Point(Math.Clamp(preferences.X, area.Left, Math.Max(area.Left, area.Right - overlay.Width)), Math.Clamp(preferences.Y, area.Top, Math.Max(area.Top, area.Bottom - overlay.Height)));
        overlay.Opacity = Math.Clamp(preferences.Opacity, .5, 1);
        overlay.Show();
        overlay.SetClickThrough(preferences.ClickThrough);
        TryCreateCapture();
        Refresh();
        timer.Tick += async (_, _) => await Tick();
        timer.Start();
    }
    void TryCreateCapture()
    {
        try { capture = new GameCapture(preferences.Language); status = "Waiting for Fortnite"; }
        catch (Exception e) { capture = null; status = e.Message; }
    }
    public void SetLanguage(string code)
    {
        if (code != "ja-JP" && code != "en-US") return;
        preferences.Language = code;
        if (!busy) TryCreateCapture(); else capture = null;
        SavePreferences(); Refresh();
    }
    async Task Tick()
    {
        if (busy || paused || exiting) return;
        if (capture is null) { Refresh(); return; }
        busy = true;
        try
        {
            // Everything after capture is work on a copy; capture only the foreground game.
            using var frame = capture.Capture(out string message);
            status = message;
            if (frame is not null)
            {
                var reader = capture;
                latest = await Task.Run(() => reader.Read(frame));
                if (exiting) return;
                if (tracker.Process(latest, DateTimeOffset.UtcNow)) store.Save(ledger);
            }
        }
        catch (Exception e) { status = "Recognition error: " + e.Message; }
        finally { busy = false; if (!exiting) { if (capture is null) TryCreateCapture(); Refresh(); } }
    }
    void Refresh()
    {
        overlay.UpdateCounts(ledger, tracker.Current, paused ? "Paused" : status, preferences.Language);
        dashboard?.UpdateState(ledger, tracker.Current, tracker.Phase, paused ? "Paused" : status, latest, preferences.Language);
    }
    void OpenDashboard()
    {
        if (dashboard is null || dashboard.IsDisposed)
        {
            dashboard = new Dashboard(SetLanguage, () => { TryCreateCapture(); Refresh(); }, store.DirectoryPath);
            dashboard.FormClosed += (_, _) => dashboard = null;
        }
        Refresh(); dashboard.Show(); dashboard.Activate();
    }
    void SavePreferences() => File.WriteAllText(preferencesPath, JsonSerializer.Serialize(preferences));
    protected override void ExitThreadCore()
    {
        exiting = true; timer.Stop(); timer.Dispose(); SavePreferences();
        tray.Visible = false; tray.Dispose(); dashboard?.Dispose(); overlay.Dispose(); base.ExitThreadCore();
    }
}

public sealed class OverlayWindow : Form
{
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern int SetWindowLong(IntPtr h, int i, int value);
    public event EventHandler? DetailsRequested;
    readonly Label match, total, rewards, state, heading;
    public OverlayWindow()
    {
        Text = "Treasure Ledger"; FormBorderStyle = FormBorderStyle.None; TopMost = true;
        ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(292, 142); BackColor = Color.FromArgb(19, 20, 29);
        Font = new Font("Segoe UI", 9); ForeColor = Color.FromArgb(235, 236, 245);
        heading = Add("TREASURE LEDGER  ·  JP", 13, 10, 240, 17, 8, Color.FromArgb(172, 158, 233));
        var details = Add("···", 260, 5, 25, 22, 12, ForeColor);
        details.Cursor = Cursors.Hand; details.Click += (_, _) => DetailsRequested?.Invoke(this, EventArgs.Empty);
        match = Add("0", 12, 28, 125, 36, 27, ForeColor);
        total = Add("0", 150, 28, 130, 36, 27, ForeColor);
        Add("THIS MATCH", 15, 70, 125, 15, 7.5f, Color.FromArgb(145, 147, 166));
        Add("TOTAL TRACKED", 152, 70, 130, 15, 7.5f, Color.FromArgb(145, 147, 166));
        rewards = Add("Gizmo 0    Screens 0    Other 0", 14, 94, 270, 18, 8, Color.FromArgb(203, 199, 221));
        state = Add("Waiting for Fortnite", 14, 119, 270, 16, 7.5f, Color.FromArgb(145, 147, 166));
        foreach (Control c in Controls) if (c != details) c.MouseDown += Drag;
        MouseDown += Drag;
        Shown += (_, _) => SetWindowDisplayAffinity(Handle, 0x11);
    }
    Label Add(string text, int x, int y, int w, int h, float size, Color color)
    { var label = new Label { Text = text, Bounds = new Rectangle(x, y, w, h), ForeColor = color, Font = new Font("Segoe UI", size), AutoEllipsis = true }; Controls.Add(label); return label; }
    void Drag(object? sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); } }
    public void SetClickThrough(bool enabled)
    { int style = GetWindowLong(Handle, -20); SetWindowLong(Handle, -20, enabled ? style | 0x20 | 0x80000 : style & ~0x20); }
    public void UpdateCounts(Ledger ledger, MatchRecord? current, string status, string language)
    {
        var counts = ledger.Totals;
        match.Text = (current?.Chests ?? 0).ToString("N0"); total.Text = counts.GetValueOrDefault("Chest").ToString("N0");
        rewards.Text = $"Gizmo {counts.GetValueOrDefault("Gizmo")}    Screens {counts.GetValueOrDefault("Loading screen")}    Other {counts.Where(x => x.Key != "Chest" && x.Key != "Gizmo" && x.Key != "Loading screen").Sum(x => x.Value)}";
        heading.Text = "TREASURE LEDGER  ·  " + (language == "ja-JP" ? "JP" : "EN");
        state.Text = status.StartsWith("Watching") ? "● Watching · estimated from visible notifications" : status;
    }
}

public sealed class Dashboard : Form
{
    readonly Label status = new(), totals = new();
    readonly ListView matches = new(), rewards = new();
    readonly TextBox diagnostics = new();
    readonly ComboBox language = new();
    bool updating;
    public Dashboard(Action<string> setLanguage, Action retry, string dataPath)
    {
        Text = "Treasure Ledger · History & settings"; MinimumSize = new Size(760, 650); Size = new Size(840, 740);
        BackColor = Color.FromArgb(19, 20, 29); ForeColor = Color.FromArgb(235, 236, 245); Font = new Font("Segoe UI", 10);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 9 };
        Controls.Add(root);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        root.Controls.Add(new Label { Text = "Treasure Ledger", Font = new Font("Segoe UI", 21), Dock = DockStyle.Fill });
        status.Dock = DockStyle.Fill; status.ForeColor = Color.FromArgb(176, 165, 232); root.Controls.Add(status);
        totals.Dock = DockStyle.Fill; totals.Font = new Font("Segoe UI", 13); root.Controls.Add(totals);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        actions.Controls.Add(new Label { Text = "Fortnite HUD:", AutoSize = true, Margin = new Padding(0, 7, 10, 0) });
        language.DropDownStyle = ComboBoxStyle.DropDownList; language.Width = 190; language.Items.AddRange(["日本語 · Japanese", "English"]);
        language.SelectedIndexChanged += (_, _) => { if (!updating) setLanguage(language.SelectedIndex == 0 ? "ja-JP" : "en-US"); };
        actions.Controls.Add(language);
        var retryButton = new Button { Text = "Retry OCR", AutoSize = true }; retryButton.Click += (_, _) => retry(); actions.Controls.Add(retryButton);
        var folder = new Button { Text = "Open saved data", AutoSize = true }; folder.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", dataPath) { UseShellExecute = true }); actions.Controls.Add(folder);
        root.Controls.Add(actions);
        ConfigureList(matches, ["Match started", "Chests", "Rewards", "State"]); root.Controls.Add(matches);
        ConfigureList(rewards, ["Reward", "Total tracked", "This match"]); root.Controls.Add(rewards);
        root.Controls.Add(new Label { Text = "Latest OCR · for checking detection (held in memory only)", AutoSize = true });
        diagnostics.Multiline = true; diagnostics.ReadOnly = true; diagnostics.ScrollBars = ScrollBars.Vertical; diagnostics.Dock = DockStyle.Fill; root.Controls.Add(diagnostics);
        root.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Counts are estimates from visible notifications, starting when this app runs. Use Windowed Fullscreen. Japanese HUD detection needs real-game verification. Right-click the overlay or tray icon for controls.", ForeColor = Color.FromArgb(158, 159, 178) });
    }
    static void ConfigureList(ListView view, string[] columns)
    { view.Dock = DockStyle.Fill; view.View = View.Details; view.FullRowSelect = true; view.BackColor = Color.FromArgb(27, 28, 40); view.ForeColor = Color.White; view.BorderStyle = BorderStyle.None; foreach (var c in columns) view.Columns.Add(c, c.Contains("started") || c == "Reward" ? 270 : 130); }
    public void UpdateState(Ledger ledger, MatchRecord? current, string phase, string message, List<Observation> latest, string selectedLanguage)
    {
        updating = true; language.SelectedIndex = selectedLanguage == "ja-JP" ? 0 : 1; updating = false;
        status.Text = message + "\n" + phase;
        totals.Text = $"{ledger.Totals.GetValueOrDefault("Chest"):N0} chests tracked   ·   {ledger.Matches.Count:N0} matches   ·   {ledger.Totals.Where(x => x.Key != "Chest").Sum(x => x.Value):N0} rewards";
        matches.BeginUpdate(); matches.Items.Clear();
        foreach (var m in ledger.Matches.AsEnumerable().Reverse().Take(100)) matches.Items.Add(new ListViewItem([m.Started.LocalDateTime.ToString("g"), m.Chests.ToString(), m.Events.Count(x => x.Kind != "Chest").ToString(), m.Partial ? "Partial" : m.Ended.HasValue ? "Finished" : m == current ? "Current" : "Previous run"]));
        matches.EndUpdate(); rewards.BeginUpdate(); rewards.Items.Clear();
        foreach (var kind in Tracker.Rewards) rewards.Items.Add(new ListViewItem([kind, ledger.Totals.GetValueOrDefault(kind).ToString(), (current?.Events.Count(x => x.Kind == kind) ?? 0).ToString()]));
        rewards.EndUpdate(); diagnostics.Text = string.Join(Environment.NewLine, latest.Select(x => x.Text));
    }
}
