// WoW Faction Lock Retrier - GUI version
// Whenever the "This faction is currently unavailable..." popup appears, it clicks Okay and retries Finish.
// When the popup no longer appears (character created), it stops and alerts you.
//
// Build (no SDK needed, uses the compiler that ships with Windows):  build.bat

using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("WoW Faction Lock Retrier")]
[assembly: System.Reflection.AssemblyProduct("WoW Faction Lock Retrier")]
[assembly: System.Reflection.AssemblyVersion("1.1.0.0")]

static class Native
{
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")]  public static extern uint GetPixel(IntPtr dc, int x, int y);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll")] public static extern bool FlashWindow(IntPtr h, bool invert);

    public static Color GetColor(Point p)
    {
        IntPtr dc = GetDC(IntPtr.Zero);
        uint c = GetPixel(dc, p.X, p.Y);
        ReleaseDC(IntPtr.Zero, dc);
        return Color.FromArgb((int)(c & 0xFF), (int)((c >> 8) & 0xFF), (int)((c >> 16) & 0xFF));
    }

    public static void ClickAt(Point p)
    {
        SetCursorPos(p.X, p.Y); Thread.Sleep(150);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero); Thread.Sleep(80);   // left down
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);                      // left up
    }

    public static void FocusWoW()
    {
        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                if (p.MainWindowTitle.IndexOf("World of Warcraft", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    SetForegroundWindow(p.MainWindowHandle); Thread.Sleep(300); return;
                }
            }
            catch { }
        }
    }
}

class MainForm : Form
{
    const int HOTKEY_ID = 1;
    const uint VK_F8 = 0x77;
    const int CountdownSec = 5;

    Point? okayPos, finishPos;
    Color okayColor;

    NumericUpDown numInterval, numMax, numTolerance;
    Button btnOkay, btnFinish, btnStart;
    Label lblOkay, lblFinish, lblStatus, lblAttempts;
    Panel swatch;
    ListBox log;
    CheckBox chkTop;

    Thread worker;
    volatile bool stopRequested;
    ManualResetEvent stopEvent = new ManualResetEvent(false);

    public MainForm()
    {
        Text = "WoW Faction Lock Retrier";
        Font = new Font("Segoe UI", 9F);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(10);
        TopMost = true;

        var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };

