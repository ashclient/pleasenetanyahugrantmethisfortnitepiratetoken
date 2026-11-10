using TreasureLedger;
using System.Text.Json;

int assertions = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); assertions++; Console.WriteLine("PASS " + name); }
var ledger = new Ledger();
var tracker = new Tracker(ledger);
var time = DateTimeOffset.Parse("2026-11-09T12:00:00Z");
Observation O(string text, double x = .5, double y = .4) => new(text, x, y);
void Feed(double seconds, params Observation[] lines) => tracker.Process(lines, time.AddSeconds(seconds));
Feed(0, O("プレイヤーを待っています"));
Feed(.4, O("プレイヤーを待っています"));
Check(ledger.Matches.Count == 1, "Japanese match start counted once");
Feed(1, O("宝箱"), O("+100 XP"));
Feed(1.4, O("宝箱"), O("+100 XP"));
Check(ledger.Totals.GetValueOrDefault("Chest") == 1, "Japanese chest plus XP deduplicated");
Feed(1.8); Feed(2.2); Feed(2.6); Feed(3); Feed(3.4);
Feed(3.8, O("宝箱"), O("+100 XP"));
Check(ledger.Totals["Chest"] == 2, "Fresh Japanese chest rearmed after absence");
Feed(4.2, O("ギズモ"));
Check(!ledger.Totals.ContainsKey("Gizmo"), "Inventory gizmo is not an award");
Feed(4.6, O("お宝を発見"), O("ギズモ"));
Feed(5, O("お宝を発見"), O("ギズモ"));
Feed(20, O("お宝を発見"), O("ギズモ"));
Check(ledger.Totals["Gizmo"] == 1, "Japanese award deduplicated including alt-tab gap");
Feed(20.4, O("獲得"), O("ロード画面"));
Check(ledger.Totals.GetValueOrDefault("Loading screen") == 1, "Japanese loading screen award");
Feed(20.8, O("あなたの順位 12"));
Check(tracker.Current!.Ended.HasValue, "Japanese result ends match");
Feed(21.2, O("バトルバス発車まで"));
Check(ledger.Matches.Count == 2 && tracker.Current!.Chests == 0, "New match resets only per-match count");
Feed(21.6, O("CHEST SEARCHED", .1));
Feed(22, O("Search chests 0 / 5", .1), O("100 XP"));
Check(ledger.Totals["Chest"] == 2, "Quest sidebar does not count as chest");
Feed(22.4, O("CHEST SEARCHED"));
Check(ledger.Totals["Chest"] == 3, "English explicit notification counted");
Feed(22.8, O("YOU FOUND"), O("PIRATE SHOP ACCESS"));
Check(ledger.Totals.GetValueOrDefault("Pirate shop access") == 1, "Pirate shop access reward");
var reload = JsonSerializer.Deserialize<Ledger>(JsonSerializer.Serialize(ledger))!;
Check(reload.Totals["Chest"] == 3 && reload.Matches.Count == 2, "History round-trip preserves counts");
var partial = new Ledger();
new Tracker(partial).Process([O("宝箱を開けた")], time);
Check(partial.Matches.Count == 1 && partial.Matches[0].Partial, "Mid-game launch creates partial match");
var jpSpaced = new Ledger();
new Tracker(jpSpaced).Process([O("宝 箱"), O("+ 1 0 0 X P", .5, .44)], time);
Check(jpSpaced.Totals.GetValueOrDefault("Chest") == 1, "Japanese OCR word spacing and split XP recognized");
var jpCombined = new Ledger();
new Tracker(jpCombined).Process([O("+100XP 宝箱")], time);
Check(jpCombined.Totals.GetValueOrDefault("Chest") == 1, "Combined XP and Japanese chest line recognized");
var distant = new Ledger();
new Tracker(distant).Process([O("宝箱", .5, .35), O("+100XP", .5, .8)], time);
Check(!distant.Totals.ContainsKey("Chest"), "Unrelated distant XP does not count a chest");
// Live events arrive once per accepted sequence change and must bypass the old toast latch.
var live = new Ledger(); var liveTracker = new Tracker(live);
for (int i = 0; i < 3; i++) liveTracker.Process([new("active",0,0,"visual-active"), new("opening",.7,.32,"event-chest")],time.AddSeconds(i));
Check(live.Totals.GetValueOrDefault("Chest") == 3, "Three live opening events count while the popup remains on screen");
liveTracker.Process([new("active",0,0,"visual-active"), O("CHEST SEARCHED"), O("+100 XP")],time.AddSeconds(3));
Check(live.Totals["Chest"] == 3, "Legacy OCR cannot duplicate a live popup event");
foreach (var name in new[] { "PORTABLE EXTRACTOR", "LUCKY LOCATOR", "SPRITE LOCATOR", "EXTRACTION SITE BOOSTER", "GOLDEN FIREWORK", "ポータブル抽出器", "ラッキーロケーター", "抽出サイトブースター", "ゴールデンファイアワーク" })
{
 var gifts = new Ledger(); new Tracker(gifts).Process([O("YOU FOUND"),O(name)],time);
 Check(gifts.Totals.GetValueOrDefault("Gizmo")==1, "Gizmo reward alias: " + name);
 var inventory = new Ledger(); new Tracker(inventory).Process([O(name)],time);
 Check(!inventory.Totals.ContainsKey("Gizmo"), "Inventory alias alone does not count: " + name);
}
Console.WriteLine($"{assertions} assertions passed. These are synthetic observations, not a live Fortnite accuracy test.");
