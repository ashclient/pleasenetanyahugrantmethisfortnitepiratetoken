using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace TreasureLedger;

// Recognizes the supplied Japanese chest XP / gizmo award labels. This is local screen analysis,
// independent of whether Windows OCR can read this small, outlined Fortnite font.
public sealed class JapaneseHudDetector(string name)
{
    public sealed class Reference
    {
        public string[] mask { get; set; } = [];
        public int referenceWidth { get; set; }
        public string kind { get; set; } = "";
        public string source { get; set; } = "";
        public string color { get; set; } = "";
        public double[] search { get; set; } = [];
        public string fixture { get; set; } = "";
        public int glyphX { get; set; }
        public int glyphY { get; set; }
        public int fixtureX { get; set; }
        public int fixtureY { get; set; }
        public int fixtureFrameWidth { get; set; }
        public int fixtureFrameHeight { get; set; }
    }
    public static Reference LoadReference(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TreasureLedger.Assets." + name + "-notification.json")
            ?? throw new InvalidOperationException("Japanese chest reference is missing.");
        return JsonSerializer.Deserialize<Reference>(stream)!;
    }
    readonly Reference reference = LoadReference(name);
    readonly Dictionary<int, List<(int W, int H, bool[] Pixels, int On)>> templates = [];
    static bool Purple(byte r, byte g, byte b) => r > 120 && b > 160 && r * 100 > g * 117 + 1500 && b * 100 > g * 115 + 2000;

    bool Mask(byte r, byte g, byte b) => reference.color == "purple" ? Purple(r, g, b) : r > 170 && g > 150 && b * 100 < g * 88 && r * 100 > b * 112;

    public Observation? Find(Bitmap source)
    {
        int left = (int)(source.Width * reference.search[0]), top = (int)(source.Height * reference.search[1]);
        int width = (int)(source.Width * reference.search[2]), height = (int)(source.Height * reference.search[3]);
        using var region = source.Clone(new Rectangle(left, top, width, height), PixelFormat.Format32bppArgb);
        var purple = BuildMask(region);
        var integral = new int[(width + 1) * (height + 1)];
        int stride = width + 1;
        for (int y = 0; y < height; y++)
        {
            int row = 0;
            for (int x = 0; x < width; x++)
            {
                if (purple[y * width + x]) row++;
                integral[(y + 1) * stride + x + 1] = integral[y * stride + x + 1] + row;
            }
        }
        int Sum(int x, int y, int w, int h) => integral[(y + h) * stride + x + w] - integral[y * stride + x + w] - integral[(y + h) * stride + x] + integral[y * stride + x];
        int Box(int x1, int y1, int x2, int y2)
        {
            x1 = Math.Clamp(x1, 0, width); x2 = Math.Clamp(x2, 0, width);
            y1 = Math.Clamp(y1, 0, height); y2 = Math.Clamp(y2, 0, height);
            return x2 > x1 && y2 > y1 ? Sum(x1, y1, x2 - x1, y2 - y1) : 0;
        }
        float best = 0; int bestX = 0, bestY = 0, bestW = 0, bestH = 0;
        var tried = new HashSet<int>();
        foreach (double multiplier in new[] { 1.0, .95, 1.05, .90, 1.10, .85, 1.15, .80, 1.20, .75, 1.25, .70, 1.30, .65, 1.35, 1.40, 1.45, 1.50, 1.55, 1.60, 1.65 })
        {
            double scale = source.Width / (double)reference.referenceWidth * multiplier;
            int tw = Math.Max(15, (int)Math.Round(reference.mask[0].Length * scale));
            if (!tried.Add(tw)) continue;
            int key = (int)Math.Round(scale * 10000);
            if (!templates.TryGetValue(key, out var variants)) { variants = MakeTemplates(scale); templates[key] = variants; }
            foreach (var template in variants)
            {
            int area = template.W * template.H;
            for (int y = 0; y <= height - template.H; y++) for (int x = 0; x <= width - template.W; x++)
            {
                int count = Sum(x, y, template.W, template.H);
                if (count < template.On * .77 || count > template.On * 1.28) continue;
                int intersection = 0;
                for (int ty = 0; ty < template.H; ty++) for (int tx = 0; tx < template.W; tx++)
                    if (template.Pixels[ty * template.W + tx] && purple[(y + ty) * width + x + tx]) intersection++;
                float recall = intersection / (float)template.On;
                float specificity = 1 - (count - intersection) / (float)(area - template.On);
                float score = (recall + specificity) / 2;
                if (recall < .80 || score < (reference.color == "purple" ? .85 : .86) || score <= best) continue;
                // Require the purple XP/level notification stack above the glyphs.
                int upper = reference.color == "purple"
                    ? Box(x - (int)(65 * scale), y - (int)(65 * scale), x + (int)(55 * scale), y - (int)(12 * scale))
                    : Box(x - (int)(10 * scale), y - (int)(150 * scale), x + template.W + (int)(10 * scale), y - (int)(26 * scale));
                if (upper < Math.Max(25, 60 * scale * scale)) continue;
                best = score; bestX = x; bestY = y; bestW = template.W; bestH = template.H;
                if (best > .995f) return Result();
            }
            }
        }
        if (best == 0) return null;
        return Result();
        Observation Result() => new(reference.kind == "Chest" ? "宝箱" : "精霊ギズモ", (left + bestX + bestW / 2.0) / source.Width,
            (top + bestY + bestH / 2.0) / source.Height, reference.source);
    }

