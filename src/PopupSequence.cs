// Popup-content tracking adapted from Gabriel Gomez's MIT-licensed
// fortnite-visual-chest-counter/detection/popup_lines.py. See THIRD_PARTY_GABRIEL_GOMEZ.txt.
namespace TreasureLedger;

internal sealed record VisionPatch(int Width, int Height, float[] Pixels)
{
    public static double Similarity(VisionPatch? a, VisionPatch? b, int radius = 0)
    {
        if (a is null || b is null) return 1;
        double best = -1;
        for (int dy = -radius; dy <= radius; dy++) for (int dx = -radius; dx <= radius; dx++)
        {
            int ax = Math.Max(0, dx), bx = Math.Max(0, -dx), ay = Math.Max(0, dy), by = Math.Max(0, -dy);
            int w = Math.Min(a.Width - Math.Abs(dx), b.Width - Math.Abs(dx)), h = Math.Min(a.Height - Math.Abs(dy), b.Height - Math.Abs(dy));
            if (w < 3 || h < 3) continue;
            double sa = 0, sb = 0, aa = 0, bb = 0, ab = 0; int n = w * h;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                double va = a.Pixels[(y + ay) * a.Width + x + ax], vb = b.Pixels[(y + by) * b.Width + x + bx];
                sa += va; sb += vb; aa += va * va; bb += vb * vb; ab += va * vb;
            }
            double den = Math.Sqrt(Math.Max(0, aa - sa * sa / n) * Math.Max(0, bb - sb * sb / n));
            best = Math.Max(best, den > 1e-9 ? (ab - sa * sb / n) / den : 0);
        }
        return best;
    }
}
internal sealed record VisionLine(int X, int Y, int Width, int Height, double Score, bool HasMultiplier, VisionPatch? Digit, int DigitInk);

internal sealed class PopupSequence
{
    List<VisionLine>? stable, candidate;
    VisionPatch? stableXp, candidateXp;
    readonly Queue<int> recentCounts = new();
    double lastSeen = double.NegativeInfinity, started, candidateAt, diffSince = double.NaN, lastChange = double.NegativeInfinity, xpMin = 1;
    int candidateFrames, sameRun;
    bool countedInitial;
    public bool Active { get; private set; }
    public int Additions { get; private set; }

    static bool Same(IReadOnlyList<VisionLine> a, IReadOnlyList<VisionLine> b) => a.Count == b.Count && a.Zip(b).All(p => p.First.HasMultiplier == p.Second.HasMultiplier && (!p.First.HasMultiplier || VisionPatch.Similarity(p.First.Digit, p.Second.Digit) >= .90));
    static bool Regresses(IReadOnlyList<VisionLine> old, IReadOnlyList<VisionLine> next) => next.Count < old.Count || old.Zip(next).Any(p => p.First.HasMultiplier && !p.Second.HasMultiplier);
    public void Stop()
    {
        Active = false; stable = candidate = null; stableXp = candidateXp = null;
        candidateFrames = sameRun = 0; recentCounts.Clear(); diffSince = double.NaN; xpMin = 1; countedInitial = false;
    }
    public int Update(double t, List<VisionLine> lines, VisionPatch? xp)
    {
        if (lines.Count == 0)
        {
            if (Active && t - lastSeen >= .33) Stop();
            candidate = null; candidateFrames = 0; return 0;
        }
        if (!Active) { Active = true; started = t; lastChange = double.NegativeInfinity; }
        lastSeen = t;
        int emitted = 0;
        // One initial observation is one observed opening; an X3 first seen mid-popup
        // is never invented as three historical openings.
        if (!countedInitial && t - started >= .10 && lines.Max(x => x.Score) >= .85)
        { countedInitial = true; emitted = 1; Additions++; }
        int count = lines.Count; var seen = lines;
        if (stable is not null)
        {
            int bottom = stable.Max(x => x.Y); var below = lines.Where(x => x.Y > bottom + .9 * x.Height).ToList();
            if (below.Count > 0 && stable.Count + below.Count > count) { count = stable.Count + below.Count; seen = stable.Concat(below).ToList(); }
        }
        recentCounts.Enqueue(count); while (recentCounts.Count > 5) recentCounts.Dequeue();
        if (stable is not null && count > stable.Count && recentCounts.Count(n => n > stable.Count) >= 3)
        {
            if (double.IsNaN(diffSince)) diffSince = t;
            xpMin = Math.Min(xpMin, VisionPatch.Similarity(stableXp, xp, 2));
            bool accepted = xpMin < .60 && diffSince - lastChange >= .60;
            double began = diffSince;
            stable = seen; stableXp = xp; xpMin = 1; diffSince = double.NaN; sameRun = 3; candidate = null; candidateFrames = 0; recentCounts.Clear();
            if (accepted && countedInitial) { lastChange = began; Additions++; emitted++; }
            return emitted;
        }
        if (stable is not null && Same(lines, stable))
        {
            sameRun++;
            if (sameRun >= 3) { stableXp = xp; xpMin = 1; diffSince = double.NaN; }
        }
        else
        {
            sameRun = 0; if (double.IsNaN(diffSince)) diffSince = t;
            if (stableXp is not null) xpMin = Math.Min(xpMin, VisionPatch.Similarity(stableXp, xp, 2));
        }
        if (candidate is not null && Same(lines, candidate) && VisionPatch.Similarity(candidateXp, xp, 2) >= .90) candidateFrames++;
        else { candidate = lines; candidateFrames = 1; candidateAt = t; candidateXp = xp; }
        if (candidateFrames < 2 || t - candidateAt < .18) return emitted;
        var next = candidate!; var old = stable;
        if (old is not null && Regresses(old, next)) { candidate = null; candidateFrames = 0; return emitted; }
        double changeAt = double.IsNaN(diffSince) || candidateAt - diffSince > 1 ? candidateAt : diffSince;
        bool xpChanged = xpMin < .60;
        stable = next; stableXp = xp; xpMin = 1; diffSince = double.NaN; sameRun = 3;
        if (old is null || !countedInitial || changeAt - lastChange < .60 || !xpChanged) return emitted;
        bool added = next.Count > old.Count;
        foreach (var (a, b) in old.Zip(next))
            added |= b.HasMultiplier && (!a.HasMultiplier || VisionPatch.Similarity(a.Digit, b.Digit) < .90 && b.Score >= a.Score - .06 && b.DigitInk >= .7 * a.DigitInk);
        if (added) { lastChange = changeAt; Additions++; emitted++; }
        return emitted;
    }
}
