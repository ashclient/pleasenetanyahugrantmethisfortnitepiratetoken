using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace TreasureLedger;

// A single alpha-composited surface avoids opaque child labels and clipped text.
public sealed class OverlayWindow : Form
{
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern int SetWindowLong(IntPtr h, int i, int value);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll", SetLastError = true)] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dstDC, ref NativePoint dst, ref NativeSize size, IntPtr srcDC, ref NativePoint src, uint colorKey, ref BlendFunction blend, uint flags);
    [StructLayout(LayoutKind.Sequential)] struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct NativeSize { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BlendFunction { public byte Op, Flags, Alpha, Format; }
    readonly Dictionary<string, Bitmap> icons = [];
    readonly ToolTip hint = new() { InitialDelay = 200, AutoPopDelay = 12000 };
    List<(string Kind, string Value)> cells = [("Chest", "0 / 0"), ("Gizmo", "0"), ("Loading screen", "0"), ("Jam track", "0")];
    readonly List<RectangleF> cellBounds = [];
    Color stateColor = Color.FromArgb(245, 186, 91);
    bool locked = true;
    public event EventHandler? DetailsRequested;
    public IReadOnlyList<RectangleF> CellBounds => cellBounds;
    public bool InteractionLocked => locked;
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x80 | 0x08000000; return cp; }
    }
    public OverlayWindow()
    {
        Text = "Treasure Ledger"; FormBorderStyle = FormBorderStyle.None; TopMost = true;
        ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(320, 44);
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("TreasureLedger.Assets.icons.json")
            ?? throw new InvalidOperationException("Bundled Fortnite icons are missing.");
        foreach (var item in JsonSerializer.Deserialize<Dictionary<string, string>>(resource)!)
        {
            using var data = new MemoryStream(Convert.FromBase64String(item.Value));
            using var image = Image.FromStream(data); icons[item.Key] = new Bitmap(image);
        }
        Shown += (_, _) => { SetWindowDisplayAffinity(Handle, 0x11); Present(); };
    }
    public void SetClickThrough(bool enabled)
    {
        locked = enabled;
        int style = GetWindowLong(Handle, -20);
        SetWindowLong(Handle, -20, enabled ? style | 0x20 : style & ~0x20);
        if (Visible) Present();
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (locked || e.Button != MouseButtons.Left) return;
        if (e.Clicks > 1) { DetailsRequested?.Invoke(this, EventArgs.Empty); return; }
        ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
    }
    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); if (Visible) Present(); }
    public void UpdateCounts(Ledger ledger, MatchRecord? current, string status, string language)
    {
        var counts = ledger.Totals;
        cells = [("Chest", $"{current?.Chests ?? 0:N0} / {counts.GetValueOrDefault("Chest"):N0}")];
        foreach (string kind in Tracker.Rewards)
            if (kind is "Gizmo" or "Loading screen" or "Jam track" || counts.GetValueOrDefault(kind) > 0)
                cells.Add((kind, counts.GetValueOrDefault(kind).ToString("N0")));
        stateColor = !locked ? Color.FromArgb(190, 161, 255) : status == "Paused" ? Color.FromArgb(249, 118, 151)
            : status.StartsWith("Watching") ? Color.FromArgb(106, 230, 181) : Color.FromArgb(245, 186, 91);
        hint.SetToolTip(this, $"Chest: this match / total tracked\nReward icons: total tracked\n{status}\n{(language == "ja-JP" ? "Japanese" : "English")} HUD · Alt+T to {(locked ? "unlock" : "lock")} interaction\nUnlocked: drag to move, right-click for settings");
        if (Visible) Present();
    }
    // Rendering is shared by the live overlay and the executable's layout regression test.
    public Bitmap RenderFrame(float scale = 1)
    {
        using var measure = new Bitmap(1, 1);
        using var mg = Graphics.FromImage(measure);
        using var font = new Font("Segoe UI", 20, FontStyle.Bold, GraphicsUnit.Pixel);
        var sizes = cells.Select(cell => (float)Math.Ceiling(mg.MeasureString(cell.Value, font, int.MaxValue, StringFormat.GenericTypographic).Width) + 44).ToArray();
        float x = 10, y = 4, widest = 0;
        cellBounds.Clear();
        for (int i = 0; i < cells.Count; i++)
        {
            if (x > 10 && x + sizes[i] > 430) { widest = Math.Max(widest, x); x = 10; y += 40; }
            cellBounds.Add(new RectangleF(x, y, sizes[i], 36)); x += sizes[i] + 12;
        }
        widest = Math.Max(widest, x);
        int width = (int)Math.Ceiling((widest + 3) * scale), height = (int)Math.Ceiling((y + 40) * scale);
        var result = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(result);
        g.Clear(Color.Transparent); g.ScaleTransform(scale, scale);
        g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.CompositingQuality = CompositingQuality.HighQuality;
        for (int i = 0; i < cells.Count; i++)
        {
            var rect = cellBounds[i]; var item = cells[i];
            if (icons.TryGetValue(item.Kind, out var icon))
            {
                float ratio = Math.Min(32f / icon.Width, 32f / icon.Height);
                var dest = new RectangleF(rect.X + (32 - icon.Width * ratio) / 2, rect.Y + 2 + (32 - icon.Height * ratio) / 2, icon.Width * ratio, icon.Height * ratio);
                g.DrawImage(icon, dest);
            }
            using var path = new GraphicsPath();
            path.AddString(item.Value, font.FontFamily, (int)FontStyle.Bold, 20, new PointF(rect.X + 39, rect.Y + 4), StringFormat.GenericTypographic);
            // An outline keeps white counts legible over both snow and dark interiors.
            using var outline = new Pen(Color.FromArgb(235, 8, 12, 20), 3) { LineJoin = LineJoin.Round };
            g.DrawPath(outline, path); g.FillPath(Brushes.White, path);
        }
        using var dot = new SolidBrush(stateColor); g.FillEllipse(dot, 2, 18, 4, 4);
        return result;
    }
    internal void SetPreviewValues(string chest, string reward)
    {
        cells = [("Chest", chest)];
        cells.AddRange(Tracker.Rewards.Select(kind => (kind, reward)));
    }
    public void Present()
    {
        using var bitmap = RenderFrame(DeviceDpi / 96f);
        var screen = GetDC(IntPtr.Zero); var memory = CreateCompatibleDC(screen);
        var hBitmap = bitmap.GetHbitmap(Color.FromArgb(0)); var previous = SelectObject(memory, hBitmap);
        try
        {
            var dest = new NativePoint { X = Left, Y = Top }; var origin = new NativePoint();
            var size = new NativeSize { Width = bitmap.Width, Height = bitmap.Height };
            var blend = new BlendFunction { Alpha = 255, Format = 1 };
            if (!UpdateLayeredWindow(Handle, screen, ref dest, ref size, memory, ref origin, 0, ref blend, 2))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot draw transparent overlay.");
        }
        finally { SelectObject(memory, previous); DeleteObject(hBitmap); DeleteDC(memory); ReleaseDC(IntPtr.Zero, screen); }
    }
    protected override void Dispose(bool disposing)
    { if (disposing) { hint.Dispose(); foreach (var icon in icons.Values) icon.Dispose(); } base.Dispose(disposing); }
}

public sealed class InteractionHotkey : NativeWindow, IDisposable
{
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    public event EventHandler? Pressed;
    public InteractionHotkey()
    {
        CreateHandle(new CreateParams { Caption = "TreasureLedger.Hotkey", Parent = (IntPtr)(-3) });
        if (!RegisterHotKey(Handle, 1, 0x0001 | 0x4000, (uint)Keys.T))
        { DestroyHandle(); throw new Win32Exception(Marshal.GetLastWin32Error(), "Alt+T is already registered by another app. Close that app and restart Treasure Ledger."); }
    }
    protected override void WndProc(ref Message m)
    { if (m.Msg == 0x0312 && m.WParam == (IntPtr)1) Pressed?.Invoke(this, EventArgs.Empty); base.WndProc(ref m); }
    public void Dispose() { UnregisterHotKey(Handle, 1); DestroyHandle(); }
}
