// 还原自 AutoInstall.exe (auto_fix) —— Win32 窗口/控件/键鼠自动化
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace auto_fix.WinApi
{
    public static class WinInterac
    {
        // ---------------- P/Invoke ----------------
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindowW(string lpClassName, string lpWindowName);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr child, string cls, string win);
        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")]
        public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
        [DllImport("user32.dll")]
        public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern int GetDlgCtrlID(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern IntPtr GetParent(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern short VkKeyScan(char ch);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, StringBuilder lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);
        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        public delegate bool EnumWindowProc(IntPtr hWnd, IntPtr lParam);
        public delegate bool EnumThreadWindowsCallback(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern bool EnumChildWindows(IntPtr hWnd, EnumWindowProc cb, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern bool EnumThreadWindows(int tid, EnumThreadWindowsCallback cb, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")]
        public static extern IntPtr GetMessageExtraInfo();
        [DllImport("user32.dll")]
        public static extern bool OpenClipboard(IntPtr hWndNewOwner);
        [DllImport("user32.dll")]
        public static extern bool EmptyClipboard();
        [DllImport("user32.dll")]
        public static extern IntPtr SetClipboardData(uint fmt, IntPtr hMem);
        [DllImport("user32.dll")]
        public static extern IntPtr GetClipboardData(uint fmt);
        [DllImport("user32.dll")]
        public static extern bool CloseClipboard();
        [DllImport("kernel32.dll")]
        public static extern IntPtr GlobalLock(IntPtr hMem);
        [DllImport("kernel32.dll")]
        public static extern bool GlobalUnlock(IntPtr hMem);
        [DllImport("kernel32.dll")]
        public static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("kernel32.dll")]
        public static extern IntPtr GlobalFree(IntPtr hMem);

        public struct RECT { public int Left, Top, Right, Bottom; }
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        public class ControlInfo
        {
            public int Level;
            public int Id;
            public IntPtr Handle;
            public string ClassName;
            public string Text;
            public override string ToString()
            {
                return string.Format("层级{0}-ID:{1}-句柄:{2}-类:{3}-文本:{4}", Level, Id, Handle, ClassName, Text);
            }
        }

        // ---------------- 消息常量 ----------------
        public const uint WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;
        public const uint WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;
        public const uint WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_CHAR = 0x0102;
        public const uint WM_SETTEXT = 0x000C, WM_GETTEXT = 0x000D, WM_GETTEXTLENGTH = 0x000E;
        public const uint WM_COMMAND = 0x0111, WM_CLOSE = 0x0010;
        public const uint BM_CLICK = 0x00F5;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint CF_UNICODETEXT = 13, GMEM_MOVEABLE = 0x0002;

        public static int MAKELONG(int lo, int hi) { return (lo & 0xFFFF) | ((hi & 0xFFFF) << 16); }

        public static void MoChick(int x, int y, bool right)
        {
            SetCursorPos(x, y);
            mouse_event(right ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(right ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        }

        public static void ClickLeftButton(int x, int y) { MoChick(x, y, false); }
        public static void ClickRightButton(int x, int y) { MoChick(x, y, true); }

        public static void DoubleClick(int x, int y)
        {
            ClickLeftButton(x, y);
            System.Threading.Thread.Sleep(60);
            ClickLeftButton(x, y);
        }

        public static void MoveTo(int x, int y) { SetCursorPos(x, y); }

        // 用 SendMessage 在控件内部点一下（不移动物理鼠标）
        public static void PerformClick(IntPtr hWnd)
        {
            int lp = MAKELONG(5, 5);
            PostMessage(hWnd, WM_LBUTTONDOWN, (IntPtr)1, (IntPtr)lp);
            PostMessage(hWnd, WM_LBUTTONUP, (IntPtr)0, (IntPtr)lp);
            string cls = GetClassNameOf(hWnd);
            if (cls != null && cls.IndexOf("BUTTON", StringComparison.OrdinalIgnoreCase) >= 0)
                SendMessage(hWnd, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        }

        public static INPUT CreateMouseInput(int x, int y, uint flags)
        {
            INPUT i = new INPUT();
            i.type = 0;
            i.u.mi.dx = x; i.u.mi.dy = y;
            i.u.mi.dwFlags = flags;
            i.u.mi.dwExtraInfo = GetMessageExtraInfo();
            return i;
        }

        public static string GetClassNameOf(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(256);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string GetTextOf(IntPtr h)
        {
            int len = GetWindowTextLength(h);
            if (len <= 0) return string.Empty;
            StringBuilder sb = new StringBuilder(len + 2);
            GetWindowTextW(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string GetWindowText_SpecificUnicode(IntPtr h)
        {
            return GetTextOf(h);
        }

        // 递归枚举控件
        public static List<ControlInfo> GetAllControls(IntPtr parent)
        {
            List<ControlInfo> list = new List<ControlInfo>();
            EnumChildWindowsRecursive(parent, list, 0);
            return list;
        }

        private static void EnumChildWindowsRecursive(IntPtr parent, List<ControlInfo> list, int level)
        {
            EnumChildWindows(parent, delegate(IntPtr h, IntPtr l)
            {
                ProcessControl(h, level + 1, list);
                EnumChildWindowsRecursive(h, list, level + 1);
                return true;
            }, IntPtr.Zero);
        }

        private static void ProcessControl(IntPtr h, int level, List<ControlInfo> list)
        {
            ControlInfo ci = new ControlInfo();
            ci.Level = CalculateControlLevel(h);
            ci.Id = GetDlgCtrlID(h);
            ci.Handle = h;
            ci.ClassName = GetClassNameOf(h);
            ci.Text = GetTextOf(h);
            list.Add(ci);
        }

        public static int CalculateControlLevel(IntPtr h)
        {
            int level = 0;
            IntPtr p = GetParent(h);
            while (p != IntPtr.Zero) { level++; p = GetParent(p); }
            return level;
        }

        // ---------------- 剪贴板 ----------------
        public static bool SetClipboardText(string text)
        {
            for (int i = 0; i < 5; i++)
            {
                if (OpenClipboard(IntPtr.Zero)) break;
                System.Threading.Thread.Sleep(50);
            }
            try
            {
                EmptyClipboard();
                byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");
                IntPtr h = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
                if (h == IntPtr.Zero) return false;
                IntPtr p = GlobalLock(h);
                Marshal.Copy(bytes, 0, p, bytes.Length);
                GlobalUnlock(h);
                return SetClipboardData(CF_UNICODETEXT, h) != IntPtr.Zero;
            }
            catch (Exception ex) { Console.WriteLine("处理剪切板内容时出错: " + ex.Message); return false; }
            finally { CloseClipboard(); }
        }

        public static string GetClipboardText()
        {
            if (!OpenClipboard(IntPtr.Zero)) return string.Empty;
            try
            {
                IntPtr h = GetClipboardData(CF_UNICODETEXT);
                if (h == IntPtr.Zero) return string.Empty;
                IntPtr p = GlobalLock(h);
                string s = Marshal.PtrToStringUni(p);
                GlobalUnlock(h);
                return s ?? string.Empty;
            }
            finally { CloseClipboard(); }
        }

        // ---------------- 键盘 ----------------
        public static void PressKey(byte vk)
        {
            keybd_event(vk, 0, 0, UIntPtr.Zero);
            System.Threading.Thread.Sleep(30);
            keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        public static void SendTab() { PressKey(0x09); }
        public static void SendEnter() { PressKey(0x0D); }
        public static void SendDownArrow() { PressKey(0x28); }
        public static void SendSpace() { PressKey(0x20); }

        public static void SendKeyCombination(byte modifier, byte vk)
        {
            keybd_event(modifier, 0, 0, UIntPtr.Zero);
            PressKey(vk);
            keybd_event(modifier, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        public static void SendPaste()
        {
            if (SetClipboardText(GetClipboardText())) { }
            SendKeyCombination(0x11, 0x56);   // CTRL + V
        }

        public static void SendText(string text)
        {
            foreach (char c in text)
            {
                short vk = VkKeyScan(c);
                byte key = (byte)(vk & 0xFF);
                bool shift = ((vk >> 8) & 1) != 0;
                if (shift) keybd_event(0x10, 0, 0, UIntPtr.Zero);
                PressKey(key);
                if (shift) keybd_event(0x10, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
        }

        public static string GetWindowPosition(IntPtr h)
        {
            RECT r;
            if (!GetWindowRect(h, out r)) return "Failed to get window rectangle.";
            return string.Format("Window Position: X={0}, Y={1}, Width={2}, Height={3}",
                r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        }

        // ---------------- 按标题/类名/进程找窗口 ----------------
        public static IntPtr GetWindowHandle(string title)
        {
            IntPtr h = FindWindow(null, title);
            if (h == IntPtr.Zero) h = FindWindow(title, null);
            return h;
        }

        public static List<IntPtr> GetProcessIntPtrs(int pid)
        {
            List<IntPtr> res = new List<IntPtr>();
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                uint p;
                GetWindowThreadProcessId(h, out p);
                if (p == (uint)pid && IsWindowVisible(h)) res.Add(h);
                return true;
            }, IntPtr.Zero);
            return res;
        }

        public static IntPtr GetProcessIntPtr(int pid)
        {
            foreach (IntPtr h in GetProcessIntPtrs(pid))
                if (!string.IsNullOrEmpty(GetTextOf(h))) return h;
            return IntPtr.Zero;
        }

        public static List<Model.WindowInfo> GetTaskbarWindows()
        {
            List<Model.WindowInfo> list = new List<Model.WindowInfo>();
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (IsWindowVisible(h) && !string.IsNullOrEmpty(GetTextOf(h)))
                    list.Add(new Model.WindowInfo(h, GetTextOf(h)));
                return true;
            }, IntPtr.Zero);
            return list;
        }
    }
}
