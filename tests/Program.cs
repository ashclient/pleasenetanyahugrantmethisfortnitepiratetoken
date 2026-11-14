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
Feed(20.4, O("見つけたもの"), O("ロード画面"));
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

// Lifecycle regressions use the same independently timestamped OCR batches as capture.
Observation Active() => new("Fast sampler", 0, 0, "visual-active");
Observation[] Header(double captured, string text) => [Active(), new("header", 0, 0, "match-pass", time.AddSeconds(captured)), new(text, .5, .07, "match-ocr", time.AddSeconds(captured))];
Observation[] Metadata(double captured, string text) => [Active(), new("metadata", 0, 0, "metadata-pass", time.AddSeconds(captured)), new(text, .5, .4, "metadata", time.AddSeconds(captured))];
Observation[] Opening() => [Active(), new("opening", .7, .32, "event-chest")];
var quick = new Ledger(); var quickTracker = new Tracker(quick);
quickTracker.Process(Header(0,"WAITING FOR PLAYERS"),time);
quickTracker.Process(Opening(), time.AddSeconds(10));
quickTracker.Process(Header(30,"WAITING FOR PLAYERS"),time.AddSeconds(30));
quickTracker.Process([Active()],time.AddSeconds(30.1));
quickTracker.Process(Metadata(30.2,""),time.AddSeconds(30.2));
Check(quick.Matches.Count==1 && quickTracker.Current!.Chests==1,"One waiting-banner sample does not reset an active match");
quickTracker.Process(Header(30.6,"WAITING FOR PLAYERS"),time.AddSeconds(30.6));
Check(quick.Matches.Count==2 && quickTracker.Current!.Chests==0 && quick.Totals["Chest"]==1,"Short match resets despite missing result; fast and reward frames cannot interrupt confirmation");
quickTracker.Process(Header(35,"BATTLE BUS LAUNCHING IN 10s"),time.AddSeconds(35));
quickTracker.Process(Header(36,"WAITING FOR PLAYERS"),time.AddSeconds(36));
Check(quick.Matches.Count==2,"Waiting and bus banners belong to one match");
quickTracker.Process(Opening(),time.AddSeconds(45));
quickTracker.Process(Header(46,"WAITING FOR PLAYERS"),time.AddSeconds(46));
quickTracker.Process(Header(46.6,"STORM EYE SHRINKS IN"),time.AddSeconds(46.6));
quickTracker.Process(Header(47.2,"WAITING FOR PLAYERS"),time.AddSeconds(47.2));
quickTracker.Process(Header(47.8,"STORM EYE SHRINKS IN"),time.AddSeconds(47.8));
Check(quick.Matches.Count==2 && quickTracker.Current!.Chests==1,"Separate single-frame false starts do not combine into a reset");
var waiting = new Ledger(); var waitingTracker = new Tracker(waiting);
for(int n=0;n<5;n++) { waitingTracker.Process(Header(n*190,"プレイヤーを待っています"),time.AddSeconds(n*190)); waitingTracker.Process([Active()],time.AddSeconds(n*190+.1)); }
Check(waiting.Matches.Count==1,"Long waiting screen and OCR flicker never create extra matches");
var delayed = new Ledger(); var delayedTracker = new Tracker(delayed);
delayedTracker.Process(Header(0,"WAITING FOR PLAYERS"),time);
delayedTracker.Process(Opening(),time.AddSeconds(10));
delayedTracker.Process(Header(1,"WAITING FOR PLAYERS"),time.AddSeconds(12));
delayedTracker.Process(Header(2,"WAITING FOR PLAYERS"),time.AddSeconds(12.6));
Check(delayed.Matches.Count==1 && delayedTracker.Current!.Chests==1,"Late OCR of old waiting-room frames cannot erase live chest count");
delayedTracker.Process(Header(20,"バトルバスの発車まで"),time.AddSeconds(20));
delayedTracker.Process(Header(20,"バトルバスの発車まで"),time.AddSeconds(20.4));
Check(delayed.Matches.Count==1,"Repeated delivery of one OCR frame is not a second confirmation");
delayedTracker.Process(Header(20.7,"バトルバスの発車まで"),time.AddSeconds(20.7));
Check(delayed.Matches.Count==2 && delayedTracker.Current!.Chests==0,"Japanese bus header can reset without a waiting banner or result screen");
delayedTracker.Process(Metadata(15,"YOU PLACED 20"),time.AddSeconds(22));
Check(!delayedTracker.Current!.Ended.HasValue,"Old result OCR cannot end a newer match");
var ended = new Ledger(); var endedTracker = new Tracker(ended);
endedTracker.Process(Opening(),time);
endedTracker.Process(Metadata(10,"YOU PLACED 30"),time.AddSeconds(10));
endedTracker.Process(Opening(),time.AddSeconds(40));
Check(ended.Matches.Count==2 && ended.Matches[0].Chests==1 && endedTracker.Current!.Chests==1 && ended.Totals["Chest"]==2,"Chest after ended match creates a partial match if all start banners were missed");
var resumed = JsonSerializer.Deserialize<Ledger>(JsonSerializer.Serialize(ended))!;
var resumedTracker = new Tracker(resumed);
Check(resumedTracker.Current!.Id==endedTracker.Current!.Id && resumedTracker.Current.Chests==1,"Restarting the app resumes its unfinished match");
resumedTracker.Process([Active()],time.AddMinutes(10));
Check(resumed.Matches.Count==2 && resumedTracker.Current.Chests==1,"Alt-tab or paused sampling alone cannot reset a match");
resumedTracker.Process(Header(650,"WAITING   FOR   PLAYERS"),time.AddSeconds(650));
resumedTracker.Process(Header(650.6,"WAITING FOR PLAYERS"),time.AddSeconds(650.6));
Check(resumed.Matches.Count==3 && resumedTracker.Current.Chests==0 && resumed.Totals["Chest"]==2,"New match after app restart resets only match count and preserves lifetime history");
var firstReward = new Ledger(); var firstRewardTracker = new Tracker(firstReward);
firstRewardTracker.Process([O("YOU FOUND"),O("GIZMO")],time);
firstRewardTracker.Process([O("YOU FOUND"),O("GIZMO")],time.AddSeconds(.4));
Check(firstReward.Totals.GetValueOrDefault("Gizmo")==1,"First reward in a partial match remains latched");

