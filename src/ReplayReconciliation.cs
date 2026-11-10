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
        // Prefer actual chest-event evidence over empty staging/lobby records.
        // Several fully contained records can be fragments of the same game
        // after an earlier reset bug; link them without deleting their history.
        var overlap = ledger.Matches.Where(m => m.Started <= end.AddSeconds(15)
            && (m.Ended ?? DateTimeOffset.MaxValue) >= replay.Started).ToList();
        var eligible = overlap.Where(m => m.ReplaySessionId is null).ToList();
        bool Inside(LedgerEvent e) => e.At >= replay.Started.AddSeconds(-45) && e.At <= end.AddSeconds(15);
        var populated = eligible.Where(m=>m.Chests>0 && m.Events.Where(e=>e.Kind=="Chest").All(Inside)).ToList();
        bool mixed = eligible.Any(m=>m.Events.Any(e=>e.Kind=="Chest" && Inside(e)) && m.Events.Any(e=>e.Kind=="Chest" && !Inside(e)));
        bool checkedConflict=overlap.Any(m=>m.ReplaySessionId is not null && m.Events.Any(e=>e.Kind=="Chest" && !e.Evidence.StartsWith("Replay correction",StringComparison.Ordinal) && e.At>=replay.Started.AddSeconds(15) && e.At<=end.AddSeconds(-15)));
        if(mixed || checkedConflict)
            return new(false,"Replay needs review: chest events cross match boundaries or overlap another checked replay; no counts changed");
        var linked = populated.Count>0 ? populated : eligible.Where(m=>m.Chests==0 && m.Started>=replay.Started.AddMinutes(-3)).ToList();
        if(populated.Count==0 && (linked.Count>1 || linked.Count==0 && overlap.Count>0))
            return new(false,$"Replay needs review: {linked.Count} possible empty matches and no unique chest-event evidence; no counts changed");
        var match = linked.OrderBy(m=>m.Started).FirstOrDefault();
        if(match is null)
        {
            match = new MatchRecord { Started = replay.Started, Ended = end };
            ledger.Matches.Add(match); linked.Add(match);
        }
        int visual = linked.Sum(m=>m.Chests), missing = Math.Max(0,replay.Chests.Count - visual);
        // Add only the deficit. A broken chest might already have generated a
        // live notification, so adding every destroyed flag would double-count it.
        for(int n=0;n<missing;n++) match.Events.Add(new LedgerEvent(end,"Chest",$"Replay correction {replay.SessionId}: {replay.Chests.Count} total, {replay.Chests.Count(c=>c.Destroyed)} destroyed; live count was {visual}"));
        foreach(var fragment in linked)
        {
            fragment.ReplaySessionId=replay.SessionId;
            fragment.ReplayFragment=fragment!=match;
            fragment.Ended??=end;
        }
        match.ReplayChestCount = replay.Chests.Count;
        match.ReplayDestroyedCount = replay.Chests.Count(c=>c.Destroyed);
        match.ReplayCountLower = replay.Chests.Count < visual;
        match.Ended ??= end;
        return new(true,match.ReplayCountLower
            ? $"Replay reports {replay.Chests.Count}, below live {visual}; saved counts preserved for review"
            : $"Replay checked: +{missing} missed chests ({match.ReplayDestroyedCount} destroyed in match)", match.Id);
    }
}