        // --- Step 1: calibration ---
        var cal = new GroupBox { Text = "1. Calibration", AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(8) };
        var calT = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Fill };
        var hint = new Label
        {
            AutoSize = true, MaximumSize = new Size(440, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 0, 3, 8),
            Text = "Run the game in Windowed or Windowed (Fullscreen) mode. Click Finish once by hand so the popup is open, " +
                   "then press 'Capture Okay' and hover the Okay button. Close the popup, press 'Capture Finish' and hover the Finish button."
        };
        calT.Controls.Add(hint, 0, 0); calT.SetColumnSpan(hint, 3);

        btnOkay = new Button { Text = "Capture Okay", Width = 130, Height = 28 };
        btnOkay.Click += delegate { CapturePoint(true); };
        swatch = new Panel { Width = 18, Height = 18, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(3, 8, 3, 3), BackColor = SystemColors.Control };
        lblOkay = new Label { Text = "not set", AutoSize = true, Margin = new Padding(3, 9, 3, 3) };
        calT.Controls.Add(btnOkay, 0, 1); calT.Controls.Add(swatch, 1, 1); calT.Controls.Add(lblOkay, 2, 1);

        btnFinish = new Button { Text = "Capture Finish", Width = 130, Height = 28 };
        btnFinish.Click += delegate { CapturePoint(false); };
        lblFinish = new Label { Text = "not set", AutoSize = true, Margin = new Padding(3, 9, 3, 3) };
        calT.Controls.Add(btnFinish, 0, 2); calT.Controls.Add(new Label { Width = 18 }, 1, 2); calT.Controls.Add(lblFinish, 2, 2);
        cal.Controls.Add(calT);

        // --- Step 2: settings ---
        var set = new GroupBox { Text = "2. Settings", AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(8) };
        var setT = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        numInterval = AddNum(setT, 0, "Seconds between attempts", 5, 3600, 30);
        numMax = AddNum(setT, 1, "Max attempts (0 = unlimited)", 0, 100000, 0);
        numTolerance = AddNum(setT, 2, "Color tolerance (0-255)", 0, 255, 40);
        chkTop = new CheckBox { Text = "Keep this window on top", Checked = true, AutoSize = true };
        chkTop.CheckedChanged += delegate { TopMost = chkTop.Checked; };
        setT.Controls.Add(chkTop, 0, 3); setT.SetColumnSpan(chkTop, 2);
        set.Controls.Add(setT);

        // --- Step 3: run ---
        var run = new GroupBox { Text = "3. Run", AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(8) };
        var runT = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        btnStart = new Button { Text = "Start", Width = 130, Height = 32, Enabled = false, Font = new Font(Font, FontStyle.Bold) };
        btnStart.Click += delegate { if (worker == null) StartRun(); else RequestStop(); };
        lblAttempts = new Label { Text = "Attempts: 0", AutoSize = true, Margin = new Padding(8, 10, 3, 3) };
        runT.Controls.Add(btnStart, 0, 0); runT.Controls.Add(lblAttempts, 1, 0);
        lblStatus = new Label { Text = "Calibrate both buttons to begin. F8 stops at any time.", AutoSize = true, MaximumSize = new Size(440, 0), Margin = new Padding(3, 6, 3, 6) };
        runT.Controls.Add(lblStatus, 0, 1); runT.SetColumnSpan(lblStatus, 2);
        log = new ListBox { Width = 440, Height = 140, IntegralHeight = false, HorizontalScrollbar = true };
        runT.Controls.Add(log, 0, 2); runT.SetColumnSpan(log, 2);
        run.Controls.Add(runT);

        root.Controls.Add(cal); root.Controls.Add(set); root.Controls.Add(run);
        Controls.Add(root);
    }

    static NumericUpDown AddNum(TableLayoutPanel t, int row, string text, int min, int max, int val)
    {
        var l = new Label { Text = text, AutoSize = true, Margin = new Padding(3, 6, 12, 3) };
        var n = new NumericUpDown { Minimum = min, Maximum = max, Value = val, Width = 80 };
        t.Controls.Add(l, 0, row); t.Controls.Add(n, 1, row);
        return n;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.RegisterHotKey(Handle, HOTKEY_ID, 0, VK_F8);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 && m.WParam.ToInt32() == HOTKEY_ID) RequestStop();   // WM_HOTKEY
        base.WndProc(ref m);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        RequestStop();
        Native.UnregisterHotKey(Handle, HOTKEY_ID);
        base.OnFormClosing(e);
    }

    void SetBusy(bool busy)
    {
        btnOkay.Enabled = btnFinish.Enabled = !busy;
        numInterval.Enabled = numMax.Enabled = numTolerance.Enabled = !busy;
    }

    // Countdown, then read the cursor position (and the Okay color).
    void CapturePoint(bool isOkay)
    {
        SetBusy(true); btnStart.Enabled = false;
        int left = CountdownSec;
        var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        lblStatus.Text = string.Format("Hover the {0} button... {1}", isOkay ? "Okay" : "Finish", left);
        timer.Tick += delegate
        {
            left--;
            if (left > 0) { lblStatus.Text = string.Format("Hover the {0} button... {1}", isOkay ? "Okay" : "Finish", left); return; }
            timer.Stop(); timer.Dispose();
            Point p = Cursor.Position;
            if (isOkay)
            {
                // Move the cursor away so we read the normal color, not the hover highlight
                Native.SetCursorPos(p.X, p.Y - 200); Thread.Sleep(400);
                okayPos = p; okayColor = Native.GetColor(p);
                swatch.BackColor = okayColor;
                lblOkay.Text = string.Format("({0}, {1})  RGB {2},{3},{4}", p.X, p.Y, okayColor.R, okayColor.G, okayColor.B);
                lblStatus.Text = "Okay captured. Close the popup, then capture Finish.";
            }
            else
            {
                finishPos = p;
                lblFinish.Text = string.Format("({0}, {1})", p.X, p.Y);
                lblStatus.Text = "Finish captured.";
                if (okayPos.HasValue && Matches(Native.GetColor(okayPos.Value), okayColor))
                    lblStatus.Text = "Warning: the Okay color is still visible. Is the popup closed? If it is, recapture Okay on the solid red part of the button.";
                else if (okayPos.HasValue)
                    lblStatus.Text = "Ready. Press Start (popup must be closed).";
            }
            SetBusy(false);
            btnStart.Enabled = okayPos.HasValue && finishPos.HasValue;
        };
        timer.Start();
    }

    bool Matches(Color a, Color b)
    {
        int tol = (int)numTolerance.Value;
        return Math.Abs(a.R - b.R) <= tol && Math.Abs(a.G - b.G) <= tol && Math.Abs(a.B - b.B) <= tol;
    }

    void StartRun()
    {
        if (Matches(Native.GetColor(okayPos.Value), okayColor))
        {
            MessageBox.Show(this, "The popup seems to be open right now (or the Okay color is not distinctive). Close the popup or recapture Okay, then try again.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        stopRequested = false; stopEvent.Reset();
        SetBusy(true);
        btnStart.Text = "Stop (F8)";
        lblAttempts.Text = "Attempts: 0";
        var cfg = new RunConfig
        {
            Okay = okayPos.Value, Finish = finishPos.Value, OkayColor = okayColor,
            IntervalMs = (int)numInterval.Value * 1000, MaxAttempts = (int)numMax.Value
        };
        worker = new Thread(() => RunLoop(cfg)) { IsBackground = true };
        worker.Start();
    }

    void RequestStop()
    {
        if (worker == null) return;
        stopRequested = true; stopEvent.Set();
    }

    class RunConfig { public Point Okay, Finish; public Color OkayColor; public int IntervalMs, MaxAttempts; }

    // Returns true if a stop was requested during the wait.
    bool Wait(int ms) { return stopEvent.WaitOne(ms); }

    void RunLoop(RunConfig c)
    {
        string result = "Stopped.";
        bool success = false;
        int attempt = 0;
        try
        {
            Status("Running...");
            while (!stopRequested)
            {
                attempt++;
                int a = attempt;
                Ui(delegate { lblAttempts.Text = "Attempts: " + a; });

                Native.FocusWoW();
                Native.ClickAt(c.Finish);
                Native.SetCursorPos(c.Finish.X, c.Finish.Y - 200);

                // Wait for the server response: does the popup appear within 10 s?
                bool blocked = false;
                for (int t = 0; t < 20 && !stopRequested; t++)
                {
                    if (Wait(500)) break;
                    if (Matches(Native.GetColor(c.Okay), c.OkayColor)) { blocked = true; break; }
                }
                if (stopRequested) break;

                if (!blocked)
                {
                    success = true;
                    result = "Popup did NOT appear - your character may have been created!";
                    Log(string.Format("Attempt #{0}: popup did not appear. Stopping.", attempt));
                    break;
                }

                Log(string.Format("Attempt #{0}: still locked, retrying in {1} s.", attempt, c.IntervalMs / 1000));
                Native.ClickAt(c.Okay);
                Native.SetCursorPos(c.Okay.X, c.Okay.Y - 200);

                if (c.MaxAttempts > 0 && attempt >= c.MaxAttempts) { result = "Maximum number of attempts reached."; break; }
                Status(string.Format("Waiting {0} s before the next attempt...", c.IntervalMs / 1000));
                if (Wait(c.IntervalMs)) break;
                Status("Running...");
            }
        }
        catch (Exception ex) { result = "Error: " + ex.Message; }

        Ui(delegate
        {
            worker = null;
            btnStart.Text = "Start";
            SetBusy(false);
            lblStatus.Text = result;
            Log(result);
            if (success)
            {
                Native.FlashWindow(Handle, true);
                new Thread(() => { for (int i = 0; i < 5; i++) { Console.Beep(880, 300); Thread.Sleep(150); } }) { IsBackground = true }.Start();
                Activate();
                MessageBox.Show(this, result + "\n\nCheck the game.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        });
    }

    void Ui(MethodInvoker a) { try { if (!IsDisposed) BeginInvoke(a); } catch (InvalidOperationException) { } }
    void Status(string s) { Ui(delegate { lblStatus.Text = s; }); }
    void Log(string s)
    {
        string line = string.Format("[{0:HH:mm:ss}] {1}", DateTime.Now, s);
        Ui(delegate { log.Items.Add(line); log.TopIndex = log.Items.Count - 1; });
    }
}

static class Program
{
    [STAThread]
    static void Main()
    {
        Native.SetProcessDPIAware();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}
