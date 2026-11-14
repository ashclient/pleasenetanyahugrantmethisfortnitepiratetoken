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
        int count = reader.ReadInt32(); int failures = 0;float[]? englishSample=null;
        for (int c = 0; c < count; c++)
        {
            string name = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
            int expected = reader.ReadInt32(), frameCount = reader.ReadInt32(), w = reader.ReadInt32(), h = reader.ReadInt32();
            if (w != FocusedVision.Width || h != FocusedVision.Height) throw new InvalidDataException("Fixture ROI differs from production.");
            using var vision = new FocusedVision(); int actual = 0; var timings = new List<double>(); var eventTimes = new List<double>();
            var debug = new List<string>();
            for (int f = 0; f < frameCount; f++)
            {
                double t = reader.ReadDouble(); byte[] packed = reader.ReadBytes((w * h + 7) / 8);
                if (packed.Length != (w * h + 7) / 8) throw new InvalidDataException("Truncated live fixture.");
                float[] mask = new float[w * h]; for (int i = 0; i < mask.Length; i++) mask[i] = (packed[i / 8] & (1 << (7 - i % 8))) != 0 ? 1 : 0;
                int added = vision.ProcessMask(mask, t, "en"); if(added>0&&englishSample is null)englishSample=(float[])mask.Clone();actual += added; if (added > 0) eventTimes.Add(t);
                timings.Add(vision.LastMilliseconds);
                if (name == "replays_stack_x2_x3") debug.Add($"{t:F2} added={added} {vision.Debug}");
            }
            var truth = json.RootElement.GetProperty("truth").GetProperty(name).EnumerateArray().Select(x => x.GetDouble()).ToList();
            int matched = 0, falseEvents = 0;
            foreach (double t in eventTimes)
            {
                var near = truth.Where(v => t >= v - .5 && t <= v + 2.5).OrderBy(v => Math.Abs(v - t)).Cast<double?>().FirstOrDefault();
                if (near.HasValue) { truth.Remove(near.Value); matched++; } else falseEvents++;
            }
            bool passed = actual == expected && matched == expected && falseEvents == 0; if (!passed) failures++;
            if (debug.Count > 0) File.WriteAllLines("stack-debug.txt", debug);
            report.Add($"{(passed ? "PASS" : "FAIL")} {name}: {actual}/{expected}, matched {matched}, false/duplicate {falseEvents}; avg {timings.Average():F1} ms; p95 {timings.OrderBy(x => x).ElementAt((int)(timings.Count * .95)):F1} ms; events {string.Join(",", eventTimes.Select(t => t.ToString("F2")))}");
            File.WriteAllLines("live-regression.txt", report);
        }
        if(englishSample is null)throw new Exception("No English notification sample available");
        foreach(double scale in new[]{.8,.9,1.1,1.25}){
            using var original=FocusedVision.Mat(englishSample,FocusedVision.Width,FocusedVision.Height);using var scaled=new OpenCvSharp.Mat();
            OpenCvSharp.Cv2.Resize(original,scaled,new OpenCvSharp.Size((int)Math.Round(original.Cols*scale),(int)Math.Round(original.Rows*scale)),0,0,OpenCvSharp.InterpolationFlags.Area);
            var pixels=new float[scaled.Rows*scaled.Cols];System.Runtime.InteropServices.Marshal.Copy(scaled.Data,pixels,0,pixels.Length);
            var centered=new float[FocusedVision.Width*FocusedVision.Height];int dx=(FocusedVision.Width-scaled.Cols)/2,dy=(FocusedVision.Height-scaled.Rows)/2;
            for(int y=0;y<scaled.Rows;y++)for(int x=0;x<scaled.Cols;x++){int xx=x+dx,yy=y+dy;if(xx>=0&&xx<FocusedVision.Width&&yy>=0&&yy<FocusedVision.Height)centered[yy*FocusedVision.Width+xx]=pixels[y*scaled.Cols+x];}
            using var scaledVision=new FocusedVision();int total=0;for(int f=0;f<12;f++)total+=scaledVision.ProcessMask(centered,f*.067,"en");
            Check(total==1,$"English notification mask at {scale:P0} HUD text scale: {total} opening(s), {scaledVision.Summary}");
        }
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
            watch.Stop(); Check(total == 1 && vision.LastLineCount == 1, $"Japanese screenshot at {scale:P0}: {total} opening(s) over 12 sampled frames; average {watch.Elapsed.TotalMilliseconds / 12:F1} ms; {vision.Summary}");
            using (var g = Graphics.FromImage(image))
                g.FillRectangle(Brushes.Black, (reference.fixtureX + reference.glyphX - 2) * scale, (reference.fixtureY + reference.glyphY - 2) * scale,
                    (reference.mask[0].Length + 4) * scale, (reference.mask.Length + 4) * scale);
            using var negative = new FocusedVision(); int falseCount = 0;
            for (int f = 0; f < 12; f++) falseCount += negative.Process(image, at.AddMilliseconds(f * 67), "ja-JP").Count(x => x.Source == "event-chest");
            Check(falseCount == 0, $"Japanese XP and sprite-level text without chest label at {scale:P0}: zero openings");
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
        using (var g = Graphics.FromImage(full)) g.FillRectangle(Brushes.Black, gizmo.fixtureX + 65, gizmo.fixtureY, 140, 115);
        var withoutIcon = new Ledger();
        new Tracker(withoutIcon).Process(Task.Run(() => capture.Read(full)).GetAwaiter().GetResult(), DateTimeOffset.UtcNow);
        Check(withoutIcon.Totals.GetValueOrDefault("Gizmo") == 1, "Japanese gizmo category still counts with item icon removed");
        Check(failures == 0, $"Recorded English popup sequences: {count - failures}/{count} correct totals");
    }
}
