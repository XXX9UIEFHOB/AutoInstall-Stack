// 还原自 AutoInstall.exe (auto_fix) —— 脚本 DSL 引擎（49 关键字）
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using auto_fix.WinApi;

namespace auto_fix.Engine
{
    public class UnExpected : Exception { }

    public class EngineThread { }

    // 原样本中 ParsingCommand 继承 ParsingCommandEntity（IL: TypeDef#20 extends ParsingCommandEntity）
    public class ParsingCommand : ParsingCommandEntity
    {

        public bool ProcessCommand(string command)
        {
            PrintInfo.Add("执行命令：" + command);
            string cmd = command.Replace("%PATH%", installPath);
            if (Variables.Count > 0)
                foreach (KeyValuePair<string, string> kv in Variables.ToList())
                    cmd = cmd.Replace(kv.Key, kv.Value);
            cmd = cmd.Replace("%DISK%", installDisk);

            string[] args = ParseCommandLine(cmd).ToArray();
            if (args.Length == 0) return true;
            string name = args[0].ToUpper();

            // 条件块状态机
            if (name == "END_IF") { IsBranch = false; return true; }
            if (name == "ELSE") { Condition = (Condition == 0) ? 1 : 0; return true; }
            if (IsBranch && Condition == 0)
            {
                PrintInfo.Add("分支条件不满足，跳过下面命令");
                return true;
            }

            switch (name)
            {
                case "IF":
                    Condition = EvaluateCondition(args) ? 1 : 0;
                    IsBranch = true;
                    return true;

                case "SET_PATH":
                    if (args.Length >= 2) SetPath(args[1]);
                    else PrintInfo.Add("错误: 请输入需要设置的路径");
                    return true;

                case "WIN":
                    if (args.Length < 2) { PrintInfo.Add("错误: 请提供窗口标题"); return true; }
                    SetWorkWindow(args[1]);
                    return true;

                case "WIN_INFO":
                    CacheAllControls();
                    return true;

                case "SET_WIN":
                    if (args.Length < 2) { PrintInfo.Add("错误: 请提供窗口标题"); return true; }
                    SetWorkWindow(args[1]);
                    return true;

                case "SET_PROCESS":
                    if (args.Length < 2) { PrintInfo.Add("错误: 请提供进程名"); return true; }
                    SetProcessWindow(args[1]);
                    return true;

                case "WHILE_PROCESS":
                    if (args.Length < 2) { PrintInfo.Add("错误: 请提供进程名"); return true; }
                    WhileSelectProcess(args[1], args.Length > 2 ? args[2] : "20");
                    return true;

                case "WHILE_TEXT_P":
                    if (args.Length < 2) { PrintInfo.Add("错误: 请提供需要观察的文本"); return true; }
                    WhileProcessText(args[1], args.Length > 2 ? args[2] : "20");
                    return true;

                case "WHILE_WIN":
                case "WAIT_WIN":
                    if (args.Length < 2) { PrintInfo.Add("错误: 请提供窗口标题"); return true; }
                    WhileWin(args[1], args.Length > 2 ? args[2] : "10");
                    return true;

                case "WAIT_PROCESS":
                    if (args.Length < 2) { PrintInfo.Add("错误: 请提供进程名"); return true; }
                    WaitProcessEnd(args[1], args.Length > 2 ? args[2] : "60");
                    return true;

                case "WHILE_TEXT":
                    if (args.Length < 2) { PrintInfo.Add("错误: 请提供需要观察的文本"); return true; }
                    WhileText(args[1], args.Length > 2 ? args[2] : "10");
                    return true;

                case "READ_PACE":
                    if (args.Length < 3) { PrintInfo.Add("错误: 需要提供进度文本特征(正则表达式)"); return true; }
                    WaitReadProgress(args[1], args[2]);
                    return true;

                case "ZIP":
                    if (args.Length < 3) { PrintInfo.Add("错误: 请提供解压文件目录"); return true; }
                    ZipFile(args[1], args[2]);
                    return true;

                case "CLICK":
                    if (args.Length < 2) { PrintInfo.Add("错误: 请提供控件ID"); return true; }
                    ClickControl(args[1]);
                    return true;

                case "CLICK_TEXT":
                case "CLICK_T":
                    if (args.Length < 2) { PrintInfo.Add("错误: 请提供要点击的文本"); return true; }
                    ClickText(args[1]);
                    return true;

                case "CLICK_RELATIVE":
                case "CLICK_EX":
                    if (args.Length < 3) { PrintInfo.Add("错误: 请提供X和Y坐标"); return true; }
                    {
                        int x, y;
                        if (!int.TryParse(args[1], out x) || !int.TryParse(args[2], out y))
                        { PrintInfo.Add("错误: 坐标必须是数字"); return true; }
                        ClickRelative(x, y, name == "CLICK_EX");
                    }
                    return true;

                case "MOVE":
                    if (args.Length < 3) { PrintInfo.Add("错误: 请提供X和Y坐标"); return true; }
                    MoveMouse(args[1], args[2]);
                    return true;

                case "MOUSE_CLICK":
                    if (args.Length < 4) { PrintInfo.Add("错误: 请提供X和Y坐标"); return true; }
                    {
                        int x, y;
                        int.TryParse(args[2], out x);
                        int.TryParse(args[3], out y);
                        bool right = string.Equals(args[1], "RIGHT", StringComparison.OrdinalIgnoreCase);
                        WinInterac.MoChick(x, y, right);
                    }
                    return true;

                case "KEY":
                    if (args.Length < 2) { PrintInfo.Add("错误: 请提输入参数！"); return true; }
                    InputKey(args[1]);
                    return true;

                case "RUN":
                    if (args.Length < 2) { PrintInfo.Add("错误: 请提供要执行的文件路径"); return true; }
                    StartProcessWithoutWaiting(args.Length > 2 ? args[1].Replace("PATH", installPath) : args[1], args.Length > 2 ? args[2] : "");
                    return true;

                case "RUN_A":
                    if (args.Length < 2) { PrintInfo.Add("错误: 请提供要执行的文件路径"); return true; }
                    StartCmd(args[1], args.Length > 2 ? args[2] : "");
                    return true;

                case "RUN_INFO":
                    if (args.Length < 2) { PrintInfo.Add("错误: 请提供要执行的文件路径"); return true; }
                    MyRun(args[1], args.Length > 2 ? args[2] : "", args.Length > 3 ? args[3] : "");
                    return true;

                case "ADI":
                    {
                        string adi = @"C:\Program Files\Autodesk\AdODIS\V1\Installer.exe";
                        string xml = @"1_Setup\setup.xml";
                        if (args.Length > 1) adi = args[1];
                        if (args.Length > 2) xml = args[2];
                        string cfg = ADI_path(installPath, Path.Combine(GetValue("%RUN_PATH%"), xml));
                        PrintInfo.Add("配置参数：" + cfg);
                        MyRun(adi, cfg, "");
                    }
                    return true;

                case "ECHO":
                case "PRINT":
                    PrintInfoView(args.Length > 1 ? string.Join(" ", args, 1, args.Length - 1) : "");
                    return true;

                case "REPLACE_TXT":
                    if (args.Length < 4) { PrintInfo.Add("错误: 请提完整参数"); return true; }
                    {
                        string msg;
                        Tools.FixTool.ReplaceText(args[1], args[2], args[3], out msg);
                        PrintInfo.Add(msg);
                    }
                    return true;

                case "CLOSE":
                    if (args.Length < 2) { PrintInfo.Add("请提供需要关闭的窗口标题！"); return true; }
                    CloseWin(args[1]);
                    return true;

                case "SLEEP":
                    if (args.Length < 2) { PrintInfo.Add("错误: 请提供休眠时间(毫秒)"); return true; }
                    {
                        int ms;
                        if (!int.TryParse(args[1], out ms) || ms < 0) { PrintInfo.Add("错误: 休眠时间必须是正整数"); return true; }
                        Sleep(ms);
                    }
                    return true;

                case "COPY_FILE":
                case "FILE":
                    if (args.Length < 3) { PrintInfo.Add("错误: 请提供源文件和目标文件路径"); return true; }
                    CopyFile(args[1], args[2]);
                    return true;

                case "COPY_FILES":
                case "FILES":
                    if (args.Length < 3) { PrintInfo.Add("错误: 请提供源文件和目标文件路径"); return true; }
                    CopyFiles(args[1], args[2]);
                    return true;

                case "PASTE_TEXT":
                    if (args.Length < 2) { PrintInfo.Add("错误: 请提供要粘贴的文本"); return true; }
                    PasteText(args[1]);
                    return true;

                case "EXECUTE_SCRIPT":
                    if (args.Length < 2) { PrintInfo.Add("错误: 请提供脚本文件路径"); return true; }
                    ExecuteScriptFile(args[1]);
                    return true;

                case "READ_TEXT":
                    if (args.Length < 2) { PrintInfo.Add("错误: 请提供控件ID或类名"); return true; }
                    ReadText(args[1]);
                    return true;

                case "GET_TEXT":
                    if (args.Length < 2) { PrintInfo.Add("错误: 请提供控件ID或类名"); return true; }
                    ReadText(args[1], true);
                    return true;

                case "GET_CODE":
                    CacheAllControls();
                    return true;

                case "SET_TEXT":
                    if (args.Length < 3) { PrintInfo.Add("错误: 请提供控件ID和要设置的文本内容"); return true; }
                    SetTextbox(args[1], string.Join(" ", args, 2, args.Length - 2));
                    return true;

                case "KILL":
                    if (args.Length < 2) { PrintInfo.Add("错误: 请提进程名"); return true; }
                    {
                        string procName = args[1].Replace(".exe", "");
                        Process[] ps = Process.GetProcessesByName(procName);
                        if (ps.Length == 0) { PrintInfo.Add("进程 " + procName + " 已结束"); return true; }
                        foreach (Process p in ps)
                        {
                            try { p.Kill(); PrintInfo.Add("进程 " + procName + " 已结束"); }
                            catch { PrintInfo.Add("无法结束进程 " + procName + ": " + p.Id); }
                        }
                    }
                    return true;

                case "IS_FILE":
                    if (args.Length < 2) { PrintInfo.Add("错误: 提供路径信息"); return true; }
                    if (File.Exists(args[1])) PrintInfo.Add("条件判断: 文件存在 - " + args[1]);
                    else PrintInfo.Add("条件判断: 文件不存在 - " + args[1]);
                    return true;

                case "APP_PATH":
                    if (args.Length < 2) { PrintInfo.Add("错误: 请提供软件名"); return true; }
                    {
                        string p = Tools.FixTool.GetAppRunPath(args[1]);
                        string varName = args.Length > 2 && !string.IsNullOrEmpty(args[2]) ? args[2] : "%APP_PATH%";
                        if (p == null) { PrintInfo.Add("没有找到软件[" + args[1] + "]"); return true; }
                        Variables[varName] = p;
                        PrintInfo.Add("找到软件[" + args[1] + "]的目录：" + p);
                    }
                    return true;

                case "LINK":
                    if (args.Length < 2) { PrintInfo.Add("错误: 请提供软件名"); return true; }
                    {
                        string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), args[1] + ".lnk");
                        string target = Path.Combine(installPath, args[1] + ".exe");
                        string msg;
                        if (Tools.FixTool.CreateShortcut(lnk, target, installPath, out msg))
                            PrintInfo.Add("快捷方式创建成功: " + lnk);
                        else
                            PrintInfo.Add("快捷方式创建失败: " + msg);
                    }
                    return true;

