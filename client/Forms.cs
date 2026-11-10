// 还原自 AutoInstall.exe (auto_fix) —— WinForms 界面（代码构建，无 .resx 依赖）
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using auto_fix.Data;
using auto_fix.Engine;
using auto_fix.Services;
using auto_fix.Tools;

namespace auto_fix
{
    public class Form1 : Form
    {
        private delegate void SetLabelDelegate(string msg);

        private string ApiUrl = WebSevice.ApiUrl;
        private string ContentType = "application/json; charset=utf-8";
        private int userId = -1;
        private string userName = string.Empty;
        private List<string> appNameList = new List<string>();
        private List<string> appPathList = new List<string>();
        private Thread _workerThread;
        private IContainer components;

        internal static List<AppInfo> auto_script_list = new List<AppInfo>();
        internal static List<AppInfo> scriptList;
        internal static ParsingCommand Starter = new ParsingCommand();
        internal static string installDisk = "C:\\";

        private TextBox textBox1;
        private Button autoBtn;
        private Button button1;
        private Button button2;
        private ComboBox diskBox;
        private Label diskLabel;
        private Label startLabel;
        private ProgressBar progressBar1;
        private Label progressBarLabel;
        private ToolTip toolTip1;

        public Form1()
        {
            ElevateIfNeeded();
            InitializeComponent();
            toolTip1 = new ToolTip();
            KeyPreview = true;
            SetupControls();

            Thread t = new Thread(LoadProductData);
            t.IsBackground = true;
            t.Start();

            List<string> drives = FixTool.GetValidInstallDrives();
            if (drives != null && drives.Count > 0)
            {
                diskBox.DataSource = drives;
                diskBox.SelectedIndex = 0;
                diskBox.Text = FixTool.MaxDrive();
            }
            else diskBox.Text = "无效";
        }

        private void SetupControls()
        {
            progressBar1.Minimum = 0;
            progressBar1.Maximum = 100;
            progressBar1.Value = 0;
            progressBar1.Style = ProgressBarStyle.Continuous;
        }

        public void UpdateLabel(string msg)
        {
            if (InvokeRequired) { Invoke(new SetLabelDelegate(UpdateLabel), new object[] { msg }); return; }
            startLabel.Text = msg;
        }

