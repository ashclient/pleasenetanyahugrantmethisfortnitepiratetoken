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
    public async Task<List<Observation>> Read(Bitmap source)
    {
        var full = await ReadRegion(source, new Rectangle(0, 0, source.Width, source.Height), false, 1920);
        // Keep the Japanese notification glyphs large, even on 1440p/4K monitors.
        // A separate high-contrast pass also removes the busy world behind XP text.
        var notification = new Rectangle((int)(source.Width * .18), (int)(source.Height * .18), (int)(source.Width * .64), (int)(source.Height * .64));
        full.AddRange(await ReadRegion(source, notification, true, 2000));
        return full;
    }
    async Task<List<Observation>> ReadRegion(Bitmap source, Rectangle region, bool highContrast, int targetWidth)
    {
        double scale = Math.Min(highContrast ? 2.0 : 1.0, Math.Min((double)targetWidth / region.Width, (OcrEngine.MaxImageDimension - 1.0) / Math.Max(region.Width, region.Height)));
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
                byte value = brightness >= 172 ? (byte)255 : (byte)0;
                bytes[i] = bytes[i + 1] = bytes[i + 2] = value; bytes[i + 3] = 255;
            }
        using var software = SoftwareBitmap.CreateCopyFromBuffer(bytes.AsBuffer(), BitmapPixelFormat.Bgra8, image.Width, image.Height, BitmapAlphaMode.Ignore);
        var result = await engine.RecognizeAsync(software);
        return result.Lines.Where(line => line.Words.Count > 0).Select(line => new Observation(line.Text,
            (region.X + line.Words.Average(w => w.BoundingRect.X + w.BoundingRect.Width / 2) / scale) / source.Width,
            (region.Y + line.Words.Average(w => w.BoundingRect.Y + w.BoundingRect.Height / 2) / scale) / source.Height)).ToList();
    }
}