var reconciliation = new Ledger();
var recorded = new MatchRecord { Started=time,Ended=time.AddSeconds(120),Events=[new(time.AddSeconds(5),"Chest","live"),new(time.AddSeconds(10),"Chest","live"),new(time.AddSeconds(15),"Chest","live"),new(time.AddSeconds(16),"Gizmo","award")] };
reconciliation.Matches.Add(recorded);
ReplaySummary R(string id, int count, int destroyed=0) => new(id,time,120,true,true,Enumerable.Range(0,count).Select(n=>new ReplayChest(5+n*10,n<destroyed)).ToList());
var correction=ReplayReconciliation.Apply(reconciliation,R("session-one",5,3));
Check(correction.Changed && recorded.Chests==5 && recorded.ReplayDestroyedCount==3,"Replay adds two missing chests, not five replay chests or all three destroyed chests");
Check(reconciliation.Totals["Gizmo"]==1,"Replay chest correction preserves reward totals");
Check(!ReplayReconciliation.Apply(reconciliation,R("session-one",5,3)).Changed && recorded.Chests==5,"Repeated replay scan never duplicates corrections");
var reconciledReload=JsonSerializer.Deserialize<Ledger>(JsonSerializer.Serialize(reconciliation))!;
Check(!ReplayReconciliation.Apply(reconciledReload,R("session-one",5,3)).Changed && reconciledReload.Totals["Chest"]==5,"Replay deduplication survives application restart");
var lower=new Ledger { Matches=[new MatchRecord {Started=time,Ended=time.AddSeconds(120),Events=Enumerable.Range(0,4).Select(n=>new LedgerEvent(time.AddSeconds(n*10),"Chest","live")).ToList()}] };
ReplayReconciliation.Apply(lower,R("lower",2));
Check(lower.Totals["Chest"]==4 && lower.Matches[0].ReplayCountLower,"Lower replay count is flagged without deleting saved live events");
var ambiguous=new Ledger {Matches=[new MatchRecord {Started=time,Ended=time.AddSeconds(120)},new MatchRecord {Started=time.AddSeconds(30),Ended=time.AddSeconds(120)}]};
Check(!ReplayReconciliation.Apply(ambiguous,R("ambiguous",3)).Changed && ambiguous.Totals.Count==0,"Ambiguous match mapping does not add a duplicate game");
var unowned=new Ledger();
Check(!ReplayReconciliation.Apply(unowned,R("other-player",4) with {OwnerKnown=false}).Changed && unowned.Matches.Count==0,"Unconfirmed recording player cannot contribute teammate or spectator chests");
Check(!ReplayReconciliation.Apply(unowned,R("creative",4) with {IsBattleRoyale=false}).Changed,"Non-Battle-Royale replay skipped");
Check(!ReplayReconciliation.Apply(unowned,R("bad-timing",1) with {Chests=[new ReplayChest(double.NaN,true)]}).Changed,"Malformed replay times rejected");
var onlyReplay = new Ledger();
ReplayReconciliation.Apply(onlyReplay,R("no-live-record",3,2));
Check(onlyReplay.Matches.Count==1 && onlyReplay.Totals["Chest"]==3 && onlyReplay.Matches[0].ReplayDestroyedCount==2,"Completed replay can create one missing match without live observations");
var alreadyLive = new Ledger {Matches=[new MatchRecord {Started=time,Ended=time.AddSeconds(120),Events=[new(time.AddSeconds(8),"Chest","live notification from broken chest")]}]};
ReplayReconciliation.Apply(alreadyLive,R("already-seen-broken",1,1));
Check(alreadyLive.Totals["Chest"]==1 && alreadyLive.Matches[0].ReplayDestroyedCount==1,"Broken chest already counted live is not counted twice");

