using System.Diagnostics;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Cv = OpenCvSharp;

namespace TreasureLedger;

// Focused, normalized matching and popup-state tracking. English templates and
// temporal rules are adapted from Gabriel Gomez (MIT; see bundled notice).
internal sealed class FocusedVision : IDisposable
{
    public const int Width = 360, Height = 202, Left = 716, Top = 158;
    sealed class Spec { public string id { get; set; } = ""; public string language { get; set; } = ""; public string[] rows { get; set; } = []; }
    sealed record Pattern(string Language, int Width, int Height, Cv.Mat Blurred);
    readonly List<Pattern> patterns = [];
    readonly PopupSequence sequence = new();
    Pattern? lockedPattern;
    readonly object diagnosticLock = new();
    readonly Queue<object> trace = new();
    readonly Queue<object> banners = new();
    int frameNumber;
    public IReadOnlyCollection<object> BannerTrace { get { lock (diagnosticLock) return banners.ToArray(); } }
    public string Summary { get; private set; } = "Waiting for frames";
    public double LastMilliseconds { get; private set; }
    public int LastLineCount { get; private set; }
    public string Debug { get; private set; } = "";
    public float[] LastMask { get; private set; } = [];
    public IReadOnlyCollection<object> Trace { get { lock (diagnosticLock) return trace.ToArray(); } }

