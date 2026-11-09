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
    public GameCapture(string language) { engine = CreateEngine(language); }
    static OcrEngine CreateEngine(string language) => OcrEngine.TryCreateFromLanguage(new Language(language))
        ?? throw new InvalidOperationException($"{language} OCR is unavailable. Install its OCR language feature in Windows Settings → Time & language → Language & region, then retry. Tracking is paused until the selected language is available.");
    public void SetLanguage(string language) { engine = CreateEngine(language); }

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
    public async Task<List<Observation>> Read(Bitmap source)
    {
        double scale = Math.Min(1, Math.Min(1920.0 / source.Width, (OcrEngine.MaxImageDimension - 1.0) / Math.Max(source.Width, source.Height)));
        using var image = new Bitmap(Math.Max(1, (int)(source.Width * scale)), Math.Max(1, (int)(source.Height * scale)), PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(image)) g.DrawImage(source, 0, 0, image.Width, image.Height);
        var data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] bytes = new byte[image.Width * image.Height * 4];
        try { for (int row = 0; row < image.Height; row++) Marshal.Copy(data.Scan0 + row * data.Stride, bytes, row * image.Width * 4, image.Width * 4); }
        finally { image.UnlockBits(data); }
        using var software = SoftwareBitmap.CreateCopyFromBuffer(bytes.AsBuffer(), BitmapPixelFormat.Bgra8, image.Width, image.Height, BitmapAlphaMode.Ignore);
        var result = await engine.RecognizeAsync(software);
        return result.Lines.Select(line => new Observation(line.Text,
            line.Words.Count == 0 ? 0 : line.Words.Average(w => w.BoundingRect.X + w.BoundingRect.Width / 2) / image.Width,
            line.Words.Count == 0 ? 0 : line.Words.Average(w => w.BoundingRect.Y + w.BoundingRect.Height / 2) / image.Height)).ToList();
    }
}
