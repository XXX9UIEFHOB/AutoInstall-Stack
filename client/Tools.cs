// 还原自 AutoInstall.exe (auto_fix) —— 工具层：路径/磁盘/指纹/快捷方式/复制
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace auto_fix.Tools
{
    public static class FixTool
    {
        public static bool IsNullOrWhiteSpace(string s) { return string.IsNullOrWhiteSpace(s); }
        public static bool IsNullOrEmpty(string s) { return string.IsNullOrEmpty(s); }
        public static string EmptyIfNull(string s) { return s ?? string.Empty; }

        public static string FormatBytes(double bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (bytes >= 1024 && i < units.Length - 1) { bytes /= 1024; i++; }
            return string.Format("{0:n2} {1}", bytes, units[i]);
        }

        // 打印全部固定磁盘信息
        public static StringBuilder WriteDriveInfo()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("计算机磁盘信息：");
            sb.AppendLine("==========================================");
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Fixed) continue;
                sb.AppendLine("盘符: " + d.Name);
                try
                {
                    sb.AppendLine("名称: " + d.VolumeLabel);
                    sb.AppendLine("磁盘空间: " + FormatBytes(d.TotalSize) + " / 剩余空间: " + FormatBytes(d.AvailableFreeSpace));
                    sb.AppendLine("文件系统: " + d.DriveFormat);
                }
                catch { sb.AppendLine("名称: 未知"); }
                sb.AppendLine("------------------------------------------");
            }
            return sb;
        }

        // 安装路径合法化：必须 X:\ 形式、拒绝目录遍历、拒绝非法字符
        public static string ValidateAndFormatInstallPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("路径不能为空");
            path = path.Trim().TrimEnd('\\', '/');
            if (!HasDriveLetter(path)) path = "C:\\" + path.TrimStart('\\', '/');
            if (!ValidatePathFormat(path)) throw new ArgumentException("路径格式无效: " + path);
            if (path.Contains("..")) throw new ArgumentException("路径包含可疑的目录遍历模式");
            if (path.Length > 200) throw new ArgumentException("路径长度超过系统限制");
            if (!ValidatePathCharacters(path)) throw new ArgumentException("路径包含非法字符: " + path);
            if (path.Split('\\').Length <= 2 && path.EndsWith(":\\")) { /* 允许纯盘符 */ }
            return path;
        }

        public static bool HasDriveLetter(string path)
        {
            return !string.IsNullOrEmpty(path) && Regex.IsMatch(path, @"^[A-Za-z]:");
        }

        public static bool ValidatePathFormat(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (!Regex.IsMatch(path, @"^[A-Za-z]:\\")) return false;
            if (Regex.IsMatch(path, @"[<>:""|?*]")) return false;
            return true;
        }

        public static bool ValidatePathCharacters(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            char[] bad = { '<', '>', ':', '"', '|', '?', '*' };
            foreach (char c in bad)
            {
                int i = path.IndexOf(c);
                if (i >= 0 && i != 1) return false;   // 允许 X: 的冒号
            }
            return true;
        }

        // 命令行切分：支持 '..' 与 ".."，与样本一致（保留引号）
        public static List<string> ParseCommandLine(string line)
        {
            List<string> args = new List<string>();
            if (line == null) return args;
            StringBuilder cur = new StringBuilder();
            char quote = '\0';
            foreach (char c in line)
            {
                if (quote != '\0')
                {
                    if (c == quote) { quote = '\0'; }
                    else cur.Append(c);
                }
                else if (c == '\'' || c == '"') { quote = c; }
                else if (c == ' ' || c == '\t')
                {
                    if (cur.Length > 0) { args.Add(cur.ToString()); cur.Length = 0; }
                }
                else cur.Append(c);
            }
            if (cur.Length > 0) args.Add(cur.ToString());
            return args;
        }

        public static void CopyDirectory(string src, string dst)
        {
            if (!Directory.Exists(src)) throw new DirectoryNotFoundException("源目录不存在: " + src);
            if (string.IsNullOrEmpty(dst)) throw new ArgumentException("目标目录路径不能为空");
            Directory.CreateDirectory(dst);
            foreach (string f in Directory.GetFiles(src))
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(src))
                CopyDirectory(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        public static bool CopyFolder(string src, string dst)
        {
            try
            {
                if (string.IsNullOrEmpty(src)) { Console.WriteLine("源目录路径不能为空"); return false; }
                if (string.IsNullOrEmpty(dst)) { Console.WriteLine("目标目录路径不能为空"); return false; }
                CopyDirectory(src, dst);
                return true;
            }
            catch (Exception ex) { Console.WriteLine("文件夹复制失败: " + ex.Message); return false; }
        }

        public static string FindProcessesLockingFile(string path)
        {
            return string.Empty;
        }

        // WinRAR 静默解压：WinRAR.exe x "<包>" "<目录>" -y
        public static bool WinrarFile(string rarPath, string dstDir, string winrarPath, out string message)
        {
            message = string.Empty;
            if (!File.Exists(winrarPath)) { message = "没有找到文件"; return false; }
            try
            {
                Process p = new Process();
                p.StartInfo.FileName = winrarPath;
                p.StartInfo.Arguments = "x \"" + rarPath + "\" \"" + dstDir + "\" -y";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.Start();
                p.WaitForExit();
                return p.ExitCode == 0;
            }
            catch (Exception ex) { message = ex.Message; return false; }
        }

        public static void MonitorProcess(Process p, Action<string> onProgress)
        {
            DateTime start = DateTime.Now;
            while (!p.HasExited)
            {
                if (onProgress != null) onProgress(string.Format("\r运行时间: {0:F1}秒 | 进程状态: {1}",
                    (DateTime.Now - start).TotalSeconds, "运行中"));
                System.Threading.Thread.Sleep(1000);
            }
        }

        public static int ParseProgressFromOutput(string line)
        {
            Match m = Regex.Match(line ?? "", @"(\d+)\s*%");
            if (m.Success)
            {
                int v;
                if (int.TryParse(m.Groups[1].Value, out v)) return v;
            }
            return -1;
        }

        // 启动进程并监控（MyStart）
        public static bool MyStart(string exe, string args, string workDir, out string message)
        {
            message = string.Empty;
            try
            {
                Process p = new Process();
                p.StartInfo.FileName = exe;
                p.StartInfo.Arguments = args;
                p.StartInfo.WorkingDirectory = workDir;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.Start();
                MonitorProcess(p, null);
                return p.ExitCode == 0;
            }
            catch (Exception ex) { message = ex.Message; return false; }
        }

        public static bool InsertContentAtPosition(string file, int position, string content, out string message)
        {
            message = string.Empty;
            try
            {
                string text = File.ReadAllText(file);
                if (position < 0 || position > text.Length) { message = "插入位置超出文件范围"; return false; }
                File.WriteAllText(file, text.Insert(position, content));
                return true;
            }
            catch (Exception ex) { message = "在位置 " + position + " 插入内容时发生错误: " + ex.Message; return false; }
        }

        public static bool ReplaceText(string file, string oldText, string newText, out string message)
        {
            message = string.Empty;
            try
            {
                File.WriteAllText(file, File.ReadAllText(file).Replace(oldText, newText));
                return true;
            }
            catch (Exception ex) { message = "替换文本时发生错误: " + ex.Message; return false; }
        }

        public static bool InsertAtBeginning(string file, string content, out string message)
        {
            return InsertContentAtPosition(file, 0, content, out message);
        }

        public static bool AppendAtEnd(string file, string content, out string message)
        {
            message = string.Empty;
            try { File.AppendAllText(file, content); return true; }
            catch (Exception ex) { message = "在文件末尾追加内容时发生错误: " + ex.Message; return false; }
        }

        // ---------------- 机器指纹 ----------------
        public static Data.RegistrationRequest GetInstallInfo()
        {
            Data.RegistrationRequest r = new Data.RegistrationRequest();
            r.UserIP = GetLocalIPAddress();
            r.ComputerName = Environment.MachineName;
            r.ComputerCode = GetUniqueIdentifier();
            r.InstallDate = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            return r;
        }

        public static string GetLocalIPAddress()
        {
            IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (IPAddress ip in host.AddressList)
                if (ip.AddressFamily == AddressFamily.InterNetwork) return ip.ToString();
            return "未找到IP地址";
        }

        // CPU ProcessorId + "-" + C: 卷序列号，去空格、转大写
        public static string GetUniqueIdentifier()
        {
            return (GetCpuId() + "-" + GetMainDiskSerial()).ToUpper().Replace(" ", "");
        }

        public static string GetCpuId()
        {
            try
            {
                foreach (ManagementObject mo in new ManagementClass("Win32_Processor").GetInstances())
                    return mo["ProcessorId"].ToString();
            }
            catch { }
            return string.Empty;
        }

        public static string GetMainDiskSerial()
        {
            try
            {
                ManagementObject mo = new ManagementObject("win32_logicaldisk.deviceid=\"C:\"");
                mo.Get();
                return mo["VolumeSerialNumber"].ToString();
            }
            catch { return string.Empty; }
        }

        // ---------------- 快捷方式 / 开始菜单 ----------------
        public static string ParseShortcut(string lnk)
        {
            if (!File.Exists(lnk)) { Console.WriteLine("错误：文件不存在 → " + lnk); return null; }
            if (!lnk.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) { Console.WriteLine("错误：不是 .lnk 快捷方式文件 → " + lnk); return null; }
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                string target = (string)sc.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, sc, null);
                return Path.GetDirectoryName(target);
            }
            catch (Exception ex) { Console.WriteLine("解析失败：" + ex.Message); return null; }
        }

        // 在开始菜单找 "<软件名>.lnk"，返回其目标目录
        public static string GetAppRunPath(string appName)
        {
            string all = "C:\\ProgramData\\Microsoft\\Windows\\Start Menu\\Programs";
            string dir = GetShortcut(appName + ".lnk", all);
            if (dir == null) dir = GetShortcut(appName + ".lnk", Environment.GetFolderPath(Environment.SpecialFolder.Programs));
            if (dir != null) dir = dir.TrimEnd('\\');
            return dir;
        }

        public static string GetShortcut(string lnkName, string root)
        {
            try
            {
                if (!Directory.Exists(root)) return null;
                foreach (string f in Directory.GetFiles(root, lnkName, SearchOption.AllDirectories))
                    return ParseShortcut(f);
            }
            catch (Exception ex) { Console.WriteLine("查找文件夹时发生错误: " + ex.Message); }
            return null;
        }

        public static bool SetCurrentWorkingDirectory(string dir)
        {
            try { Directory.SetCurrentDirectory(dir); return true; }
            catch (Exception ex) { Console.WriteLine("设置工作目录失败: " + ex.Message); return false; }
        }

        // 关键：在 exe 所在目录下找同名子目录（安装包目录），返回其完整路径
        public static string GetFolderPath(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
                foreach (string d in Directory.GetDirectories(baseDir))
                    if (new DirectoryInfo(d).Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return d;
                if (new DirectoryInfo(baseDir).Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return baseDir;
                return null;
            }
            catch (Exception ex) { Console.WriteLine("查找文件夹时发生错误: " + ex.Message); return null; }
        }

        public static int CalculatePercentage(double value, double total)
        {
            if (total <= 0) return 0;
            double p = value / total * 100.0;
            if (p < 0) return 0;
            if (p > 100) return 100;
            return (int)p;
        }

        public static List<string> GetValidInstallDrives()
        {
            List<string> list = new List<string>();
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Fixed) continue;
                if (!d.IsReady) continue;
                if (d.TotalSize - d.AvailableFreeSpace < 0) continue;
                list.Add(d.Name);
            }
            return list;
        }

        public static List<string> GetValidInstallDrives(double minGB)
        {
            List<string> all = GetValidInstallDrives();
            List<string> res = new List<string>();
            foreach (string s in all)
            {
                DriveInfo d = new DriveInfo(s);
                if (d.AvailableFreeSpace / 1024.0 / 1024 / 1024 >= minGB) res.Add(s);
            }
            return res;
        }

        // 选剩余空间最大的盘
        public static string MaxDrive()
        {
            string best = "C:\\";
            long bestFree = -1;
            foreach (string s in GetValidInstallDrives())
            {
                long free = new DriveInfo(s).AvailableFreeSpace;
                if (free > bestFree) { bestFree = free; best = s; }
            }
            return best;
        }

        public static bool IsSimilar(string a, string b, double threshold)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            if (threshold < 0 || threshold > 1) throw new ArgumentException("阈值必须在 0 到 1 之间");
            int dist = ComputeLevenshteinDistance(a, b);
            double sim = 1.0 - (double)dist / Math.Max(a.Length, b.Length);
            return sim >= threshold;
        }

        public static int ComputeLevenshteinDistance(string a, string b)
        {
            int[,] d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            return d[a.Length, b.Length];
        }

        public static string GetFileMD5(string file)
        {
            if (!File.Exists(file)) return string.Empty;
            using (FileStream fs = File.OpenRead(file))
            {
                using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
                {
                    byte[] h = md5.ComputeHash(fs);
                    StringBuilder sb = new StringBuilder();
                    foreach (byte x in h) sb.Append(x.ToString("x2"));
                    return sb.ToString();
                }
            }
        }

        // 注意：第二个参数是"目标目录"，不是目标文件名
        public static bool CopyFile(string srcFile, string dstDir, out string message)
        {
            message = string.Empty;
            try
            {
                string dst = Path.Combine(dstDir, Path.GetFileName(srcFile));
                if (!File.Exists(srcFile)) { Console.WriteLine("源文件不存在！"); return false; }
                if (!Directory.Exists(dstDir)) Directory.CreateDirectory(dstDir);
                File.Copy(srcFile, dst, true);
                Console.WriteLine("文件复制成功！");
                return true;
            }
            catch (Exception ex) { message = ex.Message; Console.WriteLine("复制失败：" + ex.Message); return false; }
        }

        public static bool VerifyFileIntegrity(string file, string md5)
        {
            string actual = GetFileMD5(file);
            return !string.IsNullOrEmpty(actual) && string.Equals(actual, md5, StringComparison.OrdinalIgnoreCase);
        }

        // 解析脚本首行元数据：#filePath=..;md5=..;appName=..;computer=strict;verify=..
        public static int AnalysisFistLineMD5(string firstLine, string installFolder)
        {
            if (string.IsNullOrEmpty(firstLine)) return 1;
            string s = firstLine.Length > 1 ? firstLine.Substring(1).Trim() : string.Empty;
            MatchCollection ms = Regex.Matches(s, @"(\w+)=([^;]+)");
            string filePath = "", md5 = "", appName = "", computer = "", verify = "";
            foreach (Match m in ms)
            {
                string k = m.Groups[1].Value;
                string v = m.Groups[2].Value.Trim();
                if (k == "filePath") filePath = v;
                else if (k == "md5") md5 = v;
                else if (k == "appName") appName = v;
                else if (k == "computer") computer = v;
                else if (k == "verify") verify = v;
            }
            try
            {
                if (filePath != "" && md5 != "")
                {
                    string full = Path.Combine(installFolder ?? "", filePath);
                    if (!VerifyFileIntegrity(full, md5)) return 0;
                }
                if (appName != "")
                {
                    string runPath = GetAppRunPath(appName);
                    if (!string.IsNullOrEmpty(runPath))
                        System.Windows.Forms.MessageBox.Show("检测到已经安装软件：" + appName);
                }
                if (computer == "strict")
                {
                    string mac = Environment.MachineName;
                    if (Regex.IsMatch(mac, @"[\u4e00-\u9fa5]"))
                        System.Windows.Forms.MessageBox.Show("警告：当前计算机名称存在中文字符，不符合软件安装要求！\n当前名称：" + mac);
                }
                if (filePath != "" && verify != "")
                {
                    string full = Path.Combine(installFolder ?? "", filePath);
                    if (!FileFingerprint.VerifyFileIntegrity_new(full, verify)) return 0;
                }
            }
            catch { return 0; }
            return 1;
        }

        // 用 WScript.Shell 创建 .lnk
        public static bool CreateShortcut(string lnkPath, string target, string workDir, out string message)
        {
            message = string.Empty;
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                sc.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
                sc.GetType().InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { workDir });
                sc.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
                Console.WriteLine("快捷方式创建成功: " + lnkPath);
                return true;
            }
            catch (Exception ex) { message = ex.Message; Console.WriteLine("快捷方式创建失败: " + ex.Message); return false; }
        }
    }

    // FNV1a-64 快速指纹（与样本 FileFingerprint/FileStableQuickFingerprint 等价）
    public static class FileFingerprint
    {
        private const ulong Offset = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        public static string ComputeFNV1a64Hex(byte[] data)
        {
            ulong h = Offset;
            foreach (byte b in data) { h ^= b; h *= Prime; }
            return h.ToString("X16");
        }

        public static string GenerateShortFingerprint(string file)
        {
            if (!File.Exists(file)) return string.Empty;
            return ComputeFNV1a64Hex(SampleFileContent(file, 64 * 1024));
        }

        public static string GenerateFingerprint(string file) { return GenerateShortFingerprint(file); }
        public static string GenerateConsistentFingerprint(string file) { return GenerateShortFingerprint(file); }
        public static string GenerateDetailedFingerprint(string file) { return GenerateShortFingerprint(file); }

        public static byte[] SampleFileContent(string file, int bytes)
        {
            using (FileStream fs = File.OpenRead(file))
            {
                int n = (int)Math.Min(bytes, fs.Length);
                byte[] buf = new byte[n];
                fs.Read(buf, 0, n);
                return buf;
            }
        }

        public static bool AreFilesIdentical(string a, string b)
        {
            return GenerateShortFingerprint(a) == GenerateShortFingerprint(b);
        }

        public static bool VerifyFileIntegrity(string file, string fingerprint)
        {
            return string.Equals(GenerateShortFingerprint(file), fingerprint, StringComparison.OrdinalIgnoreCase);
        }

        // 样本里 LoadProductData 用的就是这个（8 字节采样 + FNV1a）
        public static bool VerifyFileIntegrity_new(string file, string fingerprint)
        {
            string fp = FileStableQuickFingerprint.GenerateStableQuickFingerprint(file);
            return !string.IsNullOrEmpty(fp) && string.Equals(fp, fingerprint, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static class FileStableQuickFingerprint
    {
        public static string GenerateStableQuickFingerprint(string file)
        {
            if (!File.Exists(file)) return string.Empty;
            using (FileStream fs = File.OpenRead(file))
            {
                int n = (int)Math.Min(8192, fs.Length);
                byte[] buf = new byte[n];
                fs.Read(buf, 0, n);
                return UpdateFNV1aWithBytes(buf);
            }
        }

        public static string UpdateFNV1aWithBytes(byte[] data)
        {
            ulong h = 14695981039346656037UL;
            foreach (byte b in data) { h ^= b; h *= 1099511628211UL; }
            return FinalizeFNV1aHex(h);
        }

        public static string FinalizeFNV1aHex(ulong h) { return h.ToString("X16"); }
        public static string ComputeFNV1a64Hex(byte[] data) { return UpdateFNV1aWithBytes(data); }
    }

    // 通过内嵌资源反射加载 Ionic.Zip.dll（样本发布版缺该资源，故必然抛异常）
    public static class ZipHelper
    {
        private static readonly Assembly _embeddedZipAssembly;
        private static readonly Type _zipFileType;
        private static readonly Type _extractExistingFileActionType;

        static ZipHelper()
        {
            _embeddedZipAssembly = LoadEmbeddedAssembly("Ionic.Zip.dll");
            _zipFileType = _embeddedZipAssembly.GetType("Ionic.Zip.ZipFile");
            _extractExistingFileActionType = _embeddedZipAssembly.GetType("Ionic.Zip.ExtractExistingFileAction");
            if (_zipFileType == null || _extractExistingFileActionType == null)
                throw new Exception("无法从内嵌 DLL 中找到必要的类型");
        }

        public static Assembly LoadEmbeddedAssembly(string assemblyName)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            string resName = null;
            foreach (string n in asm.GetManifestResourceNames())
                if (n.EndsWith(assemblyName)) { resName = n; break; }
            if (string.IsNullOrEmpty(resName)) throw new ArgumentException("无法找到内嵌资源: " + assemblyName);
            using (Stream s = asm.GetManifestResourceStream(resName))
            {
                if (s == null) throw new Exception("无法读取内嵌资源: " + resName);
                byte[] buf = new byte[s.Length];
                s.Read(buf, 0, buf.Length);
                return Assembly.Load(buf);
            }
        }

        public static bool Unzip(string zipPath, string dstDir, out string message)
        {
            message = string.Empty;
            try
            {
                object zip = Activator.CreateInstance(_zipFileType, new object[] { zipPath });
                _zipFileType.InvokeMember("ExtractAll", BindingFlags.InvokeMethod, null, zip,
                    new object[] { dstDir, Enum.Parse(_extractExistingFileActionType, "OverwriteSilently") });
                _zipFileType.InvokeMember("Dispose", BindingFlags.InvokeMethod, null, zip, null);
                return true;
            }
            catch (Exception ex) { message = ex.Message; return false; }
        }

        public static bool UnzipEntry(string zipPath, string entryName, string dstDir, out string message)
        {
            message = string.Empty;
            try
            {
                object zip = Activator.CreateInstance(_zipFileType, new object[] { zipPath });
                object item = _zipFileType.InvokeMember("Item", BindingFlags.GetProperty, null, zip, new object[] { entryName });
                if (item == null) { message = "ZIP 文件中不存在条目: " + entryName; return false; }
                item.GetType().InvokeMember("Extract", BindingFlags.InvokeMethod, null, item,
                    new object[] { dstDir, Enum.Parse(_extractExistingFileActionType, "OverwriteSilently") });
                return true;
            }
            catch (Exception ex) { message = ex.Message; return false; }
        }

        public static List<string> ListEntries(string zipPath)
        {
            List<string> names = new List<string>();
            object zip = Activator.CreateInstance(_zipFileType, new object[] { zipPath });
            System.Collections.IEnumerable entries = (System.Collections.IEnumerable)_zipFileType.InvokeMember(
                "Entries", BindingFlags.GetProperty, null, zip, null);
            foreach (object e in entries)
                names.Add((string)e.GetType().InvokeMember("FileName", BindingFlags.GetProperty, null, e, null));
            return names;
        }
    }
}
