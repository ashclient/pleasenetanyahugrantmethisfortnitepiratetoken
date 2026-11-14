using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
namespace TreasureLedger;

public sealed record SharedTotals(long Chests,Dictionary<string,int> Rewards);
public sealed class LinkedMember
{
    public string Id {get;set;}="";public string Name {get;set;}="";public long Chests {get;set;}
    public Dictionary<string,int> Rewards {get;set;}=[];public long Revision {get;set;}public long Seen {get;set;}public long Updated {get;set;}
}
public sealed class LinkedState
{
    public string GroupId {get;set;}="";public string GroupName {get;set;}="";public List<LinkedMember> Members {get;set;}=[];
    public string? InviteCode {get;set;}public string? AdminToken {get;set;}
}
public sealed class LinkConfiguration
{
    public string MemberId {get;set;}=Guid.NewGuid().ToString("N");
    public string Proof {get;set;}=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_');
    public string Name {get;set;}="Player";public long Revision {get;set;}
    public string? GroupId {get;set;}public string? InviteCode {get;set;}public string? AdminToken {get;set;}
    public LinkedState? Cache {get;set;}
}
public sealed class LinkClient
{
    public const string ServiceUrl="https://treasure-ledger-sync.ashhlogg.workers.dev/api/v1/";
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    readonly string path;readonly HttpClient http;readonly Uri endpoint;readonly SemaphoreSlim gate=new(1,1);
    DateTimeOffset lastRequest;bool blocked;
    public LinkConfiguration Config {get;private set;}
    public LinkedState? State=>Config.Cache;
    public bool IsLinked=>Config.GroupId is not null && !blocked;
    public bool IsOwner=>Config.AdminToken is not null;
    public string Status {get;private set;}="Not linked";
    public LinkClient(string folder,Uri? endpoint=null,HttpClient? http=null)
    {
        Directory.CreateDirectory(folder);path=Path.Combine(folder,"linked-counts.json");
        try { Config=File.Exists(path)?JsonSerializer.Deserialize<LinkConfiguration>(File.ReadAllText(path),Json)??new():new(); }
        catch { File.Copy(path,path+".damaged-"+DateTime.UtcNow.Ticks,true);Config=new();Status="Pairing settings could not be read; personal history is safe. Rejoin and ask the creator to remove the old member."; }
        this.endpoint=endpoint??new Uri(ServiceUrl);this.http=http??new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(20)};
        this.http.DefaultRequestHeaders.UserAgent.ParseAdd("TreasureLedger/0.5.3");
        Save();if(IsLinked)Status="Connecting · cached totals retained";
    }
    void Save(){string tmp=path+".tmp";File.WriteAllText(tmp,JsonSerializer.Serialize(Config,Json));File.Move(tmp,path,true);}
    public long? Combined(long personal)=>IsLinked?checked((State?.Members.Where(m=>m.Id!=Config.MemberId).Sum(m=>m.Chests)??0)+personal):null;
    object Snapshot(SharedTotals total)=>new {chests=total.Chests,rewards=total.Rewards,revision=++Config.Revision};
    async Task<LinkedState> Request(string route,object body,string? auth,CancellationToken token)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,new Uri(endpoint,route)){Content=JsonContent.Create(body,options:Json)};
        if(auth is not null)request.Headers.Authorization=new("Bearer",auth);
        using var response=await http.SendAsync(request,token);var text=await response.Content.ReadAsStringAsync(token);
        if(!response.IsSuccessStatusCode)
        {
            string? detail=null;
            try { using var document=JsonDocument.Parse(text);if(document.RootElement.TryGetProperty("error",out var error)&&error.ValueKind==JsonValueKind.String)detail=error.GetString(); }catch(JsonException){}
            // An HTML rejection from a proxy/retired host is not proof that membership was revoked.
            if(route.EndsWith("/sync",StringComparison.Ordinal)&&detail is not null&&(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound))blocked=true;
            string reason=detail is null ? "Server did not return a sync response. Check that everyone has the latest app." : detail.Length<=240?detail:detail[..240];
            if(endpoint.Host.EndsWith(".chatgpt.site",StringComparison.OrdinalIgnoreCase))reason="Outdated sync server. Install the latest Treasure Ledger on every PC.";
            throw new InvalidOperationException($"{reason} (HTTP {(int)response.StatusCode}, {endpoint.Host})");
        }
        lastRequest=DateTimeOffset.UtcNow;
        return JsonSerializer.Deserialize<LinkedState>(text,Json)??throw new InvalidDataException("Invalid sync response");
    }
    void Accept(LinkedState state)
    {
        Config.GroupId=state.GroupId;Config.Cache=state;blocked=false;
        if(state.InviteCode is not null)Config.InviteCode=state.InviteCode;
        if(state.AdminToken is not null)Config.AdminToken=state.AdminToken;
        Config.Revision=Math.Max(Config.Revision,state.Members.FirstOrDefault(m=>m.Id==Config.MemberId)?.Revision??0);
        Status=$"Synced {DateTime.Now:t} · {state.Members.Count} members";Save();
    }
    public async Task Create(string name,string groupName,SharedTotals total,CancellationToken token=default)
    {
        await gate.WaitAsync(token);try{
            if(IsLinked)throw new InvalidOperationException("Leave your current group before creating another");
            Config.Name=name.Trim();var body=new{memberId=Config.MemberId,proof=Config.Proof,name=Config.Name,groupName=groupName.Trim(),snapshot=Snapshot(total)};Save();
            Accept(await Request("groups/create",body,null,token));
        }finally{gate.Release();}
    }
    public async Task Join(string name,string code,SharedTotals total,CancellationToken token=default)
    {
        var parts=code.Trim().Split('.');if(parts.Length!=3||parts[0]!="TL1"||parts[1].Length!=22||parts[2].Length!=43||parts.Skip(1).Any(p=>p.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='_'&&c!='-')))throw new ArgumentException("Paste the complete invite code");
        await gate.WaitAsync(token);try{
            if(IsLinked)throw new InvalidOperationException("Leave your current group before joining another");
            Config.Name=name.Trim();Config.AdminToken=null;var body=new{memberId=Config.MemberId,proof=Config.Proof,name=Config.Name,snapshot=Snapshot(total)};Save();
            Accept(await Request($"groups/{parts[1]}/join",body,parts[2],token));
        }finally{gate.Release();}
    }
    public async Task Sync(SharedTotals total,CancellationToken token=default)
    {
        if(!IsLinked||DateTimeOffset.UtcNow-lastRequest<TimeSpan.FromSeconds(4)||!await gate.WaitAsync(0,token))return;
        try{
            var body=new{memberId=Config.MemberId,name=Config.Name,snapshot=Snapshot(total)};Save();
            Accept(await Request($"groups/{Config.GroupId}/sync",body,Config.Proof,token));
        }catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
        catch(Exception e){Status=blocked?e.Message:$"Sync paused · {e.Message} · cached totals retained";}
        finally{gate.Release();}
    }
    public void Rename(string name){if(string.IsNullOrWhiteSpace(name)||name.Trim().Length>32)throw new ArgumentException("Use a name from 1 to 32 characters");Config.Name=name.Trim();Save();}
    public async Task Leave(CancellationToken token=default)
    {
        await gate.WaitAsync(token);try{
            if(Config.GroupId is not null&&!blocked)await Request($"groups/{Config.GroupId}/"+(IsOwner?"close":"leave"),new{memberId=Config.MemberId},IsOwner?Config.AdminToken:Config.Proof,token);
            Config.GroupId=Config.InviteCode=Config.AdminToken=null;Config.Cache=null;blocked=false;Status="Not linked";Save();
        }finally{gate.Release();}
    }
    public async Task Remove(string member,CancellationToken token=default)
    {
        if(member==Config.MemberId)throw new InvalidOperationException("Use Close group to leave as creator");
        await gate.WaitAsync(token);try{Accept(await Request($"groups/{Config.GroupId}/remove",new{memberId=member},Config.AdminToken,token));}finally{gate.Release();}
    }
}
