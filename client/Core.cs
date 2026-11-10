// 还原自 AutoInstall.exe (auto_fix) —— 入口与数据契约
// 目标框架 .NET Framework 4.8 / C# 5（可直接用 csc.exe 编译）
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace auto_fix
{
    internal static class Program
    {
        internal static Form1 f1;

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            f1 = new Form1();
            Application.Run(f1);
        }

        public static void OutputMessage(string message)
        {
            f1.UpdateLabel(message);
        }
    }
}

namespace auto_fix.Data
{
    public class AppInfo
    {
        public int Id { get; set; }
        public string AppName { get; set; }
        public string AppVer { get; set; }
        public string AppFirm { get; set; }
        public string AppScript { get; set; }
        public string AppPath { get; set; }
        public string AppDirectory { get; set; }
        public string AppPackageName { get; set; }
    }

    public class AppMessage
    {
        public string Message { get; set; }
        public List<AppInfo> Success { get; set; }
    }

    public class ResponseMessage
    {
        public string Message { get; set; }
        public bool Success { get; set; }
    }

    public class UserValidationRequest
    {
        public string UserName { get; set; }
    }

    public class UserValidationResponse
    {
        public string Name { get; set; }
        public int UserId { get; set; }
        public bool IsValid { get; set; }
        public string Message { get; set; }
    }

    public class RegistrationRequest
    {
        public string UserIP { get; set; }
        public string ComputerName { get; set; }
        public string ComputerCode { get; set; }
        public string Product { get; set; }
        public string InstallPath { get; set; }
        public string InstallDate { get; set; }
        public int UserId { get; set; }
        public string Other { get; set; }
    }

    public class RegistrationResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int RecordId { get; set; }
    }
}

namespace auto_fix.Model
{
    public class WindowInfo
    {
        public IntPtr Handle { get; private set; }
        public string Title { get; private set; }

        public WindowInfo(IntPtr handle, string title)
        {
            Handle = handle;
            Title = title;
        }

        public override string ToString()
        {
            return string.Format("0x{0:X16} | {1}", Handle.ToInt64(), Title);
        }
    }
}
