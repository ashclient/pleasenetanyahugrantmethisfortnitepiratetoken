using System.Text.Json;
namespace TreasureLedger;

public sealed class DashboardActions
{
    public Action<string> SetLanguage {get;init;}=_=>{}; public Action Retry {get;init;}=()=>{};
    public Action SaveDiagnosis {get;init;}=()=>{}; public Action ReviewRewards {get;init;}=()=>{};
    public Action OpenData {get;init;}=()=>{}; public Action<bool> ReplayChecking {get;init;}=_=>{};
    public Action ChooseReplayFolder {get;init;}=()=>{};public Action ReleaseDisplay {get;init;}=()=>{};
    public Func<string,string,Task> Create {get;init;}=(_,_)=>Task.CompletedTask;
    public Func<string,string,Task> Join {get;init;}=(_,_)=>Task.CompletedTask;
    public Func<Task> Leave {get;init;}=()=>Task.CompletedTask;public Func<Task> Sync {get;init;}=()=>Task.CompletedTask;
    public Func<string,Task> Remove {get;init;}=_=>Task.CompletedTask;public Action<string> Rename {get;init;}=_=>{};
}
public sealed class LedgerGrid : DataGridView
{
    string signature="";
    public LedgerGrid(params string[] columns)
    {
        DoubleBuffered=true;Dock=DockStyle.Fill;BackgroundColor=Color.White;BorderStyle=BorderStyle.None;
        AllowUserToAddRows=false;AllowUserToDeleteRows=false;ReadOnly=true;RowHeadersVisible=false;
        SelectionMode=DataGridViewSelectionMode.FullRowSelect;MultiSelect=false;AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;
        EnableHeadersVisualStyles=false;ColumnHeadersHeight=40;ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        ColumnHeadersDefaultCellStyle=new(){BackColor=Color.FromArgb(238,242,247),ForeColor=Color.FromArgb(85,99,120),Font=new Font("Segoe UI",9,FontStyle.Bold),Padding=new Padding(8,0,0,0)};
        DefaultCellStyle=new(){BackColor=Color.White,ForeColor=Color.FromArgb(32,43,62),SelectionBackColor=Color.FromArgb(226,235,255),SelectionForeColor=Color.FromArgb(25,54,110),Padding=new Padding(8,0,0,0)};
        AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(249,250,253);GridColor=Color.FromArgb(238,241,246);RowTemplate.Height=36;
        foreach(var column in columns){int i=Columns.Add(column,column);Columns[i].SortMode=DataGridViewColumnSortMode.NotSortable;Columns[i].MinimumWidth=70;}
    }
    public void UpdateRows(List<(string Key,object[] Values)> incoming)
    {
        string next=JsonSerializer.Serialize(incoming.Select(r=>new{r.Key,r.Values}));if(next==signature)return;signature=next;
        string? selected=CurrentRow?.Tag as string;string? top=FirstDisplayedScrollingRowIndex>=0?Rows[FirstDisplayedScrollingRowIndex].Tag as string:null;int offset=HorizontalScrollingOffset;
        SuspendLayout();
        // Insert only when the order changes. Existing rows and their scroll state survive count updates.
        for(int i=0;i<incoming.Count;i++){
            var item=incoming[i];
            if(i>=Rows.Count || (string?)Rows[i].Tag!=item.Key){
                var existing=Rows.Cast<DataGridViewRow>().FirstOrDefault(r=>(string?)r.Tag==item.Key);
                if(existing is not null)Rows.Remove(existing);
                Rows.Insert(i,item.Values);Rows[i].Tag=item.Key;
            }else for(int c=0;c<item.Values.Length;c++)if(!Equals(Rows[i].Cells[c].Value,item.Values[c]))Rows[i].Cells[c].Value=item.Values[c];
        }
        while(Rows.Count>incoming.Count)Rows.RemoveAt(Rows.Count-1);
        if(selected is not null){var row=Rows.Cast<DataGridViewRow>().FirstOrDefault(r=>(string?)r.Tag==selected);if(row is not null)CurrentCell=row.Cells[0];}
        if(top is not null){var row=Rows.Cast<DataGridViewRow>().FirstOrDefault(r=>(string?)r.Tag==top);if(row is not null&&row.Visible)FirstDisplayedScrollingRowIndex=row.Index;}
        HorizontalScrollingOffset=offset;ResumeLayout();
    }
}
public sealed class Dashboard : Form
{
    static readonly Color Ink=Color.FromArgb(28,40,60),Muted=Color.FromArgb(103,117,138),Accent=Color.FromArgb(64,104,222);
    readonly DashboardActions actions;readonly Panel body=new(){Dock=DockStyle.Fill,Padding=new Padding(28),BackColor=Color.FromArgb(246,248,252)};
    readonly Dictionary<string,Control> pages=[];readonly Dictionary<string,Button> navigation=[];
    readonly LedgerGrid matches=new("Started (local time)","Chests","Broken","Rewards","State"),recent=new("Started (local time)","Chests","State"),rewards=new("Reward","All-time tracked","Current game","Linked all-time"),members=new("Member","Chests","Rewards","Last online");
    readonly Label current=Value(),personal=Value(),linked=Value(),phase=Note(),capture=Note(),replay=Note(),linkStatus=Note(),groupTitle=Note(),folder=Note(),hold=Note();
    readonly TextBox diagnostics=new(){Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,BorderStyle=BorderStyle.FixedSingle};
    readonly TextBox name=new(){Width=230,MaxLength=32},group=new(){Width=230,Text="My crew",MaxLength=40},code=new(){Width=430},invite=new(){Width=480,ReadOnly=true};
    readonly ComboBox language=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=230};
    readonly CheckBox replayEnabled=new(){Text="Check completed replays automatically",AutoSize=true},freeze=new(){Text="Pause diagnostic view",AutoSize=true};
    readonly Button release,create,join,leave,remove;bool updating,linkBusy,loadedName;DateTime nextDiagnostic;
    public Dashboard(DashboardActions actions)
    {
        this.actions=actions;Text="Treasure Ledger · Settings";Font=new Font("Segoe UI",10);ForeColor=Ink;
        Size=new Size(1120,780);MinimumSize=new Size(900,640);StartPosition=FormStartPosition.CenterScreen;AutoScaleMode=AutoScaleMode.Dpi;
        var sidebar=new Panel{Dock=DockStyle.Left,Width=190,BackColor=Color.FromArgb(22,32,51),Padding=new Padding(16,28,16,16)};
        Controls.Add(body);Controls.Add(sidebar);
        var nav=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false};sidebar.Controls.Add(nav);
        nav.Controls.Add(new Label{Text="TREASURE\nLEDGER",ForeColor=Color.White,Font=new Font("Segoe UI",17,FontStyle.Bold),Size=new Size(155,85)});
        foreach(var title in new[]{"Overview","Matches","Rewards","Linked counts","Settings","Diagnostics"}){
            var b=new Button{Text=title,TextAlign=ContentAlignment.MiddleLeft,Size=new Size(154,43),FlatStyle=FlatStyle.Flat,ForeColor=Color.FromArgb(192,204,225),Margin=new Padding(0,3,0,3),Padding=new Padding(12,0,0,0)};b.FlatAppearance.BorderSize=0;
            b.Click+=(_,_)=>ShowPage(title);navigation[title]=b;nav.Controls.Add(b);
        }
        var bottom=new Label{Dock=DockStyle.Bottom,Height=56,ForeColor=Color.FromArgb(153,171,200),Text="Alt+T  ·  overlay lock\nCounts saved on this PC",Font=new Font("Segoe UI",9)};sidebar.Controls.Add(bottom);
        var overview=Page("Overview","Your treasure, at a glance",out var overviewRoot);
        overviewRoot.RowStyles[2].Height=150;
        var cards=new TableLayoutPanel{Dock=DockStyle.Top,Height=138,ColumnCount=3,Margin=new Padding(0,12,0,20)};
        for(int i=0;i<3;i++)cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,33.333f));
        cards.Controls.Add(Card("CURRENT GAME",current));cards.Controls.Add(Card("ALL-TIME TRACKED",personal));cards.Controls.Add(Card("LINKED ALL-TIME",linked));overviewRoot.Controls.Add(cards,0,2);
        var overviewDetails=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5};overviewDetails.RowStyles.Add(new(SizeType.Absolute,45));overviewDetails.RowStyles.Add(new(SizeType.Absolute,54));overviewDetails.RowStyles.Add(new(SizeType.Absolute,32));overviewDetails.RowStyles.Add(new(SizeType.Percent,100));overviewDetails.RowStyles.Add(new(SizeType.Absolute,58));
        overviewDetails.Controls.Add(phase);overviewDetails.Controls.Add(hold);overviewDetails.Controls.Add(new Label{Text="RECENT MATCHES",ForeColor=Muted,Dock=DockStyle.Fill,Font=new Font("Segoe UI",9,FontStyle.Bold)});overviewDetails.Controls.Add(recent);overviewDetails.Controls.Add(replay);overviewRoot.Controls.Add(overviewDetails,0,3);
        var matchPage=Page("Matches","Every match, with replay corrections",out var mr);mr.RowStyles[2].Height=0;mr.Controls.Add(matches,0,3);
        var rewardPage=Page("Rewards","Saved discoveries across your games",out var rr);rr.Controls.Add(Row(Button("Review saved rewards",()=>actions.ReviewRewards())),0,2);rr.Controls.Add(rewards,0,3);
        Page("Linked counts","Pair with friends using a private invite code",out var lr);
        var linkContent=Stack();lr.Controls.Add(linkContent,0,3);lr.RowStyles[2].Height=0;
        linkContent.Controls.Add(groupTitle);linkContent.Controls.Add(linkStatus);
        linkContent.Controls.Add(Note("Only your display name and totals are shared. Offline members keep their last synced total.\nThe linked total includes you. Keep invite codes private."));
        linkContent.Controls.Add(Row(Field("Your display name",name),Button("Save name",()=>{actions.Rename(name.Text);_=RunLink(actions.Sync);} )));
        create=AsyncButton("Create group",()=>actions.Create(name.Text,group.Text));join=AsyncButton("Join group",()=>actions.Join(name.Text,code.Text));
        linkContent.Controls.Add(Row(Field("New group name",group),create));linkContent.Controls.Add(Row(Field("Invite code to join",code),join));
        linkContent.Controls.Add(Row(Field("Your group's invite code",invite),Button("Copy",()=>{if(invite.Text.Length>0)Clipboard.SetText(invite.Text);} )));
        var memberPanel=new Panel{Width=720,Height=220,Margin=new Padding(0,16,0,8)};memberPanel.Controls.Add(members);linkContent.Controls.Add(memberPanel);
        leave=AsyncButton("Leave group",actions.Leave);remove=AsyncButton("Remove selected member",()=>actions.Remove(members.CurrentRow?.Tag as string??""));
        linkContent.Controls.Add(Row(AsyncButton("Sync now",actions.Sync),remove,leave));
        Page("Settings","Recognition and replay preferences",out var sr);var settings=Stack();sr.Controls.Add(settings,0,3);sr.RowStyles[2].Height=0;
        language.Items.AddRange(["English","日本語 · Japanese"]);language.SelectedIndexChanged+=(_,_)=>{if(!updating)actions.SetLanguage(language.SelectedIndex==1?"ja-JP":"en-US");};
        settings.Controls.Add(Field("Fortnite HUD language",language));settings.Controls.Add(Note("Choose the language used inside Fortnite. English is the default for new installs."));
        replayEnabled.CheckedChanged+=(_,_)=>{if(!updating)actions.ReplayChecking(replayEnabled.Checked);};settings.Controls.Add(replayEnabled);settings.Controls.Add(folder);
        settings.Controls.Add(Row(Button("Choose replay folder",actions.ChooseReplayFolder),Button("Retry recognition",actions.Retry)));
        settings.Controls.Add(Note("Turn on Record Replays in Fortnite. Completed replays can add missed or destroyed chests.\nThe previous game stays visible through replay checking and switches on the next game's first chest.\nIf its replay is unavailable, you can release the held display below."));
        release=Button("Show current game · skip pending replay",actions.ReleaseDisplay);settings.Controls.Add(release);
        settings.Controls.Add(Row(Button("Open saved data",actions.OpenData),Button("Review saved rewards",actions.ReviewRewards)));
        settings.Controls.Add(Note("Use Windowed Fullscreen for capture. Alt+T unlocks the transparent overlay for moving it.\nMatch times use your Windows timezone automatically. Personal history stays on your PC."));
        Page("Diagnostics","Local recognition details and troubleshooting",out var dr);dr.Controls.Add(Row(Button("Save diagnosis",actions.SaveDiagnosis),Button("Retry recognition",actions.Retry),freeze),0,2);
        var diagnosticPanel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};diagnosticPanel.RowStyles.Add(new(SizeType.Absolute,100));diagnosticPanel.RowStyles.Add(new(SizeType.Percent,100));diagnosticPanel.Controls.Add(capture);diagnosticPanel.Controls.Add(diagnostics);dr.Controls.Add(diagnosticPanel,0,3);
        ShowPage("Overview");
    }
    Control Page(string title,string subtitle,out TableLayoutPanel root)
    {
        root=new(){Dock=DockStyle.Fill,ColumnCount=1,RowCount=4};root.ColumnStyles.Add(new(SizeType.Percent,100));root.RowStyles.Add(new(SizeType.Absolute,48));root.RowStyles.Add(new(SizeType.Absolute,38));root.RowStyles.Add(new(SizeType.Absolute,66));root.RowStyles.Add(new(SizeType.Percent,100));
        root.Controls.Add(new Label{Text=title,Font=new Font("Segoe UI",25,FontStyle.Bold),Dock=DockStyle.Fill},0,0);root.Controls.Add(Note(subtitle),0,1);pages[title]=root;body.Controls.Add(root);return root;
    }
    public void ShowPage(string title){foreach(var pair in pages)pair.Value.Visible=pair.Key==title;pages[title].BringToFront();foreach(var pair in navigation){pair.Value.BackColor=pair.Key==title?Color.FromArgb(47,65,97):Color.FromArgb(22,32,51);pair.Value.ForeColor=pair.Key==title?Color.White:Color.FromArgb(192,204,225);}}
    static Label Value()=>new(){Dock=DockStyle.Fill,Text="—",Font=new Font("Segoe UI",25,FontStyle.Bold),ForeColor=Accent,AutoEllipsis=true};
    static Label Note(string text="")=>new(){Text=text,AutoSize=false,Height=Math.Max(42,22*text.Split('\n').Length+12),Width=720,Dock=DockStyle.Top,ForeColor=Muted,AutoEllipsis=true,Margin=new Padding(0,8,0,8)};
    static Control Card(string title,Label value){var panel=new TableLayoutPanel{Dock=DockStyle.Fill,BackColor=Color.White,Padding=new Padding(15),RowCount=2,Margin=new Padding(0,8,12,16)};panel.RowStyles.Add(new(SizeType.Absolute,28));panel.RowStyles.Add(new(SizeType.Percent,100));panel.Controls.Add(new Label{Text=title,Dock=DockStyle.Fill,ForeColor=Muted,Font=new Font("Segoe UI",8,FontStyle.Bold)});panel.Controls.Add(value);return panel;}
    static FlowLayoutPanel Stack(){var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(0,2,12,12)};
        panel.SizeChanged+=(_,_)=>{int width=Math.Max(300,panel.ClientSize.Width-30);foreach(Control child in panel.Controls){if(child is Label label){label.Width=width;label.Height=Math.Max(42,TextRenderer.MeasureText(label.Text,label.Font,new Size(width,0),TextFormatFlags.WordBreak).Height+16);}else if(child is FlowLayoutPanel row)row.MaximumSize=new Size(width,0);else if(child is Panel)child.Width=width;}};return panel;}

    static FlowLayoutPanel Row(params Control[] controls){var row=new FlowLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=true,Margin=new Padding(0,6,0,6),MaximumSize=new Size(760,0)};row.Controls.AddRange(controls);return row;}
    static Control Field(string caption,Control input){var p=new FlowLayoutPanel{FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=true,Margin=new Padding(0,0,12,0)};p.Controls.Add(new Label{Text=caption,AutoSize=true,ForeColor=Muted,Margin=new Padding(0,0,0,5)});input.Margin=Padding.Empty;p.Controls.Add(input);return p;}
    static Button Button(string text,Action action){var b=new Button{Text=text,AutoSize=true,Height=36,MinimumSize=new Size(85,35),FlatStyle=FlatStyle.Flat,BackColor=Color.White,ForeColor=Ink,Padding=new Padding(9,4,9,4),Margin=new Padding(0,6,10,6)};b.FlatAppearance.BorderColor=Color.FromArgb(211,219,232);b.Click+=(_,_)=>{try{action();}catch(Exception e){MessageBox.Show(e.Message,"Treasure Ledger");}};return b;}
    Button AsyncButton(string text,Func<Task> action)=>Button(text,()=>_=RunLink(action));
    async Task RunLink(Func<Task> action){if(linkBusy)return;linkBusy=true;SetLinkButtons(false);try{await action();}catch(Exception e){MessageBox.Show(this,e.Message,"Linked counts",MessageBoxButtons.OK,MessageBoxIcon.Information);}finally{linkBusy=false;SetLinkButtons(true);}}
    void SetLinkButtons(bool enabled){create.Enabled=join.Enabled=leave.Enabled=remove.Enabled=enabled;}
    public void UpdateState(Ledger ledger,Tracker tracker,string message,List<Observation> latest,Preferences preferences,string replayMessage,LinkClient links,string replayFolder)
    {
        updating=true;language.SelectedIndex=preferences.Language=="ja-JP"?1:0;replayEnabled.Checked=preferences.ReplayChecking;updating=false;
        var totals=ledger.Totals;long own=totals.GetValueOrDefault("Chest");current.Text=tracker.DisplayedChests.ToString("N0");personal.Text=own.ToString("N0");linked.Text=links.Combined(own)?.ToString("N0")??"—";
        phase.Text=tracker.Phase;replay.Text=replayMessage;folder.Text="Replay folder: "+replayFolder;
        hold.Text=tracker.DisplayWaitingForReplay?"Previous game held · waiting for its completed replay. New game chests are being saved.":tracker.DisplayMatch?.Id!=tracker.Current?.Id&&tracker.DisplayMatch is not null?"Previous game retained · switches after replay checking and the next game's first chest.":"Current game / all-time tracked / linked all-time tracked";
        release.Enabled=ledger.PendingDisplayMatchId.HasValue;
        var rows=ledger.Matches.OrderByDescending(m=>m.Started).Select(m=>(m.Id.ToString(),new object[]{m.Started.LocalDateTime.ToString("g"),m.Chests,m.ReplayDestroyedCount?.ToString()??"—",m.Events.Count(x=>x.Kind!="Chest"&&!x.Excluded),m.ReplayCountLower?"Replay discrepancy":m.ReplayFragment?"Replay fragment":m.ReplaySessionId is not null?"Replay checked":m.Partial?"Partial":m.Ended.HasValue?"Finished":m==tracker.Current?"Current":"Previous run"})).ToList();matches.UpdateRows(rows);
        recent.UpdateRows(rows.Take(12).Select(r=>(r.Item1,new object[]{r.Item2[0],r.Item2[1],r.Item2[4]})).ToList());
        rewards.UpdateRows(Tracker.Rewards.Select(k=>(k,new object[]{k,totals.GetValueOrDefault(k),tracker.Current?.Events.Count(e=>e.Kind==k&&!e.Excluded)??0,links.IsLinked?(links.State?.Members.Where(m=>m.Id!=links.Config.MemberId).Sum(m=>m.Rewards.GetValueOrDefault(k))??0)+totals.GetValueOrDefault(k):"—"})).ToList());
        if(!loadedName){name.Text=links.Config.Name;loadedName=true;}groupTitle.Text=links.IsLinked?links.State?.GroupName??"Linked group":"Start a shared treasure ledger";linkStatus.Text=links.Status;invite.Text=links.IsLinked?links.Config.InviteCode??"":"";
        members.UpdateRows((links.State?.Members??[]).Select(m=>(m.Id,new object[]{m.Name+(m.Id==links.Config.MemberId?" (you)":""),m.Id==links.Config.MemberId?own:m.Chests,m.Rewards.Values.Sum(),DateTimeOffset.FromUnixTimeSeconds(m.Seen).LocalDateTime.ToString("g")})).ToList());
        if(!linkBusy){create.Enabled=join.Enabled=!links.IsLinked;leave.Enabled=links.Config.GroupId is not null;remove.Enabled=links.IsLinked&&links.IsOwner;}leave.Text=links.IsOwner?"Close group":"Leave group";
        if(!freeze.Checked&&DateTime.UtcNow>=nextDiagnostic){capture.Text=message;var text=string.Join(Environment.NewLine,latest.Select(x=>x.Text));if(diagnostics.Text!=text)diagnostics.Text=text;nextDiagnostic=DateTime.UtcNow.AddSeconds(1);}
    }
    internal LedgerGrid MatchGrid=>matches;
}
public static class DashboardSmoke
{
    public static void Run()
    {
        var ledger=new Ledger();for(int i=0;i<100;i++)ledger.Matches.Add(new(){Started=DateTimeOffset.UtcNow.AddHours(-i),Ended=DateTimeOffset.UtcNow.AddHours(-i).AddMinutes(25),Events=[new(DateTimeOffset.UtcNow,"Chest","test")]});
        var tracker=new Tracker(ledger);string path=Path.Combine(Path.GetTempPath(),"TreasureLedger-ui-"+Guid.NewGuid().ToString("N"));
        try{
            var links=new LinkClient(path);using var dashboard=new Dashboard(new());dashboard.Show();Application.DoEvents();
            void Update()=>dashboard.UpdateState(ledger,tracker,"Watching Fortnite · recognition active",[],new(),"Replay checked · 2 missed chests added",links,"C:\\Users\\Player\\AppData\\Local\\FortniteGame\\Saved\\Demos");
            Update();dashboard.ShowPage("Matches");Application.DoEvents();var grid=dashboard.MatchGrid;grid.CurrentCell=grid.Rows[45].Cells[0];grid.FirstDisplayedScrollingRowIndex=40;var selected=grid.CurrentRow!.Tag;int scroll=grid.FirstDisplayedScrollingRowIndex;
            for(int i=0;i<100;i++)Update();ledger.Matches[0].Events.Add(new(DateTimeOffset.UtcNow,"Chest","new chest"));Update();
            if(grid.FirstDisplayedScrollingRowIndex!=scroll||!Equals(grid.CurrentRow!.Tag,selected))throw new Exception("Settings scrolling or selection changed during tracking updates");
            foreach(string page in new[]{"Overview","Matches","Linked counts","Settings"}){dashboard.ShowPage(page);Application.DoEvents();using var image=new Bitmap(dashboard.Width,dashboard.Height);dashboard.DrawToBitmap(image,dashboard.ClientRectangle);image.Save("settings-"+page.Replace(" ","-").ToLowerInvariant()+".png");}
            dashboard.Size=dashboard.MinimumSize;dashboard.ShowPage("Settings");Application.DoEvents();using var compact=new Bitmap(dashboard.Width,dashboard.Height);dashboard.DrawToBitmap(compact,dashboard.ClientRectangle);compact.Save("settings-compact.png");
        }finally{try{Directory.Delete(path,true);}catch{}}
    }
}
