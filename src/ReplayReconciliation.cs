namespace TreasureLedger;

public sealed record ReplayChest(double Seconds, bool Destroyed);
public sealed record ReplaySummary(string SessionId, DateTimeOffset Started, int DurationSeconds, bool OwnerKnown, bool IsBattleRoyale, List<ReplayChest> Chests);
public sealed record ReplayCorrection(bool Changed, string Message, Guid? MatchId = null);

public static class ReplayReconciliation
{
    public static ReplayCorrection Apply(Ledger ledger, ReplaySummary replay)
    {
        if(!replay.OwnerKnown || !replay.IsBattleRoyale) return new(false,"Replay skipped: recording player or Battle Royale mode not confirmed");
        if(string.IsNullOrWhiteSpace(replay.SessionId) || replay.DurationSeconds is <= 0 or > 10800 || replay.Chests.Count > 10000)
            return new(false,"Replay skipped: invalid summary");
        if(ledger.Matches.Any(m=>m.ReplaySessionId == replay.SessionId)) return new(false,"Replay already checked");
        var end = replay.Started.AddSeconds(replay.DurationSeconds);
        if(replay.Chests.Any(c=>!double.IsFinite(c.Seconds) || c.Seconds < 0 || c.Seconds > replay.DurationSeconds + 120))
            return new(false,"Replay skipped: invalid chest timing");
        // Match by time, never by the number of chests. Refuse ambiguous histories
        // rather than adding the replay as another copy of the same game.
        var overlap = ledger.Matches.Where(m => m.Started <= end.AddSeconds(15)
            && (m.Ended ?? DateTimeOffset.MaxValue) >= replay.Started.AddMinutes(-2)).ToList();
        var candidates = overlap.Where(m => m.ReplaySessionId is null && m.Started >= replay.Started.AddMinutes(-3)
            && m.Events.Where(e=>e.Kind == "Chest").All(e=>e.At >= replay.Started.AddMinutes(-3) && e.At <= end.AddSeconds(15))).ToList();
        if(candidates.Count > 1 || candidates.Count == 0 && overlap.Count > 0)
            return new(false,"Replay needs review: match history overlaps more than one game; no counts changed");
        var match = candidates.SingleOrDefault();
        if(match is null)
        {
            match = new MatchRecord { Started = replay.Started, Ended = end };
            ledger.Matches.Add(match);
        }
        int visual = match.Chests, missing = Math.Max(0,replay.Chests.Count - visual);
        // Add only the deficit. A broken chest might already have generated a
        // live notification, so adding every destroyed flag would double-count it.
        for(int n=0;n<missing;n++) match.Events.Add(new LedgerEvent(end,"Chest",$"Replay correction {replay.SessionId}: {replay.Chests.Count} total, {replay.Chests.Count(c=>c.Destroyed)} destroyed; live count was {visual}"));
        match.ReplaySessionId = replay.SessionId;
        match.ReplayChestCount = replay.Chests.Count;
        match.ReplayDestroyedCount = replay.Chests.Count(c=>c.Destroyed);
        match.ReplayCountLower = replay.Chests.Count < visual;
        match.Ended ??= end;
        return new(true,match.ReplayCountLower
            ? $"Replay reports {replay.Chests.Count}, below live {visual}; saved counts preserved for review"
            : $"Replay checked: +{missing} missed chests ({match.ReplayDestroyedCount} destroyed in match)", match.Id);
    }
}