    bool[] BuildMask(Bitmap region)
    {
        int width = region.Width, height = region.Height;
        var bits = region.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] bytes = new byte[width * height * 4];
        try { for (int y = 0; y < height; y++) Marshal.Copy(bits.Scan0 + y * bits.Stride, bytes, y * width * 4, width * 4); }
        finally { region.UnlockBits(bits); }
        var purple = new bool[width * height];
        for (int p = 0; p < purple.Length; p++) purple[p] = Mask(bytes[p * 4 + 2], bytes[p * 4 + 1], bytes[p * 4]);
        if (reference.color == "purple")
        {
            // Include the white glyph cores only when touching purple outline pixels.
            // Otherwise downsampling turns the outline into disconnected, unstable islands.
            var outline = (bool[])purple.Clone();
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                int p = (y * width + x) * 4;
                if (purple[y * width + x] || bytes[p] <= 180 || bytes[p + 1] <= 180 || bytes[p + 2] <= 180) continue;
                bool touching = false;
                for (int dy = -2; dy <= 2 && !touching; dy++) for (int dx = -2; dx <= 2; dx++)
                    if (x + dx >= 0 && x + dx < width && y + dy >= 0 && y + dy < height && outline[(y + dy) * width + x + dx]) { touching = true; break; }
                purple[y * width + x] = touching;
            }
        }
        return purple;
    }
    List<(int W, int H, bool[] Pixels, int On)> MakeTemplates(double scale)
    {
        var variants = new List<(int W, int H, bool[] Pixels, int On)>();
        if (reference.color != "purple")
        {
            int w = Math.Max(15, (int)Math.Round(reference.mask[0].Length * scale));
            int h = Math.Max(8, (int)Math.Round(reference.mask.Length * w / (double)reference.mask[0].Length));
            var pixels = new bool[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                pixels[y * w + x] = reference.mask[Math.Min(reference.mask.Length - 1, (int)((y + .5) * reference.mask.Length / h))][Math.Min(reference.mask[0].Length - 1, (int)((x + .5) * reference.mask[0].Length / w))] == '1';
            variants.Add((w, h, pixels, pixels.Count(x => x))); return variants;
        }
        using var stream = new MemoryStream(Convert.FromBase64String(reference.fixture));
        using var fixture = new Bitmap(stream);
        // Re-rasterize the color glyphs at quarter-pixel phases before extracting masks.
        // Resizing a thresholded outline directly loses the tiny Japanese strokes.
        foreach (float px in new[] { 0f, .25f, .5f, .75f }) foreach (float py in new[] { 0f, .25f, .5f, .75f })
        {
            using var scaled = new Bitmap((int)Math.Ceiling(fixture.Width * scale) + 2, (int)Math.Ceiling(fixture.Height * scale) + 2, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(scaled))
            { g.Clear(Color.FromArgb(35, 52, 65)); g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(fixture, px, py, (float)(fixture.Width * scale), (float)(fixture.Height * scale)); }
            var mask = BuildMask(scaled);
            int x0 = (int)Math.Floor(reference.glyphX * scale + px), y0 = (int)Math.Floor(reference.glyphY * scale + py);
            int w = (int)Math.Ceiling((reference.glyphX + reference.mask[0].Length) * scale + px) - x0;
            int h = (int)Math.Ceiling((reference.glyphY + reference.mask.Length) * scale + py) - y0;
            var pixels = new bool[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) pixels[y * w + x] = mask[(y0 + y) * scaled.Width + x0 + x];
            int on = pixels.Count(x => x); if (on > 0 && on < pixels.Length) variants.Add((w, h, pixels, on));
        }
        return variants;
    }
}
