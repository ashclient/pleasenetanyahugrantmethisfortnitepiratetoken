using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace TreasureLedger;

public record Observation(string Text, double X, double Y, string Source = "ocr", DateTimeOffset? CapturedAt = null);
public record LedgerEvent(DateTimeOffset At, string Kind, string Evidence);
public sealed class MatchRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Started { get; set; }
    public DateTimeOffset? Ended { get; set; }
    public bool Partial { get; set; }
    public List<LedgerEvent> Events { get; set; } = [];
    public int Chests => Events.Count(x => x.Kind == "Chest");
}
public sealed class Ledger
{
    public int SchemaVersion { get; set; } = 1;
    public List<MatchRecord> Matches { get; set; } = [];
    public Dictionary<string, int> Totals => Matches.SelectMany(x => x.Events)
        .GroupBy(x => x.Kind).ToDictionary(x => x.Key, x => x.Count());
}

// Detection consumes only OCR from the game's visible client area. No game memory or hooks.
public sealed class Tracker
{
    private readonly Ledger ledger;
    public static readonly string[] Rewards = ["Gizmo", "Loading screen", "Jam track", "Pickaxe", "Back bling", "Vehicle / decal", "Pirate shop access", "Gold outfit"];
    private readonly Dictionary<string, DateTimeOffset> lastSeen = [];
    private readonly HashSet<string> latched = [];
    private DateTimeOffset? lastFrame;
    private bool gameplaySeen;
    private DateTimeOffset? lastGameplayAt, lastMatchSample, startCandidateAt;
    private int startCandidateSamples;
    private readonly Dictionary<string, int> absentMetadata = [];
    public MatchRecord? Current { get; private set; }
    public string LastDecision { get; private set; } = "No frame processed yet";
    public string Phase { get; private set; } = "Waiting for a match";

    public Tracker(Ledger ledger)
    {
        this.ledger = ledger;
        Current = ledger.Matches.LastOrDefault(x => !x.Ended.HasValue);
        gameplaySeen = Current?.Events.Count > 0;
        if (Current is not null) Phase = "Resumed tracked match";
    }

    static string Normalize(string s)
    {
        var clean = Regex.Replace(s.Normalize(NormalizationForm.FormKC).ToUpperInvariant(), @"[^\p{L}\p{N}+ ]", " ");
        // Windows Japanese OCR can insert spaces between each Japanese word/glyph.
        return Regex.Replace(Regex.Replace(clean, @"(?<=[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}])\s+|\s+(?=[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}])", ""), @"\s+", " ");
    }

    void BeginMatch(DateTimeOffset at, bool partial)
    {
        if (Current is not null && !Current.Ended.HasValue) Current.Ended = at;
        Current = new MatchRecord { Started = at, Partial = partial };
        ledger.Matches.Add(Current);
        gameplaySeen = partial;
        startCandidateAt = null; startCandidateSamples = 0;
        latched.Clear(); lastSeen.Clear(); absentMetadata.Clear();
        Phase = partial ? "Partial match · opened mid-game" : "New match · counter reset";
    }

