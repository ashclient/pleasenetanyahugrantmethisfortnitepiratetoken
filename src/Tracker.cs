using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace TreasureLedger;

public record Observation(string Text, double X, double Y);
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
public sealed class Tracker(Ledger ledger)
{
    public static readonly string[] Rewards = ["Gizmo", "Loading screen", "Jam track", "Pickaxe", "Back bling", "Vehicle / decal", "Pirate shop access", "Gold outfit"];
    private readonly Dictionary<string, DateTimeOffset> lastSeen = [];
    private readonly HashSet<string> latched = [];
    private DateTimeOffset? lastFrame;
    private bool startVisible;
    public MatchRecord? Current { get; private set; }
    public string Phase { get; private set; } = "Waiting for a match";

    static string Normalize(string s) => Regex.Replace(s.Normalize(NormalizationForm.FormKC).ToUpperInvariant(), @"[^\p{L}\p{N}+ ]", " ");
    public bool Process(IReadOnlyList<Observation> lines, DateTimeOffset now)
    {
        // Pauses/alt-tab must not re-arm a still-visible notification.
        var uninterrupted = lastFrame.HasValue && now - lastFrame.Value < TimeSpan.FromSeconds(2);
        lastFrame = now;
        var normalized = lines.Select(x => x with { Text = Normalize(x.Text) }).ToList();
        var text = string.Join(" ", normalized.Select(x => x.Text));
        bool changed = false;
        bool start = text.Contains("プレイヤーを待っています") || text.Contains("プレイヤーを待機中") || text.Contains("バトルバス発車まで") || text.Contains("バトルバス出発まで") || text.Contains("WAITING FOR PLAYERS") || text.Contains("BATTLE BUS LAUNCHING") || text.Contains("EVERYBODY OFF") && text.Contains("LAST STOP");
        if (start && !startVisible && (Current is null || Current.Ended.HasValue || now - Current.Started > TimeSpan.FromMinutes(3)))
        {
            if (Current is not null && !Current.Ended.HasValue) Current.Ended = now;
            Current = new MatchRecord { Started = now };
            ledger.Matches.Add(Current);
            changed = true;
            Phase = "Match detected";
        }
        if (uninterrupted || start) startVisible = start;
        if (text.Contains("YOU PLACED") || text.Contains("VICTORY ROYALE") || text.Contains("あなたの順位") || text.Contains("ビクトリーロイヤル"))
        {
            if (Current is not null && !Current.Ended.HasValue) { Current.Ended = now; changed = true; }
            Phase = "Match finished";
        }
        // Restrict chest notifications to the central HUD, excluding quest lists and feed.
        var center = normalized.Where(x => x.X is > .25 and < .77 && x.Y is > .12 and < .85).ToList();
        string? chest = center.FirstOrDefault(x => Regex.IsMatch(x.Text, @"\b(SEARCHED A CHEST|CHEST SEARCHED|SEARCHED CHEST)\b"))?.Text;
        chest ??= center.FirstOrDefault(x => x.Text.Contains("宝箱を開けた") || x.Text.Contains("宝箱を開いた") || x.Text.Contains("宝箱を開けました"))?.Text;
        if (chest is null && center.Any(x => Regex.IsMatch(x.Text.Trim(), @"^(RARE |TREASURE )?CHEST$|^(レアな|レア|トレジャー)?宝箱$")) && center.Any(x => Regex.IsMatch(x.Text, @"\+?\s*[0-9]+\s*XP\b"))) chest = "CHEST + XP";
        var hits = new Dictionary<string, string>();
        if (chest is not null) hits["Chest"] = chest;
        // Reward names must be accompanied by an award banner, not merely an inventory name.
        var rewardText = string.Join(" ", center.Select(x => x.Text));
        if (rewardText.Contains("YOU FOUND") || rewardText.Contains("TREASURE FOUND") || rewardText.Contains("YOU RECEIVED") || rewardText.Contains("発見") || rewardText.Contains("見つけた") || rewardText.Contains("獲得"))
        {
            foreach (var (kind, pattern) in new (string, string)[] {
                ("Gizmo", @"\bGIZMO\b|ギズモ"), ("Loading screen", @"\bLOADING SCREEN\b|ロード画面|ローディング画面"),
                ("Jam track", @"\bJAM TRACK\b|ジャムトラック"), ("Pickaxe", @"\bPICKAXE\b|ツルハシ"),
                ("Back bling", @"\bBACK\s?BLING\b|バックアクセサリー"), ("Vehicle / decal", @"\b(VEHICLE|DECAL)\b|車両|デカール"),
                ("Pirate shop access", @"\bPIRATE SHOP ACCESS\b|パイレーツショップ|海賊ショップ"), ("Gold outfit", @"\bGOLD(EN)? OUTFIT\b|ゴールドコスチューム|黄金のコスチューム") })
                if (Regex.IsMatch(rewardText, pattern)) hits[kind] = rewardText;
        }
        foreach (string key in latched.ToArray())
            if (!hits.ContainsKey(key) && uninterrupted && now - lastSeen[key] >= TimeSpan.FromSeconds(1.8)) latched.Remove(key);
        foreach (var hit in hits)
        {
            lastSeen[hit.Key] = now;
            if (!latched.Add(hit.Key)) continue;
            if (Current is null)
            {
                Current = new MatchRecord { Started = now, Partial = true };
                ledger.Matches.Add(Current);
                Phase = "Partial match · opened mid-game";
            }
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
