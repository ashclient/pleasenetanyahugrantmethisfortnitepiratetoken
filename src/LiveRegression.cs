using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

namespace TreasureLedger;

internal static class LiveRegression
{
    public static void Run()
    {
        var report = new List<string>();
        void Check(bool passed, string text)
        {
            report.Add((passed ? "PASS " : "FAIL ") + text); File.WriteAllLines("live-regression.txt", report);
            if (!passed) throw new Exception(text);
        }
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("TreasureLedger.Assets.english-traces.json")!;
        using var json = JsonDocument.Parse(resource);
        using var compressed = new MemoryStream(Convert.FromBase64String(json.RootElement.GetProperty("gzip").GetString()!));
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var reader = new BinaryReader(gzip);
        if (System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4)) != "TLT1") throw new InvalidDataException("Invalid live trace fixture.");
        int count = reader.ReadInt32(); int failures = 0;
        for (int c = 0; c < count; c++)
        {
            string name = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
            int expected = reader.ReadInt32(), frameCount = reader.ReadInt32(), w = reader.ReadInt32(), h = reader.ReadInt32();
            if (w != FocusedVision.Width || h != FocusedVision.Height) throw new InvalidDataException("Fixture ROI differs from production.");
            using var vision = new FocusedVision(); int actual = 0; var timings = new List<double>(); var eventTimes = new List<double>();
            for (int f = 0; f < frameCount; f++)
            {
                double t = reader.ReadDouble(); byte[] packed = reader.ReadBytes((w * h + 7) / 8);
                if (packed.Length != (w * h + 7) / 8) throw new InvalidDataException("Truncated live fixture.");
                float[] mask = new float[w * h]; for (int i = 0; i < mask.Length; i++) mask[i] = (packed[i / 8] & (1 << (7 - i % 8))) != 0 ? 1 : 0;
                int added = vision.ProcessMask(mask, t, "en"); actual += added; if (added > 0) eventTimes.Add(t);
                timings.Add(vision.LastMilliseconds);
            }
            bool passed = actual == expected; if (!passed) failures++;
            report.Add($"{(passed ? "PASS" : "FAIL")} {name}: {actual}/{expected}; avg {timings.Average():F1} ms; p95 {timings.OrderBy(x => x).ElementAt((int)(timings.Count * .95)):F1} ms; events {string.Join(",", eventTimes.Select(t => t.ToString("F2")))}");
            File.WriteAllLines("live-regression.txt", report);
        }
        Check(failures == 0, $"Recorded English popup sequences: {count - failures}/{count} correct totals");
        // Exercise the actual bitmap normalization path using the supplied Japanese XP image.
        var reference = JapaneseHudDetector.LoadReference("chest");
        using var stream = new MemoryStream(Convert.FromBase64String(reference.fixture)); using var crop = new Bitmap(stream);
        foreach (float scale in new[] { 1f, .75f, 1.25f, 1.5f })
        {
            using var image = new Bitmap((int)(reference.fixtureFrameWidth * scale), (int)(reference.fixtureFrameHeight * scale), PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(image)) { g.Clear(Color.FromArgb(35, 52, 65)); g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(crop, reference.fixtureX * scale, reference.fixtureY * scale, crop.Width * scale, crop.Height * scale); }
            using var vision = new FocusedVision(); int total = 0; var at = DateTimeOffset.UtcNow;
            var watch = Stopwatch.StartNew();
            for (int f = 0; f < 12; f++) total += vision.Process(image, at.AddMilliseconds(f * 67), "ja-JP").Count(x => x.Source == "event-chest");
            watch.Stop(); Check(total == 1, $"Japanese screenshot at {scale:P0}: one opening over 12 sampled frames; average {watch.Elapsed.TotalMilliseconds / 12:F1} ms");
        }
        // The gizmo photo uses a category label, independent of which item icon is above it.
        var gizmo = JapaneseHudDetector.LoadReference("gizmo");
        using var gizmoStream = new MemoryStream(Convert.FromBase64String(gizmo.fixture)); using var gizmoCrop = new Bitmap(gizmoStream);
        using var full = new Bitmap(gizmo.fixtureFrameWidth, gizmo.fixtureFrameHeight, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(full)) { g.Clear(Color.Black); g.DrawImage(gizmoCrop, gizmo.fixtureX, gizmo.fixtureY); }
        var capture = new GameCapture("ja-JP");
        var observations = Task.Run(() => capture.Read(full)).GetAwaiter().GetResult();
        var ledger = new Ledger(); new Tracker(ledger).Process(observations, DateTimeOffset.UtcNow);
        Check(ledger.Totals.GetValueOrDefault("Gizmo") == 1, "Japanese gizmo screenshot counts its reward category");
    }
}
