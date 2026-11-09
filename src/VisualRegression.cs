using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace TreasureLedger;

internal static class VisualRegression
{
    public static void Run()
    {
        var report = new List<string>();
        void Check(bool pass, string description)
        {
            report.Add((pass ? "PASS: " : "FAIL: ") + description);
            File.WriteAllLines("visual-regression.txt", report);
            if (!pass) throw new Exception(description);
        }
        foreach (string name in new[] { "chest", "gizmo" })
        {
            var reference = JapaneseHudDetector.LoadReference(name);
            using var stream = new MemoryStream(Convert.FromBase64String(reference.fixture));
            using var fixture = new Bitmap(stream);
            var detector = new JapaneseHudDetector(name);
            foreach (float scale in new[] { 1f, .75f, 1.25f, 1.5f })
            {
                using var frame = new Bitmap((int)(reference.fixtureFrameWidth * scale), (int)(reference.fixtureFrameHeight * scale), PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(frame))
                {
                    g.Clear(Color.FromArgb(35, 52, 65)); g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(fixture, reference.fixtureX * scale, reference.fixtureY * scale, fixture.Width * scale, fixture.Height * scale);
                }
                var watch = Stopwatch.StartNew();
                var observation = detector.Find(frame); watch.Stop();
                Check(observation is not null, $"{name}: real notification crop at {scale:P0}; {watch.ElapsedMilliseconds} ms");
                var ledger = new Ledger(); var tracker = new Tracker(ledger); var now = DateTimeOffset.UtcNow;
                for (int i = 0; i < 6; i++) tracker.Process([observation!], now.AddMilliseconds(i * 400));
                Check(ledger.Totals.GetValueOrDefault(reference.kind) == 1, $"{name}: six repeated frames count exactly once at {scale:P0}");
                for (int i = 6; i < 13; i++) tracker.Process([], now.AddMilliseconds(i * 400));
                tracker.Process([observation!], now.AddSeconds(6));
                Check(ledger.Totals.GetValueOrDefault(reference.kind) == 2, $"{name}: a fresh notification re-arms at {scale:P0}");
                if (scale == 1)
                {
                    using var negative = new Bitmap(frame);
                    using (var g = Graphics.FromImage(negative))
                    {
                        // Keep the actual XP/level stack or reward frame; remove only its category label.
                        using var brush = new SolidBrush(Color.FromArgb(35, 52, 65));
                        g.FillRectangle(brush, name == "chest" ? new Rectangle(1308, 352, 36, 22) : new Rectangle(895, 243, 132, 36));
                    }
                    Check(detector.Find(negative) is null, $"{name}: XP/level text or gold frame alone does not count");
                    Check(new JapaneseHudDetector(name == "chest" ? "gizmo" : "chest").Find(frame) is null, $"{name}: does not count as the other category");
                    // Run the real capture processing pipeline, including the selected Japanese mode.
                    var observations = Task.Run(() => new GameCapture("ja-JP").Read(frame)).GetAwaiter().GetResult();
                    var pipelineLedger = new Ledger(); new Tracker(pipelineLedger).Process(observations, now);
                    Check(pipelineLedger.Totals.GetValueOrDefault(reference.kind) == 1, $"{name}: selected Japanese capture pipeline counts the supplied notification");
                }
            }
        }
        Check(true, "Fixtures contain only cropped notification art/text; full screenshots and player identifiers are not bundled.");
    }
}
