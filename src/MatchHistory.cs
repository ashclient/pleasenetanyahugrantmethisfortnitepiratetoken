namespace TreasureLedger;

// Storage records remain intact. A replay session identifies one physical game,
// even when earlier recognition/restarts split it into multiple records.
public sealed record MatchSummary(string Key,Guid PrimaryId,DateTimeOffset Started,DateTimeOffset? Ended,int Chests,int? Broken,int Rewards,int Segments,int? ReplayTotal,bool Unconfirmed,bool Waiting,string State);
public static class MatchHistory
{
    public static List<MatchSummary> Summarize(Ledger ledger,Guid? currentId=null)=>ledger.Matches
        .GroupBy(m=>m.ReplaySessionId is {} id?"replay:"+id:"record:"+m.Id)
        .Select(group=>{
            var records=group.ToList();var primary=records.OrderBy(m=>m.ReplayFragment).ThenBy(m=>m.Started).First();
            bool replay=primary.ReplaySessionId is not null;int chests=records.Sum(m=>m.Chests),rewards=records.Sum(m=>m.Events.Count(e=>e.Kind!="Chest"&&!e.Excluded));
            int? reported=records.Select(m=>m.ReplayChestCount).FirstOrDefault(x=>x.HasValue),broken=records.Select(m=>m.ReplayDestroyedCount).FirstOrDefault(x=>x.HasValue);
            bool review=records.Any(m=>m.ReplayCountLower)||reported.HasValue&&chests>reported.Value;
            bool active=records.Any(m=>m.Id==currentId&&!m.Ended.HasValue);
            bool waiting=!replay&&active&&chests==0&&rewards==0;
            bool unconfirmed=!replay&&records.All(m=>m.Partial);
            string state=review?$"Review: {chests} tracked / {reported} replay":replay?"Replay checked":waiting?"Waiting / staging":unconfirmed?active?"Current · unconfirmed start":"Unconfirmed segment":active?"Current":primary.Ended.HasValue?"Finished":"Previous run";
            return new MatchSummary(group.Key,primary.Id,records.Select(m=>m.ReplayStarted).FirstOrDefault(t=>t.HasValue)??records.Min(m=>m.Started),records.Select(m=>m.ReplayEnded).FirstOrDefault(t=>t.HasValue)??(records.All(m=>m.Ended.HasValue)?records.Max(m=>m.Ended):null),chests,broken,rewards,records.Count,reported,unconfirmed,waiting,state);
        }).OrderByDescending(m=>m.Started).ToList();
}