Observation[] Lobby(double captured,bool japanese=false,bool returnButton=false,double navY=.06) => [Active(),new("header",0,0,"match-pass",time.AddSeconds(captured)),new("lobby",0,0,"lobby-pass",time.AddSeconds(captured)),new(japanese?"プレイ":"PLAY",.12,navY,"lobby-ocr",time.AddSeconds(captured)),new(japanese?"ロッカー":"LOCKER",.30,navY,"lobby-ocr",time.AddSeconds(captured)),new(japanese?"ショップ":"SHOP",.48,navY,"lobby-ocr",time.AddSeconds(captured)),new(returnButton?"RETURN TO LOBBY":japanese?"準備 OK！":"READY!",.16,.80,"lobby-ocr",time.AddSeconds(captured))];
var lobbyLedger=new Ledger();var lobbyTracker=new Tracker(lobbyLedger);
lobbyTracker.Process(Opening(),time);
var finishedId=lobbyTracker.Current!.Id;
lobbyTracker.Process(Lobby(10,returnButton:true),time.AddSeconds(10));
lobbyTracker.Process(Lobby(10.7,returnButton:true),time.AddSeconds(10.7));
Check(lobbyTracker.Current?.Chests==1 && !lobbyTracker.InLobby,"Return-to-lobby menu command does not reset a match");
lobbyTracker.Process(Lobby(12,navY:.3),time.AddSeconds(12));lobbyTracker.Process(Lobby(12.7,navY:.3),time.AddSeconds(12.7));
Check(lobbyTracker.Current?.Chests==1,"Quest/menu text outside lobby navigation cannot reset a match");
lobbyTracker.Process(Lobby(20),time.AddSeconds(20));
lobbyTracker.Process([Active()],time.AddSeconds(20.1));
Check(lobbyTracker.Current?.Chests==1,"Single lobby frame does not erase the displayed match");
lobbyTracker.Process(Lobby(20.7),time.AddSeconds(20.7));
Check(lobbyTracker.InLobby && lobbyTracker.Current is null && lobbyLedger.Totals["Chest"]==1 && lobbyLedger.Matches.Count==1 && lobbyLedger.Matches[0].Ended.HasValue,"Confirmed lobby closes active tracking and preserves the completed match and lifetime total");
lobbyTracker.Process(Lobby(22),time.AddSeconds(22));lobbyTracker.Process(Lobby(23),time.AddSeconds(23));
Check(lobbyTracker.Current is null && lobbyLedger.Matches.Count==1,"Remaining in lobby never creates empty matches");
lobbyTracker.ReplayApplied(finishedId);
Check(lobbyTracker.Current is null,"Late replay does not resume active lobby tracking");
lobbyTracker.Process(Opening(),time.AddSeconds(24));
Check(lobbyTracker.Current is null && lobbyLedger.Totals["Chest"]==1,"Late live notification is ignored while confirmed in lobby");
lobbyTracker.Process(Header(30,"WAITING FOR PLAYERS"),time.AddSeconds(30));lobbyTracker.Process(Opening(),time.AddSeconds(35));
Check(!lobbyTracker.InLobby && lobbyTracker.Current?.Chests==1 && lobbyLedger.Totals["Chest"]==2 && lobbyLedger.Matches.Count==2,"Next game counts from zero after lobby reset");
lobbyTracker.Process(Lobby(21),time.AddSeconds(40));lobbyTracker.Process(Lobby(22),time.AddSeconds(40.7));
Check(lobbyTracker.Current?.Chests==1 && !lobbyTracker.InLobby,"Delayed old lobby OCR cannot clear a newer game");
var japaneseLobbyLedger=new Ledger();var japaneseLobbyTracker=new Tracker(japaneseLobbyLedger);
japaneseLobbyTracker.Process(Opening(),time);japaneseLobbyTracker.Process(Lobby(5,true),time.AddSeconds(5));japaneseLobbyTracker.Process(Lobby(5.7,true),time.AddSeconds(5.7));
Check(japaneseLobbyTracker.Current is null && japaneseLobbyTracker.InLobby && japaneseLobbyLedger.Totals["Chest"]==1,"Japanese lobby tabs and Ready panel reset match count");

