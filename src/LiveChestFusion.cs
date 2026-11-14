namespace TreasureLedger;

// Fast visual events stay immediate. A two-sample label+XP confirmation fills
// visual misses; matching visual events arriving later consume the OCR credit.
public sealed class LiveChestFusion
{
    readonly Queue<DateTimeOffset> visualTimes=new();
    DateTimeOffset? lastSample,firstSeen,lastSeen,candidateAt;
    string candidate="",acceptedXp="";
    int candidateFrames,steps,acceptedMultiplier,fallbacks,absorbed,absent,baseline;
    public string Summary {get;private set;}="Live chest OCR backup ready";
    void Reset(){firstSeen=lastSeen=candidateAt=null;candidate="";acceptedXp="";candidateFrames=steps=acceptedMultiplier=fallbacks=absorbed=absent=baseline=0;}
    public int Update(IReadOnlyList<Observation> batch,DateTimeOffset now,bool visualPopupCounted,int visualEvents)
    {
        if(lastSeen.HasValue&&now-lastSeen.Value>TimeSpan.FromSeconds(3))Reset();
        while(visualTimes.Count>0&&now-visualTimes.Peek()>TimeSpan.FromSeconds(30))visualTimes.Dequeue();
        int acceptedVisual=visualEvents;
        for(int i=0;i<visualEvents;i++)visualTimes.Enqueue(now);
        int credit=fallbacks-absorbed;
        if(credit>0&&lastSeen.HasValue&&now-lastSeen.Value<TimeSpan.FromSeconds(1.2)){
            int duplicate=Math.Min(credit,visualEvents);absorbed+=duplicate;acceptedVisual-=duplicate;
        }
        var marker=batch.LastOrDefault(x=>x.Source=="rare-pass");if(marker is null)return acceptedVisual;
        var at=marker.CapturedAt??now;
        if(lastSample.HasValue&&at<=lastSample.Value||now-at>TimeSpan.FromSeconds(3))return acceptedVisual;
        lastSample=at;
        var rows=batch.Where(x=>x.Source=="rare-ocr"&&(x.CapturedAt??at)==at).ToList();
        if(!RareChestBackup.ReadEvidence(rows,out int multiplier,out string xp,true)){
            candidate="";candidateFrames=0;candidateAt=null;
            if(lastSeen.HasValue&&++absent>=3&&at-lastSeen.Value>=TimeSpan.FromMilliseconds(650))Reset();
            return acceptedVisual;
        }
        if(!firstSeen.HasValue){firstSeen=at;baseline=visualPopupCounted?1:0;}
        lastSeen=at;absent=0;
        string key=$"{multiplier}|{xp}";
        if(candidate!=key){candidate=key;candidateFrames=1;candidateAt=at;return acceptedVisual;}
        if(++candidateFrames<2||at-candidateAt!.Value<TimeSpan.FromMilliseconds(150))return acceptedVisual;
        if(steps==0){steps=1;acceptedMultiplier=multiplier;acceptedXp=xp;}
        else if(multiplier>acceptedMultiplier&&xp!=acceptedXp){steps++;acceptedMultiplier=multiplier;acceptedXp=xp;}
        int visuals=Math.Max(baseline,visualTimes.Count(t=>t>=firstSeen.Value.AddMilliseconds(-500)));
        int represented=visuals+Math.Max(0,fallbacks-absorbed);
        int extra=Math.Max(0,steps-represented);fallbacks+=extra;
        Summary=extra>0?"Live chest confirmed by label + XP":$"Chest OCR confirmed · {steps} observed opening(s), duplicates suppressed";
        return acceptedVisual+extra;
    }
}
