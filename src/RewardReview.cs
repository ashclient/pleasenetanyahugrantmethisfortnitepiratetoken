namespace TreasureLedger;

internal sealed class RewardReview : Form
{
    readonly ListView list=new() {Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,MultiSelect=true,HideSelection=false};
    readonly Ledger ledger; readonly Action changed;
    public RewardReview(Ledger ledger,Action changed)
    {
        this.ledger=ledger;this.changed=changed;
        Text="Review recorded rewards";Size=new Size(1050,560);MinimumSize=new Size(740,400);StartPosition=FormStartPosition.CenterParent;
        list.Columns.Add("State",85);list.Columns.Add("Reward",140);list.Columns.Add("Recorded",165);list.Columns.Add("Recognition evidence",610);
        Controls.Add(list);
        var hint=new Label {Dock=DockStyle.Top,Height=42,Padding=new Padding(12,8,12,0),Text="Select a false reward entry and exclude it from totals. Excluded entries remain here and can be restored. Chest history is unchanged."};Controls.Add(hint);
        var buttons=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=52,Padding=new Padding(10)};
        var exclude=new Button {Text="Exclude selected",AutoSize=true};exclude.Click+=(_,_)=>SetExcluded(true);
        var restore=new Button {Text="Restore selected",AutoSize=true};restore.Click+=(_,_)=>SetExcluded(false);
        var close=new Button {Text="Close",AutoSize=true};close.Click+=(_,_)=>Close();
        buttons.Controls.AddRange([exclude,restore,close]);Controls.Add(buttons);RefreshRows();
    }
    void RefreshRows()
    {
        list.Items.Clear();
        foreach(var e in ledger.Matches.SelectMany(m=>m.Events).Where(e=>e.Kind!="Chest").OrderByDescending(e=>e.At))
            list.Items.Add(new ListViewItem([e.Excluded?"Excluded":"Counted",e.Kind,e.At.LocalDateTime.ToString("g"),e.Evidence]) {Tag=e});
    }
    void SetExcluded(bool excluded)
    {
        if(list.SelectedItems.Count==0) return;
        foreach(ListViewItem item in list.SelectedItems) ((LedgerEvent)item.Tag!).Excluded=excluded;
        changed();RefreshRows();
    }
}