var withEmpty=new Ledger {Matches=[new MatchRecord {Started=time,Ended=time.AddSeconds(120),Events=[new(time.AddSeconds(10),"Chest","live")]},new MatchRecord {Started=time.AddSeconds(115)}]};
var emptyFix=ReplayReconciliation.Apply(withEmpty,R("with-empty-next-game",3,2));
Check(emptyFix.Changed && withEmpty.Totals["Chest"]==3 && withEmpty.Matches[1].Events.Count==0,"Replay prefers real chest evidence over overlapping empty next-match record");
var splitGame=new Ledger {Matches=[new MatchRecord {Started=time,Ended=time.AddSeconds(30),Events=[new(time.AddSeconds(10),"Chest","live")]},new MatchRecord {Started=time.AddSeconds(35),Ended=time.AddSeconds(120),Events=[new(time.AddSeconds(50),"Chest","live")]}]};
Check(ReplayReconciliation.Apply(splitGame,R("split-game",5,3)).Changed && splitGame.Totals["Chest"]==5 && splitGame.Matches.All(m=>m.ReplaySessionId=="split-game"),"Split records from one replay reconcile as a group without adding five on top of live counts");
Check(!ReplayReconciliation.Apply(splitGame,R("split-game",5,3)).Changed && splitGame.Totals["Chest"]==5,"Grouped replay fragments stay idempotent");
var crossing=new Ledger {Matches=[new MatchRecord {Started=time.AddMinutes(-2),Ended=time.AddSeconds(120),Events=[new(time.AddSeconds(-90),"Chest","previous game"),new(time.AddSeconds(10),"Chest","new game")]}]};
Check(!ReplayReconciliation.Apply(crossing,R("mixed-history",5)).Changed && crossing.Totals["Chest"]==2,"Mixed-game history remains flagged instead of guessing");
Observation[] Rare(double t,string label="レア宝箱",string? xp="+900 XP") => [new("rare",0,0,"rare-pass",time.AddSeconds(t)),new(label,.7,.34,"rare-ocr",time.AddSeconds(t)),new(xp??"",.7,.29,"rare-ocr",time.AddSeconds(t))];
Observation[] NoRare(double t) => [new("rare",0,0,"rare-pass",time.AddSeconds(t))];
int FinishRare(RareChestBackup b) {int n=0;foreach(double t in new[]{2d,2.3,2.7}) n+=b.Update(NoRare(t),time.AddSeconds(t),false,0);return n;}
var rb=new RareChestBackup();rb.Update(Rare(0),time,false,0);rb.Update(Rare(.3),time.AddSeconds(.3),false,0);
Check(FinishRare(rb)==1,"Japanese rare-label and XP backup counts a missed popup once after it ends");
var coveredRare=new RareChestBackup();coveredRare.Update(Rare(0,"RARE CHEST"),time,false,0);coveredRare.Update(Rare(.3,"RARE CHEST"),time.AddSeconds(.3),true,1);
Check(FinishRare(coveredRare)==0,"Rare popup already counted visually is never added by the backup");
var earlyVision=new RareChestBackup();earlyVision.Update([],time,false,1);earlyVision.Update(Rare(.2),time.AddSeconds(.2),false,0);earlyVision.Update(Rare(.5),time.AddSeconds(.5),false,0);
Check(FinishRare(earlyVision)==0,"Vision event arriving before delayed rare OCR suppresses duplicate backup");
var noXpRare=new RareChestBackup();noXpRare.Update(Rare(0,xp:null),time,false,0);noXpRare.Update(Rare(.3,xp:null),time.AddSeconds(.3),false,0);
Check(FinishRare(noXpRare)==0,"Rare name in inventory without XP cannot count");
var questRare=new RareChestBackup();questRare.Update(Rare(0,"Search rare chests 0/5"),time,false,0);questRare.Update(Rare(.3,"Search rare chests 0/5"),time.AddSeconds(.3),false,0);
Check(FinishRare(questRare)==0,"Rare-chest quest wording cannot count");
var multRare=new RareChestBackup();multRare.Update(Rare(0,"レアな宝箱 X2","+1800 XP"),time,false,0);multRare.Update(Rare(.3,"レアな宝箱 X2","+1800 XP"),time.AddSeconds(.3),false,0);multRare.Update(Rare(.8,"レアな宝箱 X3","+2700 XP"),time.AddSeconds(.8),false,0);multRare.Update(Rare(1.1,"レアな宝箱 X3","+2700 XP"),time.AddSeconds(1.1),false,0);
Check(FinishRare(multRare)==2,"Rare backup counts observed X2-to-X3 change without inventing unseen initial openings");

