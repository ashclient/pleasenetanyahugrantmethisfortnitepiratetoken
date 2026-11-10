using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace TreasureLedger;

public sealed class GameCapture
{
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    private OcrEngine engine;
    private readonly OcrEngine matchEngine;
    private readonly OcrEngine chestEngine;
    private readonly RareChestBackup rareBackup = new();
    private volatile bool chestBusy;
    private DateTimeOffset nextChest;
    private string languageCode;
    private readonly JapaneseHudDetector japaneseGizmo = new("gizmo");
    private readonly FocusedVision vision = new();
    private readonly Queue<Observation> metadata = new();
    private readonly object metadataLock = new();
    private volatile bool metadataBusy;
    private volatile bool matchBusy;
    private DateTimeOffset nextMetadata;
    private DateTimeOffset nextMatch;
    private readonly Queue<object> ocrTrace = new();
    private int metadataRound;
    public string VisionSummary => vision.Summary + " · " + rareBackup.Summary;
    public double VisionMilliseconds => vision.LastMilliseconds;
    public object Diagnostic { get { lock (metadataLock) return new { engine = "focused-popup-sequence-v4.2", rareBackup=rareBackup.Summary, chestError=LastChestError, vision = vision.Summary, frames = vision.Trace, banners = vision.BannerTrace, context = vision.ContextTrace, samples = ocrTrace.ToArray(), matchError = LastMatchError }; } }
    public GameCapture(string language) { engine = CreateEngine(language); matchEngine = CreateEngine(language); chestEngine = CreateEngine(language); languageCode = language; }
    public string RecognitionNote => languageCode == "ja-JP" && !engine.RecognizerLanguage.LanguageTag.StartsWith("ja", StringComparison.OrdinalIgnoreCase)
        ? "Japanese OCR is unavailable; chest/gizmo visual detection is active. Automatic match resets and other Japanese rewards need Japanese OCR." : "";
    public string LastOcrError { get; private set; } = "";
    public string LastMatchError { get; private set; } = "";
    public string LastChestError { get; private set; } = "";
    static OcrEngine CreateEngine(string language) => OcrEngine.TryCreateFromLanguage(new Language(language))
        ?? (language == "ja-JP" ? OcrEngine.TryCreateFromLanguage(new Language("en-US")) : null)
        ?? throw new InvalidOperationException($"{language} OCR is unavailable. Install its OCR language feature in Windows Settings → Time & language → Language & region, then retry. Tracking is paused until the selected language is available.");
    public void SetLanguage(string language) { engine = CreateEngine(language); languageCode = language; }

