using System.Drawing.Imaging;
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
    readonly Dictionary<int, (int W, int H, bool[] Pixels, int On)> templates = [];
    static bool Purple(byte r, byte g, byte b) => r > 120 && b > 160 && r * 100 > g * 117 + 1500 && b * 100 > g * 115 + 2000;

    bool Mask(byte r, byte g, byte b) => reference.color == "purple" ? Purple(r, g, b) : r > 170 && g > 150 && b * 100 < g * 88 && r * 100 > b * 112;

    public Observation? Find(Bitmap source)
    {
        int left = (int)(source.Width * reference.search[0]), top = (int)(source.Height * reference.search[1]);
        int width = (int)(source.Width * reference.search[2]), height = (int)(source.Height * reference.search[3]);
        using var region = source.Clone(new Rectangle(left, top, width, height), PixelFormat.Format32bppArgb);
        var bits = region.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] bytes = new byte[width * height * 4];
        try { for (int y = 0; y < height; y++) Marshal.Copy(bits.Scan0 + y * bits.Stride, bytes, y * width * 4, width * 4); }
        finally { region.UnlockBits(bits); }
        var purple = new bool[width * height];
        var integral = new int[(width + 1) * (height + 1)];
        int stride = width + 1;
        for (int y = 0; y < height; y++)
        {
            int row = 0;
            for (int x = 0; x < width; x++)
            {
                int p = (y * width + x) * 4;
                bool on = Mask(bytes[p + 2], bytes[p + 1], bytes[p]);
                purple[y * width + x] = on; if (on) row++;
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
            if (!templates.TryGetValue(tw, out var template))
            {
                int th = Math.Max(8, (int)Math.Round(reference.mask.Length * tw / (double)reference.mask[0].Length));
                var pixels = new bool[tw * th]; int on = 0;
                for (int y = 0; y < th; y++) for (int x = 0; x < tw; x++)
                {
                    bool value = reference.mask[Math.Min(reference.mask.Length - 1, (int)((y + .5) * reference.mask.Length / th))][Math.Min(reference.mask[0].Length - 1, (int)((x + .5) * reference.mask[0].Length / tw))] == '1';
                    pixels[y * tw + x] = value; if (value) on++;
                }
                template = (tw, th, pixels, on); templates[tw] = template;
            }
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
                if (recall < .80 || score < .86 || score <= best) continue;
                // Require the purple XP/level notification stack above the glyphs.
                int upper = reference.color == "purple"
                    ? Box(x - (int)(65 * scale), y - (int)(65 * scale), x + (int)(55 * scale), y - (int)(12 * scale))
                    : Box(x - (int)(10 * scale), y - (int)(150 * scale), x + template.W + (int)(10 * scale), y - (int)(26 * scale));
                if (upper < Math.Max(25, 60 * scale * scale)) continue;
                best = score; bestX = x; bestY = y; bestW = template.W; bestH = template.H;
                if (best > .995f) return Result();
            }
        }
        if (best == 0) return null;
        return Result();
        Observation Result() => new(reference.kind == "Chest" ? "宝箱" : "精霊ギズモ", (left + bestX + bestW / 2.0) / source.Width,
            (top + bestY + bestH / 2.0) / source.Height, reference.source);
    }
}