var longQueue=new Ledger {Matches=[new MatchRecord {Started=time.AddMinutes(-10),Ended=time.AddSeconds(120),Events=[new(time.AddSeconds(20),"Chest","live")]}]};
Check(ReplayReconciliation.Apply(longQueue,R("long-queue",3)).Changed && longQueue.Totals["Chest"]==3,"Long pregame queue cannot invalidate direct in-match chest evidence");
var neighborChecked=new Ledger {Matches=[new MatchRecord {Started=time.AddMinutes(-10),Ended=time.AddSeconds(20),ReplaySessionId="previous-session",Events=[new(time.AddSeconds(-90),"Chest","live"),new(time.AddSeconds(20),"Chest","Replay correction previous-session")]},new MatchRecord {Started=time,Ended=time.AddSeconds(120),Events=[new(time.AddSeconds(40),"Chest","live")]}]};
Check(ReplayReconciliation.Apply(neighborChecked,R("next-session",3)).Changed && neighborChecked.Totals["Chest"]==5,"Previous checked replay with overlapping end timestamp cannot block a distinct populated match");
var jpQuestRare=new RareChestBackup();jpQuestRare.Update(Rare(0,"レア宝箱を3個開ける"),time,false,0);jpQuestRare.Update(Rare(.3,"レア宝箱を3個開ける"),time.AddSeconds(.3),false,0);
Check(FinishRare(jpQuestRare)==0,"Japanese rare-chest quest text is not an opening notification");

var unspecifiedClock=new DateTime(2026,11,9,6,47,8,DateTimeKind.Unspecified);
Check(TreasureLedger.ReplayProtocol.ReplayClock.AsUtc(unspecifiedClock).Hour==6 && TreasureLedger.ReplayProtocol.ReplayClock.AsUtc(unspecifiedClock).Kind==DateTimeKind.Utc,"Unspecified replay UTC ticks never receive the local timezone offset twice");
Check(TreasureLedger.ReplayProtocol.ReplayClock.AsUtc(DateTime.SpecifyKind(unspecifiedClock,DateTimeKind.Utc)).Hour==6,"Explicit UTC replay timestamps remain unchanged");
// Anonymized relative offsets from the player's diagnostic: 13 live vs 15 replay,
// with exactly the two destruction events absent from the live observations.
var sampleOffsets=new double[]{118.069,125.357,143.160,175.418,187.458,222.941,236.534,263.830,281.743,285.042,297.851,306.493,327.153};
var sampleReplayOffsets=new double[]{116.9,124,140.3,174.1,186.2,221.7,235.3,254.1,262.7,280.6,283.9,296.7,305.3,316.2,326};
var sampleGame=new Ledger {Matches=[new MatchRecord {Started=time.AddSeconds(12),Ended=time.AddSeconds(368),Events=sampleOffsets.Select(t=>new LedgerEvent(time.AddSeconds(t),"Chest","live")).ToList()}]};
var sampleSummary=new ReplaySummary("anonymized-clock-regression",time,351,true,true,sampleReplayOffsets.Select(t=>new ReplayChest(t,t==254.1 || t==316.2)).ToList());
Check(ReplayReconciliation.Apply(sampleGame,sampleSummary).Changed && sampleGame.Totals["Chest"]==15 && sampleGame.Matches[0].ReplayDestroyedCount==2,"Correct UTC alignment reconciles 13 observed openings plus two destroyed chests to 15");
Observation[] Award(double t,string category,string source="reward-ocr") => [Active(),new("metadata",0,0,"metadata-pass",time.AddSeconds(t)),new("YOU FOUND",.5,.20,source,time.AddSeconds(t)),new(category,.5,.24,source,time.AddSeconds(t))];
Observation[] CleanAwards(double t) => [Active(),new("metadata",0,0,"metadata-pass",time.AddSeconds(t))];
var verifiedRewardLedger=new Ledger();var verifiedRewardTracker=new Tracker(verifiedRewardLedger);
verifiedRewardTracker.Process(Award(0,"PICKAXE"),time);verifiedRewardTracker.Process(Award(.5,"PICKAXE"),time.AddSeconds(.5));
Check(!verifiedRewardLedger.Totals.ContainsKey("Pickaxe"),"Award panel already visible at startup cannot count as a newly observed drop");
verifiedRewardTracker.Process(CleanAwards(1),time.AddSeconds(1));verifiedRewardTracker.Process(CleanAwards(1.5),time.AddSeconds(1.5));
verifiedRewardTracker.Process(Award(2,"SOLID GOLD GAUNTLETS PICKAXE"),time.AddSeconds(2));
Check(verifiedRewardLedger.Totals.GetValueOrDefault("Pickaxe")==1,"Fresh focused gauntlet award after a clean baseline counts once");
verifiedRewardTracker.Process(CleanAwards(3),time.AddSeconds(3));verifiedRewardTracker.Process(CleanAwards(4),time.AddSeconds(4));verifiedRewardTracker.Process(Award(5,"SOLID GOLD GAUNTLETS PICKAXE"),time.AddSeconds(5));
Check(verifiedRewardLedger.Totals["Pickaxe"]==1,"Same one-time gauntlet award cannot increment again after OCR re-arms");
var reloadRewardLedger=JsonSerializer.Deserialize<Ledger>(JsonSerializer.Serialize(verifiedRewardLedger))!;var reloadRewardTracker=new Tracker(reloadRewardLedger);
reloadRewardTracker.Process(CleanAwards(6),time.AddSeconds(6));reloadRewardTracker.Process(CleanAwards(7),time.AddSeconds(7));reloadRewardTracker.Process(Award(8,"SOLID GOLD GAUNTLETS PICKAXE"),time.AddSeconds(8));
Check(reloadRewardLedger.Totals["Pickaxe"]==1,"Gauntlet identity deduplication survives app restart");
var fullScreenFake=RewardEvidence.Find([new("YOU FOUND",.5,.2,"scene-ocr"),new("PICKAXE",.5,.24,"scene-ocr")]);
Check(fullScreenFake.Count==0,"Full-screen scene OCR cannot create a reward");
Check(RewardEvidence.Find([new("獲得",.5,.2,"reward-ocr"),new("バックアクセサリー",.5,.24,"reward-ocr")]).Count==0,"Generic Japanese XP/acquired text cannot create a back-bling reward");
Check(RewardEvidence.Find([new("YOU FOUND",.5,.2,"reward-ocr"),new("PICKAXE",.75,.45,"reward-ocr")]).Count==0,"Distant inventory pickaxe cannot attach to an award heading");
Check(RewardEvidence.Find([new("YOU FOUND",.5,.2,"reward-ocr"),new("OWNED PICKAXE",.5,.24,"reward-ocr")]).Count==0,"Owned item panel cannot count as a new reward");
Check(RewardEvidence.Find([new("YOU FOUND",.5,.2,"reward-ocr"),new("PICKAXE BACK BLING",.5,.24,"reward-ocr")]).Count==0,"Ambiguous multi-category card cannot award both gauntlets and back bling");
Check(RewardEvidence.Find([new("見つけたもの",.5,.2,"reward-ocr"),new("バックアクセサリー",.5,.24,"reward-ocr")]).ContainsKey("Back bling"),"Japanese fresh award header and nearby back-bling category are recognized");
var rewardEntry=reloadRewardLedger.Matches.SelectMany(m=>m.Events).Single(e=>e.Kind=="Pickaxe");rewardEntry.Excluded=true;
Check(!reloadRewardLedger.Totals.ContainsKey("Pickaxe") && reloadRewardLedger.Matches.SelectMany(m=>m.Events).Contains(rewardEntry),"Excluding a false reward corrects totals without deleting the historical entry");
rewardEntry.Excluded=false;
Check(reloadRewardLedger.Totals["Pickaxe"]==1,"Excluded reward entry can be restored");

