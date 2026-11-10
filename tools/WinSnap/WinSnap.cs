// WinSnap —— 控件抓取工具（配合 AutoInstall 脚本 DSL）
// 编译： csc /target:winexe /out:WinSnap.exe WinSnap.cs
// 用法：
//   1) 双击运行 → 按住鼠标左键，把十字准星拖到目标安装窗口上松开 → 自动列出全部控件
//   2) 命令行： WinSnap.exe /dump <进程名|PID>   直接输出控件清单和脚本片段（便于批量/自动化）
//   3) 生成 SET_WIN / CLICK / CLICK_TEXT / SET_TEXT / CLICK_RELATIVE 片段，一键复制
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace WinSnap
{
    internal static class Native
    {
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
        public delegate bool EnumProc(IntPtr h, IntPtr l);
        public struct RECT { public int Left, Top, Right, Bottom; }
        public struct POINT { public int X, Y; }
        public const uint GA_ROOT = 2;
    }

    internal class Ctl
    {
        public int Level, Id;
        public IntPtr Handle;
        public string Cls = "", Text = "";
        public int RelX, RelY, W, H;
        public override string ToString()
        {
            return string.Format("层级{0} ID:{1,-6} 句柄:0x{2:X8} 类:{3,-28} 文本:{4}",
                Level, Id, Handle.ToInt64(), Cls, Text);
        }
        public bool IsButton { get { return Cls.IndexOf("BUTTON", StringComparison.OrdinalIgnoreCase) >= 0 || Cls.IndexOf("BitBtn", StringComparison.OrdinalIgnoreCase) >= 0; } }
        public bool IsEdit { get { return Cls.IndexOf("EDIT", StringComparison.OrdinalIgnoreCase) >= 0; } }
    }

    public class SnapForm : Form
    {
        private Label tip;
        private TextBox log;
        private Button btnCopy, btnDump, btnPick;
        private CheckBox chkOnlyInteractive;
        private IntPtr target = IntPtr.Zero;
        private string targetTitle = "";
        private List<Ctl> controls = new List<Ctl>();
        private string snippet = "";
        private bool dragging = false;
        private int dragStartX, dragStartY;

        [STAThread]
        public static void Main(string[] args)
        {
            if (args.Length >= 2 && args[0].ToLower() == "/dump")
            {
                DumpConsole(args[1]);
                return;
            }
            try { Native.SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.Run(new SnapForm());
        }

        public SnapForm()
        {
            Text = "WinSnap - 控件抓取（拖到安装窗口上松开）";
            ClientSize = new Size(860, 560);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);

            tip = new Label();
            tip.Text = "① 按住左键拖到目标窗口（不是点）  ② 松开后自动枚举  ③ 复制脚本片段";
            tip.Dock = DockStyle.Top;
            tip.Height = 28;
            tip.TextAlign = ContentAlignment.MiddleLeft;
            tip.Padding = new Padding(8, 0, 0, 0);
            tip.BackColor = Color.FromArgb(255, 244, 214);
            Controls.Add(tip);

            Panel bar = new Panel();
            bar.Dock = DockStyle.Top;
            bar.Height = 40;
            Controls.Add(bar);

            btnPick = new Button();
            btnPick.Text = "拖拽选取窗口";
            btnPick.SetBounds(8, 7, 120, 26);
            btnPick.MouseDown += BtnPick_MouseDown;
            btnPick.MouseUp += BtnPick_MouseUp;
            bar.Controls.Add(btnPick);

            btnDump = new Button();
            btnDump.Text = "抓前台窗口(3秒后)";
            btnDump.SetBounds(136, 7, 150, 26);
            btnDump.Click += delegate { GrabForegroundAfter(3000); };
            bar.Controls.Add(btnDump);

            chkOnlyInteractive = new CheckBox();
            chkOnlyInteractive.Text = "只看可交互控件(按钮/输入框)";
            chkOnlyInteractive.SetBounds(296, 9, 220, 22);
            chkOnlyInteractive.CheckedChanged += delegate { Render(); };
            bar.Controls.Add(chkOnlyInteractive);

            btnCopy = new Button();
            btnCopy.Text = "复制脚本片段";
            btnCopy.SetBounds(524, 7, 120, 26);
            btnCopy.Enabled = false;
            btnCopy.Click += delegate
            {
                if (snippet.Length > 0)
                {
                    try { Clipboard.SetText(snippet); tip.Text = "已复制到剪贴板（" + snippet.Split('\n').Length + " 行）"; }
                    catch (Exception ex) { MessageBox.Show("复制失败: " + ex.Message); }
                }
            };
            bar.Controls.Add(btnCopy);

            log = new TextBox();
            log.Multiline = true;
            log.ScrollBars = ScrollBars.Both;
            log.WordWrap = false;
            log.Dock = DockStyle.Fill;
            log.Font = new Font("Consolas", 9F);
            log.ReadOnly = true;
            log.BackColor = Color.White;
            Controls.Add(log);
            log.BringToFront();
        }

        // ---------------- 抓取 ----------------
        private void BtnPick_MouseDown(object sender, MouseEventArgs e)
        {
            dragging = true;
            tip.Text = "拖到目标窗口上松开…";
            Cursor = Cursors.Cross;
            btnPick.Capture = true;
            Native.POINT p;
            Native.GetCursorPos(out p);
            dragStartX = p.X; dragStartY = p.Y;
        }

        private void BtnPick_MouseUp(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            dragging = false;
            Cursor = Cursors.Default;
            btnPick.Capture = false;
            Native.POINT p;
            Native.GetCursorPos(out p);
            IntPtr root = PickRoot(p.X, p.Y);
            if (root == IntPtr.Zero) root = Native.WindowFromPoint(p);
            if (root == this.Handle)
            {
                tip.Text = "拖到一个**别的**程序的窗口上再松开";
                return;
            }
            GrabWindow(root);
        }

        private void GrabForegroundAfter(int ms)
        {
            Timer t = new Timer();
            t.Interval = ms;
            t.Tick += delegate
            {
                t.Stop();
                IntPtr h = GetForegroundWindowSafe();
                if (h != IntPtr.Zero && h != this.Handle) GrabWindow(h);
            };
            tip.Text = ms / 1000 + " 秒后抓取前台窗口，请先切到安装界面…";
            t.Start();
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        private IntPtr GetForegroundWindowSafe() { return GetForegroundWindow(); }

        private void GrabWindow(IntPtr root)
        {
            target = root;
            targetTitle = GetText(root);
            controls.Clear();
            Native.RECT rr;
            Native.GetWindowRect(root, out rr);
            Walk(root, rr.Left, rr.Top, 1);
            Render();
            btnCopy.Enabled = true;
            tip.Text = string.Format("已抓取：{0}  共 {1} 个控件", targetTitle, controls.Count);
        }

        private void Walk(IntPtr parent, int originX, int originY, int level)
        {
            Native.EnumChildWindows(parent, delegate(IntPtr h, IntPtr l)
            {
                Ctl c = new Ctl();
                c.Level = level;
                c.Handle = h;
                c.Id = Native.GetDlgCtrlID(h);
                c.Cls = GetClass(h);
                c.Text = GetText(h);
                Native.RECT r;
                if (Native.GetWindowRect(h, out r))
                {
                    c.RelX = r.Left - originX; c.RelY = r.Top - originY;
                    c.W = r.Right - r.Left; c.H = r.Bottom - r.Top;
                }
                controls.Add(c);
                Walk(h, originX, originY, level + 1);
                return true;
            }, IntPtr.Zero);
        }

        // 跳过远程会话/投屏类透明覆盖层，取真正在鼠标下方的顶层窗口
        private static readonly string[] OVERLAY = {
            "SDL_app", "ShellHandwritingCanvas", "XamlExplorerHostIslandWindow",
            "WindowsDashboard", "RECORDER_NOTIFY_WND", "Progman", "WorkerW",
            "Microsoft.UI.Content.PopupWindowSiteBridge", "InputSiteWindowClass",
            "Microsoft.UI.Content.DesktopChildSiteBridge", "ForegroundStaging"
        };

        private static IntPtr PickRoot(int x, int y)
        {
            IntPtr best = IntPtr.Zero;
            Native.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (!Native.IsWindowVisible(h)) return true;
                Native.RECT r;
                if (!Native.GetWindowRect(h, out r)) return true;
                if (x < r.Left || x > r.Right || y < r.Top || y > r.Bottom) return true;
                string cls = GetClass(h);
                foreach (string o in OVERLAY) if (cls == o) return true;
                best = h;
                return false;
            }, IntPtr.Zero);
            return best;
        }

        private static string GetClass(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(256);
            Native.GetClassName(h, sb, sb.Capacity);
            string s = sb.ToString();
            int dot = s.IndexOf(".app.");
            if (dot > 0) s = s.Substring(0, dot);      // WindowsForms10.BUTTON.app.0.xxx → WindowsForms10.BUTTON
            return s;
        }

        private static string GetText(IntPtr h)
        {
            int n = 4096;
            StringBuilder sb = new StringBuilder(n);
            Native.GetWindowText(h, sb, n);
            return sb.ToString().Replace("\r", " ").Replace("\n", " ");
        }

        // ---------------- 输出 ----------------
        private void Render()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("目标窗口: " + targetTitle + "   (句柄 0x" + target.ToInt64().ToString("X") + ")");
            sb.AppendLine(new string('-', 100));
            foreach (Ctl c in controls)
            {
                if (chkOnlyInteractive.Checked && !(c.IsButton || c.IsEdit)) continue;
                sb.AppendLine(c.ToString());
            }
            sb.AppendLine(new string('-', 100));
            snippet = BuildSnippet();
            sb.AppendLine("=== 生成的脚本片段（可直接粘到脚本里）===");
            sb.AppendLine(snippet);
            log.Text = sb.ToString();
        }

        private string BuildSnippet()
        {
            StringBuilder s = new StringBuilder();
            s.AppendLine("SET_WIN '" + targetTitle + "'");
            Dictionary<string, bool> used = new Dictionary<string, bool>();
            foreach (Ctl c in controls)
            {
                if (c.IsButton && c.Text.Length > 0)
                {
                    string key = c.Text;
                    if (used.ContainsKey(key)) continue;
                    used[key] = true;
                    s.AppendLine("CLICK_TEXT '" + key + "'        # ID " + c.Id);
                }
            }
            foreach (Ctl c in controls)
            {
                if (c.IsEdit)
                {
                    s.AppendLine("SET_TEXT " + c.Id + " PATH        # 输入框(ID " + c.Id + ", 类 " + c.Cls + ")  ← 填安装路径");
                    break;
                }
            }
            s.AppendLine("# 需要按坐标点时用（相对窗口左上角）：");
            int n = 0;
            foreach (Ctl c in controls)
            {
                if (!c.IsButton || c.Text.Length == 0) continue;
                s.AppendLine("# CLICK_RELATIVE " + (c.RelX + c.W / 2) + " " + (c.RelY + c.H / 2) + "   # " + c.Text);
                if (++n >= 5) break;
            }
            return s.ToString();
        }

        // ---------------- 命令行模式 ----------------
        private static void DumpConsole(string who)
        {
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
            IntPtr root = IntPtr.Zero;
            int pid;
            if (int.TryParse(who, out pid))
            {
                foreach (Process p in Process.GetProcesses())
                    if (p.Id == pid && p.MainWindowHandle != IntPtr.Zero) { root = p.MainWindowHandle; break; }
            }
            else
            {
                foreach (Process p in Process.GetProcessesByName(who.Replace(".exe", "")))
                    if (p.MainWindowHandle != IntPtr.Zero) { root = p.MainWindowHandle; break; }
            }
            if (root == IntPtr.Zero) { Console.WriteLine("没找到窗口: " + who); return; }
            Native.RECT rr;
            Native.GetWindowRect(root, out rr);
            List<Ctl> list = new List<Ctl>();
            Collect(root, rr.Left, rr.Top, 1, list);
            Console.WriteLine("目标窗口: " + GetText(root) + "  控件数: " + list.Count);
            foreach (Ctl c in list) Console.WriteLine(c.ToString());
            Console.WriteLine("---- 片段 ----");
            Console.WriteLine("SET_WIN '" + GetText(root) + "'");
            foreach (Ctl c in list)
                if (c.IsButton && c.Text.Length > 0) Console.WriteLine("CLICK_TEXT '" + c.Text + "'"); 
        }

        private static void Collect(IntPtr parent, int ox, int oy, int level, List<Ctl> list)
        {
            Native.EnumChildWindows(parent, delegate(IntPtr h, IntPtr l)
            {
                Ctl c = new Ctl { Level = level, Handle = h, Id = Native.GetDlgCtrlID(h), Cls = GetClass(h), Text = GetText(h) };
                Native.RECT r;
                if (Native.GetWindowRect(h, out r)) { c.RelX = r.Left - ox; c.RelY = r.Top - oy; c.W = r.Right - r.Left; c.H = r.Bottom - r.Top; }
                list.Add(c);
                Collect(h, ox, oy, level + 1, list);
                return true;
            }, IntPtr.Zero);
        }
    }
}
