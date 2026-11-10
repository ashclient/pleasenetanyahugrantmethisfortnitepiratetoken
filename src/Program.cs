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
                overlay.Show();
                using (var bitmap = overlay.RenderFrame())
                {
                    if (bitmap.GetPixel(0, 0).A != 0) throw new Exception("Overlay background is not transparent.");
                    bitmap.Save("overlay-preview.png");
                }
                overlay.SetClickThrough(true);
                if (!overlay.InteractionLocked) throw new Exception("Overlay must start click-through.");
                overlay.SetClickThrough(false); overlay.SetClickThrough(true);
                using (var hotkey = new InteractionHotkey()) { }
                overlay.SetPreviewValues("999,999 / 9,999,999", "999,999");
                using (var stress = overlay.RenderFrame(1.5f))
                {
                    var bounds = overlay.CellBounds;
                    for (int i = 0; i < bounds.Count; i++)
                    {
                        if (bounds[i].Right * 1.5 > stress.Width || bounds[i].Bottom * 1.5 > stress.Height) throw new Exception("Count exceeds overlay bounds.");
                        for (int j = i + 1; j < bounds.Count; j++) if (bounds[i].IntersectsWith(bounds[j])) throw new Exception("Overlay counts overlap.");
                    }
                    stress.Save("overlay-large-counts.png");
                    using var proof = new Bitmap(stress.Width, stress.Height * 2);
                    using (var g = Graphics.FromImage(proof))
                    {
                        g.Clear(Color.FromArgb(211, 224, 195)); g.DrawImage(stress, 0, 0);
                        using var dark = new SolidBrush(Color.FromArgb(23, 30, 43)); g.FillRectangle(dark, 0, stress.Height, stress.Width, stress.Height);
                        g.DrawImage(stress, 0, stress.Height);
                    }
                    proof.Save("overlay-background-check.png");
                }
                using var sample = new Bitmap(1600, 900);
                using (var g = Graphics.FromImage(sample))
                { g.Clear(Color.Black); using var font = new Font("Segoe UI", 40); g.DrawString("CHEST SEARCHED", font, Brushes.White, 520, 380); }
                var capture = new GameCapture("en-US");
                var lines = Task.Run(() => capture.Read(sample)).GetAwaiter().GetResult();
                if (!lines.Any(x => x.Text.Contains("CHEST", StringComparison.OrdinalIgnoreCase))) throw new Exception("Windows OCR did not read the synthetic text.");
                using var waitingSample = new Bitmap(1600, 900);
                using (var g = Graphics.FromImage(waitingSample))
                { g.Clear(Color.FromArgb(25, 55, 80)); using var font = new Font("Segoe UI", 30, FontStyle.Bold); g.DrawString("WAITING FOR PLAYERS", font, Brushes.White, 500, 45); }
                var headers = Task.Run(() => capture.ReadMatchHeader(waitingSample)).GetAwaiter().GetResult();
                var matchLedger = new Ledger(); var matchTracker = new Tracker(matchLedger); var matchAt = DateTimeOffset.UtcNow;
                List<Observation> HeaderBatch(DateTimeOffset at) => [new("active", 0, 0, "visual-active"), new("header", 0, 0, "match-pass", at), .. headers.Select(x => x with { CapturedAt = at })];
                matchTracker.Process(HeaderBatch(matchAt), matchAt);
                matchTracker.Process([new("active", 0, 0, "visual-active"), new("opening", .7, .32, "event-chest")], matchAt.AddSeconds(10));
                matchTracker.Process(HeaderBatch(matchAt.AddSeconds(20)), matchAt.AddSeconds(20));
                matchTracker.Process([new("active", 0, 0, "visual-active")], matchAt.AddSeconds(20.1));
                matchTracker.Process(HeaderBatch(matchAt.AddSeconds(20.7)), matchAt.AddSeconds(20.7));
                if (matchLedger.Matches.Count != 2 || matchTracker.Current?.Chests != 0 || matchLedger.Totals.GetValueOrDefault("Chest") != 1)
                    throw new Exception("Packaged match-header OCR/reset integration failed: " + string.Join(" | ", headers.Select(x => x.Text)));
                using var japaneseSample = new Bitmap(1600, 900);
                using (var g = Graphics.FromImage(japaneseSample))
                {
                    g.Clear(Color.FromArgb(38, 56, 79)); using var font = new Font("Yu Gothic", 42, FontStyle.Bold);
                    g.DrawString("宝箱", font, Brushes.White, 650, 370);
                    g.DrawString("+100 XP", font, Brushes.Gold, 630, 440);
                }
                LiveRegression.Run();
                string japaneseReport;
                if (Windows.Media.Ocr.OcrEngine.IsLanguageSupported(new Windows.Globalization.Language("ja-JP")))
                {
                    var japaneseCapture = new GameCapture("ja-JP");
                    var japaneseLines = Task.Run(() => japaneseCapture.Read(japaneseSample)).GetAwaiter().GetResult();
                    var japaneseLedger = new Ledger();
                    new Tracker(japaneseLedger).Process(japaneseLines.Select(x => x with { Source = "ocr" }).ToList(), DateTimeOffset.UtcNow);
                    File.WriteAllText("japanese-ocr.txt", JsonSerializer.Serialize(japaneseLines));
                    japaneseSample.Save("japanese-ocr-sample.png");
                    if (japaneseLedger.Totals.GetValueOrDefault("Chest") != 1) throw new Exception("Japanese synthetic OCR failed: " + string.Join(" | ", japaneseLines.Select(x => x.Text)));
                    japaneseReport = "PASS: Japanese synthetic Windows OCR integration.";
                }
                else
                {
                    japaneseReport = "SKIPPED: Japanese OCR integration; this test machine lacks the Japanese OCR feature. Japanese parsing tests are separate; live Japanese game capture still needs validation.";
                    File.WriteAllText("japanese-ocr.txt", japaneseReport);
                }
                File.WriteAllText("smoke-test.txt", "PASS: per-pixel overlay rendered; transparent corners; large counts never overlap at 150% DPI; click-through toggles; Alt+T registers; packaged English Windows OCR reads the synthetic metadata text and dedicated waiting header; short-match reset preserves lifetime total; live chest counts are checked against recorded sequences.\n" + japaneseReport + "\nLive popup-sequence regression results are in live-regression.txt. Captured gameplay on the user PC still requires verification.");
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
    readonly InteractionHotkey interactionHotkey;
    Bitmap? latestFrame;
    DateTimeOffset? capturedAt;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 67 };
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
        preferences.ClickThrough = true;
        interactionHotkey = new InteractionHotkey();
        interactionHotkey.Pressed += (_, _) =>
        {
            preferences.ClickThrough = !preferences.ClickThrough;
            overlay.SetClickThrough(preferences.ClickThrough);
            Refresh();
        };
        menu.Items.Add(new ToolStripMenuItem("Alt+T · unlock / lock overlay") { Enabled = false });
        menu.Items.Add("Show overlay", null, (_, _) => overlay.Show());
        menu.Items.Add("Third-party notices", null, (_, _) =>
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly(); var parts = new List<string>();
            foreach (var name in assembly.GetManifestResourceNames().Where(x => x.StartsWith("ThirdParty.")))
            { using var stream = assembly.GetManifestResourceStream(name)!; using var reader = new StreamReader(stream); parts.Add(name + Environment.NewLine + reader.ReadToEnd()); }
            var path = Path.Combine(store.DirectoryPath, "THIRD_PARTY_NOTICES.txt"); File.WriteAllText(path, string.Join(Environment.NewLine + Environment.NewLine, parts));
            Process.Start(new ProcessStartInfo("notepad.exe", path) { UseShellExecute = true });
        });
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
        // Pixel alpha comes from the overlay renderer; do not set Form.Opacity.
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
                latestFrame?.Dispose(); latestFrame = new Bitmap(frame); capturedAt = DateTimeOffset.UtcNow; latest = [];
                if (GameCapture.LooksBlack(frame))
                { status = "Capture is black · switch Fortnite to Windowed Fullscreen"; latest = []; return; }
                latest = await Task.Run(() => reader.ReadFast(frame, capturedAt!.Value));
                if (exiting) return;
                if (tracker.Process(latest, capturedAt!.Value)) store.Save(ledger);
                status = "Watching Fortnite · " + reader.VisionSummary + " · " + tracker.LastDecision + (string.IsNullOrEmpty(reader.RecognitionNote) ? "" : " · " + reader.RecognitionNote) + (string.IsNullOrEmpty(reader.LastOcrError) ? "" : " · OCR error: " + reader.LastOcrError) + (string.IsNullOrEmpty(reader.LastMatchError) ? "" : " · Match OCR error: " + reader.LastMatchError);
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
            dashboard = new Dashboard(SetLanguage, () => { TryCreateCapture(); Refresh(); }, SaveDiagnosis, store.DirectoryPath);
            dashboard.FormClosed += (_, _) => dashboard = null;
        }
        Refresh(); dashboard.Show(); dashboard.Activate();
    }
    void SaveDiagnosis()
    {
        if (latestFrame is null) { MessageBox.Show("No Fortnite frame has been captured. Bring Fortnite to the foreground in Windowed Fullscreen, then reopen settings using the tray icon."); return; }
        var folder = Path.Combine(store.DirectoryPath, "Diagnostics"); Directory.CreateDirectory(folder);
        latestFrame.Save(Path.Combine(folder, "game-frame.png"), System.Drawing.Imaging.ImageFormat.Png);
        File.WriteAllText(Path.Combine(folder, "recognition.json"), JsonSerializer.Serialize(new { capturedAt, language = preferences.Language, status, phase = tracker.Phase, matchId = tracker.Current?.Id, matchStarted = tracker.Current?.Started, matchEnded = tracker.Current?.Ended, recognitionNote = capture?.RecognitionNote, ocrError = capture?.LastOcrError, decision = tracker.LastDecision, lines = latest }, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(folder, "live-trace.json"), JsonSerializer.Serialize(capture?.Diagnostic));
        Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
    }
    void SavePreferences() => File.WriteAllText(preferencesPath, JsonSerializer.Serialize(preferences));
    protected override void ExitThreadCore()
    {
        exiting = true; timer.Stop(); timer.Dispose(); SavePreferences();
        tray.Visible = false; tray.Dispose(); interactionHotkey.Dispose(); latestFrame?.Dispose(); dashboard?.Dispose(); overlay.Dispose(); base.ExitThreadCore();
    }
}