var menuRewardLedger=new Ledger();var menuRewardTracker=new Tracker(menuRewardLedger);
menuRewardTracker.Process(Opening(),time);menuRewardTracker.Process(Lobby(5),time.AddSeconds(5));menuRewardTracker.Process(Lobby(5.7),time.AddSeconds(5.7));
menuRewardTracker.Process(Header(6,""),time.AddSeconds(6));menuRewardTracker.Process(Header(6.7,""),time.AddSeconds(6.7));
menuRewardTracker.Process(CleanAwards(7),time.AddSeconds(7));menuRewardTracker.Process(CleanAwards(7.5),time.AddSeconds(7.5));menuRewardTracker.Process(Award(8,"PICKAXE"),time.AddSeconds(8));
Check(!menuRewardLedger.Totals.ContainsKey("Pickaxe") && menuRewardLedger.Totals["Chest"]==1,"Opening an owned-item screen after lobby tabs disappear cannot create a reward");

var heldLedger=new Ledger();var held=new Tracker(heldLedger);held.SetReplayWaiting(true);
held.Process(Opening(),time);var first=held.Current!;
for(int i=1;i<13;i++)held.Process(Opening(),time.AddSeconds(i));
held.Process(Lobby(20),time.AddSeconds(20));held.Process(Lobby(20.7),time.AddSeconds(20.7));
Check(held.Current is null&&held.DisplayedChests==13,"Lobby retains the last game instead of displaying zero");
first.Events.Add(new(time,"Chest","replay"));first.Events.Add(new(time,"Chest","replay"));first.ReplaySessionId="first";held.ReplayApplied(first.Id);
Check(held.DisplayedChests==15,"Replay correction updates the held game's display");
held.Process(Header(30,"WAITING FOR PLAYERS"),time.AddSeconds(30));
Check(held.DisplayedChests==15,"Next game's waiting phase keeps the corrected previous total");
held.Process(Opening(),time.AddSeconds(35));
Check(held.DisplayedChests==1&&heldLedger.Totals["Chest"]==16,"First next-game chest switches directly to one while lifetime stays cumulative");
held.ReplayApplied(first.Id);Check(held.DisplayedChests==1,"Old replay cannot replace the new game's display");
var second=held.Current!;held.Process(Lobby(45),time.AddSeconds(45));held.Process(Lobby(45.7),time.AddSeconds(45.7));held.Process(Header(50,"WAITING FOR PLAYERS"),time.AddSeconds(50));
held.Process(Opening(),time.AddSeconds(55));held.Process(Opening(),time.AddSeconds(56));
Check(held.DisplayMatch?.Id==second.Id&&held.DisplayedChests==1&&held.Current!.Chests==2&&held.DisplayWaitingForReplay,"New-game chests are saved while the prior replay is pending");
var persistedHold=JsonSerializer.Deserialize<Ledger>(JsonSerializer.Serialize(heldLedger))!;var resumedHold=new Tracker(persistedHold);resumedHold.SetReplayWaiting(true);
Check(resumedHold.DisplayedChests==1&&resumedHold.DisplayWaitingForReplay,"Display hold and pending match survive restart");
second.ReplaySessionId="second";held.ReplayApplied(second.Id);
Check(held.DisplayedChests==2&&!held.DisplayWaitingForReplay,"Delayed replay releases the display to already-counted new-game chests");
resumedHold.SetReplayWaiting(false);Check(resumedHold.DisplayedChests==2,"Disabling unavailable replay checks releases the pending game");
var linkPath=Path.Combine(Path.GetTempPath(),"ledger-link-test-"+Guid.NewGuid().ToString("N"));
try{
 var client=new LinkClient(linkPath);string identity=client.Config.MemberId;
 Check(client.Combined(10) is null,"Unpaired linked count stays unavailable");
 client.Config.GroupId="test";client.Config.Cache=new(){Members=[new(){Id=identity,Chests=10},new(){Id="peer",Chests=20}]};client.Rename("Tester");
 Check(client.Combined(12)==32,"Linked total includes fresh local counts exactly once and cached peers");
 var reopened=new LinkClient(linkPath);Check(reopened.Config.MemberId==identity&&reopened.Combined(12)==32,"Restart retains member identity and never adds a second lifetime snapshot");
 Check(reopened.Combined(8)==28,"Legitimate personal corrections replace the old contribution");
}finally{Directory.Delete(linkPath,true);}
var emptyReplay=new MatchRecord{Started=time.AddHours(10),Ended=time.AddHours(11),ReplaySessionId="empty"};heldLedger.Matches.Add(emptyReplay);held.ReplayApplied(emptyReplay.Id);
Check(held.DisplayedChests==2,"An empty replay cannot reset a retained display to zero");