        // 授权码 = 输入框内容，在线校验；返回 true 才继续
        private bool IsPassword()
        {
            string key = textBox1.Text.Trim();
            if (textBox1.Text == string.Empty) return false;
            try
            {
                string url = ApiUrl + "/api/user/validate";
                WebReqst.HttpRequester c = new WebReqst.HttpRequester();
                var reqObj = new { userName = key };
                byte[] body = System.Text.Encoding.UTF8.GetBytes(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(reqObj));
                string text = c.Post(url, null, body, ContentType).BodyString;
                UserValidationResponse resp = new System.Web.Script.Serialization.JavaScriptSerializer()
                    .Deserialize<UserValidationResponse>(text);
                if (resp != null && resp.IsValid)
                {
                    userId = resp.UserId;
                    WebSevice.userId = resp.UserId;   // 与原样本一致：静态字段也写
                    userName = resp.Name;
                }
                else
                {
                    MessageBox.Show("验证错误");
                    Environment.Exit(0);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                Environment.Exit(0);
            }
            if (userName == "admin")   // 隐藏调试控制台
            {
                Info_Form f = new Info_Form();
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(515, 100);
                f.Show();
            }
            return true;
        }

        // 拉任务：解析 AppPackageName 中的逗号候选，定位 BaseDirectory 下的安装包目录
        private void LoadProductData()
        {
            try
            {
                AppMessage msg = WebSevice.GetAppData();
                if (msg.Success != null)
                {
                    foreach (AppInfo app in msg.Success)
                    {
                        foreach (string pkg in (app.AppPackageName ?? "").Split(','))
                        {
                            string dir = FixTool.GetFolderPath(pkg);
                            if (dir != null)
                            {
                                dir = dir.TrimEnd('\\', '/');
                                string first = app.AppScript.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None)[0].Trim();
                                if (FixTool.AnalysisFistLineMD5(first, dir) == 0) break;
                                app.AppPath = dir;
                                auto_script_list.Add(app);
                                break;
                            }
                        }
                    }
                }
                Invoke(new Action(delegate { UpdateSoft("安装：" + string.Join("、", auto_script_list.Select(a => a.AppName).ToArray())
                    + string.Format("...共[{0}]个软件", auto_script_list.Count)); }));
            }
            catch (Exception ex)
            {
                DialogResult r = MessageBox.Show("获取安装数据异常：" + ex.Message + "\n是否重新获取？", "异常",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (r == DialogResult.OK) { LoadProductData(); return; }
                Environment.Exit(0);
            }
        }

        private void DoTimeConsumingWork()
        {
            scriptList = auto_script_list;
            int total = scriptList.Count * 100;
            try
            {
                for (int i = 0; i < scriptList.Count; i++)
                {
                    Starter.SetRunPath(scriptList[i].AppPath);
                    string[] lines = scriptList[i].AppScript.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
                    appNameList.Add(scriptList[i].AppName);
                    Starter.PrintInfo.Add(string.Format("开始执行脚本: {0}，共 {1} 条命令", scriptList[i].AppName, lines.Length));
                    for (int j = 0; j < lines.Length; j++)
                    {
                        string head = string.Format("程序运行中，请不要关闭程序[{0}/{1}]... \r\n安装软件：{2}[{3}/{4}]",
                            j, lines.Length, scriptList[i].AppName, i + 1, scriptList.Count);
                        string soft = string.Format("安装软件[{0}/{1}]:{2}", i + 1, scriptList.Count, scriptList[i].AppName);
                        UpdateSoft(soft);
                        UpdateProgress(FixTool.CalculatePercentage(i * 100 + FixTool.CalculatePercentage(j, lines.Length), total), head);

                        string line = lines[j].Trim();
                        if (string.IsNullOrEmpty(line) || line.StartsWith("//") || line.StartsWith("#")) continue;
                        Starter.PrintInfo.Add(string.Format("[{0}/{1}] 执行: {2}", j + 1, lines.Length, line));
                        ProcessCommand(line);
                        Thread.Sleep(100);
                    }
                    appPathList.Add(Starter.installPath);
                }
                UpdateProgress(100, "软件安装完成！");
                if (appPathList.Count > 0) UpdateFinish(appPathList[0]);
                SubmitInfo("【安装完成】\r\n");
            }
            catch (ThreadAbortException)
            {
                UpdateProgress(0, "任务被中止");
            }
            catch (Exception ex)
            {
                SubmitInfo("错误: " + ex.Message + "\r\n");
                UpdateProgress(0, "错误: " + ex.Message);
            }
        }

        private void ProcessCommand(string line)
        {
            try { Starter.ProcessCommand(line); }
            catch (Exception ex)
            {
                Starter.PrintInfo.Add(string.Format("脚本执行出现错误，在[{0}]的[{1}]位置：{2}", 0, Starter.PrintInfo.Count - 1, ex.Message));
            }
        }

        private void SubmitInfo(string log)
        {
            try
            {
                RegistrationRequest req = new RegistrationRequest();
                req.UserIP = FixTool.GetLocalIPAddress();
                req.ComputerName = Environment.MachineName;
                req.ComputerCode = FixTool.GetUniqueIdentifier();
                req.InstallDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                req.InstallPath = string.Join(";", appPathList.ToArray());
                req.Product = string.Join(";", appNameList.ToArray());
                req.UserId = userId;
                req.Other = log + "\r\n" + string.Join("\r\n", Starter.PrintInfo.ToArray());
                WebSevice.SubmitInfo(req, "");
            }
            catch (Exception ex) { UpdateLabel("安装信息提交异常：" + ex.Message); }
        }

        public void UpdateProgress(int percent, string text)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<int, string>(UpdateProgress), new object[] { percent, text });
                return;
            }
            progressBar1.Value = percent;
            progressBarLabel.Text = "进度" + percent + "%";
            startLabel.Text = text;
        }

        public void UpdateSoft(string name)
        {
            if (InvokeRequired) { Invoke(new Action<string>(UpdateSoft), new object[] { name }); return; }
            startLabel.Text = name;
        }

        public void UpdateFinish(string path)
        {
            if (InvokeRequired) { Invoke(new Action<string>(UpdateFinish), new object[] { path }); return; }
            button2.Enabled = true;
            button1.Visible = true;
        }

        private void autoBtn_Click(object sender, EventArgs e)
        {
            if (_workerThread != null && _workerThread.IsAlive) return;
            if (auto_script_list.Count == 0) { MessageBox.Show("没有找到安装包或者安装文件"); return; }
            if (!IsPassword()) return;

            autoBtn.Enabled = false;
            autoBtn.Visible = false;
            textBox1.Visible = false;
            diskBox.Enabled = false;

            installDisk = string.IsNullOrEmpty(diskBox.Text) ? "C:\\" : diskBox.Text.Trim();
            diskLabel.Text = "安装到磁盘：" + installDisk;
            diskBox.Visible = false;
            Starter.installDisk = installDisk;

            _workerThread = new Thread(DoTimeConsumingWork);
            _workerThread.IsBackground = true;
            _workerThread.Start();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (_workerThread != null && _workerThread.IsAlive) _workerThread.Abort();
            // 自删除
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd",
                Arguments = "/c timeout /t 2 && del /f \"" + Application.ExecutablePath + "\"",
                WindowStyle = ProcessWindowStyle.Hidden
            });
            Application.Exit();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (appPathList.Count == 0) return;
            try { Process.Start("explorer.exe", appPathList[0]); }
            catch { }
            if (_workerThread != null && _workerThread.IsAlive) _workerThread.Abort();
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd",
                Arguments = "/c timeout /t 2 && del /f \"" + Application.ExecutablePath + "\"",
                WindowStyle = ProcessWindowStyle.Hidden
            });
            Application.Exit();
        }

        private void appInfo_Click(object sender, EventArgs e)
        {
            if (pathListVisible()) { }
        }

        private bool pathListVisible() { return button2 != null; }

        private static bool IsAdministrator()
        {
            WindowsIdentity id = WindowsIdentity.GetCurrent();
            WindowsPrincipal p = new WindowsPrincipal(id);
            return p.IsInRole(WindowsBuiltInRole.Administrator);
        }

        // 关键：非管理员则 runas 重启自身
        private static void ElevateIfNeeded()
        {
            if (!IsAdministrator())
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                ProcessStartInfo psi = new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" };
                Process.Start(psi);
                Environment.Exit(0);
            }
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F12)
            {
                Info_Form f = new Info_Form();
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(387, 100);
                f.Show();
            }
            if (e.KeyCode == Keys.Enter && autoBtn.Visible) autoBtn_Click(sender, e);
        }

        private void textBox1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) autoBtn_Click(sender, e);
        }

        private void Form1_Shown(object sender, EventArgs e) { textBox1.Focus(); }

        private void Form1_Load(object sender, EventArgs e) { }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_workerThread != null && _workerThread.IsAlive) _workerThread.Abort();
            Application.Exit();
            Environment.Exit(0);
        }

        private void InitializeComponent()
        {
            this.components = new Container();
            this.Text = "auto_install";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ClientSize = new Size(620, 260);
            this.FormClosing += new FormClosingEventHandler(this.Form1_FormClosing);
            this.Load += new EventHandler(this.Form1_Load);
            this.Shown += new EventHandler(this.Form1_Shown);
            this.KeyDown += new KeyEventHandler(this.Form1_KeyDown);

            Label title = new Label();
            title.Text = "安装：";
            title.Location = new Point(16, 14);
            title.AutoSize = true;
            this.Controls.Add(title);

            this.startLabel = new Label();
            this.startLabel.Location = new Point(16, 46);
            this.startLabel.Size = new Size(590, 60);
            this.startLabel.Text = "正在获取安装数据...";
            this.Controls.Add(this.startLabel);

            this.diskLabel = new Label();
            this.diskLabel.Text = "安装到磁盘：";
            this.diskLabel.Location = new Point(16, 112);
            this.diskLabel.AutoSize = true;
            this.Controls.Add(this.diskLabel);

            this.diskBox = new ComboBox();
            this.diskBox.Location = new Point(110, 108);
            this.diskBox.Width = 120;
            this.diskBox.DropDownStyle = ComboBoxStyle.DropDownList;
            this.Controls.Add(this.diskBox);

            this.textBox1 = new TextBox();
            this.textBox1.Location = new Point(16, 146);
            this.textBox1.Width = 300;
            this.textBox1.KeyDown += new KeyEventHandler(this.textBox1_KeyDown);
            this.Controls.Add(this.textBox1);

            this.autoBtn = new Button();
            this.autoBtn.Text = "开始";
            this.autoBtn.Location = new Point(330, 144);
            this.autoBtn.Width = 90;
            this.autoBtn.Click += new EventHandler(this.autoBtn_Click);
            this.Controls.Add(this.autoBtn);

            this.progressBar1 = new ProgressBar();
            this.progressBar1.Location = new Point(16, 190);
            this.progressBar1.Size = new Size(500, 20);
            this.Controls.Add(this.progressBar1);

            this.progressBarLabel = new Label();
            this.progressBarLabel.Text = "就绪";
            this.progressBarLabel.Location = new Point(526, 194);
            this.progressBarLabel.AutoSize = true;
            this.Controls.Add(this.progressBarLabel);

            this.button2 = new Button();
            this.button2.Text = "打开";
            this.button2.Location = new Point(430, 214);
            this.button2.Width = 80;
            this.button2.Enabled = false;
            this.button2.Click += new EventHandler(this.button2_Click);
            this.Controls.Add(this.button2);

            this.button1 = new Button();
            this.button1.Text = "退出";
            this.button1.Location = new Point(524, 214);
            this.button1.Width = 80;
            this.button1.Click += new EventHandler(this.button1_Click);
            this.Controls.Add(this.button1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }
    }

    // 调试控制台（授权码对应 Name == "admin" 或 F12 打开）
    public class Info_Form : Form
    {
        private delegate void SetLabelDelegate(string msg);
        private TextBox textBox1;
        private Button button1;
        private Thread CmdThread;

        public Info_Form()
        {
            this.Text = "调试信息";
            this.ClientSize = new Size(520, 340);
            this.textBox1 = new TextBox();
            this.textBox1.Multiline = true;
            this.textBox1.ScrollBars = ScrollBars.Vertical;
            this.textBox1.Location = new Point(8, 8);
            this.textBox1.Size = new Size(500, 290);
            this.textBox1.Font = new Font("宋体", 9F);
            this.textBox1.KeyPress += new KeyPressEventHandler(this.textBox1_KeyPress);
            this.Controls.Add(this.textBox1);

            this.button1 = new Button();
            this.button1.Text = "关闭";
            this.button1.Location = new Point(210, 306);
            this.button1.Click += new EventHandler(this.button1_Click);
            this.Controls.Add(this.button1);

            this.Load += new EventHandler(this.Info_Form_Load);
        }

        private void Info_Form_Load(object sender, EventArgs e)
        {
            CmdThread = new Thread(PrintDebugInfo);
            CmdThread.IsBackground = true;
            CmdThread.Start();
        }

        public void UpdateLabel(string msg)
        {
            if (InvokeRequired) { Invoke(new SetLabelDelegate(UpdateLabel), new object[] { msg }); return; }
            textBox1.AppendText(msg + "\r\n");
        }

        // 把引擎 printInfo 里的增量刷到界面上
        private void PrintDebugInfo()
        {
            int printed = 0;
            while (true)
            {
                List<string> all = Form1.Starter.PrintInfo;
                if (all.Count > printed)
                {
                    for (int i = printed; i < all.Count; i++) UpdateLabel(all[i]);
                    printed = all.Count;
                }
                Thread.Sleep(300);
            }
        }

        private void button1_Click(object sender, EventArgs e) { this.Hide(); }

        private void textBox1_KeyPress(object sender, KeyPressEventArgs e) { }
    }

    // 安装目录选择框
    public class InstallPathSelect : Form
    {
        private ComboBox diskBox;
        private TextBox textBox1;
        private Button button1;
        private Button button2;
        private string _path = "C:\\MySoftware";

        public InstallPathSelect()
        {
            this.Text = "选择安装目录";
            this.ClientSize = new Size(420, 140);
            this.diskBox = new ComboBox();
            this.diskBox.Location = new Point(16, 20);
            this.diskBox.Width = 120;
            this.Controls.Add(this.diskBox);
            this.textBox1 = new TextBox();
            this.textBox1.Location = new Point(16, 56);
            this.textBox1.Width = 380;
            this.textBox1.Text = _path;
            this.Controls.Add(this.textBox1);
            this.button1 = new Button();
            this.button1.Text = "确认";
            this.button1.Location = new Point(230, 96);
            this.button1.Click += new EventHandler(this.button1_Click);
            this.Controls.Add(this.button1);
            this.button2 = new Button();
            this.button2.Text = "取消";
            this.button2.Location = new Point(316, 96);
            this.button2.Click += new EventHandler(this.button2_Click);
            this.Controls.Add(this.button2);
            this.Load += new EventHandler(this.InstallPathSelect_Load);
            this.diskBox.SelectedIndexChanged += new EventHandler(this.diskBox_SelectedIndexChanged);
        }

        private void InstallPathSelect_Load(object sender, EventArgs e)
        {
            diskBox.DataSource = FixTool.GetValidInstallDrives();
        }

        private void diskBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (diskBox.Text != null && diskBox.Text.Length > 0)
                textBox1.Text = diskBox.Text + "MySoftware";
        }

        private void button1_Click(object sender, EventArgs e)
        {
            string msg;
            try { _path = FixTool.ValidateAndFormatInstallPath(textBox1.Text); }
            catch (Exception ex) { MessageBox.Show(ex.Message); return; }
            this.DialogResult = DialogResult.OK;
            this.Hide();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Hide();
        }

        public string GetPath() { return _path; }
        public void SetPath(string p) { _path = p; textBox1.Text = p; }
        public Control GetControl(string name) { return this.Controls.ContainsKey(name) ? this.Controls[name] : null; }
    }
}