                case "EXIT":
                    PrintInfo.Add("程序退出");
                    Environment.Exit(0);
                    return true;

                case "HELP":
                    ShowHelp("\n");
                    return true;

                default:
                    PrintInfo.Add("未知命令: " + name + "，输入 HELP 查看可用命令");
                    return true;
            }
        }

        public bool ExecuteScriptCommand(string command)
        {
            return ProcessCommand(command);
        }

        public string ExtractText(string source)
        {
            return source == null ? string.Empty : source.Trim();
        }

        public bool ExecuteScriptFile(string path)
        {
            if (!File.Exists(path)) { PrintInfo.Add("错误: 脚本文件不存在: " + path); return false; }
            bool ok = true;
            foreach (string line in File.ReadAllLines(path, Encoding.Default))
            {
                string s = line.Trim();
                if (s.Length == 0 || s.StartsWith("//") || s.StartsWith("#")) continue;
                try { if (!ProcessCommand(s)) ok = false; }
                catch (Exception ex) { PrintInfo.Add("执行脚本时出错: " + ex.Message); ok = false; }
            }
            PrintInfo.Add("脚本执行完成");
            return ok;
        }

        // 支持 '单引号' / "双引号" 的参数切分
        public static List<string> ParseCommandLine(string line)
        {
            List<string> args = new List<string>();
            if (string.IsNullOrEmpty(line)) return args;
            StringBuilder cur = new StringBuilder();
            char quote = '\0';
            bool has = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                    else cur.Append(c);
                }
                else if (c == '\'' || c == '"') { quote = c; has = true; }
                else if (c == ' ' || c == '\t')
                {
                    if (cur.Length > 0 || has) { args.Add(cur.ToString()); cur.Length = 0; has = false; }
                }
                else { cur.Append(c); has = true; }
            }
            if (cur.Length > 0 || has) args.Add(cur.ToString());
            return args;
        }
    }

    public class ParsingCommandEntity
    {
        public string installDisk = "C:\\";
        public string winTitle = string.Empty;
        public string winProcessName = string.Empty;
        private IntPtr _targetWindowHandle = IntPtr.Zero;
        public string textValue = string.Empty;
        public string installPath = "C:\\MySoftware";
        private List<string> printInfo = new List<string>();
        public bool isShowInfo = false;
        private List<WinInterac.ControlInfo> _controlsCache = new List<WinInterac.ControlInfo>();
        private Dictionary<string, string> _variables = new Dictionary<string, string>();
        private IntPtr previousPth = IntPtr.Zero;
        private Process installProcess;
        private DateTime startTime;

        // 条件块
        public bool IsBranch { get; set; }
        public int Condition { get; set; }

        public List<string> PrintInfo { get { return printInfo; } }
        public Dictionary<string, string> Variables { get { return _variables; } }

        public void SetRunPath(string path)
        {
            _variables["%RUN_PATH%"] = path ?? string.Empty;
        }

        public void SetPath(string sub)
        {
            string p = Path.Combine(installDisk, sub.TrimStart('\\', '/'));
            installPath = p;
            _variables["%PATH%"] = p;
            printInfo.Add("设置安装路径为：" + p);
        }

        public void SetPath(string disk, string sub)
        {
            installDisk = disk;
            SetPath(sub);
        }

        public void SetWorkWindow(string title)
        {
            winTitle = title;
            IntPtr h = WinInterac.GetWindowHandle(title);
            if (h == IntPtr.Zero) { printInfo.Add("未找到窗口: " + title); return; }
            _targetWindowHandle = h;
            printInfo.Add("找到目标窗口: " + title);
            printInfo.Add(string.Format("窗口句柄: 0x{0:X}", h.ToInt64()));
            WinInterac.RECT r;
            if (WinInterac.GetWindowRect(h, out r))
            {
                printInfo.Add(string.Format("窗口位置: ({0}, {1})", r.Left, r.Top));
                printInfo.Add(string.Format("窗口大小: {0} x {1}", r.Right - r.Left, r.Bottom - r.Top));
            }
            CacheAllControls();
        }

        public void SetWorkWindow(IntPtr h)
        {
            _targetWindowHandle = h;
        }

        public void SetProcessWindow(string processName)
        {
            winProcessName = processName.Replace(".exe", "");
            Process[] ps = Process.GetProcessesByName(winProcessName);
            if (ps.Length == 0) { printInfo.Add("未找到进程：" + processName); return; }
            IntPtr h = WinInterac.GetProcessIntPtr(ps[0].Id);
            if (h == IntPtr.Zero) { printInfo.Add("超时，没有找到目标进程: " + processName); return; }
            _targetWindowHandle = h;
            printInfo.Add("成功设置目标进程: " + processName);
            CacheAllControls();
        }

        public bool WhileSelectProcess(string processName, string seconds)
        {
            int sec = 20;
            int.TryParse(seconds, out sec);
            DateTime end = DateTime.Now.AddSeconds(sec);
            while (DateTime.Now < end)
            {
                Process[] ps = Process.GetProcessesByName(processName.Replace(".exe", ""));
                if (ps.Length > 0)
                {
                    IntPtr h = WinInterac.GetProcessIntPtr(ps[0].Id);
                    if (h != IntPtr.Zero) { _targetWindowHandle = h; CacheAllControls(); return true; }
                }
                System.Threading.Thread.Sleep(500);
            }
            printInfo.Add("超时，没有找到目标进程: " + processName);
            return false;
        }

        public void CacheAllControls()
        {
            if (_targetWindowHandle == IntPtr.Zero) { printInfo.Add("错误: 请先使用 SET_WIN 设置目标窗口"); return; }
            printInfo.Add("开始枚举窗口控件...");
            _controlsCache = WinInterac.GetAllControls(_targetWindowHandle);
            printInfo.Add(string.Format("控件枚举完成，共找到 {0} 个控件。", _controlsCache.Count));
            foreach (WinInterac.ControlInfo ci in _controlsCache)
                printInfo.Add(ci.ToString());
        }

        public bool WhileWin(string title, string seconds)
        {
            int sec = 10;
            int.TryParse(seconds, out sec);
            DateTime end = DateTime.Now.AddSeconds(sec);
            while (DateTime.Now < end)
            {
                IntPtr h = WinInterac.GetWindowHandle(title);
                if (h != IntPtr.Zero) { _targetWindowHandle = h; printInfo.Add("找到目标窗口：" + title); return true; }
                System.Threading.Thread.Sleep(300);
            }
            printInfo.Add("未找到目标窗口：" + title);
            return false;
        }

        public bool WhileText(string text, string seconds)
        {
            int sec = 10;
            int.TryParse(seconds, out sec);
            DateTime end = DateTime.Now.AddSeconds(sec);
            while (DateTime.Now < end)
            {
                foreach (WinInterac.ControlInfo ci in _controlsCache)
                    if (ci.Text != null && ci.Text.Contains(text)) { printInfo.Add("找到文本：" + text); return true; }
                CacheAllControls();
                System.Threading.Thread.Sleep(500);
            }
            printInfo.Add("超时，没有找到文字内容：" + text);
            return false;
        }

        public bool WhileTextEx(string text, string seconds, bool exact)
        {
            return WhileText(text, seconds);
        }

        public void ReadText(string idOrClass) { ReadText(idOrClass, false); }

        public void ReadText(string idOrClass, bool toPrint)
        {
            int id;
            if (int.TryParse(idOrClass, out id))
            {
                foreach (WinInterac.ControlInfo ci in _controlsCache)
                    if (ci.Id == id)
                    {
                        textValue = ci.Text;
                        WinInterac.SetClipboardText(ci.Text);
                        printInfo.Add("控件 '" + id + "' 的文本内容: " + ci.Text);
                        return;
                    }
                printInfo.Add(string.Format("错误: 未在缓存中找到控件ID为 {0} 的控件", id));
                return;
            }
            foreach (WinInterac.ControlInfo ci in _controlsCache)
                if (string.Equals(ci.ClassName, idOrClass, StringComparison.OrdinalIgnoreCase))
                {
                    textValue = ci.Text;
                    WinInterac.SetClipboardText(ci.Text);
                    printInfo.Add("控件 '" + idOrClass + "' 的文本内容: " + ci.Text);
                    return;
                }
            printInfo.Add("错误: 请提供控件ID或类名");
        }

        public void ClickControl(string idOrText)
        {
            int id;
            if (int.TryParse(idOrText, out id))
            {
                foreach (WinInterac.ControlInfo ci in _controlsCache)
                    if (ci.Id == id)
                    {
                        WinInterac.PerformClick(ci.Handle);
                        printInfo.Add(string.Format("成功点击控件ID: {0} (句柄: 0x{1:X}, 类名: {2})", id, ci.Handle.ToInt64(), ci.ClassName));
                        return;
                    }
                printInfo.Add(string.Format("错误: 未在缓存中找到控件ID为 {0} 的控件", id));
                return;
            }
            ClickText(idOrText);
        }

        public void ClickText(string text)
        {
            foreach (WinInterac.ControlInfo ci in _controlsCache)
                if (ci.Text != null && ci.Text.Contains(text))
                {
                    WinInterac.PerformClick(ci.Handle);
                    printInfo.Add("成功点击文本控件: " + text);
                    return;
                }
            printInfo.Add("错误: 在窗口中未找到包含文本 [ " + text + " ]的控件");
        }

        public void ClickRelative(int x, int y, bool sendMessage)
        {
            if (_targetWindowHandle == IntPtr.Zero) { printInfo.Add("错误: 请先使用 SET_WIN 设置目标窗口"); return; }
            WinInterac.RECT r;
            WinInterac.GetWindowRect(_targetWindowHandle, out r);
            if (sendMessage)
            {
                int lp = WinInterac.MAKELONG(x, y);
                WinInterac.PostMessage(_targetWindowHandle, WinInterac.WM_LBUTTONDOWN, (IntPtr)1, (IntPtr)lp);
                WinInterac.PostMessage(_targetWindowHandle, WinInterac.WM_LBUTTONUP, (IntPtr)0, (IntPtr)lp);
                printInfo.Add(string.Format("使用SendMessage在窗口相对位置({0}, {1})点击成功", x, y));
            }
            else
            {
                WinInterac.MoChick(r.Left + x, r.Top + y, false);
                printInfo.Add(string.Format("在窗口位置({0}, {1})点击", r.Left + x, r.Top + y));
            }
        }

        public void MoveMouse(string xs, string ys)
        {
            int x, y;
            int.TryParse(xs, out x);
            int.TryParse(ys, out y);
            WinInterac.RECT r;
            if (_targetWindowHandle != IntPtr.Zero && WinInterac.GetWindowRect(_targetWindowHandle, out r))
                WinInterac.MoveTo(r.Left + x, r.Top + y);
            else
                WinInterac.MoveTo(x, y);
        }

        public void InputKey(string key)
        {
            string k = key.Trim().ToUpper();
            switch (k)
            {
                case "TAB": WinInterac.SendTab(); break;
                case "ENTER": WinInterac.SendEnter(); break;
                case "SPACE": WinInterac.SendSpace(); break;
                case "DOWN": WinInterac.SendDownArrow(); break;
                case "PASTE": WinInterac.SendPaste(); break;
                case "CTRL_A": WinInterac.SendKeyCombination(0x11, 0x41); break;
                case "CTRL_C": WinInterac.SendKeyCombination(0x11, 0x43); break;
                case "CTRL_V": WinInterac.SendKeyCombination(0x11, 0x56); break;
                default:
                    int vk;
                    if (int.TryParse(k, out vk)) WinInterac.PressKey((byte)vk);
                    else printInfo.Add("未识别的按键 \"" + key + "\" ");
                    break;
            }
        }

        public bool StartProcessWithoutWaiting(string exe, string args)
        {
            try
            {
                Process p = new Process();
                p.StartInfo.FileName = exe;
                p.StartInfo.Arguments = args;
                p.StartInfo.UseShellExecute = true;
                p.Start();
                return true;
            }
            catch (Exception ex) { printInfo.Add("启动进程失败: " + ex.Message); return false; }
        }

        // 落盘 _i.bat 再执行（cd 到目标目录 + 命令）
        public void StartCmd(string command, string workDir)
        {
            string text = "cd \"" + workDir + "\"\r\n" + command;
            string bat = Path.Combine(string.IsNullOrEmpty(workDir) ? "." : workDir, "_i.bat");
            File.WriteAllText(bat, text, Encoding.Default);
            System.Threading.Thread.Sleep(100);
            if (!string.IsNullOrEmpty(workDir)) Tools.FixTool.SetCurrentWorkingDirectory(workDir);
            MyRun(bat, " ", workDir);
        }

        public void PrintInfoView(string text)
        {
            printInfo.Add(text);
            Console.WriteLine(text);
        }

        public void CloseWin(string title)
        {
            IntPtr h = WinInterac.GetWindowHandle(title);
            if (h == IntPtr.Zero) { printInfo.Add("未找到指定窗口"); return; }
            WinInterac.PostMessage(h, WinInterac.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            printInfo.Add("已关闭窗口: " + title);
        }

        public void Sleep(int ms)
        {
            System.Threading.Thread.Sleep(ms);
        }

        public void MouseClick(string button, int x, int y)
        {
            WinInterac.MoChick(x, y, string.Equals(button, "RIGHT", StringComparison.OrdinalIgnoreCase));
        }

        // Autodesk ODIS 静默安装参数
        public string ADI_path(string path, string xml)
        {
            string s = "-i install -d \"" + Path.GetFullPath(path).TrimEnd('\\') +
                       "\" --ui_mode basic -m \"" + Path.GetFullPath(xml).TrimEnd('\\') + "\"";
            return s.Replace("\"\"", "\"");
        }

        public void PasteText(string text)
        {
            if (_targetWindowHandle != IntPtr.Zero) WinInterac.SetForegroundWindow(_targetWindowHandle);
            WinInterac.SetClipboardText(text);
            WinInterac.SendKeyCombination(0x11, 0x56);
            printInfo.Add("文本粘贴成功: " + text);
        }

        public void SetTextbox(string idOrClass, string content)
        {
            string value = content;
            if (value == "__") value = WinInterac.GetClipboardText();
            if (value == "PATH" || value == "%PATH%") value = installPath;
            int id;
            WinInterac.ControlInfo target = null;
            if (int.TryParse(idOrClass, out id))
            {
                foreach (WinInterac.ControlInfo ci in _controlsCache) if (ci.Id == id) { target = ci; break; }
            }
            else
            {
                foreach (WinInterac.ControlInfo ci in _controlsCache)
                    if (string.Equals(ci.ClassName, idOrClass, StringComparison.OrdinalIgnoreCase)) { target = ci; break; }
            }
            if (target == null) { printInfo.Add("错误: 未在缓存中找到控件ID为 " + idOrClass + " 的控件"); return; }
            WinInterac.SendMessage(target.Handle, WinInterac.WM_SETTEXT, IntPtr.Zero, value);
            printInfo.Add("成功设置控件 '" + idOrClass + "' 的文本为: " + value);
        }

        public string GetValue(string key)
        {
            if (_variables.ContainsKey(key)) return _variables[key];
            return string.Empty;
        }

        public bool EvaluateCondition(string[] args)
        {
            if (args.Length < 3) return false;
            string kind = args[1].ToUpper();
            if (kind == "IS_FILE") return File.Exists(args[2]);
            if (kind == "IS_DIR") return Directory.Exists(args[2]);
            return string.Equals(args[1], args[2], StringComparison.OrdinalIgnoreCase);
        }

        public void ShowHelp(string pre)
        {
            string[] lines = {
                "== 命令帮助 ===",
                "SET_PATH [路径] - 设置安装路径，后续使用SET_TEXT PATH设置这个路径",
                "SET_WIN [标题/类名] - 设置目标窗口",
                "SET_PROCESS [进程名] - 设置目标进程窗口",
                "WHILE_WIN [标题/类名] [等待时间(s)] - 等待窗口出现，默认等待10秒",
                "WHILE_PROCESS [进程名] [等待时间(s)] - 等待进程窗口出现，默认等待20秒",
                "WHILE_TEXT [文本] [等待时间(s)]  - 等待窗口内出现某个文本内容，默认等待10秒",
                "WHILE_TEXT_P [文本] [等待时间(s)]  - 等待进程窗口内出现某个文本内容，默认等待10秒",
                "CLICK [控件ID/文本] - 点击窗口中的控件",
                "CLICK_RELATIVE [X] [Y] - 点击窗口内的相对坐标",
                "CLICK_EX [X] [Y] - 使用SendMessage方法向目标窗口发送点击消息（不移动物理鼠标）",
                "MOVE [X] [Y] - 移动鼠标到窗口内的相对坐标",
                "KEY [CODE] - 模拟键盘输入",
                "SLEEP [毫秒] - 程序休眠指定时间",
                "MOUSE_CLICK [LEFT|RIGHT] [X] [Y] - 在屏幕绝对坐标进行鼠标点击",
                "COPY_FILE [源路径] [目标路径] - 复制文件",
                "COPY_FILES [源路径] [目标路径] - 把源路径所有文件夹/文件复制到目标文件夹",
                "PASTE_TEXT [文本] - 粘贴文本到当前活动窗口",
                "EXECUTE_SCRIPT [文件路径] - 执行包含多条命令的脚本文件",
                "RUN [exe路径] (参数) - 运行指定的程序",
                "RUN_A [命令] [工作目录] - 写 _i.bat 到工作目录并执行（提权）",
                "RUN_INFO [程序路径] [启动参数] - 启动程序并附带参数，后续检测其运行",
                "ECHO/PRINT [文本] - 在窗口安装软件位置显示文本内容",
                "ADI [安装程序路径] [配置文件路径] - Autodesk安装程序专用，可为空",
                "READ_TEXT [控件ID/类名] - 读取指定控件的文本内容并复制到剪切板中",
                "SET_TEXT [控件ID] [文本] - 设置指定文本框的内容 关键字'__'为粘贴前面READ_TEXT读取的值，关键字'PATH'为设置的安装目录",
                "LIST_CONTROLS - 列出窗口内所有控件的ID和内容",
                "引入变量 \"%PATH%\":安装路径 和 \"%DISK%\"磁盘（例如：C:\\）",
                "%RUN_PATH%：安装包目录(由于后台设置)",
                "KILL [进程名] - 结束指定进程",
                "APP_PATH [软件名] [存放的变量名] - 查询开始菜单里面对应软件名的目录",
                "HELP - 显示此帮助信息",
                "EXIT - 退出程序",
                "================"
            };
            foreach (string s in lines) printInfo.Add(s);
        }

        public bool WhileProcessText(string text, string seconds)
        {
            return WhileText(text, seconds);
        }

        // 提权启动 + 轮询等待（上限 3400 秒）
        public void MyRun(string exe, string args, string workDir)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = exe;
            psi.WorkingDirectory = string.IsNullOrEmpty(workDir) ? Path.GetDirectoryName(exe) : workDir;
            psi.Arguments = args;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.Verb = "runas";
            Console.WriteLine("当前工作目录: " + Environment.CurrentDirectory);
            installProcess = Process.Start(psi);
            startTime = DateTime.Now;
            Console.WriteLine(string.Format("安装程序已启动 - 进程ID: {0}", installProcess.Id));
            Console.WriteLine(string.Format("启动时间: {0}", startTime));
            Console.WriteLine("开始监控安装进度...");
            int guard = 3400;
            installProcess.Refresh();
            while (!installProcess.HasExited && guard-- > 0)
            {
                TimeSpan ts = DateTime.Now - startTime;
                printInfo.Add(string.Format(" 安装进行中... 已运行: {0:F1}秒", ts.TotalSeconds));
                System.Threading.Thread.Sleep(1000);
            }
            TimeSpan total = DateTime.Now - startTime;
            Console.WriteLine(string.Format("\r运行时间: {0:F1}秒 | 进程状态: {1}", total.TotalSeconds,
                installProcess.HasExited ? "已结束" : "无响应"));
            if (installProcess.HasExited)
            {
                Console.WriteLine("\n安装程序执行完成!");
                Console.WriteLine(string.Format("退出代码: {0}", installProcess.ExitCode));
                Console.WriteLine(string.Format("总执行时间: {0:F2}秒", total.TotalSeconds));
            }
        }

        public string Sub(string text)
        {
            string s = text.Replace("%PATH%", installPath).Replace("%DISK%", installDisk);
            foreach (KeyValuePair<string, string> kv in _variables) s = s.Replace(kv.Key, kv.Value);
            return s;
        }

        public void CopyFile(string src, string dstDir)
        {
            string msg;
            if (Tools.FixTool.CopyFile(src, dstDir, out msg))
                printInfo.Add("文件复制成功！复制文件[" + src + "]到[" + Path.Combine(dstDir, Path.GetFileName(src)) + "]");
            else
                printInfo.Add("复制失败：" + msg);
        }

        public void CopyFiles(string srcDir, string dstDir)
        {
            if (Tools.FixTool.CopyFolder(srcDir, dstDir))
                printInfo.Add("解压成功！解压文件[" + srcDir + "]到[" + dstDir + "]");
            else
                printInfo.Add("解压失败！解压文件[" + srcDir + "]");
        }

        public void ZipFile(string zip, string dstDir)
        {
            string msg;
            if (Tools.ZipHelper.Unzip(zip, dstDir, out msg)) printInfo.Add("解压成功！解压文件[" + zip + "]");
            else printInfo.Add("解压失败！" + msg);
        }

        public void WaitProcessEnd(string processName, string seconds)
        {
            int sec = 60;
            int.TryParse(seconds, out sec);
            DateTime end = DateTime.Now.AddSeconds(sec);
            while (DateTime.Now < end)
            {
                if (Process.GetProcessesByName(processName.Replace(".exe", "")).Length == 0)
                { printInfo.Add("进程 " + processName + " 已结束。"); return; }
                System.Threading.Thread.Sleep(500);
            }
            printInfo.Add("正在等待进程 " + processName + " 结束...");
        }

        // 监控日志文件里的进度百分比（80 分钟超时）
        public void WaitReadProgress(string logPath, string pattern)
        {
            if (!File.Exists(logPath)) { printInfo.Add("日志文件不存在: " + logPath); return; }
            printInfo.Add("开始监控日志文件安装进度");
            DateTime end = DateTime.Now.AddMinutes(80);
            int last = -1;
            while (DateTime.Now < end)
            {
                try
                {
                    string line = ReadLastMatchedLine(logPath, pattern);
                    int v = Tools.FixTool.ParseProgressFromOutput(line);
                    if (v >= 0 && v != last) { last = v; printInfo.Add("当前进度：" + v + "%"); }
                    if (v >= 100) { printInfo.Add("安装完成（100%）"); return; }
                }
                catch (Exception ex) { printInfo.Add("读取日志异常：" + ex.Message); }
                System.Threading.Thread.Sleep(1000);
            }
            printInfo.Add("监控超时（80分钟）");
        }

        public static string ReadLastMatchedLine(string logPath, string pattern)
        {
            Regex re = new Regex(pattern);
            string last = string.Empty;
            using (FileStream fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader sr = new StreamReader(fs, Encoding.Default))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                    if (re.IsMatch(line)) last = line;
            }
            return last;
        }
    }
}
