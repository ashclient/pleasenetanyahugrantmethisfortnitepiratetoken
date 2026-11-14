using System.Text;
using System.Text.RegularExpressions;
namespace TreasureLedger;

// Conservative second opinion: named rare-chest text plus nearby XP, independent
// of glyph color/template. Wait for the toast to end so late visual detections
// can suppress the backup rather than produce duplicate chest events.
public sealed class RareChestBackup
{
    DateTimeOffset? lastSample, firstSeen, lastSeen, candidateAt;
    int candidateFrames, pending, acceptedMultiplier, absent;
    string candidate="", acceptedXp="";
    bool covered;
    readonly Queue<DateTimeOffset> visualTimes=new();
    public string Summary {get;private set;}="Rare-chest OCR backup ready";
    static string Clean(string text) => text.Normalize(NormalizationForm.FormKC).ToUpperInvariant().Replace('×','X');
    public static bool ReadEvidence(IReadOnlyList<Observation> rows,out int multiplier,out string xp,bool includeRegular=false)
    {
        multiplier=1;xp="";
        foreach(var row in rows)
        {
            string label=Clean(row.Text);string compact=Regex.Replace(label,@"\s+","");
            string rare=@"(RARECHEST(SEARCHED)?|SEARCHED(A)?RARECHEST|レア(な)?(宝箱|チェスト)(を(開けた|開いた|開けました|開封))?)";
            string names="^("+rare+(includeRegular?@"|CHEST(SEARCHED)?|SEARCHED(A)?CHEST|宝箱(を(開けた|開いた|開けました|開封))?":"")+@")(?<mult>X[2-9])?$";
            if(!Regex.IsMatch(compact,names)||compact.Length>48)continue;
            var number=Regex.Match(compact,@"X([2-9])");
            foreach(var near in rows.Where(r=>Math.Abs(r.X-row.X)<.30 && Math.Abs(r.Y-row.Y)<.15))
            {
                var amount=Regex.Match(Clean(near.Text),@"\+?\s*([0-9O][0-9O ,.]*)\s*X\s*P(?![A-Z])");
                if(!amount.Success) continue;
                string digits=Regex.Replace(amount.Groups[1].Value.Replace('O','0'),@"\D","");
                if(!long.TryParse(digits,out var value) || value<=0) continue;
                multiplier=number.Success?int.Parse(number.Groups[1].Value):1; xp=digits;return true;
            }
        }
        return false;
    }
    void Reset()
    { firstSeen=lastSeen=candidateAt=null;candidateFrames=pending=absent=acceptedMultiplier=0;candidate=acceptedXp="";covered=false; }
    public int Update(IReadOnlyList<Observation> batch,DateTimeOffset now,bool visualPopupCounted,int visualEvents)
    {
        if(visualEvents>0) visualTimes.Enqueue(now);
        while(visualTimes.Count>0 && now-visualTimes.Peek()>TimeSpan.FromSeconds(20)) visualTimes.Dequeue();
        if(firstSeen.HasValue && (visualPopupCounted || visualEvents>0)) covered=true;
        var marker=batch.LastOrDefault(x=>x.Source=="rare-pass");if(marker is null) return 0;
        var at=marker.CapturedAt??now;
        if(lastSample.HasValue && at<=lastSample.Value || now-at>TimeSpan.FromSeconds(3)) return 0;
        if(lastSample.HasValue && at-lastSample.Value>TimeSpan.FromSeconds(5)) Reset();
        lastSample=at;
        var rows=batch.Where(x=>x.Source=="rare-ocr" && (x.CapturedAt??at)==at).ToList();
        if(!ReadEvidence(rows,out int mult,out string xp))
        {
            candidateFrames=0;candidate="";candidateAt=null;
            if(!lastSeen.HasValue || ++absent<3 || at-lastSeen.Value<TimeSpan.FromMilliseconds(650)) return 0;
            int added=covered?0:pending;
            Summary=covered?"Rare-chest popup already handled by visual detector":$"Rare-chest OCR backup +{added}";
            Reset();return added;
        }
        firstSeen??=at;lastSeen=at;absent=0;
        covered|=visualPopupCounted || visualEvents>0 || visualTimes.Any(t=>t>=firstSeen.Value.AddMilliseconds(-500) && t<=at.AddMilliseconds(800));
        string key=$"{mult}|{xp}";
        if(candidate!=key) {candidate=key;candidateFrames=1;candidateAt=at;return 0;}
        candidateFrames++;
        if(candidateFrames<2 || at-candidateAt!.Value<TimeSpan.FromMilliseconds(150)) return 0;
        if(pending==0) {pending=1;acceptedMultiplier=mult;acceptedXp=xp;}
        else if(mult>acceptedMultiplier && xp!=acceptedXp) {pending++;acceptedMultiplier=mult;acceptedXp=xp;}
        Summary=covered?"Rare-chest popup covered by visual detector":$"Rare-chest OCR pending: {pending}";
        return 0;
    }
}
