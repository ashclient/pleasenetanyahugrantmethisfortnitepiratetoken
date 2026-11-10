using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace TreasureLedger;

internal static class ReplayWorkerFiles
{
    static Stream Resource(string name) => Assembly.GetExecutingAssembly().GetManifestResourceStream(name) ?? throw new InvalidOperationException("Replay worker is not bundled in this build");
    public static string EnsureWorker(string dataPath)
    {
        using var hashFile = new StreamReader(Resource("ReplayWorker.Sha256")); string hash = hashFile.ReadToEnd().Trim().ToLowerInvariant();
        if(hash.Length != 64 || hash.Any(c=>!Uri.IsHexDigit(c))) throw new InvalidDataException("Invalid bundled replay worker hash");
        string dir = Path.Combine(dataPath,"ReplayWorker",hash[..16]); Directory.CreateDirectory(dir);
        string path = Path.Combine(dir,"TreasureLedger.ReplayWorker.exe");
        if(File.Exists(path)) { using var old = File.OpenRead(path); if(Convert.ToHexString(SHA256.HashData(old)).Equals(hash,StringComparison.OrdinalIgnoreCase)) return path; }
        using(var input = Resource("ReplayWorker.Exe")) using(var output = File.Create(path)) input.CopyTo(output);
        using(var output = File.OpenRead(path)) if(!Convert.ToHexString(SHA256.HashData(output)).Equals(hash,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Replay worker hash mismatch");
        return path;
    }
    public static string ExtractSource(string dataPath)
    {
        Directory.CreateDirectory(dataPath); string path = Path.Combine(dataPath,"ReplayWorkerSource.zip");
        using var input = Resource("ReplayWorker.Source"); using var output = File.Create(path); input.CopyTo(output); return path;
    }
    public static async Task<(int Code,string Output,string Error)> Run(string exe,string argument,CancellationToken token)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Cannot start replay reader");
        try { process.PriorityClass=ProcessPriorityClass.BelowNormal; } catch { }
        using var cancellation = token.Register(()=> { try { process.Kill(true); } catch { } });
        var stdout=process.StandardOutput.ReadToEndAsync(token); var stderr=process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        catch { try { process.Kill(true); } catch { } throw; }
        return (process.ExitCode,await stdout,await stderr);
    }
}

internal sealed class ReplayMonitor(string dataPath,DateTimeOffset from)
{
    readonly Dictionary<string,DateTimeOffset> retryAfter = new();
    readonly HashSet<string> checkedFiles = new();
    public string Status { get; private set; } = "Replay check: waiting for a completed match";
    public string Folder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FortniteGame","Saved","Demos");
    public async Task<ReplaySummary?> ScanNext(CancellationToken token)
    {
        if(!Directory.Exists(Folder)) { Status="Replay folder missing · enable Record Replays in Fortnite Settings → Game"; return null; }
        var now=DateTimeOffset.UtcNow;
        var files=new DirectoryInfo(Folder).EnumerateFiles("*.replay").Where(f=>f.LastWriteTimeUtc >= from.UtcDateTime && now.UtcDateTime-f.LastWriteTimeUtc > TimeSpan.FromSeconds(10)).OrderBy(f=>f.LastWriteTimeUtc);
        foreach(var file in files)
        {
            string fingerprint=$"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
            if(checkedFiles.Contains(fingerprint) || retryAfter.TryGetValue(fingerprint,out var next) && now<next) continue;
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(4));
            try
            {
                Status="Checking completed replay…";
                var result=await ReplayWorkerFiles.Run(ReplayWorkerFiles.EnsureWorker(dataPath),file.FullName,timeout.Token);
                if(result.Code==4) { retryAfter[fingerprint]=now.AddMinutes(1); Status="Replay still recording or encryption key unavailable · waiting for match end"; continue; }
                if(result.Code!=0) throw new InvalidDataException(result.Error.Trim());
                using var json=JsonDocument.Parse(result.Output);var root=json.RootElement;
                var summary=new ReplaySummary(root.GetProperty("sessionId").GetString() ?? "",root.GetProperty("started").GetDateTimeOffset(),root.GetProperty("durationSeconds").GetInt32(),root.GetProperty("ownerKnown").GetBoolean(),root.GetProperty("isBattleRoyale").GetBoolean(),root.GetProperty("chests").EnumerateArray().Select(c=>new ReplayChest(c.GetProperty("seconds").GetDouble(),c.GetProperty("destroyed").GetBoolean())).ToList());
                checkedFiles.Add(fingerprint);Status="Replay parsed · checking match history";return summary;
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested) { throw; }
            catch(Exception e) { retryAfter[fingerprint]=now.AddMinutes(5);Status="Replay check failed: "+e.Message; File.AppendAllText(Path.Combine(dataPath,"replay-errors.log"),$"{now:O} {file.Name}: {e.Message}{Environment.NewLine}");return null; }
        }
        if(!retryAfter.Any()) Status="Replay check: waiting for a completed match · Record Replays must be enabled";
        return null;
    }
    public void Report(string text) => Status=text;
}