var multiPath=Path.Combine(Path.GetTempPath(),"ledger-multi-test-"+Guid.NewGuid().ToString("N"));
try{
 int calls=0;
 var handler=new SyncTestHandler(_=>{calls++;return new(calls==1?System.Net.HttpStatusCode.Forbidden:System.Net.HttpStatusCode.OK){Content=new StringContent(calls==1?"<html>Request blocked</html>":"{\"groupId\":\"room\",\"groupName\":\"Crew\",\"members\":[]}")};});
 var groupClient=new LinkClient(Path.Combine(multiPath,"group"),http:new System.Net.Http.HttpClient(handler));
 var myId=groupClient.Config.MemberId;groupClient.Config.GroupId="room";
 groupClient.Config.Cache=new(){Members=[new(){Id=myId,Chests=10},new(){Id="peer2",Chests=20},new(){Id="peer3",Chests=30},new(){Id="peer4",Chests=40}]};
 Check(groupClient.Combined(10)==100,"Native linked total sums all four members, including self once");
 await groupClient.Sync(new(10,[]));
 Check(groupClient.IsLinked&&groupClient.Combined(10)==100&&groupClient.Status.Contains("HTTP 403")&&groupClient.Status.Contains("workers.dev"),"HTML proxy rejection reports host/status and preserves cached membership and counts");
 await groupClient.Sync(new(10,[]));Check(groupClient.Status.StartsWith("Synced")&&calls==2,"Sync retries and recovers after a temporary HTML rejection");
 var revoked=new LinkClient(Path.Combine(multiPath,"revoked"),http:new System.Net.Http.HttpClient(new SyncTestHandler(_=>new(System.Net.HttpStatusCode.Forbidden){Content=new StringContent("{\"error\":\"Membership is no longer active\"}")})));revoked.Config.GroupId="room";
 await revoked.Sync(new(1,[]));Check(!revoked.IsLinked&&revoked.Status.Contains("Membership is no longer active"),"Authenticated membership rejection remains distinct from temporary proxy failure");
 var timeout=new LinkClient(Path.Combine(multiPath,"timeout"),http:new System.Net.Http.HttpClient(new SyncTestHandler(_=>throw new TaskCanceledException("Simulated HTTP timeout"))));timeout.Config.GroupId="room";
 await timeout.Sync(new(1,[]));Check(timeout.IsLinked&&timeout.Status.Contains("timeout"),"HTTP timeout retains membership and does not escape background sync");
 var legacy=new LinkClient(Path.Combine(multiPath,"legacy"),new Uri("https://retired.chatgpt.site/api/v1/"),new System.Net.Http.HttpClient(new SyncTestHandler(_=>new(System.Net.HttpStatusCode.Unauthorized){Content=new StringContent("<html>Sign in</html>")})));
 try{await legacy.Create("Player","Crew",new(0,[]));Check(false,"Legacy endpoint must reject");}catch(InvalidOperationException e){Check(e.Message.Contains("Outdated sync server")&&e.Message.Contains("HTTP 401")&&!e.Message.Contains("<html>"),"Retired-site rejection explains updating all clients without displaying HTML");}
}finally{Directory.Delete(multiPath,true);}

