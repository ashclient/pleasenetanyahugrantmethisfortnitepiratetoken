using System.Text.RegularExpressions;
namespace TreasureLedger;

public static class RewardEvidence
{
    static readonly (string Kind,string Pattern)[] Categories = [
        ("Gizmo",@"\bGIZM[O0]S?\b|[ギキ][ズス]モ|\b(PORTABLE EXTRACTOR|LUCKY LOCATOR|SPRITE LOCATOR|EXTRACTION (SITE BOOSTER|BOOSTER|ACCELERATOR)|GOLDEN FIREWORKS?)\b|ポータブル抽出器|ラッキーロケーター|ラッキー探知機|抽出サイトブースター|ゴールデンファイアワーク|黄金の花火"),
        ("Loading screen",@"\bLOADING SCREEN\b|ロード画面|ローディング画面"),
        ("Jam track",@"\bJAM TRACK\b|ジャムトラック"),
        ("Pickaxe",@"\b(PICKAXE|GAUNTLETS)\b|ツルハシ|ガントレット"),
        ("Back bling",@"\bBACK\s?BLING\b|バックアクセサリー"),
        ("Vehicle / decal",@"\b(VEHICLE|DECAL)\b|車両|デカール"),
        ("Pirate shop access",@"\bPIRATE SHOP ACCESS\b|パイレーツショップ|海賊ショップ"),
        ("Gold outfit",@"\bGOLD(EN)? OUTFIT\b|ゴールドコスチューム|黄金のコスチューム")
    ];
    public static Dictionary<string,string> Find(IReadOnlyList<Observation> lines)
    {
        var hits=new Dictionary<string,string>();
        // Full-screen, match, inventory and rare-chest OCR are never reward evidence.
        var rows=lines.Where(x=>x.Source=="ocr" || x.Source=="reward-ocr" && x.X is >.35 and <.65 && x.Y is >.07 and <.37).ToList();
        foreach(var header in rows.Where(x=>Regex.IsMatch(x.Text,@"^(YOU\s*(HAVE\s*|VE\s*)?(FOUND|RECEIVED|UNLOCKED)|TREASURE\s*FOUND|見つけたもの|お宝を発見|トレジャーを発見)")))
        {
            var cluster=rows.Where(x=>x.CapturedAt==header.CapturedAt && Math.Abs(x.X-header.X)<.14 && x.Y>=header.Y-.015 && x.Y<=header.Y+.12).ToList();
            string text=string.Join(" ",cluster.Select(x=>x.Text));
            if(Regex.IsMatch(text,@"\b(OWNED|EQUIPPED|LOCKER|PREVIEW|INSPECT)\b|所有済み|装備中|プレビュー|ロッカー")) continue;
            var found=Categories.Where(p=>Regex.IsMatch(text,p.Pattern)).ToList();
            // One award card cannot award both a pickaxe and a back bling, etc.
            if(found.Count!=1) continue;
            hits[found[0].Kind]=text;
        }
        return hits;
    }
    public static string? UniqueKey(string kind,string evidence)
    {
        return kind switch {
            "Pickaxe" when Regex.IsMatch(evidence,@"GAUNTLET|ガントレット") => "pickaxe:solid-gold-gauntlets",
            "Back bling" when Regex.IsMatch(evidence,@"DIAMOND AND GOLD|LIL UZI|ダイヤモンド") => "back-bling:diamond-and-gold",
            "Pickaxe" => "pickaxe:unidentified",
            "Back bling" => "back-bling:unidentified",
            _ => null
        };
    }
    public static bool AlreadyRecorded(IEnumerable<LedgerEvent> events,string kind,string key)
    {
        return events.Where(e=>e.Kind==kind).Any(e=> {
            string? previous=e.RewardKey ?? UniqueKey(e.Kind,e.Evidence);
            return previous==key || previous?.EndsWith(":unidentified")==true || key.EndsWith(":unidentified");
        });
    }
}
