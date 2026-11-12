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
    …4604 tokens truncated…bine(store.DirectoryPath,"replay-diagnosis.json"),JsonSerializer.Serialize(new { replay=summary,decision=correction,matches=ledger.Matches.Select(m=>new {m.Id,m.Started,m.Ended,m.ReplaySessionId,chests=m.Events.Where(e=>e.Kind=="Chest")}) }));
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
                SetLanguage=SetLanguage, Retry=()=>{TryCreateCapture();Refresh();}, SaveDiagnosis=SaveDiagnosis, ReviewRewards=OpenRewardReview,
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
