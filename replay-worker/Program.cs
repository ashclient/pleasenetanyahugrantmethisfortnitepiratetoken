// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using ChestCounter;
using TreasureLedger.ReplayProtocol;
try
{
    if(args.Length == 1 && args[0] == "--self-test")
    {
        _ = new ReplayAnalyzer();
        if(ReplayClock.AsUtc(new DateTime(2026,11,9,6,47,8,DateTimeKind.Unspecified)).Hour!=6) throw new Exception("UTC conversion failed");
        if(!ChestKinds.IsChest(ChestKinds.Classify("Tiered_Chest_Normal")) || ChestKinds.IsChest(ChestKinds.Classify("AmmoBox"))) throw new Exception("Container classifier failed.");
        Console.WriteLine("{\"ok\":true,\"selfTest\":true}"); return 0;
    }
    if(args.Length != 1 || !File.Exists(args[0])) throw new ArgumentException("Pass one completed .replay file.");
    var file = Path.GetFullPath(args[0]);
    if(ReplayHeaderProbe.IsLive(file) != false || ReplayHeaderProbe.KeyMissing(file))
    { Console.WriteLine("{\"ok\":false,\"pending\":true}"); return 4; }
    using var stream = new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite | FileShare.Delete);
    var match = new ReplayAnalyzer().Analyze(stream,file,null);
    Console.WriteLine(JsonSerializer.Serialize(new {
        ok = true, sessionId = match.SessionId, started = match.MatchStartUtc.HasValue ? ReplayClock.AsUtc(match.MatchStartUtc.Value) : (DateTime?)null, durationSeconds = match.DurationSeconds,
        ownerKnown = match.IsReplayOwner && match.NotTrackedOwner is null, isBattleRoyale = match.IsBattleRoyale,
        chests = match.Chests.Select(c => new { seconds = c.TimeSeconds, destroyed = c.Destroyed })
    }));
    return 0;
}
catch(Exception e) { Console.Error.WriteLine(e.GetType().Name + ": " + e.Message); return 2; }
namespace ChestCounter
{
    // The worker exposes machine-readable data, not the upstream UI.
    internal static class L
    {
        public static string T(string fr,string en,string es) => en;
        public static string Name(string value) => value;
    }
}