    public Bitmap? Capture(out string status)
    {
        var hwnd = GetForegroundWindow();
        GetWindowThreadProcessId(hwnd, out uint pid);
        using var process = Process.GetProcessById((int)pid);
        if (!process.ProcessName.StartsWith("FortniteClient-Win64-Shipping", StringComparison.OrdinalIgnoreCase))
        { status = "Waiting · bring Fortnite to the foreground"; return null; }
        if (!GetClientRect(hwnd, out var rect)) throw new InvalidOperationException("Cannot read the Fortnite window size.");
        var origin = new POINT();
        if (!ClientToScreen(hwnd, ref origin)) throw new InvalidOperationException("Cannot locate the Fortnite window.");
        int w = rect.Right - rect.Left, h = rect.Bottom - rect.Top;
        if (w < 320 || h < 240) { status = "Waiting · Fortnite is minimized"; return null; }
        var bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        try { using var g = Graphics.FromImage(bitmap); g.CopyFromScreen(origin.X, origin.Y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy); }
        catch { bitmap.Dispose(); throw; }
        status = "Watching Fortnite · local screen recognition";
        return bitmap;
    }
    public static bool LooksBlack(Bitmap frame)
    {
        int dark = 0, total = 0;
        for (int y = 1; y < 8; y++) for (int x = 1; x < 12; x++)
        {
            var c = frame.GetPixel(frame.Width * x / 12, frame.Height * y / 8);
            if (c.R < 8 && c.G < 8 && c.B < 8) dark++;
            total++;
        }
        return dark == total;
    }
    public List<Observation> ReadFast(Bitmap source, DateTimeOffset at)
    {
        var result = vision.Process(source, at, languageCode);
        lock (metadataLock) while (metadata.Count > 0) result.Add(metadata.Dequeue());
        int backupEvents=rareBackup.Update(result,at,vision.PopupCounted,result.Count(x=>x.Source=="event-chest"));
        for(int n=0;n<backupEvents;n++) result.Add(new Observation("Rare chest · confirmed text + XP backup",.7,.32,"event-chest"));
        if(!chestBusy && at>=nextChest)
        {
            chestBusy=true;nextChest=at.AddMilliseconds(220);var copy=new Bitmap(source);
            _=Task.Run(async()=>
            {
                try
                {
                    using(copy) EnqueueSample("rare-chest",at,(await ReadChestLabels(copy)).Select(x=>x with {CapturedAt=at}).ToList(),"rare-pass");
                    LastChestError="";
                }
                catch(Exception e) {LastChestError=e.Message;copy.Dispose();}
                finally {chestBusy=false;}
            });
        }
        // A dedicated, small OCR task reads the pregame/bus header. Reward OCR
        // and its slower full-screen scans cannot hold up match resets.
        if (!matchBusy && at >= nextMatch)
        {
            matchBusy = true; nextMatch = at.AddMilliseconds(600);
            var copy = new Bitmap(source);
            _ = Task.Run(async () =>
            {
                try
                {
                    using (copy)
                    {
                        var items = (await ReadMatchHeader(copy)).Select(x => x with { CapturedAt = at }).ToList();
                        EnqueueSample("match", at, items, "match-pass");
                    }
                    LastMatchError = "";
                }
                catch (Exception e) { LastMatchError = e.Message; copy.Dispose(); }
                finally { matchBusy = false; }
            });
        }
        if (!metadataBusy && at >= nextMetadata)
        {
            metadataBusy = true; nextMetadata = at.AddMilliseconds(450);
            var copy = new Bitmap(source);
            _ = Task.Run(async () =>
            {
                try
                {
                    using (copy)
                    {
                        var items = await ReadMetadata(copy);
                        EnqueueSample("rewards", at, items.Select(x => x with { CapturedAt = at }).ToList(), "metadata-pass");
                    }
                    LastOcrError = "";
                }
                catch (Exception e) { LastOcrError = e.Message; copy.Dispose(); }
                finally { metadataBusy = false; }
            });
        }
        return result;
    }
    internal async Task<List<Observation>> ReadMatchHeader(Bitmap source)
    {
        var region = new Rectangle((int)(source.Width * .28), (int)(source.Height * .01), (int)(source.Width * .44), (int)(source.Height * .20));
        var result = (await ReadRegion(source, region, false, 1400, matchEngine)).Select(x => x with { Source = "match-ocr" }).ToList();
        // Lobby navigation and the Play/Ready panel are outside the bus-header
        // crop. Read their screen positions together to distinguish the lobby
        // from an in-match "Return to lobby" menu button.
        result.Add(new Observation("Lobby sample",0,0,"lobby-pass"));
        result.AddRange((await ReadRegion(source,new Rectangle(0,0,source.Width,source.Height),false,1920,matchEngine)).Select(x=>x with {Source="lobby-ocr"}));
        return result;
    }
    internal async Task<List<Observation>> ReadChestLabels(Bitmap source)
    {
        var region=new Rectangle((int)(source.Width*.53),(int)(source.Height*.20),(int)(source.Width*.33),(int)(source.Height*.30));
        return (await ReadRegion(source,region,true,1300,chestEngine)).Select(x=>x with {Source="rare-ocr"}).ToList();
    }
    void EnqueueSample(string lane, DateTimeOffset at, List<Observation> items, string marker)
    {
        lock (metadataLock)
        {
            metadata.Enqueue(new Observation("OCR sample", 0, 0, marker, at));
            foreach (var item in items) metadata.Enqueue(item);
            ocrTrace.Enqueue(new { lane, at, lines = items });
            while (ocrTrace.Count > 220) ocrTrace.Dequeue();
        }
    }
    // Synchronous metadata entry point used by packaged OCR checks and diagnostics.
    public Task<List<Observation>> Read(Bitmap source) => ReadMetadata(source, true);
    async Task<List<Observation>> ReadMetadata(Bitmap source, bool full = false)
    {
        var result = new List<Observation>();
        if (full || metadataRound++ % 4 == 0)
            result.AddRange((await ReadRegion(source, new Rectangle(0, 0, source.Width, source.Height), false, 1920)).Select(x=>x with {Source="scene-ocr"}));
        var banner = new Rectangle((int)(source.Width * .35), (int)(source.Height * .07), (int)(source.Width * .30), (int)(source.Height * .30));
        result.AddRange((await ReadRegion(source, banner, false, 1500)).Select(x=>x with {Source="reward-ocr"}));
        result.AddRange((await ReadRegion(source, banner, true, 1500)).Select(x=>x with {Source="reward-ocr"}));

        if (languageCode == "ja-JP")
        {
            using var normalized = new Bitmap(1919, 1079, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(normalized)) g.DrawImage(source, 0, 0, normalized.Width, normalized.Height);
            var gizmo = japaneseGizmo.Find(normalized);
            if (gizmo is not null) result.Add(gizmo);
        }
        return result;
    }
    async Task<List<Observation>> ReadRegion(Bitmap source, Rectangle region, bool highContrast, int targetWidth, OcrEngine? recognizer = null)
    {
        double scale = Math.Min(highContrast ? 4.0 : 3.0, Math.Min((double)targetWidth / region.Width, (OcrEngine.MaxImageDimension - 1.0) / Math.Max(region.Width, region.Height)));
        using var image = new Bitmap(Math.Max(1, (int)(region.Width * scale)), Math.Max(1, (int)(region.Height * scale)), PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(image))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, new Rectangle(0, 0, image.Width, image.Height), region, GraphicsUnit.Pixel);
        }
        var data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] bytes = new byte[image.Width * image.Height * 4];
        try { for (int row = 0; row < image.Height; row++) Marshal.Copy(data.Scan0 + row * data.Stride, bytes, row * image.Width * 4, image.Width * 4); }
        finally { image.UnlockBits(data); }
        if (highContrast)
            for (int i = 0; i < bytes.Length; i += 4)
            {
                int brightness = (bytes[i] * 11 + bytes[i + 1] * 59 + bytes[i + 2] * 30) / 100;
                bool purpleText = bytes[i + 2] > 120 && bytes[i] > 160 && bytes[i + 2] * 100 > bytes[i + 1] * 117 + 1500 && bytes[i] * 100 > bytes[i + 1] * 115 + 2000;
                byte value = brightness >= 172 || purpleText ? (byte)255 : (byte)0;
                bytes[i] = bytes[i + 1] = bytes[i + 2] = value; bytes[i + 3] = 255;
            }
        using var software = SoftwareBitmap.CreateCopyFromBuffer(bytes.AsBuffer(), BitmapPixelFormat.Bgra8, image.Width, image.Height, BitmapAlphaMode.Ignore);
        var result = await (recognizer ?? engine).RecognizeAsync(software);
        return result.Lines.Where(line => line.Words.Count > 0).Select(line => new Observation(line.Text,
            (region.X + line.Words.Average(w => w.BoundingRect.X + w.BoundingRect.Width / 2) / scale) / source.Width,
            (region.Y + line.Words.Average(w => w.BoundingRect.Y + w.BoundingRect.Height / 2) / scale) / source.Height)).ToList();
    }
}