    public FocusedVision()
    {
        using var file = Assembly.GetExecutingAssembly().GetManifestResourceStream("TreasureLedger.Assets.fast-templates.json")!;
        foreach (var spec in JsonSerializer.Deserialize<List<Spec>>(file)!)
        {
            using var raw = Mat(spec.rows.SelectMany(r => r.Select(c => c == '1' ? 1f : 0f)).ToArray(), spec.rows[0].Length, spec.rows.Length);
            foreach (double scale in spec.language == "en" ? new[] { 1d } : new[] { 1d, 1.15, 1.30 })
            {
                using var resized = new Cv.Mat(); Cv.Cv2.Resize(raw, resized, new Cv.Size(Math.Max(4, (int)Math.Round(raw.Cols * scale)), Math.Max(4, (int)Math.Round(raw.Rows * scale))), 0, 0, Cv.InterpolationFlags.Area);
                using var pad = new Cv.Mat(); Cv.Cv2.CopyMakeBorder(resized, pad, 2, 2, 2, 2, Cv.BorderTypes.Constant, Cv.Scalar.All(0));
                var blur = new Cv.Mat(); Cv.Cv2.GaussianBlur(pad, blur, new Cv.Size(0, 0), .6, .6, Cv.BorderTypes.Reflect);
                patterns.Add(new Pattern(spec.language, resized.Cols, resized.Rows, blur));
            }
        }
    }
    internal static Cv.Mat Mat(float[] pixels, int width, int height)
    { var mat = new Cv.Mat(height, width, Cv.MatType.CV_32FC1); Marshal.Copy(pixels, 0, mat.Data, pixels.Length); return mat; }
    static float[] Pixels(Cv.Mat source)
    { using var copy = source.Clone(); var values = new float[copy.Rows * copy.Cols]; Marshal.Copy(copy.Data, values, 0, values.Length); return values; }
    static Cv.Rect ClampRect(Cv.Rect r, Cv.Mat m)
    { int x = Math.Clamp(r.X, 0, m.Cols - 1), y = Math.Clamp(r.Y, 0, m.Rows - 1); return new Cv.Rect(x, y, Math.Max(1, Math.Min(r.Width, m.Cols - x)), Math.Max(1, Math.Min(r.Height, m.Rows - y))); }
    static VisionPatch? Patch(Cv.Mat mask, Cv.Rect rect, double sigma)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return null;
        using var sub = new Cv.Mat(mask, ClampRect(rect, mask)); using var blur = new Cv.Mat();
        Cv.Cv2.GaussianBlur(sub, blur, new Cv.Size(0, 0), sigma, sigma, Cv.BorderTypes.Reflect);
        return new VisionPatch(blur.Cols, blur.Rows, Pixels(blur));
    }
    sealed class Moments
    {
        readonly double[] sum, square;
        readonly int stride;
        public Moments(Cv.Mat image)
        {
            stride = image.Cols + 1; sum = new double[stride * (image.Rows + 1)]; square = new double[sum.Length];
            var pixels = Pixels(image);
            for (int y = 0; y < image.Rows; y++)
            {
                double row = 0, rowSquare = 0;
                for (int x = 0; x < image.Cols; x++)
                {
                    double v = pixels[y * image.Cols + x]; row += v; rowSquare += v * v;
                    int p = (y + 1) * stride + x + 1;
                    sum[p] = sum[p - stride] + row; square[p] = square[p - stride] + rowSquare;
                }
            }
        }
        public double Variance(int x, int y, int w, int h)
        {
            int a = y * stride + x, b = a + w, c = (y + h) * stride + x, d = c + w;
            double s = sum[d] - sum[b] - sum[c] + sum[a], q = square[d] - square[b] - square[c] + square[a];
            return q / (w * h) - Math.Pow(s / (w * h), 2);
        }
    }
    static List<VisionLine> FindLines(Cv.Mat mask, Cv.Mat blurredMask, Moments moments, Pattern pattern, double threshold)
    {
        using var result = new Cv.Mat(); Cv.Cv2.MatchTemplate(blurredMask, pattern.Blurred, result, Cv.TemplateMatchModes.CCoeffNormed);
        // Mask every low-variance window before peak selection. Native normalized
        // correlation may produce near-1 peaks in flat regions depending on CPU
        // rounding; rejecting only a handful of those peaks can hide real text.
        var scores = Pixels(result);
        for (int y = 0; y < result.Rows; y++) for (int x = 0; x < result.Cols; x++)
            if (moments.Variance(x, y, pattern.Blurred.Cols, pattern.Blurred.Rows) <= 5e-4) scores[y * result.Cols + x] = -1;
        Marshal.Copy(scores, 0, result.Data, scores.Length);
        var found = new List<(double Score, int Y, int X)>();
        for (int i = 0; i < 12; i++)
        {
            Cv.Cv2.MinMaxLoc(result, out _, out double score, out _, out Cv.Point location);
            if (score < threshold || double.IsNaN(score)) break;
            int x = location.X + 2, y = location.Y + 2;
            if (!found.Any(f => Math.Abs(f.Y - y) <= pattern.Height / 2 + 1)) found.Add((score, y, x));
            Cv.Cv2.Rectangle(result, ClampRect(new Cv.Rect(location.X - pattern.Width / 2, location.Y - pattern.Height / 2, pattern.Width, pattern.Height), result), Cv.Scalar.All(-1), -1);
        }
        var lines = new List<VisionLine>();
        int bw = Math.Max(6, (int)Math.Round(16d / 27 * pattern.Width)), d0 = (int)Math.Round(8d / 27 * pattern.Width);
        foreach (var item in found.OrderBy(f => f.Y))
        {
            int x = item.X + pattern.Width + 1, y = item.Y - 1, h = pattern.Height + 2;
            if (x + bw >= mask.Cols || y < 0 || y + h >= mask.Rows)
            { lines.Add(new VisionLine(item.X, item.Y, pattern.Width, pattern.Height, item.Score, false, null, 0)); continue; }
            using var box = new Cv.Mat(mask, new Cv.Rect(x, y, bw, h));
            using var above = new Cv.Mat(mask, ClampRect(new Cv.Rect(x, Math.Max(0, y - 3), bw, 3), mask));
            using var below = new Cv.Mat(mask, ClampRect(new Cv.Rect(x, y + h, bw, 3), mask));
            double ink = Cv.Cv2.Sum(box).Val0, halo = Cv.Cv2.Sum(above).Val0 + Cv.Cv2.Sum(below).Val0;
            bool has = ink >= .18 * bw * h && halo <= .35 * ink;
            var digit = has ? Patch(mask, new Cv.Rect(x + d0, y, bw - d0, h), .6) : null;
            int digitInk = 0;
            if (has) { using var d = new Cv.Mat(mask, new Cv.Rect(x + d0, y, bw - d0, h)); digitInk = (int)Cv.Cv2.Sum(d).Val0; }
            lines.Add(new VisionLine(item.X, item.Y, pattern.Width, pattern.Height, item.Score, has, digit, digitInk));
        }
        return lines;
    }
    public int ProcessMask(float[] values, double seconds, string language, bool record = false)
    {
        var watch = Stopwatch.StartNew(); LastMask = values;
        using var mask = Mat(values, Width, Height); using var blurred = new Cv.Mat();
        Cv.Cv2.GaussianBlur(mask, blurred, new Cv.Size(0, 0), .6, .6, Cv.BorderTypes.Reflect);
        var moments = new Moments(blurred);
        List<VisionLine> lines = []; Pattern? best = lockedPattern;
        if (values.Sum() >= 20)
        {
            if (sequence.Active && lockedPattern is not null) lines = FindLines(mask, blurred, moments, lockedPattern, language == "ja" ? .84 : lockedPattern.Width == 22 ? .85 : .75);
            else
            {
                double score = 0;
                foreach (var pattern in patterns.Where(p => p.Language == language))
                {
                    var candidates = FindLines(mask, blurred, moments, pattern, language == "ja" ? .84 : pattern.Width == 22 ? .85 : .75);
                    double candidateScore = candidates.Select(c => c.Score).DefaultIfEmpty(0).Max();
                    if (candidateScore > score) { score = candidateScore; lines = candidates; best = pattern; }
                }
                if (lines.Count > 0) lockedPattern = best;
            }
        }
        VisionPatch? xp = null;
        if (lines.Count > 0)
        {
            var first = lines[0]; int y0 = Math.Max(0, first.Y - (int)Math.Round(2.4 * first.Height)), x0 = Math.Max(0, first.X - (int)Math.Round(2.2 * first.Width));
            xp = Patch(mask, new Cv.Rect(x0, y0, first.X + (int)Math.Round(1.2 * first.Width) - x0, Math.Max(1, first.Y - 1 - y0)), .8);
            // Chest-word matches without any XP text above them are not chest notifications.
            if (xp is null || xp.Pixels.Sum() < 8) lines = [];
        }
        int added = sequence.Update(seconds, lines, xp);
        if (!sequence.Active) lockedPattern = null;
        watch.Stop(); LastMilliseconds = watch.Elapsed.TotalMilliseconds;
        LastLineCount = lines.Count;
        Debug = $"font={lockedPattern?.Width} lines={string.Join(';', lines.Select(x => $"{x.X},{x.Y}:{x.Score:F3}:{x.HasMultiplier}:{x.DigitInk}"))} {sequence.Debug}";
        double maxScore = lines.Select(l => l.Score).DefaultIfEmpty(0).Max();
        Summary = $"{lines.Count} chest lines · score {maxScore:F2} · {LastMilliseconds:F0} ms";
        if (record)
        {
            var packed = new byte[(values.Length + 7) / 8]; for (int i = 0; i < values.Length; i++) if (values[i] >= .5f) packed[i / 8] |= (byte)(1 << (7 - i % 8));
            lock (diagnosticLock) { trace.Enqueue(new { seconds, language, width = Width, height = Height, mask = Convert.ToBase64String(packed), added, score = maxScore, lines = lines.Count, milliseconds = LastMilliseconds });
            while (trace.Count > 300) trace.Dequeue(); }
        }
        return added;
    }
    public List<Observation> Process(Bitmap frame, DateTimeOffset at, string language)
    {
        var rect = new Rectangle(0, 0, frame.Width, frame.Height);
        var bits = frame.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        using var normalized = new Cv.Mat();
        try
        {
            using var input = Cv.Mat.FromPixelData(frame.Height, frame.Width, Cv.MatType.CV_8UC4, bits.Scan0, bits.Stride);
            Cv.Cv2.Resize(input, normalized, new Cv.Size(1280, 720), 0, 0, Cv.InterpolationFlags.Area);
        }
        finally { frame.UnlockBits(bits); }
        if (frameNumber++ % 3 == 0)
        {
            using var banner = new Cv.Mat(normalized, new Cv.Rect(448, 50, 384, 216));
            Cv.Cv2.ImEncode(".png", banner, out byte[] png);
            lock (diagnosticLock) { banners.Enqueue(new { at, png = Convert.ToBase64String(png) }); while (banners.Count > 100) banners.Dequeue(); }
        }
        using var roi = new Cv.Mat(normalized, new Cv.Rect(Left, Top, Width, Height)); using var copy = roi.Clone();
        byte[] bytes = new byte[Width * Height * 4]; Marshal.Copy(copy.Data, bytes, 0, bytes.Length);
        float[] mask = new float[Width * Height];
        for (int i = 0; i < mask.Length; i++)
        { int b = bytes[i * 4], g = bytes[i * 4 + 1], r = bytes[i * 4 + 2], m = Math.Min(r, b); mask[i] = m > 170 && g < m - 8 ? 1 : 0; }
        int count = ProcessMask(mask, at.ToUnixTimeMilliseconds() / 1000d, language == "ja-JP" ? "ja" : "en", true);
        var observations = new List<Observation> { new("Fast visual sampling", 0, 0, "visual-active") };
        for (int i = 0; i < count; i++) observations.Add(new Observation("Chest opening · live popup sequence", .7, .32, "event-chest"));
        return observations;
    }
    public void Dispose() { foreach (var pattern in patterns) pattern.Blurred.Dispose(); }
}
