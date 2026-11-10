// CtlSpy —— 跟随鼠标的控件探针（配合 AutoInstall 脚本 DSL）
// 特点：不用拖拽、不用点，鼠标移到哪就显示那个控件的 控件ID / 类名 / 文本
//       适合弹窗、嵌套控件、Tab 页里那些 WinSnap 一次抓不全的场景
// 热键是【全局】的，不管当前哪个窗口在前台都有效：
//   F8 冻结/继续   F9 换下一层窗口（叠了覆盖层时用）
//   如果 F8 被别的软件占用，会自动换 F6/F4，界面左上角会显示实际生效的键
// 编译： csc /target:winexe /out:CtlSpy.exe CtlSpy.cs
// 命令行：CtlSpy.exe /probe <x> <y> [层号]    CtlSpy.exe /stack <x> <y>
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace CtlSpy
{
    internal static class N
    {
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern IntPtr ChildWindowFromPointEx(IntPtr h, POINT p, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr h, ref POINT p);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        public delegate bool EnumProc(IntPtr h, IntPtr l);
        public struct POINT { public int X, Y; }
        public struct RECT { public int Left, Top, Right, Bottom; }
        public const uint GA_ROOT = 2;
        public const uint CWP_SKIPINVISIBLE = 0x0001, CWP_SKIPDISABLED = 0x0002;
        public const uint WM_HOTKEY = 0x0312;
        public const uint VK_F4 = 0x73, VK_F6 = 0x75, VK_F7 = 0x76, VK_F8 = 0x77, VK_F9 = 0x78, VK_F10 = 0x79, VK_F11 = 0x7A;
    }

    internal class Hit
    {
        public IntPtr Root, Target;
        public string RootTitle = "", Cls = "", Text = "";
        public int Id, RelX, RelY;
        public override string ToString()
        {
            string t = Text.Length > 60 ? Text.Substring(0, 60) + "…" : Text;
            return string.Format("ID={0}  类={1}  文本={2}\r\n窗口={3}   相对坐标=({4},{5})", Id, Cls, t, RootTitle, RelX, RelY);
        }
    }

    public class SpyForm : Form
    {
        private const int HK_FREEZE = 0xA1, HK_NEXT = 0xA2;
        private Label lblInfo, lblHint, lblState;
        private Button btnFreeze, btnNext, btnCopyAll;
        private Timer timer;
        private Hit last = new Hit();
        private bool frozen;
        private int skip;
        private uint freezeKey, nextKey;

        [STAThread]
        public static void Main(string[] args)
        {
            if (args.Length >= 3 && args[0].ToLower() == "/probe")
            {
                try { Console.OutputEncoding = Encoding.UTF8; } catch { }
                int x = int.Parse(args[1]), y = int.Parse(args[2]);
                int sk = args.Length > 3 ? int.Parse(args[3]) : 0;
                Hit h = Probe(x, y, sk);
                if (h == null) { Console.WriteLine("该坐标没有窗口"); return; }
                Console.WriteLine(h.ToString());
                Console.WriteLine("---- 片段 ----");
                Console.WriteLine("SET_WIN '" + h.RootTitle + "'");
                Console.WriteLine("CLICK " + h.Id);
                if (h.Text.Length > 0) Console.WriteLine("CLICK_TEXT '" + h.Text + "'");
                Console.WriteLine("SET_TEXT " + h.Id + " PATH");
                return;
            }
            if (args.Length >= 3 && args[0].ToLower() == "/stack")
            {
                try { Console.OutputEncoding = Encoding.UTF8; } catch { }
                int sx = int.Parse(args[1]), sy = int.Parse(args[2]);
                int idx = 0;
                N.EnumWindows(delegate(IntPtr h, IntPtr l)
                {
                    N.RECT r;
                    if (!N.GetWindowRect(h, out r)) return true;
                    if (sx < r.Left || sx > r.Right || sy < r.Top || sy > r.Bottom) return true;
                    Console.WriteLine("#{0,-2} 可见={1,-5} 子控件={2,-4} 类={3,-28} 标题={4} rect=({5},{6})-({7},{8})",
                        idx++, N.IsWindowVisible(h), CountChildren(h), Cls_(h), Text_(h), r.Left, r.Top, r.Right, r.Bottom);
                    return true;
                }, IntPtr.Zero);
                return;
            }
            try { N.SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.Run(new SpyForm());
        }

        public SpyForm()
        {
            Text = "CtlSpy - 控件探针";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            TopMost = true;
            ClientSize = new Size(452, 236);
            StartPosition = FormStartPosition.Manual;
            Location = new Point(30, 30);
            Font = new Font("Microsoft YaHei UI", 9F);

            lblState = new Label();
            lblState.SetBounds(8, 6, 200, 18);
            lblState.Text = "跟随中";
            lblState.ForeColor = Color.SeaGreen;
            Controls.Add(lblState);

            lblHint = new Label();
            lblHint.SetBounds(150, 6, 294, 18);
            lblHint.Text = "热键注册中…";
            lblHint.TextAlign = ContentAlignment.MiddleRight;
            lblHint.ForeColor = Color.DimGray;
            Controls.Add(lblHint);

            lblInfo = new Label();
            lblInfo.SetBounds(8, 26, 436, 76);
            lblInfo.BorderStyle = BorderStyle.FixedSingle;
            lblInfo.BackColor = Color.FromArgb(252, 252, 240);
            lblInfo.Font = new Font("Consolas", 9F);
            Controls.Add(lblInfo);

            MkBtn("复制 CLICK id", 8, 108, 120, delegate { DoCopy("CLICK " + last.Id, "CLICK " + last.Id); });
            MkBtn("复制 CLICK_TEXT", 134, 108, 140, delegate
            {
                if (last.Text.Length == 0) { DoCopy("", "这个控件没有文本"); return; }
                DoCopy("CLICK_TEXT '" + last.Text + "'", "CLICK_TEXT '" + last.Text + "'");
            });
            MkBtn("复制 SET_TEXT", 280, 108, 164, delegate { DoCopy("SET_TEXT " + last.Id + " PATH", "SET_TEXT " + last.Id + " PATH"); });

            btnFreeze = MkBtn("冻结", 8, 142, 120, delegate { ToggleFreeze(); });
            btnNext = MkBtn("换下一层", 134, 142, 140, delegate { NextLayer(); });
            btnCopyAll = MkBtn("复制全部片段", 280, 142, 164, delegate
            {
                string s = "SET_WIN '" + last.RootTitle + "'\r\nCLICK " + last.Id + "\r\n";
                if (last.Text.Length > 0) s += "CLICK_TEXT '" + last.Text + "'\r\n";
                s += "SET_TEXT " + last.Id + " PATH";
                DoCopy(s, "已复制全部片段");
            });

            lblHint.Text = "正在注册全局热键…";
            HandleCreated += delegate { RegisterKeys(); };

            timer = new Timer();
            timer.Interval = 150;
            timer.Tick += delegate { Update_(); };
            timer.Start();
        }

        private Button MkBtn(string text, int x, int y, int w, EventHandler onClick)
        {
            Button b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, 26);
            b.Click += onClick;
            Controls.Add(b);
            return b;
        }

        // ---------------- 全局热键 ----------------
        private void RegisterKeys()
        {
            uint[] freeTry = { N.VK_F8, N.VK_F6, N.VK_F4 };
            uint[] nextTry = { N.VK_F9, N.VK_F10, N.VK_F11 };
            foreach (uint k in freeTry)
                if (N.RegisterHotKey(Handle, HK_FREEZE, 0, k)) { freezeKey = k; break; }
            foreach (uint k in nextTry)
                if (N.RegisterHotKey(Handle, HK_NEXT, 0, k)) { nextKey = k; break; }

            string fs = freezeKey == 0 ? "冻结键被占用" : KeyName(freezeKey) + " 冻结";
            string ns = nextKey == 0 ? "换层键被占用" : KeyName(nextKey) + " 换层";
            btnFreeze.Text = freezeKey == 0 ? "冻结(按钮)" : "冻结 (" + KeyName(freezeKey) + ")";
            btnNext.Text = nextKey == 0 ? "换下一层(按钮)" : "换下一层 (" + KeyName(nextKey) + ")";
            lblHint.Text = fs + " ｜ " + ns + "（全局有效）";
        }

        private static string KeyName(uint vk)
        {
            switch (vk)
            {
                case N.VK_F4: return "F4";
                case N.VK_F6: return "F6";
                case N.VK_F8: return "F8";
                case N.VK_F9: return "F9";
                case N.VK_F10: return "F10";
                case N.VK_F11: return "F11";
            }
            return "?";
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == (int)N.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HK_FREEZE) { ToggleFreeze(); return; }
                if (id == HK_NEXT) { NextLayer(); return; }
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (freezeKey != 0) N.UnregisterHotKey(Handle, HK_FREEZE);
            if (nextKey != 0) N.UnregisterHotKey(Handle, HK_NEXT);
            base.OnFormClosed(e);
        }

        // ---------------- 状态 ----------------
        private void ToggleFreeze()
        {
            frozen = !frozen;
            timer.Enabled = !frozen;
            lblState.Text = frozen ? "已冻结（画面不动，可安心复制）" : "跟随中";
            lblState.ForeColor = frozen ? Color.Firebrick : Color.SeaGreen;
            if (!frozen) Update_();
        }

        private void NextLayer()
        {
            skip++;
            lblState.Text = "换层：第 " + (skip + 1) + " 层";
            lblState.ForeColor = Color.DarkOrange;
            Update_();
        }

        private void DoCopy(string s, string tip)
        {
            if (s.Length == 0) return;
            try { Clipboard.SetText(s); lblHint.Text = "已复制：" + tip; }
            catch (Exception ex) { lblHint.Text = "复制失败：" + ex.Message; }
        }

        private void Update_()
        {
            N.POINT p;
            N.GetCursorPos(out p);
            if (p.X >= Left && p.X <= Right && p.Y >= Top && p.Y <= Bottom) return;  // 鼠标在自己身上，不抓
            Hit h = Probe(p.X, p.Y, skip);
            if (h == null) return;
            last = h;
            lblInfo.Text = h.ToString();
        }

        // ---------------- 核心 ----------------
        private static readonly string[] OVERLAY = {
            "SDL_app", "ShellHandwritingCanvas", "XamlExplorerHostIslandWindow",
            "WindowsDashboard", "RECORDER_NOTIFY_WND", "Progman", "WorkerW",
            "Microsoft.UI.Content.PopupWindowSiteBridge", "InputSiteWindowClass",
            "Microsoft.UI.Content.DesktopChildSiteBridge", "ForegroundStaging"
        };

        private static bool IsOverlay(IntPtr h)
        {
            string c = Cls_(h);
            foreach (string x in OVERLAY) if (c == x) return true;
            return false;
        }

        internal static IntPtr PickRoot(int x, int y, int skip)
        {
            IntPtr[] found = new IntPtr[32];
            int n = 0;
            N.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (!N.IsWindowVisible(h)) return true;
                N.RECT r;
                if (!N.GetWindowRect(h, out r)) return true;
                if (x < r.Left || x > r.Right || y < r.Top || y > r.Bottom) return true;
                if (IsOverlay(h)) return true;
                if (n < found.Length) found[n++] = h;
                return true;
            }, IntPtr.Zero);
            if (n == 0) return IntPtr.Zero;
            return found[skip % n];
        }

        internal static int CountChildren(IntPtr h)
        {
            int n = 0;
            N.EnumChildWindows(h, delegate(IntPtr c, IntPtr l) { n++; return true; }, IntPtr.Zero);
            return n;
        }

        internal static Hit Probe(int x, int y, int skip)
        {
            IntPtr root = PickRoot(x, y, skip);
            if (root == IntPtr.Zero) return null;

            N.POINT p = new N.POINT();
            p.X = x; p.Y = y;
            IntPtr cur = root;
            for (int i = 0; i < 16; i++)
            {
                N.POINT pc = p;
                N.ScreenToClient(cur, ref pc);
                IntPtr child = N.ChildWindowFromPointEx(cur, pc, N.CWP_SKIPINVISIBLE | N.CWP_SKIPDISABLED);
                if (child == IntPtr.Zero || child == cur) break;
                cur = child;
            }

            Hit hit = new Hit();
            hit.Root = root;
            hit.Target = cur;
            hit.RootTitle = Text_(root);
            hit.Cls = Cls_(cur);
            hit.Text = Text_(cur);
            hit.Id = N.GetDlgCtrlID(cur);
            N.RECT r;
            if (N.GetWindowRect(cur, out r)) { hit.RelX = r.Left; hit.RelY = r.Top; }
            N.RECT rr;
            if (N.GetWindowRect(root, out rr)) { hit.RelX -= rr.Left; hit.RelY -= rr.Top; }
            return hit;
        }

        private static string Cls_(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(256);
            N.GetClassName(h, sb, sb.Capacity);
            string s = sb.ToString();
            int dot = s.IndexOf(".app.");
            if (dot > 0) s = s.Substring(0, dot);
            return s;
        }

        private static string Text_(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(4096);
            N.GetWindowText(h, sb, sb.Capacity);
            return sb.ToString().Replace("\r", " ").Replace("\n", " ");
        }
    }
}
