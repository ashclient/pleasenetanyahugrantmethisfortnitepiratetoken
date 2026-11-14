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
                overlay.SetPreviewValues("999,999 / 9,999,999 / 99,999,999", "999,999");
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
                var workerTestFolder = Path.Combine(Path.GetTempPath(), "TreasureLedger-worker-test-" + Guid.NewGuid().ToString("N"));
                try
                {
                    using var workerTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    string worker = ReplayWorkerFiles.EnsureWorker(workerTestFolder);
                    var workerResult = Task.Run(() => ReplayWorkerFiles.Run(worker,"--self-test",workerTimeout.Token)).GetAwaiter().GetResult();
                    if(workerResult.Code != 0 || !workerResult.Output.Contains("\"ok\":true")) throw new Exception("Bundled replay worker failed: " + workerResult.Error);
                    using var workerSource = System.IO.Compression.ZipFile.OpenRead(ReplayWorkerFiles.ExtractSource(workerTestFolder));
                    if(workerSource.GetEntry("replay-worker/COPYING") is null || workerSource.GetEntry("external/chestreader/lib/OozSharp/Kraken.cs") is null) throw new Exception("Replay worker source bundle is incomplete");
                }
                finally { try { Directory.Delete(workerTestFolder,true); } catch { } }
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
                using var lobbySample = new Bitmap(1600,900);
                using(var g = Graphics.FromImage(lobbySample))
                {
                    g.Clear(Color.FromArgb(25,55,80)); using var font=new Font("Segoe UI",30,FontStyle.Bold);
                    g.DrawString("PLAY     LOCKER     SHOP",font,Brushes.White,100,40);
                    g.DrawString("READY!",font,Brushes.White,100,650);
                }
                var lobbyLines=Task.Run(()=>capture.ReadMatchHeader(lobbySample)).GetAwaiter().GetResult();
                List<Observation> LobbyBatch(DateTimeOffset at) => [new("active",0,0,"visual-active"),new("header",0,0,"match-pass",at),..lobbyLines.Select(x=>x with {CapturedAt=at})];
                matchTracker.Process([new("active",0,0,"visual-active"),new("opening",.7,.32,"event-chest")],matchAt.AddSeconds(30));
                var beforeLobby=matchTracker.Current!.Id;
                matchTracker.Process(LobbyBatch(matchAt.AddSeconds(40)),matchAt.AddSeconds(40));
                matchTracker.Process(LobbyBatch(matchAt.AddSeconds(40.7)),matchAt.AddSeconds(40.7));
                matchTracker.ReplayApplied(beforeLobby);
                if(!matchTracker.InLobby || matchTracker.Current is not null || matchLedger.Totals.GetValueOrDefault("Chest")!=2)
                    throw new Exception("Packaged lobby OCR/reset integration failed: "+string.Join(" | ",lobbyLines.Select(x=>x.Text)));
                if(new Preferences().Language!="en-US" || JsonSerializer.Deserialize<Preferences>("{\"Language\":\"ja-JP\"}")!.Language!="ja-JP") throw new Exception("Default/saved HUD language regression");
                using var rareSample=new Bitmap(1600,900);
                using(var g=Graphics.FromImage(rareSample))
                {g.Clear(Color.FromArgb(20,40,60));using var font=new Font("Segoe UI",28,FontStyle.Bold);g.DrawString("+900 XP",font,Brushes.White,1000,220);g.DrawString("RARE CHEST",font,Brushes.LightCyan,1000,270);}
                var rareLines=Task.Run(()=>capture.ReadChestLabels(rareSample)).GetAwaiter().GetResult();
                if(!RareChestBackup.ReadEvidence(rareLines,out _,out _)) throw new Exception("Rare-chest focused OCR failed: "+string.Join(" | ",rareLines.Select(x=>x.Text)));
                using var ordinarySample=new Bitmap(1600,900);
                using(var g=Graphics.FromImage(ordinarySample)){
                    g.Clear(Color.FromArgb(20,40,60));using var font=new Font("Segoe UI",28,FontStyle.Bold);
                    g.DrawString("+100 XP",font,Brushes.White,1000,220);g.DrawString("CHEST",font,Brushes.White,1000,270);
                }
                var ordinaryLines=Task.Run(()=>capture.ReadChestLabels(ordinarySample)).GetAwaiter().GetResult();
                var fusionTest=new LiveChestFusion();var fusionAt=DateTimeOffset.UtcNow;
                List<Observation> FocusedBatch(DateTimeOffset t)=>[new("OCR sample",0,0,"rare-pass",t),..ordinaryLines.Select(x=>x with{CapturedAt=t})];
                fusionTest.Update(FocusedBatch(fusionAt),fusionAt,false,0);
                if(fusionTest.Update(FocusedBatch(fusionAt.AddSeconds(.3)),fusionAt.AddSeconds(.3),false,0)!=1||fusionTest.Update([],fusionAt.AddSeconds(.4),true,1)!=0)throw new Exception("Packaged ordinary-chest live OCR fallback/deduplication failed");
                var visibilityRoundTrip=JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(new Preferences{OverlayVisible=false}));
                if(visibilityRoundTrip!.OverlayVisible)throw new Exception("Hidden-overlay preference was not persisted");
                using var japaneseSample = new Bitmap(1600, 900);
                using (var g = Graphics.FromImage(japaneseSample))
                {
                    g.Clear(Color.FromArgb(38, 56, 79)); using var font = new Font("Yu Gothic", 42, FontStyle.Bold);
                    g.DrawString("宝箱", font, Brushes.White, 650, 370);
                    g.DrawString("+100 XP", font, Brushes.Gold, 630, 440);
                }
                DashboardSmoke.Run();
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
                File.WriteAllText("smoke-test.txt", "PASS: bundled replay worker hash, extraction, process launch, parser construction, container classifier and corresponding source archive; per-pixel overlay rendered; transparent corners; large counts never overlap at 150% DPI; click-through toggles; Alt+T registers; packaged English Windows OCR reads the synthetic metadata text and dedicated waiting header; short-match and lobby resets preserve lifetime totals; replay retains the completed game without resuming active lobby tracking; focused rare-chest text/XP OCR; English default and saved Japanese preference preserved; live chest counts are checked against recorded sequences.\n" + japaneseReport + "\nLive popup-sequence regression results are in live-regression.txt. Encrypted replay parsing and destroyed-chest attribution still need a real completed replay from the player PC.");
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
    public bool ReplayChecking { get; set; } = true;
    public bool OverlayVisible {get;set;}=true;
    public string? ReplayFolder { get; set; }
    public string Language { get; set; } = "en-US";
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
    readonly LinkClient links;
    readonly System.Windows.Forms.Timer linkTimer = new() { Interval = 10000 };
    readonly Preferences preferences;
    readonly OverlayWindow overlay = new();
    readonly NotifyIcon tray;
    readonly ToolStripMenuItem overlayToggle;
    readonly InteractionHotkey interactionHotkey;
    Bitmap? latestFrame;
    DateTimeOffset? capturedAt;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 67 };
    readonly System.Windows.Forms.Timer replayTimer = new() { Interval = 15000 };
    readonly CancellationTokenSource replayCancellation = new();
    readonly ReplayMonitor replayMonitor;
    bool replayBusy;
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
        links=new LinkClient(store.DirectoryPath);
        tracker.SetReplayWaiting(preferences.ReplayChecking);
        if(!ledger.ReplayTrackingFrom.HasValue) { ledger.ReplayTrackingFrom=DateTimeOffset.UtcNow; store.Save(ledger); }
        replayMonitor = new ReplayMonitor(store.DirectoryPath,ledger.ReplayTrackingFrom.Value);
        if(!string.IsNullOrWhiteSpace(preferences.ReplayFolder)) replayMonitor.Folder=preferences.ReplayFolder;
        var menu = new ContextMenuStrip();
        menu.Items.Add("History & settings", null, (_, _) => OpenDashboard());
        menu.Items.Add("Pause / resume", null, (_, _) => { paused = !paused; Refresh(); });
        var languages = new ToolStripMenuItem("HUD language");
        foreach (var (label, code) in new[] { ("日本語 · Japanese", "ja-JP"), ("English", "en-US") })
            languages.DropDownItems.Add(label, null, (_, _) => SetLanguage(code));
        menu.Items.Add(languages);
        var replayToggle = new ToolStripMenuItem("Check completed replays automatically") { Checked=preferences.ReplayChecking, CheckOnClick=true };
        replayToggle.CheckedChanged += (_,_) => { SetReplayChecking(replayToggle.Checked); };
        menu.Items.Add(replayToggle);
        menu.Items.Add("Choose replay folder…",null,(_,_) =>
        {
            using var folder = new FolderBrowserDialog { Description="Fortnite replay folder (Saved/Demos)", SelectedPath=replayMonitor.Folder };
            if(folder.ShowDialog()==DialogResult.OK) { preferences.ReplayFolder=folder.SelectedPath; replayMonitor.Folder=folder.SelectedPath; SavePreferences(); }
        });
        menu.Items.Add("Replay worker source & licenses",null,(_,_) =>
        {
            try { var path=ReplayWorkerFiles.ExtractSource(store.DirectoryPath); Process.Start(new ProcessStartInfo("explorer.exe", "/select,\""+path+"\"") { UseShellExecute=true }); }
            catch(Exception e) { MessageBox.Show(e.Message); }
        });
        preferences.ClickThrough = true;
        interactionHotkey = new InteractionHotkey();
        interactionHotkey.Pressed += (_, _) =>
        {
            preferences.ClickThrough = !preferences.ClickThrough;
            overlay.SetClickThrough(preferences.ClickThrough);
            Refresh();
        };
        menu.Items.Add(new ToolStripMenuItem("Alt+T · unlock / lock overlay") { Enabled = false });
        overlayToggle=new ToolStripMenuItem("Show overlay"){Checked=preferences.OverlayVisible,CheckOnClick=true};
        overlayToggle.CheckedChanged+=(_,_)=>SetOverlayVisible(overlayToggle.Checked);menu.Items.Add(overlayToggle);
        menu.Items.Add("Reset overlay position",null,(_,_)=>ResetOverlayPosition());
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
        overlay.FormClosing += (_, e) => { if (!exiting) { e.Cancel = true; SetOverlayVisible(false); } };
        var requested = new Rectangle(preferences.X, preferences.Y, overlay.Width, overlay.Height);
        var area = Screen.AllScreens.FirstOrDefault(s => s.WorkingArea.IntersectsWith(requested))?.WorkingArea ?? Screen.PrimaryScreen!.WorkingArea;
        overlay.Location = new Point(Math.Clamp(preferences.X, area.Left, Math.Max(area.Left, area.Right - overlay.Width)), Math.Clamp(preferences.Y, area.Top, Math.Max(area.Top, area.Bottom - overlay.Height)));
        // Pixel alpha comes from the overlay renderer; do not set Form.Opacity.
        if(preferences.OverlayVisible)overlay.Show();
        overlay.SetClickThrough(preferences.ClickThrough);
        TryCreateCapture();
        Refresh();
        timer.Tick += async (_, _) => await Tick();
        timer.Start();
        replayTimer.Tick += async (_,_) => await CheckReplays();
        replayTimer.Start();
        linkTimer.Tick += async (_,_) => await SyncLinks();
        linkTimer.Start();
        _ = SyncLinks();
    }
    async Task CheckReplays()
    {
        if(replayBusy || exiting || !preferences.ReplayChecking) return;
        replayBusy=true;
        try
        {
            var summary=await Task.Run(()=>replayMonitor.ScanNext(replayCancellation.Token));
            if(exiting || !preferences.ReplayChecking || summary is null) return;
            if(summary.Started.AddSeconds(summary.DurationSeconds) < ledger.ReplayTrackingFrom!.Value) { replayMonitor.Report("Replay predates automatic checking; history unchanged"); return; }
            var correction=ReplayReconciliation.Apply(ledger,summary);
            File.WriteAllText(Path.Combine(store.DirectoryPath,"replay-diagnosis.json"),JsonSerializer.Serialize(new { replay=summary,decision=correction,matches=ledger.Matches.Select(m=>new {m.Id,m.Started,m.Ended,m.ReplaySessionId,chests=m.Events.Where(e=>e.Kind=="Chest")}) }));
            replayMonitor.Report(correction.Message);
            if(correction.MatchId.HasValue) tracker.ReplayApplied(correction.MatchId.Value);
            if(correction.Changed || correction.MatchId.HasValue) store.Save(ledger);
        }
        catch(OperationCanceledException) when(exiting) { }
        catch(Exception e) { replayMonitor.Report("Replay check failed: "+e.Message); }
        finally { replayBusy=false; if(!exiting) Refresh(); }
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
                status = "Watching Fortnite · " + reader.VisionSummary + " · " + tracker.LastDecision + (string.IsNullOrEmpty(reader.RecognitionNote) ? "" : " · " + reader.RecognitionNote) + (string.IsNullOrEmpty(reader.LastOcrError) ? "" : " · OCR error: " + reader.LastOcrError) + (string.IsNullOrEmpty(reader.LastMatchError) ? "" : " · Match OCR error: " + reader.LastMatchError) + (string.IsNullOrEmpty(reader.LastChestError) ? "" : " · Rare-chest OCR error: " + reader.LastChestError);
            }
        }
        catch (Exception e) { status = "Recognition error: " + e.Message; }
        finally { busy = false; if (!exiting) { if (capture is null) TryCreateCapture(); Refresh(); } }
    }
    void SetOverlayVisible(bool visible){
        preferences.OverlayVisible=visible;
        if(overlayToggle.Checked!=visible)overlayToggle.Checked=visible;
        if(visible)overlay.Show();else overlay.Hide();SavePreferences();Refresh();
    }
    void ResetOverlayPosition(){
        var area=Screen.PrimaryScreen!.WorkingArea;
        overlay.Location=new Point(area.Left+Math.Min(24,Math.Max(0,area.Width-overlay.Width)),area.Top+Math.Min(70,Math.Max(0,area.Height-overlay.Height)));
        preferences.X=overlay.Left;preferences.Y=overlay.Top;SavePreferences();Refresh();
    }
    SharedTotals SharedSnapshot() { var totals=ledger.Totals;return new(totals.GetValueOrDefault("Chest"),totals.Where(x=>x.Key!="Chest").ToDictionary(x=>x.Key,x=>x.Value)); }
    async Task SyncLinks() { try{await links.Sync(SharedSnapshot(),replayCancellation.Token);if(!exiting)Refresh();}catch(OperationCanceledException)when(exiting){} }
    void SetReplayChecking(bool enabled) { preferences.ReplayChecking=enabled;tracker.SetReplayWaiting(enabled);store.Save(ledger);SavePreferences();Refresh(); }
    void ChooseReplayFolder() {
        using var folder=new FolderBrowserDialog{Description="Fortnite replay folder (Saved/Demos)",SelectedPath=replayMonitor.Folder};
        if(folder.ShowDialog(dashboard)==DialogResult.OK){preferences.ReplayFolder=folder.SelectedPath;replayMonitor.Folder=folder.SelectedPath;SavePreferences();Refresh();}
    }
    void Refresh()
    {
        overlay.UpdateCounts(ledger, tracker.DisplayMatch, paused ? "Paused" : status, preferences.Language,tracker.DisplayedChests,links.Combined(ledger.Totals.GetValueOrDefault("Chest")));
        dashboard?.UpdateState(ledger, tracker, paused ? "Paused" : status, latest, preferences, preferences.ReplayChecking ? replayMonitor.Status : "Replay check disabled",links,replayMonitor.Folder);
    }
    void OpenDashboard()
    {
        if (dashboard is null || dashboard.IsDisposed)
        {
            dashboard = new Dashboard(new DashboardActions {
                OverlayVisible=SetOverlayVisible,ResetOverlay=ResetOverlayPosition,SetLanguage=SetLanguage, Retry=()=>{TryCreateCapture();Refresh();}, SaveDiagnosis=SaveDiagnosis, ReviewRewards=OpenRewardReview,
                OpenData=()=>Process.Start(new ProcessStartInfo("explorer.exe",store.DirectoryPath){UseShellExecute=true}),
                ReplayChecking=SetReplayChecking, ChooseReplayFolder=ChooseReplayFolder,
                ReleaseDisplay=()=>{tracker.ReleaseHeldDisplay();store.Save(ledger);Refresh();},
                Create=(name,group)=>links.Create(name,group,SharedSnapshot()),Join=(name,code)=>links.Join(name,code,SharedSnapshot()),
                Leave=()=>links.Leave(),Remove=id=>links.Remove(id),Sync=SyncLinks,Rename=name=>links.Rename(name)
            });
            dashboard.FormClosed += (_, _) => dashboard = null;
        }
        Refresh(); dashboard.Show(); dashboard.Activate();
    }
    void OpenRewardReview()
    {
        using var review=new RewardReview(ledger,()=> {store.Save(ledger);Refresh();});
        review.ShowDialog(dashboard);
    }
    void SaveDiagnosis()
    {
        if (latestFrame is null) { MessageBox.Show("No Fortnite frame has been captured. Bring Fortnite to the foreground in Windowed Fullscreen, then reopen settings using the tray icon."); return; }
        var folder = Path.Combine(store.DirectoryPath, "Diagnostics"); Directory.CreateDirectory(folder);
        latestFrame.Save(Path.Combine(folder, "game-frame.png"), System.Drawing.Imaging.ImageFormat.Png);
        File.WriteAllText(Path.Combine(folder, "recognition.json"), JsonSerializer.Serialize(new { capturedAt, language = preferences.Language, status, phase = tracker.Phase, matchId = tracker.Current?.Id, matchStarted = tracker.Current?.Started, matchEnded = tracker.Current?.Ended, recognitionNote = capture?.RecognitionNote, ocrError = capture?.LastOcrError, decision = tracker.LastDecision, lines = latest }, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(folder, "live-trace.json"), JsonSerializer.Serialize(capture?.Diagnostic));
        var replayDiagnosis=Path.Combine(store.DirectoryPath,"replay-diagnosis.json");
        if(File.Exists(replayDiagnosis)) File.Copy(replayDiagnosis,Path.Combine(folder,"replay-diagnosis.json"),true);
        Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
    }
    void SavePreferences() => File.WriteAllText(preferencesPath, JsonSerializer.Serialize(preferences));
    protected override void ExitThreadCore()
    {
        exiting = true; timer.Stop(); timer.Dispose(); SavePreferences();
        replayTimer.Stop(); replayTimer.Dispose(); linkTimer.Stop();linkTimer.Dispose();replayCancellation.Cancel();
        tray.Visible = false; tray.Dispose(); interactionHotkey.Dispose(); latestFrame?.Dispose(); dashboard?.Dispose(); overlay.Dispose(); base.ExitThreadCore();
    }
}