foreach(string label in new[]{"CHEST","CHEST SEARCHED","宝箱","RARE CHEST","レア宝箱"}){
 var fusion=new LiveChestFusion();Check(fusion.Update(Rare(0,label,"+100 XP"),time,false,0)==0,"Live OCR waits for a second fresh sample: "+label);
 Check(fusion.Update(Rare(.3,label,"+100 XP"),time.AddSeconds(.3),false,0)==1,"Live OCR counts confirmed popup before it disappears: "+label);
 Check(fusion.Update([],time.AddSeconds(.4),true,1)==0,"Late visual confirmation cannot duplicate OCR event: "+label);
}
var visualFirstFusion=new LiveChestFusion();Check(visualFirstFusion.Update([],time,true,1)==1,"Visual events still count immediately without OCR");
visualFirstFusion.Update(Rare(.2,"CHEST","+100 XP"),time.AddSeconds(.2),true,0);
Check(visualFirstFusion.Update(Rare(.5,"CHEST","+100 XP"),time.AddSeconds(.5),true,0)==0,"Delayed ordinary-chest OCR cannot duplicate an existing visual event");
var stackFusion=new LiveChestFusion();stackFusion.Update(Rare(0,"宝箱 X3","+300 XP"),time,false,0);
Check(stackFusion.Update(Rare(.3,"宝箱 X3","+300 XP"),time.AddSeconds(.3),false,0)==1,"Live OCR first seeing X3 counts only one observed opening");
stackFusion.Update(Rare(.7,"宝箱 X4","+400 XP"),time.AddSeconds(.7),false,0);
Check(stackFusion.Update(Rare(1,"宝箱 X4","+400 XP"),time.AddSeconds(1),false,0)==1,"Fresh multiplier plus changed XP confirms another opening live");
Check(stackFusion.Update([],time.AddSeconds(1.1),true,2)==0,"Two late visual openings consume two OCR credits");
var rearmFusion=new LiveChestFusion();rearmFusion.Update(Rare(0,"CHEST"),time,false,0);rearmFusion.Update(Rare(.3,"CHEST"),time.AddSeconds(.3),false,0);
foreach(double t in new[]{.8,1.1,1.5})rearmFusion.Update(NoRare(t),time.AddSeconds(t),false,0);
Check(rearmFusion.Update([],time.AddSeconds(2),true,1)==1,"A cleared popup cannot suppress a new visual chest");
foreach(string label in new[]{"Search chests 0/5","OPEN CHEST","宝箱を3個開ける"}){
 var negativeFusion=new LiveChestFusion();negativeFusion.Update(Rare(0,label),time,false,0);
 Check(negativeFusion.Update(Rare(.3,label),time.AddSeconds(.3),false,0)==0,"Live OCR rejects quest and interaction text: "+label);
}
var noXpFusion=new LiveChestFusion();noXpFusion.Update(Rare(0,"CHEST",null),time,false,0);
Check(noXpFusion.Update(Rare(.3,"CHEST",null),time.AddSeconds(.3),false,0)==0,"Ordinary chest label without XP cannot count");
var staleFusion=new LiveChestFusion();staleFusion.Update(Rare(0,"CHEST"),time,false,0);
Check(staleFusion.Update(Rare(.3,"CHEST"),time.AddSeconds(10),false,0)==0,"Old asynchronous OCR cannot add a live chest");
Console.WriteLine($"{assertions} assertions passed. These are synthetic observations, not a live Fortnite accuracy test.");

sealed class SyncTestHandler(Func<System.Net.Http.HttpRequestMessage,System.Net.Http.HttpResponseMessage> respond):System.Net.Http.HttpMessageHandler
{
 protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request,CancellationToken token)=>Task.FromResult(respond(request));
}