public sealed class Dashboard : Form
{
    readonly Label status = new(), totals = new();
    readonly ListView matches = new(), rewards = new();
    readonly TextBox diagnostics = new();
    readonly ComboBox language = new();
    bool updating;
    public Dashboard(Action<string> setLanguage, Action retry, Action saveDiagnosis, string dataPath)
    {
        Text = "Treasure Ledger · History & settings"; MinimumSize = new Size(760, 650); Size = new Size(840, 740);
        BackColor = Color.FromArgb(19, 20, 29); ForeColor = Color.FromArgb(235, 236, 245); Font = new Font("Segoe UI", 10);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 9 };
        Controls.Add(root);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
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
        var diagnostic = new Button { Text = "Save diagnosis", AutoSize = true }; diagnostic.Click += (_, _) => saveDiagnosis(); actions.Controls.Add(diagnostic);
        root.Controls.Add(actions);
        ConfigureList(matches, ["Match started", "Chests", "Rewards", "State"]); root.Controls.Add(matches);
        ConfigureList(rewards, ["Reward", "Total tracked", "This match"]); root.Controls.Add(rewards);
        root.Controls.Add(new Label { Text = "Live recognition · Save diagnosis retains the last notification samples", AutoSize = true });
        diagnostics.Multiline = true; diagnostics.ReadOnly = true; diagnostics.ScrollBars = ScrollBars.Vertical; diagnostics.Dock = DockStyle.Fill; root.Controls.Add(diagnostics);
        root.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Counts are estimates from visible notifications, starting when this app runs. Use Windowed Fullscreen. Japanese HUD detection needs real-game verification. Alt+T unlocks/locks the transparent overlay; right-click while unlocked for controls. Save diagnosis exports the preceding notification-area frames and recognition trace locally, even after a missed popup disappears.", ForeColor = Color.FromArgb(158, 159, 178) });
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