    bool ObserveMatch(IReadOnlyList<Observation> lines, DateTimeOffset now, bool visualActive)
    {
        // Fast chest-only frames contain no evidence about the match lifecycle.
        if (visualActive && !lines.Any(x => x.Source is "metadata-pass" or "match-pass")) return false;
        var samples = lines.Where(x => x.Source is not "visual-active" and not "event-chest").ToList();
        bool headerSample = samples.Any(x => x.Source == "match-pass");
        bool dedicatedHeader = visualActive || headerSample;
        var at = samples.Where(x => !headerSample || x.Source == "match-pass").Select(x => x.CapturedAt).Where(x => x.HasValue).Max() ?? now;
        string header = string.Join(" ", samples.Where(x => !dedicatedHeader || x.Source == "match-ocr").Select(x => x.Text));
        string allText = string.Join(" ", samples.Select(x => x.Text));
        bool start = Regex.IsMatch(header, @"WAITING F[O0]R (OTHER )?PLAYERS|BATTLE\s?BUS (IS )?(LAUNCHING|LEAVING)|EVERYBODY OFF.*LAST STOP")
            || Regex.IsMatch(header, @"プレイヤー(を)?(待っています|待機中|待ち)|バトルバス(の)?(発車|出発|発進)|最終降車地点|最後の停留所");
        bool finish = Regex.IsMatch(allText, @"YOU PLACED|VICTORY ROYALE|あなたの順位|ビクトリー?ロイヤル");
        if (finish)
        {
            var finishAt = samples.Where(x => Regex.IsMatch(x.Text, @"YOU PLACED|VICTORY ROYALE|あなたの順位|ビクトリー?ロイヤル")).Select(x => x.CapturedAt).Max() ?? at;
            if (Current is not null && finishAt < Current.Started || lastGameplayAt.HasValue && finishAt < lastGameplayAt.Value) return false;
            startCandidateAt = null; startCandidateSamples = 0;
            Phase = "Match finished";
            if (Current is not null && !Current.Ended.HasValue) { Current.Ended = finishAt; return true; }
            return false;
        }
        if (visualActive && !headerSample) return false;
        if (lastMatchSample.HasValue && at <= lastMatchSample.Value) return false;
        lastMatchSample = at;
        if (!start)
        { startCandidateAt = null; startCandidateSamples = 0; return false; }
        // An OCR job can finish after a chest was counted; old waiting-room text
        // must not roll that chest into another match.
        if (lastGameplayAt.HasValue && at <= lastGameplayAt.Value) return false;
        if (Current is null || Current.Ended.HasValue)
        { BeginMatch(at, false); return true; }
        if (!gameplaySeen) return false; // Waiting -> bus is still the same match.
        if (!startCandidateAt.HasValue || at - startCandidateAt.Value > TimeSpan.FromSeconds(8))
        { startCandidateAt = at; startCandidateSamples = 1; return false; }
        startCandidateSamples++;
        if (startCandidateSamples < 2 || at - startCandidateAt.Value < TimeSpan.FromMilliseconds(300)) return false;
        BeginMatch(startCandidateAt.Value, false); return true;
    }
    public bool Process(IReadOnlyList<Observation> lines, DateTimeOffset now)
    {
        // Pauses/alt-tab must not re-arm a still-visible notification.
        var uninterrupted = lastFrame.HasValue && now - lastFrame.Value < TimeSpan.FromSeconds(5);
        lastFrame = now;
        bool visualActive = lines.Any(x => x.Source == "visual-active");
        bool metadataFrame = lines.Any(x => x.Source == "metadata-pass");
        var normalized = lines.Select(x => x with { Text = Normalize(x.Text) }).ToList();
        bool changed = ObserveMatch(normalized, now, visualActive);
        // Restrict chest notifications to the central HUD, excluding quest lists and feed.
        var center = normalized.Where(x => x.X is > .18 and < .83 && x.Y is > .12 and < .88
            && (!x.CapturedAt.HasValue || Current is null || x.CapturedAt.Value >= Current.Started)).ToList();
        bool Xp(Observation line) => Regex.IsMatch(line.Text, @"[0-9][0-9 ,+]*\s*X\s*P(?![A-Z])");
        bool Notification(Observation line)
        {
            if (visualActive || line.Source == "metadata") return false;
            if (line.Source == "visual-ja-chest") return true;
            string t = line.Text.Trim();
            if (Regex.IsMatch(t, @"\b(SEARCHED A CHEST|CHEST SEARCHED|SEARCHED CHEST)\b")) return true;
            if (t.Contains("宝箱を開けた") || t.Contains("宝箱を開いた") || t.Contains("宝箱を開けました") || t.Contains("宝箱を開封")) return true;
            // A chest mention alone is insufficient: require a short notification + nearby XP.
            bool chestName = Regex.IsMatch(t, @"^(RARE |TREASURE )?CHEST$|^(レアな|レア|トレジャー)?宝箱$")
                || (t.Contains("宝箱") || Regex.IsMatch(t, @"\bCHEST\b")) && t.Length < 35 && Xp(line);
            if (!chestName) return false;
            return center.Any(x => Xp(x) && Math.Abs(x.X - line.X) < .28 && Math.Abs(x.Y - line.Y) < .15);
        }
        string? chest = center.FirstOrDefault(Notification)?.Text;
        int liveChests = lines.Count(x => x.Source == "event-chest");
        if (liveChests > 0)
        {
            if (Current is null || Current.Ended.HasValue) BeginMatch(now, true);
            foreach (var observation in lines.Where(x => x.Source == "event-chest")) Current.Events.Add(new LedgerEvent(now, "Chest", observation.Text));
            gameplaySeen = true; lastGameplayAt = now; Phase = "In match";
            changed = true;
        }
        var hits = new Dictionary<string, string>();
        if (chest is not null) hits["Chest"] = center.Any(x => x.Source == "visual-ja-chest") ? "宝箱 · Japanese HUD visual match" : chest;
        if (center.Any(x => x.Source == "visual-ja-gizmo")) hits["Gizmo"] = "見つけたもの 精霊ギズモ · Japanese HUD visual match";
        // Reward names must be accompanied by an award banner, not merely an inventory name.
        var rewardText = string.Join(" ", center.Select(x => x.Text));
        if (System.Text.RegularExpressions.Regex.IsMatch(rewardText, @"YOU (HAVE |VE )?(FOUND|RECEIVED|UNLOCKED)") || rewardText.Contains("YOU FOUND") || rewardText.Contains("YOUFOUND") || rewardText.Contains("TREASURE FOUND") || rewardText.Contains("YOU RECEIVED") || rewardText.Contains("発見") || rewardText.Contains("見つけた") || rewardText.Contains("獲得"))
        {
            foreach (var (kind, pattern) in new (string, string)[] {
                ("Gizmo", @"\bGIZM[O0]S?\b|[ギキ][ズス]モ|\b(PORTABLE EXTRACTOR|LUCKY LOCATOR|SPRITE LOCATOR|EXTRACTION (SITE BOOSTER|BOOSTER|ACCELERATOR)|GOLDEN FIREWORKS?)\b|ポータブル抽出器|ラッキーロケーター|ラッキー探知機|抽出サイトブースター|ゴールデンファイアワーク|黄金の花火"), ("Loading screen", @"\bLOADING SCREEN\b|ロード画面|ローディング画面"),
                ("Jam track", @"\bJAM TRACK\b|ジャムトラック"), ("Pickaxe", @"\bPICKAXE\b|ツルハシ"),
                ("Back bling", @"\bBACK\s?BLING\b|バックアクセサリー"), ("Vehicle / decal", @"\b(VEHICLE|DECAL)\b|車両|デカール"),
                ("Pirate shop access", @"\bPIRATE SHOP ACCESS\b|パイレーツショップ|海賊ショップ"), ("Gold outfit", @"\bGOLD(EN)? OUTFIT\b|ゴールドコスチューム|黄金のコスチューム") })
                if (Regex.IsMatch(rewardText, pattern)) hits[kind] = rewardText;
        }
        LastDecision = liveChests > 0 ? $"Chest +{liveChests}" : hits.Count > 0 ? "Detected: " + string.Join(", ", hits.Keys) : $"Read {lines.Count} OCR lines; no supported chest/reward notification";
        foreach (string key in latched.ToArray())
        {
            if (hits.ContainsKey(key)) { absentMetadata[key] = 0; continue; }
            if (visualActive && !metadataFrame) continue;
            absentMetadata[key] = absentMetadata.GetValueOrDefault(key) + 1;
            if (uninterrupted && absentMetadata[key] >= 2 && now - lastSeen[key] >= TimeSpan.FromSeconds(.6)) latched.Remove(key);
        }
        foreach (var hit in hits)
        {
            if (Current is null || hit.Key == "Chest" && Current.Ended.HasValue) BeginMatch(now, true);
            lastSeen[hit.Key] = now;
            if (!latched.Add(hit.Key)) continue;
            if (hit.Key == "Chest") { gameplaySeen = true; lastGameplayAt = now; Phase = "In match"; }
            Current.Events.Add(new LedgerEvent(now, hit.Key, hit.Value));
            changed = true;
        }
        return changed;
    }
}

public sealed class LedgerStore
{
    public string DirectoryPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TreasureLedger");
    public string FilePath => Path.Combine(DirectoryPath, "ledger.json");
    public Ledger Load()
    {
        Directory.CreateDirectory(DirectoryPath);
        if (!File.Exists(FilePath)) return new();
        // Do not silently replace unreadable history with zeroes.
        var ledger = JsonSerializer.Deserialize<Ledger>(File.ReadAllText(FilePath)) ?? throw new InvalidDataException("History file is empty.");
        if (ledger.SchemaVersion != 1 || ledger.Matches is null) throw new InvalidDataException("Unsupported history format.");
        return ledger;
    }
    public void Save(Ledger ledger)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(ledger, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".bak", true);
        File.Move(temp, FilePath, true);
    }
}
