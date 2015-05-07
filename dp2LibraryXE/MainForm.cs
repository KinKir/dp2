﻿
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Deployment.Application;
using System.Diagnostics;
using System.Xml;
using System.Collections;

using Ionic.Zip;

using System.Runtime.InteropServices;
using System.Threading;
using System.Web;

using DigitalPlatform.IO;
using DigitalPlatform.Xml;
using DigitalPlatform.Text;
using DigitalPlatform.GUI;

using DigitalPlatform.CirculationClient;
using DigitalPlatform;
using DigitalPlatform.CirculationClient.localhost;

using dp2LibraryXE.Properties;
using DigitalPlatform.CommonControl;

namespace dp2LibraryXE
{
    public partial class MainForm : Form
    {
        FloatingMessageForm _floatingMessage = null;

        const string default_opac_rights = "denychangemypassword,getsystemparameter,getres,search,getbiblioinfo,setbiblioinfo,getreaderinfo,writeobject,getbibliosummary,listdbfroms,simulatereader,simulateworker";
        const string localhost_opac_url = "http://localhost:8081/dp2OPAC";

        /// <summary>
        /// 数据目录
        /// </summary>
        public string DataDir = "";

        /// <summary>
        /// 用户目录
        /// </summary>
        public string UserDir = "";

        /// <summary>
        /// dp2Kernel 数据目录
        /// </summary>
        public string KernelDataDir = "";

        /// <summary>
        /// dp2Library 数据目录
        /// </summary>
        public string LibraryDataDir = "";

        /// <summary>
        /// dp2OPAC 数据目录
        /// </summary>
        public string OpacDataDir = "";

        /// <summary>
        /// dp2OPAC 应用程序目录 (虚拟目录)
        /// </summary>
        public string OpacAppDir = "";

        /// <summary>
        /// dp2 站点目录 (虚拟目录)
        /// </summary>
        public string dp2SiteDir = "";

        /// <summary>
        /// Stop 管理器
        /// </summary>
        public DigitalPlatform.StopManager stopManager = new DigitalPlatform.StopManager();

        /// <summary>
        /// dp2library 服务器监听 URL 列表。一个或者多个URL。如果是多个 URL，用分号分隔
        /// </summary>
        public string LibraryServerUrlList = LibraryHost.default_single_url;    // "net.pipe://localhost/dp2library/xe"; // "net.tcp://localhost:8002/dp2library/xe";

        /// <summary>
        /// 配置存储
        /// </summary>
        public ApplicationInfo AppInfo = null;

        public MainForm()
        {
            InitializeComponent();

            {
                _floatingMessage = new FloatingMessageForm();
                _floatingMessage.Font = new System.Drawing.Font(this.Font.FontFamily, this.Font.Size * 2, FontStyle.Bold);
                _floatingMessage.Opacity = 0.7;
                _floatingMessage.RectColor = Color.Green;
                _floatingMessage.Show(this);
            }
        }

        private const int CP_NOCLOSE_BUTTON = 0x200;
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams myCp = base.CreateParams;
                myCp.ClassStyle = myCp.ClassStyle | CP_NOCLOSE_BUTTON;
                return myCp;
            }
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            GuiUtil.AutoSetDefaultFont(this);

            ClearForPureTextOutputing(this.webBrowser1);

#if NO
            if (Settings.Default.UpdateSettings)
            {
                Settings.Default.Upgrade();
                Settings.Default.UpdateSettings = false;
                Settings.Default.Save();
            }
#endif

            this.toolStripProgressBar_main.Visible = false;

            // 创建事件日志分支目录
            CreateEventSource();
#if LOG
            WriteLibraryEventLog("开始启动", EventLogEntryType.Information);
            WriteLibraryEventLog("当前操作系统信息：" + Environment.OSVersion.ToString(), EventLogEntryType.Information);
            WriteLibraryEventLog("当前操作系统版本号：" + Environment.OSVersion.Version.ToString(), EventLogEntryType.Information);
#endif

            string[] args = Environment.GetCommandLineArgs();
            if (args != null && args.Length >=2 )
            {
#if LOG
                WriteLibraryEventLog("命令行参数=" + string.Join(",", args), EventLogEntryType.Information);
#endif
                // MessageBox.Show(string.Join(",", args));
                for (int i = 1; i < args.Length; i++)
                {
                    string strArg = args[i];
                    if (StringUtil.HasHead(strArg, "datadir=") == true)
                    {
                        this.DataDir = strArg.Substring("datadir=".Length);
#if LOG
                        WriteLibraryEventLog("从命令行参数得到, this.DataDir=" + this.DataDir, EventLogEntryType.Information);
#endif
                    }
                    else if (StringUtil.HasHead(strArg, "userdir=") == true)
                    {
                        this.UserDir = strArg.Substring("userdir=".Length);
#if LOG
                        WriteLibraryEventLog("从命令行参数得到, this.UserDir=" + this.UserDir, EventLogEntryType.Information);
#endif
                    }
                }
            }

            if (string.IsNullOrEmpty(this.DataDir) == true)
            {
                if (ApplicationDeployment.IsNetworkDeployed == true)
                {
#if LOG
                    WriteLibraryEventLog("从网络安装启动", EventLogEntryType.Information);
#endif
                    // MessageBox.Show(this, "network");
                    this.DataDir = Application.LocalUserAppDataPath;
                }
                else
                {
#if LOG
                    WriteLibraryEventLog("绿色安装方式启动", EventLogEntryType.Information);
#endif
                    // MessageBox.Show(this, "no network");
                    this.DataDir = Environment.CurrentDirectory;
                }
#if LOG
                WriteLibraryEventLog("普通方法得到, this.DataDir=" + this.DataDir, EventLogEntryType.Information);
#endif
            }

            if (string.IsNullOrEmpty(this.UserDir) == true)
            {
                this.UserDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "dp2LibraryXE_v1");
#if LOG
                WriteLibraryEventLog("普通方法得到, this.UserDir=" + this.UserDir, EventLogEntryType.Information);
#endif
            }
            PathUtil.CreateDirIfNeed(this.UserDir);

            this.AppInfo = new ApplicationInfo(Path.Combine(this.UserDir, "settings.xml"));

            this.KernelDataDir = Path.Combine(this.UserDir, "kernel_data");
            PathUtil.CreateDirIfNeed(this.KernelDataDir);

            this.LibraryDataDir = Path.Combine(this.UserDir, "library_data");
            PathUtil.CreateDirIfNeed(this.LibraryDataDir);

            this.OpacDataDir = Path.Combine(this.UserDir, "opac_data");
            PathUtil.CreateDirIfNeed(this.OpacDataDir);

            this.OpacAppDir = Path.Combine(this.UserDir, "opac_app");
            PathUtil.CreateDirIfNeed(this.OpacAppDir);

            this.dp2SiteDir = Path.Combine(this.UserDir, "dp2_site");
            PathUtil.CreateDirIfNeed(this.dp2SiteDir);

            stopManager.Initial(this.toolButton_stop,
    (object)this.toolStripStatusLabel_main,
    (object)this.toolStripProgressBar_main);

            this.AppInfo.LoadFormStates(this,
"mainformstate",
FormWindowState.Normal);
#if NO
            if (Settings.Default.WindowSize != null)
                this.Size = Settings.Default.WindowSize;
            if (Settings.Default.WindowLocation != null)
                this.Location = Settings.Default.WindowLocation;
#endif

            // cfgcache
            _versionManager.Load(Path.Combine(this.UserDir, "file_version.xml"));

            Delegate_Initialize d = new Delegate_Initialize(Initialize);
            this.BeginInvoke(d);

            AutoStartDp2circulation = AutoStartDp2circulation;
        }

        // MessageBar _messageBar = null;

        delegate void Delegate_Initialize();

        // 启动后要执行的初始化操作
        void Initialize()
        {
            string strError = "";
            int nRet = 0;

#if SN
            nRet = VerifySerialCode(out strError);
            if (nRet == -1)
            {
                Application.Exit();
                return;
            }

            GetMaxClients();
            GetLicenseType();
#else
            this.MenuItem_resetSerialCode.Visible = false;
#endif

#if NO
            _messageBar = new MessageBar();
            _messageBar.TopMost = false;
            _messageBar.Font = this.Font;
            _messageBar.BackColor = SystemColors.Info;
            _messageBar.ForeColor = SystemColors.InfoText;
            _messageBar.Text = "dp2Library XE";
            _messageBar.MessageText = "正在启动 dp2Library XE，请等待 ...";
            _messageBar.StartPosition = FormStartPosition.CenterScreen;
            _messageBar.Show(this);
            _messageBar.Update();
#endif
            this._floatingMessage.Text = "正在启动 dp2Library XE，请等待 ...";

            Application.DoEvents();

            try
            {
                // 首次运行自动安装数据目录
                {
                    nRet = SetupKernelDataDir(
                        true,
                        out strError);
                    if (nRet == -1)
                    {
                        WriteKernelEventLog("dp2Library XE 自动初始化数据目录出错: " + strError, EventLogEntryType.Error);
                        MessageBox.Show(this, strError);
                    }
                    else
                    {
                        WriteKernelEventLog("dp2Library XE 自动初始化数据目录成功", EventLogEntryType.Information);
                    }

                    nRet = SetupLibraryDataDir(
                        true,
                        out strError);
                    if (nRet == -1)
                    {
                        WriteLibraryEventLog("dp2Library XE 自动初始化数据目录出错: " + strError, EventLogEntryType.Error);
                        MessageBox.Show(this, strError);
                    }
                    else
                    {
                        WriteLibraryEventLog("dp2Library XE 自动初始化数据目录成功", EventLogEntryType.Information);
                    }
                }

                // 更新数据目录
                UpdateCfgs();

                // 启动两个后台服务
                nRet = dp2Kernel_start(true,
                    out strError);
                if (nRet == -1)
                {
                    WriteKernelEventLog("dp2Library XE 启动 dp2Kernel 时出错: " + strError, EventLogEntryType.Error);
                    MessageBox.Show(this, strError);
                }
                nRet = dp2Library_start(true,
                    out strError);
                if (nRet == -1)
                {
                    WriteLibraryEventLog("dp2Library XE 启动 dp2Library 时出错: " + strError, EventLogEntryType.Error);
                    MessageBox.Show(this, strError);
                }

                bool bInstalled = this.AppInfo.GetBoolean("OPAC", "installed", false);
                if (bInstalled == true)
                {
                    nRet = dp2OPAC_UpdateAppDir(false, out strError);
                    if (nRet == -1)
                        MessageBox.Show(this, "自动升级 dp2OPAC 过程出错: " + strError);

                    // 检查当前超级用户帐户是否为空密码
                    // return:
                    //      -1  检查中出错
                    //      0   空密码
                    //      1   已经设置了密码
                    nRet = CheckNullPassword(out strError);
                    if (nRet == -1)
                        MessageBox.Show(this, "检查超级用户密码的过程出错: " + strError);

                    if (nRet == 0)
                    {
                        AutoCloseMessageBox.Show(this,
                            "当前超级用户 " + this.SupervisorUserName + " 的密码为空，如果启动 dp2OPAC，其他人将可能通过浏览器冒用此账户。\r\n\r\n请(使用 dp2circulation (内务前端))为此账户设置密码，然后重新启动 dp2libraryXE。\r\n\r\n为确保安全，本次未启动 dp2OPAC",
                            20*1000,
                            "dp2library XE 警告");
#if NO
                        MessageBox.Show(this,
                            "当前超级用户 " + this.SupervisorUserName + " 的密码为空，如果启动 dp2OPAC，其他人将可能通过浏览器冒用此账户。\r\n\r\n请(使用 dp2circulation (内务前端))为此账户设置密码，然后重新启动 dp2libraryXE。\r\n\r\n为确保安全，本次未启动 dp2OPAC",
                            "dp2library XE 警告",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
#endif
                    }
                    else
                    {
                        // return:
                        //      -1  出错
                        //      0   程序文件不存在
                        //      1   成功启动
                        nRet = StartIIsExpress("dp2Site", true, out strError);
                        if (nRet != 1)
                            MessageBox.Show(this, strError);
                    }
                }

                // 2014/11/16
                try
                {
                    EventWaitHandle.OpenExisting("dp2libraryXE V1 library host started").Set();
                }
                catch
                {
                }
            }
            finally
            {
#if NO
                _messageBar.Close();
                _messageBar = null;
#endif 
                this._floatingMessage.Text = "";

                this.SetTitle();
            }

#if NO
            if (this.AutoStartDp2circulation == true)
            {
                try
                {
                    System.Diagnostics.Process.Start("http://dp2003.com/dp2circulation/v2/dp2circulation.application");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "启动 dp2Circulation 时出错: " + ex.Message);
                }
            }
#endif
        }

        public bool AutoStartDp2circulation
        {
            get
            {
                return this.AppInfo.GetBoolean("main_form", "auto_start_dp2circulation", true);
            }
            set
            {
                this.AppInfo.SetBoolean("main_form", "auto_start_dp2circulation", value);
                this.MenuItem_autoStartDp2Circulation.Checked = value;
            }
        }


        void SetListenUrl(string strMode)
        {
            // 设置监听 URL
            if (strMode == "miniServer")
            {
                string strNewUrl = InputDlg.GetInput(
this,
"请指定服务器绑定的 URL",
"URL: ",
LibraryHost.default_miniserver_urls,
this.Font);
                if (strNewUrl == null)
                {
                    strNewUrl = LibraryHost.default_miniserver_urls;    //  "http://localhost:8001/dp2library/xe;net.pipe://localhost/dp2library/xe";
                    MessageBox.Show(this, "自动使用缺省的 URL " + strNewUrl);
                }

                this.AppInfo.SetString("main_form", "listening_url", strNewUrl);
                // TODO: 检查，
            }
            else
                this.AppInfo.SetString("main_form", "listening_url", LibraryHost.default_single_url);

        }

        #region 序列号机制

        bool _testMode = false;

        public bool TestMode
        {
            get
            {
                return this._testMode;
            }
            set
            {
                if (this._testMode != value)
                {
                    this._testMode = value;
                    SetTitle();
                }
            }
        }

        void SetTitle()
        {
            if (this.IsServer == true)
            {
                if (this.TestMode == true)
                    this.Text = "dp2Library XE 小型服务器 (评估模式)";
                else
                    this.Text = "dp2Library XE 小型服务器";
            }
            else
            {
                if (this.TestMode == true)
                    this.Text = "dp2Library XE 单机 (评估模式)";
                else
                    this.Text = "dp2Library XE 单机";
            }

                string strContent = @"
dp2Library XE
---
dp2 图书馆集成系统 图书馆应用服务器 " 
                    + (this.IsServer == false ? "单机版" : "小型版") +
@"
---
(C) 版权所有 2014-2015 数字平台(北京)软件有限责任公司
http://dp2003.com" + (this.IsServer == false ? "" : @"
---
最大通道数： " + this.MaxClients.ToString())
         + @"
本机 MAC 地址: " + StringUtil.MakePathList(SerialCodeForm.GetMacAddress()) + "\r\n\r\n";

                WriteTextToConsole(strContent);
        }
#if SN
        int _maxClients = 5;
#else
        int _maxClients = 200;
#endif
        public int MaxClients
        {
            get
            {
                return _maxClients;
            }
            set
            {
                if (_maxClients != value)
                {
                    _maxClients = value;
                    this.SetTitle();
                }
            }
        }

#if SN
        void GetMaxClients()
        {
            string strLocalString = GetEnvironmentString(this.IsServer, "");
            Hashtable table = StringUtil.ParseParameters(strLocalString);
            string strProduct = (string)table["product"];

            string strMaxClients = (string)table["clients"];
            if (string.IsNullOrEmpty(strMaxClients) == false)
            {
                int v = this.MaxClients;
                if (int.TryParse(strMaxClients, out v) == true)
                {
                    this.MaxClients = v;
                }
                else
                    throw new Exception("clients 参数值 '" + strMaxClients + "' 格式错误");
            }

            // this.SetTitle();
        }

#endif

        string _licenseType = "";
        public string LicenseType
        {
            get
            {
                return _licenseType;
            }
            set
            {
                if (_licenseType != value)
                {
                    _licenseType = value;
                    // this.SetTitle();
                }
            }
        }

#if SN
        void GetLicenseType()
        {
            string strLocalString = GetEnvironmentString(this.IsServer, "");
            Hashtable table = StringUtil.ParseParameters(strLocalString);
            // string strProduct = (string)table["product"];

            this.LicenseType = (string)table["licensetype"];

            // this.SetTitle();
        }

        // 获得 xxx|||xxxx 的左边部分
        static string GetCheckCode(string strSerialCode)
        {
            string strSN = "";
            string strExtParam = "";
            StringUtil.ParseTwoPart(strSerialCode,
                "|||",
                out strSN,
                out strExtParam);

            return strSN;
        }

        // 获得 xxx|||xxxx 的右边部分
        static string GetExtParams(string strSerialCode)
        {
            string strSN = "";
            string strExtParam = "";
            StringUtil.ParseTwoPart(strSerialCode,
                "|||",
                out strSN,
                out strExtParam);

            return strExtParam;
        }

        // 将本地字符串匹配序列号
        bool MatchLocalString(bool bIsServer, string strSerialNumber)
        {
            List<string> macs = SerialCodeForm.GetMacAddress();
            foreach (string mac in macs)
            {
                string strLocalString = GetEnvironmentString(bIsServer, mac);
                string strSha1 = Cryptography.GetSHA1(StringUtil.SortParams(strLocalString) + "_reply");
                if (strSha1 == SerialCodeForm.GetCheckCode(strSerialNumber))
                    return true;
            }

            return false;
        }

#endif

#if SN
        int VerifySerialCode(out string strError)
        {
            strError = "";
            int nRet = 0;

            // 2014/11/15
            string strFirstMac = "";
            List<string> macs = SerialCodeForm.GetMacAddress();
            if (macs.Count != 0)
            {
                strFirstMac = macs[0];
            }

            string strSerialCode = this.AppInfo.GetString("sn", "sn", "");

            // 首次运行
            if (string.IsNullOrEmpty(strSerialCode) == true)
            {
                // 如果当前窗口没有在最前面
                {
                    if (this.WindowState == FormWindowState.Minimized)
                        this.WindowState = FormWindowState.Normal;
                    this.Activate();
                    API.SetForegroundWindow(this.Handle);
                }

                FirstRunDialog first_dialog = new FirstRunDialog();
                MainForm.SetControlFont(first_dialog, this.Font);
                first_dialog.MainForm = this;
                first_dialog.Mode = this.AppInfo.GetString("main_form", "last_mode", "standard");
                first_dialog.StartPosition = FormStartPosition.CenterScreen;
                if (first_dialog.ShowDialog(this) == System.Windows.Forms.DialogResult.Cancel)
                {
                    strError = "放弃";
                    return -1;
                }

                // 首次写入 运行模式 信息
                this.AppInfo.SetString("main_form", "last_mode", first_dialog.Mode);
                if (first_dialog.Mode == "test")
                {
                    this.AppInfo.SetString("sn", "sn", "test");
                    this.AppInfo.Save();
                }

                ////
                SetListenUrl(first_dialog.Mode);
            }

        REDO_VERIFY:
            strSerialCode = this.AppInfo.GetString("sn", "sn", "");
            if (strSerialCode == "test")
            {
                this.TestMode = true;
                // 覆盖写入 运行模式 信息，防止用户作弊
                // 小型版没有对应的评估模式
                this.AppInfo.SetString("main_form", "last_mode", "test");
                return 0;
            }
            else
                this.TestMode = false;

            // string strLocalString = GetEnvironmentString(this.IsServer);

            // string strSha1 = Cryptography.GetSHA1(StringUtil.SortParams(strLocalString) + "_reply");

            if (// strSha1 != GetCheckCode(strSerialCode)
                (MatchLocalString(this.IsServer, strSerialCode) == false
                && MatchLocalString(!this.IsServer, strSerialCode) == false)
                || String.IsNullOrEmpty(strSerialCode) == true)
            {
                if (String.IsNullOrEmpty(strSerialCode) == false)
                {
                    MessageBox.Show(this, "序列号无效。请重新输入");
                }

                // 出现设置序列号对话框
                nRet = ResetSerialCode(
                    false,
                    strSerialCode,
                    GetEnvironmentString(this.IsServer, strFirstMac));
                if (nRet == 0)
                {
                    strError = "放弃";
                    return -1;
                }
                goto REDO_VERIFY;
            }
            return 0;
        }

#endif


#if SN
        // parameters:
        //      bServer     是否为小型服务器版本。如果是小型服务器版本，用 net.tcp 协议绑定 dp2library host；如果不是单机版本，用 net.pipe 绑定 dp2library host
        string GetEnvironmentString(bool bServer, string strMAC)
        {
            Hashtable table = new Hashtable();
            table["mac"] = strMAC;  //  SerialCodeForm.GetMacAddress();
            table["time"] = GetTimeRange();

            if (bServer == true)
                table["product"] = "dp2libraryXE server";
            else
                table["product"] = "dp2libraryXE";

#if NO
            string strMaxClients = this.AppInfo.GetString("main_form", "clients", "");
            if (string.IsNullOrEmpty(strMaxClients) == false)
                table["clients"] = strMaxClients;
#endif
            string strSerialCode = this.AppInfo.GetString("sn", "sn", "");
            if (string.IsNullOrEmpty(strSerialCode) == false)
            {
                string strExtParam = GetExtParams(strSerialCode);
                if (string.IsNullOrEmpty(strExtParam) == false)
                {
                    Hashtable ext_table = StringUtil.ParseParameters(strExtParam);
                    string strMaxClients = (string)ext_table["clients"];
                    if (string.IsNullOrEmpty(strMaxClients) == false)
                        table["clients"] = strMaxClients;

                    string strLicenseType = (string)ext_table["licensetype"];
                    if (string.IsNullOrEmpty(strLicenseType) == false)
                        table["licensetype"] = strLicenseType;

                }
            }

            return StringUtil.BuildParameterString(table);
        }

        static string GetTimeRange()
        {
#if NO
            DateTime now = DateTime.Now;
            return now.Year.ToString().PadLeft(4, '0')
            + now.Month.ToString().PadLeft(2, '0');
#endif
            DateTime now = DateTime.Now;
            return now.Year.ToString().PadLeft(4, '0');
        }

        string CopyrightKey = "dp2libraryXE_sn_key";

        // return:
        //      0   Cancel
        //      1   OK
        int ResetSerialCode(
            bool bAllowSetBlank,
            string strOldSerialCode,
            string strOriginCode)
        {
            Hashtable ext_table = StringUtil.ParseParameters(strOriginCode);
            string strMAC = (string)ext_table["mac"];
            if (string.IsNullOrEmpty(strMAC) == true)
                strOriginCode = "!error";
            else 
                strOriginCode = Cryptography.Encrypt(strOriginCode,
                this.CopyrightKey);
            SerialCodeForm dlg = new SerialCodeForm();
            dlg.Font = this.Font;
            dlg.SerialCode = strOldSerialCode;
            dlg.StartPosition = FormStartPosition.CenterScreen;
            dlg.OriginCode = strOriginCode;

            REDO:
            dlg.ShowDialog(this);
            if (dlg.DialogResult != DialogResult.OK)
                return 0;

            if (string.IsNullOrEmpty(dlg.SerialCode) == true)
            {
                if (bAllowSetBlank == true)
                {
                    DialogResult result = MessageBox.Show(this,
        "确实要将序列号设置为空?\r\n\r\n(一旦将序列号设置为空，dp2Library XE 将自动退出，下次启动需要重新设置运行模式和序列号。此时可重新选择评估模式运行，但数据库数量和可修改的记录数量都会受到一定限制)",
        "dp2Library XE",
        MessageBoxButtons.YesNo,
        MessageBoxIcon.Question,
        MessageBoxDefaultButton.Button2);
                    if (result == System.Windows.Forms.DialogResult.No)
                    {
                        return 0;
                    }
                }
                else
                {
                    MessageBox.Show(this, "序列号不允许为空。请重新设置");
                    goto REDO;
                }
            }

            this.AppInfo.SetString("sn", "sn", dlg.SerialCode);
            this.AppInfo.Save();

            return 1;
        }

#endif

        #endregion

        [DllImport("user32.dll")]
        public extern static bool ShutdownBlockReasonCreate(IntPtr hWnd, [MarshalAs(UnmanagedType.LPWStr)] string pwszReason);

        [DllImport("user32.dll")]
        public extern static bool ShutdownBlockReasonDestroy(IntPtr hWnd);

        bool _skipFinalize = false; // 是否要忽略普通的结束的过程

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                // 警告关闭
                DialogResult result = MessageBox.Show(this,
                    "确实要退出 dp2Library XE?\r\n\r\n(本程序提供了 “dp2Library 应用服务器单机版/小型版” 的后台服务功能，一旦退出，图书馆业务前端将无法运行。平时应保持运行状态，将窗口最小化即可)",
                    "dp2Library XE",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);
                if (result != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            if (e.CloseReason == CloseReason.WindowsShutDown)
            {
                // Abort Shutdown
                // Process.Start("shutdown.exe", "-s -t 4");

                // http://stackoverflow.com/questions/11089259/shutdownblockreasoncreate-create-multiple-reasons-to-display-during-logoff-shu

                try
                {
                    ShutdownBlockReasonCreate(this.Handle, "正在退出 dp2Library XE，请稍候 ...");
                }
                catch (System.EntryPointNotFoundException)
                {
                    // Windows Server 2003 下面会抛出此异常
                }


                {
                    this._isBlocked = true;
                    e.Cancel = true;
                }

                this._skipFinalize = true;
                this.BeginInvoke(new Action<bool>(Finalize), true);
            }
        }

        private bool _isBlocked = false;

        protected override void WndProc(ref Message aMessage)
        {
            const int WM_QUERYENDSESSION = 0x0011;
            const int WM_ENDSESSION = 0x0016;

            if (_isBlocked && (aMessage.Msg == WM_QUERYENDSESSION || aMessage.Msg == WM_ENDSESSION))
                return;

            base.WndProc(ref aMessage);
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            if ( this._skipFinalize == false)
                Finalize(false);
#if NO
            this.toolStripStatusLabel_main.Text = "正在退出 dp2Library XE，请稍候 ...";
            Application.DoEvents();

            AppInfo.Save();
            AppInfo = null;	// 避免后面再用这个对象

            if (this.WindowState == FormWindowState.Normal)
            {
                Settings.Default.WindowSize = this.Size;
                Settings.Default.WindowLocation = this.Location;
            }
            else
            {
                Settings.Default.WindowSize = this.RestoreBounds.Size;
                Settings.Default.WindowLocation = this.RestoreBounds.Location;
            }

            Settings.Default.Save();

            dp2Library_stop();
            dp2Kernel_stop();
#endif
            if (_floatingMessage != null)
                _floatingMessage.Close();
        }

        void Finalize(bool bExitAndShutdown)
        {
            if (this.AppInfo != null)
            {
                AppInfo.SaveFormStates(this,
        "mainformstate");

                AppInfo.Save();
                AppInfo = null;	// 避免后面再用这个对象
            }

            if (_versionManager != null)
            {
                _versionManager.AutoSave();
            }

            try
            {
#if NO
                if (this.WindowState == FormWindowState.Normal)
                {
                    Settings.Default.WindowSize = this.Size;
                    Settings.Default.WindowLocation = this.Location;
                }
                else
                {
                    Settings.Default.WindowSize = this.RestoreBounds.Size;
                    Settings.Default.WindowLocation = this.RestoreBounds.Location;
                }

                Settings.Default.Save();
#endif
            }
            catch
            {
                // 可能在第一个进程退出的时候会遇到异常 2014/11/14
            }

            CloseIIsExpress(false);

            this.toolStripStatusLabel_main.Text = "正在退出 dp2Library XE，请稍候 ...";
            Application.DoEvents();

            dp2Library_stop();
            dp2Kernel_stop();

            // 测试用 Thread.Sleep(10000);
            if (bExitAndShutdown == true)
            {
                // MessageBox.Show(this, "end");
                try
                {
                    ShutdownBlockReasonDestroy(this.Handle);
                }
                catch (System.EntryPointNotFoundException)
                {
                }
                _isBlocked = false;

                if (
    Environment.OSVersion.Version.Major == 5 &&
    (
    Environment.OSVersion.Version.Minor == 1 || // Windows XP
    Environment.OSVersion.Version.Minor == 2)   // Windows Server 2003
    )
                {
                    // Windows XP 和 Server 2003 时候补充 shutdown 命令
                    Process.Start("shutdown.exe", "-s -t 4");
                }

                Application.Exit();
            }
        }

        private void button_setupKernelDataDir_Click(object sender, EventArgs e)
        {
            string strError = "";
            // 安装 dp2kernel 的数据目录
            // parameters:
            //      bAutoSetup  是否自动安装。自动安装时，如果已经存在数据文件，则不会再次安装。否则会强行重新安装，但安装前会出现对话框警告
            int nRet = SetupKernelDataDir(
                false,
                out strError);
            if (nRet == -1 || nRet == 0)
                goto ERROR1;

            MessageBox.Show(this, "dp2Kernel 数据目录安装成功");
            return;
        ERROR1:
            MessageBox.Show(this, strError);
        }

        #region dp2Kernel

        KernelHost kernel_host = null;

        int dp2Kernel_start(
            bool bAutoStart,
            out string strError)
        {
            strError = "";

            Debug.Assert(string.IsNullOrEmpty(this.KernelDataDir) == false, "");

            string strFilename = Path.Combine(this.KernelDataDir, "databases.xml");
            if (File.Exists(strFilename) == false)
            {
                strError = "dp2Kernel XE 尚未初始化";
                return 0;
            }

            if (bAutoStart == true && kernel_host != null)
            {
                strError = "dp2Kernel 先前已经启动了";
                return 0;
            }

            dp2Kernel_stop();

            kernel_host = new KernelHost();
            kernel_host.DataDir = this.KernelDataDir;
            int nRet = kernel_host.Start(out strError);
            if (nRet == -1)
                return -1;

            return 1;
        }

        void dp2Kernel_stop()
        {
            if (kernel_host != null)
            {
                kernel_host.Stop();
                kernel_host = null;
            }
        }

        // 删除以前的目录
        int DeleteDataDirectory(
            string strDirectory,
            out string strError)
        {
            strError = "";

            // 检查目录是否符合规则
            // 不能使用根目录
            string strRoot = Directory.GetDirectoryRoot(strDirectory);
            if (PathUtil.IsEqual(strRoot, strDirectory) == true)
            {
                strError = "数据目录 '" + strDirectory + "' 不合法。不能是根目录";
                return -1;
            }

            try
            {
                PathUtil.RemoveReadOnlyAttr(strDirectory);
            }
            catch
            {
                string strCurrentDir = Directory.GetCurrentDirectory();
                int i = 0;
                i++;
            }

            try
            {
                PathUtil.DeleteDirectory(strDirectory);
            }
            catch (Exception ex)
            {
                string strCurrentDir = Directory.GetCurrentDirectory();

                strError = "删除目录 " + strDirectory + " 的过程中出现错误: " + ex.Message + "。\r\n请手动删除此目录后，重新进行操作"; 
                return -1;
            }
            return 0;
        }

        // 安装 dp2kernel 的数据目录
        // parameters:
        //      bAutoSetup  是否自动安装。自动安装时，如果已经存在数据文件，则不会再次安装。否则会强行重新安装，但安装前会出现对话框警告
        int SetupKernelDataDir(
            bool bAutoSetup,
            out string strError)
        {
            strError = "";
            int nRet = 0;

            string strFilename = Path.Combine(this.KernelDataDir, "databases.xml");
            if (File.Exists(strFilename) == true)
            {
                if (bAutoSetup == true)
                {
                    strError = "dp2Kernel 数据目录先前已经安装过，本次没有重新安装";
                    return 0;
                }

                DialogResult result = MessageBox.Show(this,
    "警告：dp2Kernel 数据目录先前已经安装过了，本次重新安装，将摧毁以前的全部数据。\r\n\r\n确实要重新安装？",
    "dp2Library XE",
    MessageBoxButtons.YesNo,
    MessageBoxIcon.Question,
    MessageBoxDefaultButton.Button2);
                if (result == System.Windows.Forms.DialogResult.No)
                {
                    strError = "放弃重新安装 dp2Kernel 数据目录";
                    return 0;
                }

            }

            dp2Kernel_stop();

            // 删除以前的目录
            nRet = DeleteDataDirectory(
                Path.GetDirectoryName(strFilename),
                out strError);
            if (nRet == -1)
                return -1;
            
#if NO
            if (_messageBar != null)
                _messageBar.MessageText = "正在初始化 dp2Kernel 数据目录 ...";
#endif
            if (string.IsNullOrEmpty(this._floatingMessage.Text) == false)
                this._floatingMessage.Text = "正在初始化 dp2Kernel 数据目录 ...";

            nRet = dp2Kernel_CreateNewDataDir(out strError);
            if (nRet == -1)
                return -1;

            // 创建/修改 databases.xml 文件
            // return:
            //      -1  error
            //      0   succeed
            nRet = dp2Kernel_createXml(this.KernelDataDir,
                out strError);
            if (nRet == -1)
                return -1;

            // 修改root用户记录文件
            // parameters:
            //      strUserName 如果为null，表示不修改用户名
            //      strPassword 如果为null，表示不修改密码
            //      strRights   如果为null，表示不修改权限
            nRet = dp2Kernel_ModifyRootUser(this.KernelDataDir,
                "root",
                "",
                null,
                out strError);
            if (nRet == -1)
                return -1;

            nRet = dp2Kernel_start(true,
                out strError);
            if (nRet == -1)
                return -1;

            return 1;
        }


        // 创建数据目录，并复制进基本内容
        int dp2Kernel_CreateNewDataDir(
            out string strError)
        {
            strError = "";

            string strZipFileName = Path.Combine(this.DataDir, "kernel_data.zip");

            // 要求在 KernelData.zip 内准备要安装的数据文件(初次安装而不是升级安装)
            try
            {
                using (ZipFile zip = ZipFile.Read(strZipFileName))
                {
                    foreach (ZipEntry e in zip)
                    {
                        e.Extract(this.UserDir, ExtractExistingFileAction.OverwriteSilently);
                    }
                }
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                return -1;
            }

#if NO
            int nRet = PathUtil.CopyDirectory(strTempDataDir,
    this.KernelDataDir,
    true,
    out strError);
            if (nRet == -1)
            {
                strError = "拷贝临时目录 '" + strTempDataDir + "' 到数据目录 '" + this.KernelDataDir + "' 时发生错误：" + strError;
                return -1;
            }
#endif

            return 0;
        }

        // 创建/修改 databases.xml 文件
        // return:
        //      -1  error
        //      0   succeed
        public int dp2Kernel_createXml(string strDataDir,
            out string strError)
        {
            strError = "";

            string strFilename = Path.Combine(strDataDir, "databases.xml");
            XmlDocument dom = new XmlDocument();
            try
            {
                dom.Load(strFilename);
            }
            catch (FileNotFoundException)
            {
                strError = "文件 " + strFilename + " 没有找到";
                return -1;
            }
            catch (Exception ex)
            {
                strError = "加载文件 " + strFilename + " 到 XMLDOM 时出错：" + ex.Message;
                return -1;
            }

            XmlNode nodeDatasource = dom.DocumentElement.SelectSingleNode("datasource");
            if (nodeDatasource == null)
            {
                strError = "文件 " + strFilename + " 内容不合法，根下的<datasource>元素不存在。";
                return -1;
            }

            DomUtil.SetAttr(nodeDatasource, "mode", null);

            /*
             * 
    <datasource userid="" password="7E/u3+nbJxg=" servername="~sqlite" servertype="SQLite" />             * 
             * */

            DomUtil.SetAttr(nodeDatasource,
                "servertype",
                "SQLite");
            DomUtil.SetAttr(nodeDatasource,
                "servername",
                "~sqlite");
            DomUtil.SetAttr(nodeDatasource,
                 "userid",
                 "");
#if NO
            string strPassword = Cryptography.Encrypt(this.DatabaseLoginPassword, "dp2003");
            DomUtil.SetAttr(nodeDatasource,
                "password",
                strPassword);
#endif

            XmlNode nodeDbs = dom.DocumentElement.SelectSingleNode("dbs");
            if (nodeDbs == null)
            {
                strError = "文件 " + strFilename + " 内容不合法，根下的<dbs>元素不存在。";
                return -1;
            }
            DomUtil.SetAttr(nodeDbs,
                 "instancename",
                 "");

            dom.Save(strFilename);
            return 0;
        }

        // 修改root用户记录文件
        // parameters:
        //      strUserName 如果为null，表示不修改用户名
        //      strPassword 如果为null，表示不修改密码
        //      strRights   如果为null，表示不修改权限
        static int dp2Kernel_ModifyRootUser(string strDataDir,
            string strUserName,
            string strPassword,
            string strRights,
            out string strError)
        {
            strError = "";

            if (strUserName == null
                && strPassword == null
                && strRights == null)
                return 0;

            string strFileName = PathUtil.MergePath(strDataDir, "userdb\\0000000001.xml");

            XmlDocument dom = new XmlDocument();
            try
            {
                dom.Load(strFileName);
            }
            catch (Exception ex)
            {
                strError = "装载root用户记录文件 " + strFileName + " 到DOM时发生错误: " + ex.Message;
                return -1;
            }

            string strOldUserName = "";
            if (strUserName != null)
            {
                strOldUserName = DomUtil.GetElementText(dom.DocumentElement,
                    "name");
                DomUtil.SetElementText(dom.DocumentElement,
                    "name",
                    strUserName);
            }

            if (strPassword != null)
            {
                DomUtil.SetElementText(dom.DocumentElement, "password",
                    Cryptography.GetSHA1(strPassword));
            }

            if (strRights != null)
            {
                XmlNode nodeServer = dom.DocumentElement.SelectSingleNode("server");
                if (nodeServer == null)
                {
                    Debug.Assert(false, "不可能的情况");
                    strError = "root用户记录文件 " + strFileName + " 格式错误: 根元素下没有<server>元素";
                    return -1;
                }

                DomUtil.SetAttr(nodeServer, "rights", strRights);
            }

            dom.Save(strFileName);

            // 修改keys_name.xml文件
            if (strUserName != null
                && strUserName != strOldUserName)
            {
                strFileName = PathUtil.MergePath(strDataDir, "userdb\\keys_name.xml");

                dom = new XmlDocument();
                try
                {
                    dom.Load(strFileName);
                }
                catch (Exception ex)
                {
                    strError = "装载用户keys文件 " + strFileName + " 到DOM时发生错误: " + ex.Message;
                    return -1;
                }

                XmlNode node = dom.DocumentElement.SelectSingleNode("key/keystring[text()='" + strOldUserName + "']");
                if (node == null)
                {
                    strError = "更新用户keys文件时出错：" + "根下 key/keystring 文本值为 '" + strOldUserName + "' 的元素没有找到";
                    return -1;
                }
                node.InnerText = strUserName;
                dom.Save(strFileName);
            }

            return 0;
        }

        #endregion

        #region dp2Library


        LibraryHost library_host = null;

        int dp2Library_start(
            bool bAutoStart,
            out string strError)
        {
            strError = "";

            Debug.Assert(string.IsNullOrEmpty(this.LibraryDataDir) == false, "");

            string strFilename = Path.Combine(this.LibraryDataDir, "library.xml");
            if (File.Exists(strFilename) == false)
            {
                strError = "dp2Library XE 尚未初始化";
                return 0;
            }

            if (bAutoStart == true && library_host != null)
            {
                strError = "dp2Library 先前已经启动了";
                return 0;
            }

            dp2Library_stop();

            // 获得 监听 URL
#if NO
            if (this.IsServer == true)
            {
                string strUrl = this.AppInfo.GetString("main_form", "listening_url", "");
                if (string.IsNullOrEmpty(strUrl) == true)
                {
                    string strNewUrl = InputDlg.GetInput(
    this,
    "请指定服务器绑定的 URL",
    "URL: ",
    "http://localhost:8001/dp2library/xe",
    this.Font);
                    if (strNewUrl == null)
                    {
                        strNewUrl = "http://localhost:8001/dp2library/xe";
                        MessageBox.Show(this, "自动使用缺省的 URL " + strNewUrl);
                    }

                    this.AppInfo.SetString("main_form", "listening_url", strNewUrl);

                    this.LibraryServerUrl = strNewUrl;
                }
                else
                    this.LibraryServerUrl = strUrl;
            }
            else
            {
                this.LibraryServerUrl = "net.pipe://localhost/dp2library/xe";
            }
#endif

            // 检查监听 URL
            if (this.IsServer == true)
            {
                this.LibraryServerUrlList = this.AppInfo.GetString("main_form", "listening_url", "");
                if (string.IsNullOrEmpty(this.LibraryServerUrlList) == true)
                {
                    strError = "尚未正确配置监听URL， dp2library server 无法启动";
                    return -1;
                }


                // TODO: 必须是 http net.tcp 协议之一
            }
            else
            {
                // 强制设置为固定值
                this.LibraryServerUrlList = LibraryHost.default_single_url; //  "net.pipe://localhost/dp2library/xe";
            }

#if NO
            if (this.IsServer == true)
                this.LibraryServerUrl = "http://localhost/dp2library/xe";
            else
                this.LibraryServerUrl = "net.pipe://localhost/dp2library/xe";
#endif

            library_host = new LibraryHost();
            library_host.DataDir = this.LibraryDataDir;
            library_host.HostUrl = this.LibraryServerUrlList;  
            int nRet = library_host.Start(out strError);
            if (nRet == -1)
                return -1;

            if (this.library_host != null)
            {
                this.library_host.SetTestMode(this.TestMode);
                this.library_host.SetMaxClients(this.MaxClients);
                this.library_host.SetLicenseType(this.LicenseType);
            }

            return 1;
        }

        void dp2Library_stop()
        {
            if (library_host != null)
            {
                library_host.Stop();
                library_host = null;
            }
        }

        // 安装 dp2Library 的数据目录
        // parameters:
        //      bAutoSetup  是否自动安装。自动安装时，如果已经存在数据文件，则不会再次安装。否则会强行重新安装，但安装前会出现对话框警告
        int SetupLibraryDataDir(
            bool bAutoSetup,
            out string strError)
        {
            strError = "";
            int nRet = 0;

            // TODO: 是否改为探测目录是否存在?

            string strFilename = PathUtil.MergePath(this.LibraryDataDir, "library.xml");
            if (File.Exists(strFilename) == true)
            {
                if (bAutoSetup == true)
                {
                    strError = "dp2Library 数据目录先前已经安装过，本次没有重新安装";
                    return 0;
                }

                DialogResult result = MessageBox.Show(this,
    "警告：dp2Library 数据目录先前已经安装过了，本次重新安装，将摧毁以前的全部数据。\r\n\r\n确实要重新安装？",
    "dp2Library XE",
    MessageBoxButtons.YesNo,
    MessageBoxIcon.Question,
    MessageBoxDefaultButton.Button2);
                if (result == System.Windows.Forms.DialogResult.No)
                {
                    strError = "放弃重新安装 dp2Library 数据目录";
                    return 0;
                }
            }

            // 删除 dp2kernel 中的全部数据库

            dp2Library_stop();
            dp2Kernel_stop();

            // 删除以前的目录
            nRet = DeleteDataDirectory(
                Path.GetDirectoryName(strFilename),
                out strError);
            if (nRet == -1)
                return -1;

            // 清空残留的前端密码，避免后面登录时候的困惑
            AppInfo.SetString(
    "default_account",
    "password",
    "");

#if NO
            if (_messageBar != null)
                _messageBar.MessageText = "正在初始化 dp2Library 数据目录 ...";
#endif
            if (string.IsNullOrEmpty(this._floatingMessage.Text) == false)
                this._floatingMessage.Text = "正在初始化 dp2Library 数据目录 ...";


            nRet = dp2Library_CreateNewDataDir(out strError);
            if (nRet == -1)
                return -1;

            // 创建/修改 library.xml 文件
            // return:
            //      -1  error
            //      0   succeed
            nRet = dp2Library_createXml(this.LibraryDataDir,
                "supervisor",
                "",
                null,
                "本地图书馆",
                out strError);
            if (nRet == -1)
                return -1;

            // TODO: 每次升级安装后，需要覆盖 templates 目录和 cfgs 目录

            nRet = dp2Kernel_start(true,
                out strError);
            if (nRet == -1)
                return -1;
            nRet = dp2Library_start(true,
                out strError);
            if (nRet == -1)
                return -1;

#if NO
            if (_messageBar != null)
                _messageBar.MessageText = "正在创建基本数据库，可能需要几分钟时间 ...";
#endif
            if (string.IsNullOrEmpty(this._floatingMessage.Text) == false)
                this._floatingMessage.Text = "正在创建基本数据库，可能需要几分钟时间 ...";


            // 创建缺省的几个数据库
            nRet = CreateDefaultDatabases(out strError);
            if (nRet == -1)
            {
                strError = "创建数据库时出错: " + strError;
                return -1;
            }

            return 1;
        }

        // 创建缺省的几个数据库
        // TODO: 把过程显示在控制台
        // return:
        //      -1  出错
        //      0   成功
        int CreateDefaultDatabases(out string strError)
        {
            strError = "";

            int nRet = PrepareSearch();

            EnableControls(false);

            Stop.OnStop += new StopEventHandler(this.DoStop);
            Stop.Initial("正在创建数据库 ...");
            Stop.BeginLoop();

            try
            {
                return ManageHelper.CreateDefaultDatabases(Channel, Stop, null, out strError);
            }
            finally
            {
                Stop.EndLoop();
                Stop.OnStop -= new StopEventHandler(this.DoStop);
                Stop.Initial("");

                EnableControls(true);

                EndSearch();
            }
        }

#if NO
        // 创建缺省的几个数据库
        // TODO: 把过程显示在控制台
        // return:
        //      -1  出错
        //      0   成功
        int CreateDefaultDatabases(out string strError)
        {
            strError = "";

            // 创建书目库的定义
            XmlDocument database_dom = new XmlDocument();
            database_dom.LoadXml("<root />");

            List<string> biblio_dbnames = new List<string>();

            // 创建书目库
            {
                // parameters:
                //      strUsage    book/series
                //      strSyntax   unimarc/usmarc
                CreateBiblioDatabaseNode(database_dom,
                    "中文图书",
                    "book",
                    "orderRecommendStore",
                    "unimarc",
                    true);
                biblio_dbnames.Add("中文图书");

                CreateBiblioDatabaseNode(database_dom,
    "中文期刊",
    "series",
    "",
    "unimarc",
    true);
                biblio_dbnames.Add("中文期刊");

                CreateBiblioDatabaseNode(database_dom,
    "西文图书",
    "book",
    "",
    "usmarc",
    true);
                biblio_dbnames.Add("西文图书");

                CreateBiblioDatabaseNode(database_dom,
    "西文期刊",
    "series",
    "",
    "usmarc",
    true);
                biblio_dbnames.Add("西文期刊");

            }

            // 创建读者库
            CreateReaderDatabaseNode(database_dom,
                "读者",
                "",
                true);

            // 预约到书
            CreateSimpleDatabaseNode(database_dom,
    "预约到书",
    "arrived");

            // 违约金
            CreateSimpleDatabaseNode(database_dom,
                "违约金",
                "amerce");

            // 出版者
            CreateSimpleDatabaseNode(database_dom,
    "出版者",
    "publisher");

            // 消息
            CreateSimpleDatabaseNode(database_dom,
    "消息",
    "message");


            // 创建 OPAC 数据库的定义
            XmlDocument opac_dom = new XmlDocument();
            opac_dom.LoadXml("<virtualDatabases />");

            foreach (string dbname in biblio_dbnames)
            {
                XmlElement node = opac_dom.CreateElement("database");
                opac_dom.DocumentElement.AppendChild(node);
                node.SetAttribute("name", dbname);
            }

            // 浏览格式
            // 插入格式节点
            XmlDocument browse_dom = new XmlDocument();
            browse_dom.LoadXml("<browseformats />");

            foreach (string dbname in biblio_dbnames)
            {
                XmlElement database = browse_dom.CreateElement("database");
                browse_dom.DocumentElement.AppendChild(database);
                database.SetAttribute("name", dbname);

                XmlElement format = browse_dom.CreateElement("format");
                database.AppendChild(format);
                format.SetAttribute("name", "详细");
                format.SetAttribute("type", "biblio");
                format.InnerXml = "<caption lang=\"zh-CN\">详细</caption><caption lang=\"en\">Detail</caption>";
            }

            int nRet = PrepareSearch();
            try
            {
                EnableControls(false);

                Stop.OnStop += new StopEventHandler(this.DoStop);
                Stop.Initial("正在创建数据库 ...");
                Stop.BeginLoop();

                try
                {
                    string strOutputInfo = "";
                    long lRet = Channel.ManageDatabase(
                        Stop,
                        "create",
                        "",
                        database_dom.OuterXml,
                        out strOutputInfo,
                        out strError);
                    if (lRet == -1)
                        return -1;

                    lRet = Channel.SetSystemParameter(
    Stop,
    "opac",
    "databases",
    opac_dom.DocumentElement.InnerXml,
    out strError);
                    if (lRet == -1)
                        return -1;

                    lRet = Channel.SetSystemParameter(
    Stop,
    "opac",
    "browseformats",
    browse_dom.DocumentElement.InnerXml,
    out strError);
                    if (lRet == -1)
                        return -1;

                    return 0;
                }
                finally
                {
                    Stop.EndLoop();
                    Stop.OnStop -= new StopEventHandler(this.DoStop);
                    Stop.Initial("");

                    EnableControls(true);
                }
            }
            finally
            {
                EndSearch();
            }
        }

#endif

        void DoStop(object sender, StopEventArgs e)
        {
            if (this.Channel != null)
                this.Channel.Abort();
        }

        /// <summary>
        /// 通讯通道。MainForm 自己使用
        /// </summary>
        public LibraryChannel Channel = new LibraryChannel();

        /// <summary>
        /// 停止控制
        /// </summary>
        public DigitalPlatform.Stop Stop = null;

        /// <summary>
        /// 准备进行检索
        /// </summary>
        /// <returns>0: 没有成功; 1: 成功</returns>
        public int PrepareSearch()
        {
            if (String.IsNullOrEmpty(this.LibraryServerUrlList) == true)
                return 0;

            if (this.Channel == null)
                this.Channel = new LibraryChannel();

            this.Channel.Url = GetFirstUrl(this.LibraryServerUrlList);

            this.Channel.BeforeLogin -= new DigitalPlatform.CirculationClient.BeforeLoginEventHandle(Channel_BeforeLogin);
            this.Channel.BeforeLogin += new DigitalPlatform.CirculationClient.BeforeLoginEventHandle(Channel_BeforeLogin);

            Stop = new DigitalPlatform.Stop();
            Stop.Register(stopManager, true);	// 和容器关联

            return 1;
        }

        static string GetFirstUrl(string strUrlList)
        {
            List<string> urls = StringUtil.SplitList(strUrlList, ';');
            if (urls.Count == 0)
                return "";

            return urls[0];
        }

        /// <summary>
        /// 结束检索
        /// </summary>
        /// <returns>返回 0</returns>
        public int EndSearch()
        {
            if (Stop != null) // 脱离关联
            {
                Stop.Unregister();	// 和容器关联
                Stop = null;
            }

            this.Channel.BeforeLogin -= new DigitalPlatform.CirculationClient.BeforeLoginEventHandle(Channel_BeforeLogin);
            this.Channel.Close();
            this.Channel = null;

            return 0;
        }

        internal void Channel_BeforeLogin(object sender,
    DigitalPlatform.CirculationClient.BeforeLoginEventArgs e)
        {
            if (e.FirstTry == true)
            {
                e.UserName = this.SupervisorUserName;   //  "supervisor";

                e.Password = this.DecryptPasssword(AppInfo.GetString(
"default_account",
"password",
""));

                string strLocation = "manager";
                e.Parameters = "location=" + strLocation;

                if (String.IsNullOrEmpty(e.UserName) == false)
                    return; // 立即返回, 以便作第一次 不出现 对话框的自动登录

            }

#if NO
            e.Cancel = true;
            e.ErrorInfo = "管理帐户无效"; 
#endif
            // 
            IWin32Window owner = null;

            if (sender is IWin32Window)
                owner = (IWin32Window)sender;
            else
                owner = this;





            CirculationLoginDlg dlg = SetDefaultAccount(
                e.LibraryServerUrl,
                null,
                e.ErrorInfo,
                e.LoginFailCondition,
                owner);
            if (dlg == null)
            {
                e.Cancel = true;
                return;
            }


            e.UserName = dlg.UserName;
            e.Password = dlg.Password;
            e.SavePasswordShort = dlg.SavePasswordShort;
            e.Parameters = "location=" + dlg.OperLocation;

#if NO
            if (dlg.IsReader == true)
                e.Parameters += ",type=reader";

            // 2014/9/13
            e.Parameters += ",mac=" + StringUtil.MakePathList(SerialCodeForm.GetMacAddress(), "|");

            // 从序列号中获得 expire= 参数值
            {
                string strExpire = GetExpireParam();
                if (string.IsNullOrEmpty(strExpire) == false)
                    e.Parameters += ",expire=" + strExpire;
            }

            // 2014/10/23
            if (this.TestMode == true)
                e.Parameters += ",testmode=true";
#endif

            e.SavePasswordLong = dlg.SavePasswordLong;
            if (e.LibraryServerUrl != dlg.ServerUrl)
            {
                e.LibraryServerUrl = dlg.ServerUrl;
                // _expireVersionChecked = false;
            }

        }

        public string SupervisorUserName
        {
            get
            {
                if (this.AppInfo == null)
                    return "supervisor";
                return AppInfo.GetString(
    "default_account",
    "username",
    "supervisor");
            }
            set
            {
                AppInfo.SetString(
    "default_account",
    "username",
    value);
            }
        }

        // parameters:
        //      bLogin  是否在对话框后立即登录？如果为false，表示只是设置缺省帐户，并不直接登录
        CirculationLoginDlg SetDefaultAccount(
            string strServerUrl,
            string strTitle,
            string strComment,
            LoginFailCondition fail_contidion,
            IWin32Window owner)
        {
            CirculationLoginDlg dlg = new CirculationLoginDlg();
            MainForm.SetControlFont(dlg, this.Font);

            if (String.IsNullOrEmpty(strServerUrl) == true)
            {
                dlg.ServerUrl = GetFirstUrl(this.LibraryServerUrlList);
            }
            else
            {
                dlg.ServerUrl = strServerUrl;
            }

            if (owner == null)
                owner = this;

            if (String.IsNullOrEmpty(strTitle) == false)
                dlg.Text = strTitle;
#if NO
            if (bLogin == false)
                dlg.SetDefaultMode = true;
#endif

            dlg.SupervisorMode = true;

            dlg.Comment = strComment;
            dlg.UserName = AppInfo.GetString(
                "default_account",
                "username",
                "supervisor");

            dlg.SavePasswordShort =
    AppInfo.GetBoolean(
    "default_account",
    "savepassword_short",
    false);

            dlg.SavePasswordLong =
                AppInfo.GetBoolean(
                "default_account",
                "savepassword_long",
                false);

            if (dlg.SavePasswordShort == true || dlg.SavePasswordLong == true)
            {
                dlg.Password = AppInfo.GetString(
        "default_account",
        "password",
        "");
                dlg.Password = this.DecryptPasssword(dlg.Password);
            }
            else
            {
                dlg.Password = "";
            }

            dlg.IsReader = false;
            dlg.OperLocation = AppInfo.GetString(
                "default_account",
                "location",
                "");

            this.AppInfo.LinkFormState(dlg,
                "logindlg_state");

            if (fail_contidion == LoginFailCondition.PasswordError
                && dlg.SavePasswordShort == false
                && dlg.SavePasswordLong == false)
                dlg.AutoShowShortSavePasswordTip = true;

            dlg.ShowDialog(owner);

            this.AppInfo.UnlinkFormState(dlg);

            if (dlg.DialogResult == DialogResult.Cancel)
            {
                return null;
            }

            AppInfo.SetString(
                "default_account",
                "username",
                dlg.UserName);
            AppInfo.SetString(
                "default_account",
                "password",
                (dlg.SavePasswordShort == true || dlg.SavePasswordLong == true) ?
                this.EncryptPassword(dlg.Password) : "");

            AppInfo.SetBoolean(
    "default_account",
    "savepassword_short",
    dlg.SavePasswordShort);

            AppInfo.SetBoolean(
                "default_account",
                "savepassword_long",
                dlg.SavePasswordLong);

            AppInfo.SetString(
                "default_account",
                "location",
                dlg.OperLocation);

#if NO
            AppInfo.SetString(
                "config",
                "circulation_server_url",
                dlg.ServerUrl);
#endif
            return dlg;
        }

        string EncryptKey = "dp2libraryxe_client_password_key";

        internal string DecryptPasssword(string strEncryptedText)
        {
            if (String.IsNullOrEmpty(strEncryptedText) == false)
            {
                try
                {
                    string strPassword = Cryptography.Decrypt(
        strEncryptedText,
        EncryptKey);
                    return strPassword;
                }
                catch
                {
                    return "errorpassword";
                }
            }

            return "";
        }

        internal string EncryptPassword(string strPlainText)
        {
            return Cryptography.Encrypt(strPlainText, this.EncryptKey);
        }

                /// <summary>
        /// 允许或者禁止界面控件。在长操作前，一般需要禁止界面控件；操作完成后再允许
        /// </summary>
        /// <param name="bEnable">是否允许界面控件。true 为允许， false 为禁止</param>
        public void EnableControls(bool bEnable)
        {
            this.menuStrip1.Enabled = bEnable;
        }



        // 创建数据目录，并复制进基本内容
        int dp2Library_CreateNewDataDir(
            out string strError)
        {
            strError = "";

            string strZipFileName = Path.Combine(this.DataDir, "library_data.zip");

            // 要求在 library_data.zip 内准备要安装的数据文件(初次安装而不是升级安装)
            try
            {
                using (ZipFile zip = ZipFile.Read(strZipFileName))
                {
                    foreach (ZipEntry e in zip)
                    {
                        try
                        {
                            e.Extract(this.UserDir, ExtractExistingFileAction.OverwriteSilently);
                        }
                        catch (Exception ex)
                        {
                            strError = ex.Message;
                            return -1;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                return -1;
            }
#if NO
            int nRet = PathUtil.CopyDirectory(strTempDataDir,
    this.LibraryDataDir,
    true,
    out strError);
            if (nRet == -1)
            {
                strError = "拷贝临时目录 '" + strTempDataDir + "' 到数据目录 '" + this.LibraryDataDir + "' 时发生错误：" + strError;
                return -1;
            }
#endif

            return 0;
        }

        // 备份一个文件
        // 顺次备份为 ._1 ._2 ...
        static void BackupFile(string strFullPath)
        {
            for(int i = 0;;i++)
            {
                string strBackupFilePath = strFullPath + "._" + (i+1).ToString();
                if (File.Exists(strBackupFilePath) == false)
                {
                    File.Copy(strFullPath, strBackupFilePath);
                    return;
                }
            }
        }

        // 从 library_data.zip 中展开部分目录内容
        int dp2Library_extractPartDir(
            out string strError)
        {
            strError = "";

            string strCfgsDir = Path.Combine(this.UserDir, "library_data/cfgs");
            string strTemplatesDir = Path.Combine(this.UserDir, "library_data/templates");

            string strZipFileName = Path.Combine(this.DataDir, "library_data.zip");

            // 要求在 library_data.zip 内准备要安装的数据文件(初次安装而不是升级安装)
            try
            {
                using (ZipFile zip = ZipFile.Read(strZipFileName))
                {
                    foreach (ZipEntry e in zip)
                    {
                        string strFullPath = Path.Combine(this.UserDir, e.FileName);


                        // 测试strPath1是否为strPath2的下级目录或文件
                        //	strPath1正好等于strPath2的情况也返回true
                        if (PathUtil.IsChildOrEqual(strFullPath, strTemplatesDir) == true)
                        {
                            e.Extract(this.UserDir, ExtractExistingFileAction.OverwriteSilently);
                        }
                        else if (PathUtil.IsChildOrEqual(strFullPath, strCfgsDir) == true)
                        {
                            // 观察文件版本
                            if (File.Exists(strFullPath) == true)
                            {
                                string strTimestamp = "";
                                int nRet = _versionManager.GetFileVersion(strFullPath, out strTimestamp);
                                if (nRet == 1)
                                {
                                    // .zip 中的对应文件的时间戳
                                    string strZipTimesamp = e.LastModified.ToString();
                                    if (strZipTimesamp == strTimestamp)
                                        continue;

                                    // 看看当前物理文件是否已经是修改过
                                    string strPhysicalTimestamp = File.GetLastWriteTime(strFullPath).ToString();
                                    if (strPhysicalTimestamp != strTimestamp)
                                    {
                                        // 需要备份
                                        BackupFile(strFullPath);
                                    }


                                }
                            }


                            e.Extract(this.UserDir, ExtractExistingFileAction.OverwriteSilently);
                            if ((e.Attributes & FileAttributes.Directory) == 0)
                            {
                                if (e.LastModified != File.GetLastWriteTime(strFullPath))
                                {
                                    /*
#if LOG
                                    string strText = "文件 " + strFullPath + " 的最后修改时间为 '" + File.GetLastWriteTime(strFullPath).ToString() + "'，不是期望的 '" + e.LastModified.ToString() + "' ";
                                    WriteLibraryEventLog(strText, EventLogEntryType.Information);
#endif
                                     * */
                                    File.SetLastWriteTime(strFullPath, e.LastModified);
                                }
                                Debug.Assert(e.LastModified == File.GetLastWriteTime(strFullPath));
                                _versionManager.SetFileVersion(strFullPath, e.LastModified.ToString());
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                return -1;
            }

            _versionManager.AutoSave();
            return 0;
        }

        // 创建/修改 library.xml 文件
        // return:
        //      -1  error
        //      0   succeed
        public int dp2Library_createXml(string strDataDir,
            string strSupervisorUserName,
            string strSupervisorPassword,
            string strSupervisorRights,
            string strLibraryName,
            out string strError)
        {
            strError = "";

            string strFilename = PathUtil.MergePath(strDataDir, "library.xml");
            XmlDocument dom = new XmlDocument();
            try
            {
                dom.Load(strFilename);
            }
            catch (FileNotFoundException)
            {
                strError = "文件 " + strFilename + " 没有找到";
                return -1;
            }
            catch (Exception ex)
            {
                strError = "加载文件 " + strFilename + " 到 XMLDOM 时出错：" + ex.Message;
                return -1;
            }

            XmlNode nodeRmsServer = dom.DocumentElement.SelectSingleNode("rmsserver");
            if (nodeRmsServer == null)
            {
                nodeRmsServer = dom.CreateElement("rmsserver");
                dom.DocumentElement.AppendChild(nodeRmsServer);
            }

            DomUtil.SetAttr(nodeRmsServer,
                "url",
                "net.pipe://localhost/dp2kernel/XE"
                );
            DomUtil.SetAttr(nodeRmsServer,
                 "username",
                 "root");

            string strPassword = Cryptography.Encrypt("", "dp2circulationpassword");
            DomUtil.SetAttr(nodeRmsServer,
                "password",
                strPassword);

            // 
            XmlNode nodeAccounts = dom.DocumentElement.SelectSingleNode("accounts");
            if (nodeAccounts == null)
            {
                nodeAccounts = dom.CreateElement("accounts");
                dom.DocumentElement.AppendChild(nodeAccounts);
            }
            XmlNode nodeSupervisor = nodeAccounts.SelectSingleNode("account[@type='']");
            if (nodeSupervisor == null)
            {
                nodeSupervisor = dom.CreateElement("account");
                nodeAccounts.AppendChild(nodeSupervisor);
            }

            if (strSupervisorUserName != null)
                DomUtil.SetAttr(nodeSupervisor, "name", strSupervisorUserName);
            if (strSupervisorPassword != null)
            {
                DomUtil.SetAttr(nodeSupervisor, "password",
                    Cryptography.Encrypt(strSupervisorPassword, "dp2circulationpassword")
                    );
            }
            if (strSupervisorRights != null)
                DomUtil.SetAttr(nodeSupervisor, "rights", strSupervisorRights);

            if (strLibraryName != null)
            {
                DomUtil.SetElementText(dom.DocumentElement,
                        "libraryInfo/libraryName",
                        strLibraryName);
            }

            dom.Save(strFilename);
            return 0;
        }

        // 修改 library.xml 文件，创建或修改用户帐户
        // parameters:
        //      strStyle    写入风格。 merge 表示需要和以前的权限合并
        // return:
        //      -1  error
        //      0   succeed
        public int dp2Library_changeXml_addAccount(string strDataDir,
            string strUserName,
            string strType,
            string strRights,
            string strStyle,
            out string strError)
        {
            strError = "";

            string strFilename = PathUtil.MergePath(strDataDir, "library.xml");
            XmlDocument dom = new XmlDocument();
            try
            {
                dom.Load(strFilename);
            }
            catch (FileNotFoundException)
            {
                strError = "文件 " + strFilename + " 没有找到";
                return -1;
            }
            catch (Exception ex)
            {
                strError = "加载文件 " + strFilename + " 到 XMLDOM 时出错：" + ex.Message;
                return -1;
            }

            XmlElement accounts_root = dom.DocumentElement.SelectSingleNode("accounts") as XmlElement;
            if (accounts_root == null)
            {
                accounts_root = dom.CreateElement("accounts");
                dom.DocumentElement.AppendChild(accounts_root);
            }

            XmlElement account = accounts_root.SelectSingleNode("account[@name='"+strUserName+"']") as XmlElement;
            if (account == null)
            {
                account = dom.CreateElement("account");
                accounts_root.AppendChild(account);
            }

            account.SetAttribute("name", strUserName);
            if (string.IsNullOrEmpty(strType) == false)
                account.SetAttribute("type", strType);
            else
                account.RemoveAttribute("type");

            string strOldRights = account.GetAttribute("rights");
            if (StringUtil.IsInList("merge", strStyle) == true)
            {
                List<string> old_rights = StringUtil.SplitList(strOldRights, ',');
                List<string> new_rights = StringUtil.SplitList(strRights, ',');
                new_rights.AddRange(old_rights);
                StringUtil.RemoveDupNoSort(ref new_rights);
                strRights = StringUtil.MakePathList(new_rights);
            }

            account.SetAttribute("rights", strRights);

            dom.Save(strFilename);
            return 0;
        }

        #endregion

        private void button_setupLibraryDataDir_Click(object sender, EventArgs e)
        {
            string strError = "";
            // 安装 dp2Library 的数据目录
            // parameters:
            //      bAutoSetup  是否自动安装。自动安装时，如果已经存在数据文件，则不会再次安装。否则会强行重新安装，但安装前会出现对话框警告
            int nRet = SetupLibraryDataDir(
                false,
                out strError);
            if (nRet == -1 || nRet == 0)
                goto ERROR1;

            MessageBox.Show(this, "dp2Library 数据目录安装成功");
            return;
        ERROR1:
            MessageBox.Show(this, strError);
        }

        private void MenuItem_exit_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void MenuItem_setupKernelDataDir_Click(object sender, EventArgs e)
        {
            string strError = "";
            // 安装 dp2kernel 的数据目录
            // parameters:
            //      bAutoSetup  是否自动安装。自动安装时，如果已经存在数据文件，则不会再次安装。否则会强行重新安装，但安装前会出现对话框警告
            int nRet = SetupKernelDataDir(
                false,
                out strError);
            if (nRet == -1 || nRet == 0)
                goto ERROR1;

            MessageBox.Show(this, "dp2Kernel 数据目录安装成功");
            return;
        ERROR1:
            MessageBox.Show(this, strError);
        }

        private void MenuItem_setupLibraryDataDir_Click(object sender, EventArgs e)
        {
            string strError = "";
            // 安装 dp2Library 的数据目录
            // parameters:
            //      bAutoSetup  是否自动安装。自动安装时，如果已经存在数据文件，则不会再次安装。否则会强行重新安装，但安装前会出现对话框警告
            int nRet = SetupLibraryDataDir(
                false,
                out strError);
            if (nRet == -1 || nRet == 0)
                goto ERROR1;

            MessageBox.Show(this, "dp2Library 数据目录安装成功");
            return;
        ERROR1:
            MessageBox.Show(this, strError);
        }

        private void MenuItem_openLibraryWsdl_Click(object sender, EventArgs e)
        {
            Process.Start("IExplore.exe", library_host.MetadataUrl);
        }

        private void MenuItem_openKernelWsdl_Click(object sender, EventArgs e)
        {
            Process.Start("IExplore.exe", kernel_host.MetadataUrl);
        }

        private void toolButton_stop_Click(object sender, EventArgs e)
        {
            if (Control.ModifierKeys == Keys.Control)
                stopManager.DoStopAll(null);    // 2012/3/25
            else
                stopManager.DoStopActive();

        }

        private void ToolStripMenuItem_stopAll_Click(object sender, EventArgs e)
        {
            stopManager.DoStopAll(null);
        }

        private void MenuItem_openUserFolder_Click(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(this.UserDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message);
            }
        }

        private void MenuItem_openDataFolder_Click(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(this.DataDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message);
            }
        }

        private void MenuItem_openProgramFolder_Click(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(Environment.CurrentDirectory);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message);
            }
        }

        void CreateEventSource()
        {
            // 创建事件日志目录
            if (!EventLog.SourceExists("dp2Library"))
            {
                EventLog.CreateEventSource("dp2Library", "DigitalPlatform");
            }
            if (!EventLog.SourceExists("dp2Kernel"))
            {
                EventLog.CreateEventSource("dp2Kernel", "DigitalPlatform");
            }
        }

        void WriteLibraryEventLog(
            string strText,
            EventLogEntryType type)
        {
            EventLog Log = new EventLog();
            Log.Source = "dp2Library";
            Log.WriteEntry(strText, type);
        }

        void WriteKernelEventLog(
    string strText,
    EventLogEntryType type)
        {
            EventLog Log = new EventLog();
            Log.Source = "dp2Kernel";
            Log.WriteEntry(strText, type);
        }

        // 是否为小型服务器版本
        bool IsServer
        {
            get
            {
                string strMode = this.AppInfo.GetString("main_form", "last_mode", "standard");
                if (strMode == "miniServer")
                    return true;
                return false;
                // return this.AppInfo.GetBoolean("product", "isServer", false);
            }
            /*
            set
            {
                this.AppInfo.SetBoolean("product", "isServer", value);
            }
             * */
        }

        private void MenuItem_resetSerialCode_Click(object sender, EventArgs e)
        {
#if SN
            string strError = "";
            int nRet = 0;

            // 2014/11/15
            string strFirstMac = "";
            List<string> macs = SerialCodeForm.GetMacAddress();
            if (macs.Count != 0)
            {
                strFirstMac = macs[0];
            }

            // 修改前的模式
            string strOldMode = this.AppInfo.GetString("main_form", "last_mode", "standard");

            string strSerialCode = "";
        REDO_VERIFY:

            //string strLocalString = GetEnvironmentString(this.IsServer);

            //string strSha1 = Cryptography.GetSHA1(StringUtil.SortParams(strLocalString) + "_reply");

            if (
                // MatchLocalString(this.IsServer, strSerialCode) == false

                (MatchLocalString(this.IsServer, strSerialCode) == false
                && MatchLocalString(!this.IsServer, strSerialCode) == false)

                || String.IsNullOrEmpty(strSerialCode) == true)
            {
                if (String.IsNullOrEmpty(strSerialCode) == false)
                {
                    MessageBox.Show(this, "序列号无效。请重新输入");
                }

                // 出现设置序列号对话框
                nRet = ResetSerialCode(
                    true,
                    strSerialCode,
                    GetEnvironmentString(this.IsServer, strFirstMac));
                if (nRet == 0)
                {
                    strError = "放弃";
                    goto ERROR1;
                }

                strSerialCode = this.AppInfo.GetString("sn", "sn", "");
                if (string.IsNullOrEmpty(strSerialCode) == true)
                {
                    Application.Exit();
                    return;
                }
                if (strSerialCode == "test")
                {
                    this.TestMode = true;
                    this.AppInfo.SetString("main_form", "last_mode", "test");
                    this.AppInfo.Save();
                    return;
                }
                else
                    this.TestMode = false;

                // 如果小型服务器/单机方式发生了改变
                if (MatchLocalString(!this.IsServer, strSerialCode) == true)
                {
                    if (this.IsServer == true)
                    {
                        // 反转为 单机版
                        if (this.TestMode == true)
                            this.AppInfo.SetString("main_form", "last_mode", "test");
                        else
                            this.AppInfo.SetString("main_form", "last_mode", "standard");
                    }
                    else
                    {
                        // 反转为小型服务器
                        this.AppInfo.SetString("main_form", "last_mode", "miniServer");
                        this.TestMode = false;
                    }

                    // 设置监听 URL
                    SetListenUrl(this.AppInfo.GetString("main_form", "last_mode", "standard"));

                    SetTitle();
                }

                // 解析 product 参数，重新设置授权模式
                {
                    Hashtable table = StringUtil.ParseParameters(GetEnvironmentString(this.IsServer, ""));
                    string strProduct = (string)table["product"];

                    if (strProduct == "dp2libraryXE server")
                        this.AppInfo.SetString("main_form", "last_mode", "miniServer");
                    else if (this.TestMode == true)
                        this.AppInfo.SetString("main_form", "last_mode", "test");
                    else
                        this.AppInfo.SetString("main_form", "last_mode", "standard");
                }
                this.AppInfo.Save();

                goto REDO_VERIFY;
            }

            // 修改后的模式
            //string strNewMode = this.AppInfo.GetString("main_form", "last_mode", "standard");
            //if (strOldMode != strNewMode)
            {
                GetMaxClients();
                GetLicenseType();
                // 重新启动
                RestartDp2libraryIfNeed();
            }

            return;
        ERROR1:
            MessageBox.Show(this, strError);
#endif
        }

        // parameters:
        //      bForce  是否强制设置。强制设置是指DefaultFont == null 的时候，也要按照Control.DefaultFont来设置
        /// <summary>
        /// 设置控件字体
        /// </summary>
        /// <param name="control">控件</param>
        /// <param name="font">字体</param>
        /// <param name="bForce">是否强制设置。强制设置是指DefaultFont == null 的时候，也要按照Control.DefaultFont来设置</param>
        public static void SetControlFont(Control control,
            Font font,
            bool bForce = false)
        {
            if (font == null)
            {
                if (bForce == false)
                    return;
                font = Control.DefaultFont;
            }
            if (font.Name == control.Font.Name
                && font.Style == control.Font.Style
                && font.SizeInPoints == control.Font.SizeInPoints)
            { }
            else
                control.Font = font;

            ChangeDifferentFaceFont(control, font);
        }

        static void ChangeDifferentFaceFont(Control parent,
            Font font)
        {
            // 修改所有下级控件的字体，如果字体名不一样的话
            foreach (Control sub in parent.Controls)
            {
                Font subfont = sub.Font;

#if NO
                float ratio = subfont.SizeInPoints / font.SizeInPoints;
                if (subfont.Name != font.Name
                    || subfont.SizeInPoints != font.SizeInPoints)
                {
                    sub.Font = new Font(font.FontFamily, ratio * font.SizeInPoints, subfont.Style, GraphicsUnit.Point);


                    // sub.Font = new Font(font, subfont.Style);
                }
#endif
                ChangeFont(font, sub);

                if (sub is ToolStrip)
                {
                    ChangeDifferentFaceFont((ToolStrip)sub, font);
                }

                // 递归
                ChangeDifferentFaceFont(sub, font);
            }
        }

        static void ChangeDifferentFaceFont(ToolStrip tool,
    Font font)
        {
            // 修改所有事项的字体，如果字体名不一样的话
            for (int i = 0; i < tool.Items.Count; i++)
            {
                ToolStripItem item = tool.Items[i];

                Font subfont = item.Font;
                float ratio = subfont.SizeInPoints / font.SizeInPoints;
                if (subfont.Name != font.Name
                    || subfont.SizeInPoints != font.SizeInPoints)
                {
                    // item.Font = new Font(font, subfont.Style);
                    item.Font = new Font(font.FontFamily, ratio * font.SizeInPoints, subfont.Style, GraphicsUnit.Point);
                }
            }
        }
        // 修改一个控件的字体
        static void ChangeFont(Font font,
            Control item)
        {
            Font subfont = item.Font;
            float ratio = subfont.SizeInPoints / font.SizeInPoints;
            if (subfont.Name != font.Name
                || subfont.SizeInPoints != font.SizeInPoints)
            {
                // item.Font = new Font(font, subfont.Style);
                item.Font = new Font(font.FontFamily, ratio * font.SizeInPoints, subfont.Style, GraphicsUnit.Point);
            }
        }

        private void MenuItem_autoStartDp2Circulation_Click(object sender, EventArgs e)
        {
            if (this.AutoStartDp2circulation == false)
            {
                this.AutoStartDp2circulation = true;
            }
            else
            {
                this.AutoStartDp2circulation = false;
            }
        }

        private void MenuItem_copyright_Click(object sender, EventArgs e)
        {
            MessageBox.Show(this, "dp2Library XE\r\ndp2 图书馆集成系统 图书馆应用服务器 单机版/小型版\r\n\r\n(C)2006-2015 版权所有 数字平台(北京)软件有限责任公司");
        }

        private void MenuItem_setListeningUrl_Click(object sender, EventArgs e)
        {
            if (this.IsServer == true)
            {
                string strUrl = this.AppInfo.GetString("main_form", "listening_url", "");
                if (string.IsNullOrEmpty(strUrl) == true)
                    strUrl = LibraryHost.default_miniserver_urls;   //  "http://localhost:8001/dp2library/xe;net.pipe://localhost/dp2library/xe";

                string strNewUrl = InputDlg.GetInput(
this,
"请指定服务器绑定的 URL",
"URL: (多个 URL 之间可以用分号间隔)",
strUrl,
this.Font);
                if (strNewUrl == null)
                {
                    MessageBox.Show(this, "放弃修改");
                    return;
                }

                this.AppInfo.SetString("main_form", "listening_url", strNewUrl);
                this.LibraryServerUrlList = strNewUrl;

                // 重新启动
                RestartDp2libraryIfNeed();

            }
            else
            {
                MessageBox.Show(this, "单机版监听 URL 为 " + this.LibraryServerUrlList + "， 不可修改");
            }

        }

        // 如果必要，重新启动 dp2library
        void RestartDp2libraryIfNeed()
        {
            dp2Library_stop();

            // 重新启动
            if (library_host == null)
            {
                string strError = "";
                int nRet = dp2Library_start(
                    false,
                    out strError);
                if (nRet == -1)
                {
                    MessageBox.Show(this, "重新启动 dp2Library 时出错：" + strError);
                    return;
                }
            }
        }

        // 从安装包更新数据目录中的配置文件
        private void MenuItem_updateDataDir_Click(object sender, EventArgs e)
        {
            string strError = "";
            // 从 library_data.zip 中展开部分目录内容
            int nRet = dp2Library_extractPartDir(out strError);
            if (nRet == -1)
                goto ERROR1;

            return;
        ERROR1:
            MessageBox.Show(this, strError);
        }

        FileVersionManager _versionManager = new FileVersionManager();

        // 更新 library_data 中的 cfgs 子目录 和 templates 子目录
        void UpdateCfgs()
        {
            string strError = "";

            string strZipFileName = Path.Combine(this.DataDir, "library_data.zip");

            string strOldTimestamp = "";
            int nRet = _versionManager.GetFileVersion(Path.GetFileName(strZipFileName), out strOldTimestamp);
            string strNewTimestamp = File.GetLastWriteTime(strZipFileName).ToString();
            if (strOldTimestamp != strNewTimestamp)
            {
                // 从 library_data.zip 中展开部分目录内容
                nRet = dp2Library_extractPartDir(out strError);
                if (nRet == -1)
                    goto ERROR1;
                _versionManager.SetFileVersion(Path.GetFileName(strZipFileName), strNewTimestamp);
                _versionManager.AutoSave();
            }
            return;
        ERROR1:
            MessageBox.Show(this, strError);
        }

        // 获得一个目录下的全部文件名。包括子目录中的
        static List<string> GetFileNames(string strDataDir)
        {
            // Application.DoEvents();

            DirectoryInfo di = new DirectoryInfo(strDataDir);

            List<string> result = new List<string>();

            FileInfo[] fis = di.GetFiles();
            foreach (FileInfo fi in fis)
            {
                        result.Add(fi.FullName);
            }

            // 处理下级目录，递归
            DirectoryInfo[] dis = di.GetDirectories();
            foreach (DirectoryInfo subdir in dis)
            {
                result.AddRange(GetFileNames(subdir.FullName));
            }

            return result;
        }

#if NO
        // 记忆配置文件的版本
        void SaveFileInfo()
        {
            string strCfgsDir = Path.Combine(this.UserDir, "library_data/cfgs");

            List<string> filenames = GetFileNames(strCfgsDir);

            foreach (string filename in filenames)
            {
                _versionManager.SetFileVersion(filename, File.GetLastWriteTime(filename).ToString());
            }

            _versionManager.AutoSave();
        }
#endif

        #region dp2OPAC

        // 安装 dp2OPAC 的数据目录
        // parameters:
        //      bAutoSetup  是否自动安装。自动安装时，如果已经存在数据文件，则不会再次安装。否则会强行重新安装，但安装前会出现对话框警告
        // return:
        //      -1  出错
        //      0   放弃安装
        //      1   安装成功
        int SetupOpacDataAndAppDir(
            bool bAutoSetup,
            out string strError)
        {
            strError = "";
            int nRet = 0;

            CloseIIsExpress(true);

            // TODO: 是否改为探测目录是否存在?

            string strFilename = PathUtil.MergePath(this.OpacDataDir, "opac.xml");
            if (File.Exists(strFilename) == true)
            {
                if (bAutoSetup == true)
                {
                    strError = "dp2OPAC 数据目录先前已经安装过，本次没有重新安装";
                    return 0;
                }

                DialogResult result = MessageBox.Show(this,
    "警告：dp2OPAC 数据目录先前已经安装过了，本次重新安装，将摧毁以前的全部数据。\r\n\r\n确实要重新安装？",
    "dp2Library XE",
    MessageBoxButtons.YesNo,
    MessageBoxIcon.Question,
    MessageBoxDefaultButton.Button2);
                if (result == System.Windows.Forms.DialogResult.No)
                {
                    strError = "放弃重新安装 dp2OPAC 数据目录";
                    return 0;
                }
            }

            // TODO: 停止 IIS Express

            // 删除以前的目录
            nRet = DeleteDataDirectory(
                Path.GetDirectoryName(strFilename),
                out strError);
            if (nRet == -1)
                return -1;

#if NO
            if (_messageBar != null)
                _messageBar.MessageText = "正在初始化 dp2OPAC 数据目录和应用程序目录 ...";
#endif
            if (string.IsNullOrEmpty(this._floatingMessage.Text) == false)
                this._floatingMessage.Text = "正在初始化 dp2OPAC 数据目录和应用程序目录 ...";


            nRet = dp2OPAC_CreateNewDataDir(out strError);
            if (nRet == -1)
                return -1;

            // 删除以前的目录
            nRet = DeleteDataDirectory(
                this.OpacAppDir,
                out strError);
            if (nRet == -1)
                return -1;

            nRet = dp2OPAC_CreateNewAppDir(this.OpacDataDir, out strError);
            if (nRet == -1)
                return -1;

            // 修改 library.xml 文件，创建或修改用户帐户
            // return:
            //      -1  error
            //      0   succeed
            nRet = dp2Library_changeXml_addAccount(this.LibraryDataDir,
                "opac",
                null,
                default_opac_rights,
                "merge",
                out strError);
            if (nRet == -1)
                return -1;

            // 创建/修改 library.xml 文件
            // return:
            //      -1  error
            //      0   succeed
            nRet = dp2OPAC_createXml(this.OpacDataDir,
                "opac",
                "",
                out strError);
            if (nRet == -1)
                return -1;

            return 1;
        }

        // 刷新应用程序目录
        // parameters:
        //      bForce  true 强制升级  false 自动升级，如果 .zip 文件时间戳没有变化就不升级
        int dp2OPAC_UpdateAppDir(
            bool bForce,
            out string strError)
        {
            strError = "";

            string strZipFileName = Path.Combine(this.DataDir, "opac_app.zip");

            string strOldTimestamp = "";
            int nRet = _versionManager.GetFileVersion(Path.GetFileName(strZipFileName), out strOldTimestamp);
            string strNewTimestamp = File.GetLastWriteTime(strZipFileName).ToString();

            if (bForce == true || strOldTimestamp != strNewTimestamp)
            {
                if (bForce == false)
                    AppendSectionTitle("自动升级 dp2OPAC");

                // 要求在 opac_data.zip 内准备要安装的数据文件(初次安装而不是升级安装)
                try
                {
                    using (ZipFile zip = ZipFile.Read(strZipFileName))
                    {
                        foreach (ZipEntry e in zip)
                        {
                            if (e.FileName.ToLower() == "opac_app/web.config")
                                continue;

                            AppendString(e.FileName + "\r\n");

                            e.Extract(this.UserDir, ExtractExistingFileAction.OverwriteSilently);
                        }
                    }
                }
                catch (Exception ex)
                {
                    strError = ex.Message;
                    return -1;
                }

                _versionManager.SetFileVersion(Path.GetFileName(strZipFileName), strNewTimestamp);
                _versionManager.AutoSave();

                if (bForce == false)
                    AppendSectionTitle("结束升级 dp2OPAC");
            }

            return 0;
        }

        // 创建应用程序目录，并复制进基本内容
        int dp2OPAC_CreateNewAppDir(
            string strDataDir,
            out string strError)
        {
            strError = "";

            string strZipFileName = Path.Combine(this.DataDir, "opac_app.zip");

            try
            {
                using (ZipFile zip = ZipFile.Read(strZipFileName))
                {
                    foreach (ZipEntry e in zip)
                    {
                        e.Extract(this.UserDir, ExtractExistingFileAction.OverwriteSilently);
                    }
                }
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                return -1;
            }

            // 创建start.xml文件
            string strStartXmlFileName = Path.Combine(this.OpacAppDir, "start.xml");
            int nRet = this.CreateStartXml(strStartXmlFileName,
                strDataDir,
                out strError);
            if (nRet == -1)
               return -1;

            return 0;
        }

        // 创建start.xml文件
        // parameters:
        //      strFileName start.xml文件名
        int CreateStartXml(string strFileName,
            string strDataDir,
            out string strError)
        {
            strError = "";

            try
            {
                string strXml = "<root datadir=''/>";

                XmlDocument dom = new XmlDocument();
                dom.LoadXml(strXml);

                DomUtil.SetAttr(dom.DocumentElement, "datadir", strDataDir);

                dom.Save(strFileName);

                return 0;
            }
            catch (Exception ex)
            {
                strError = "创建start.xml文件出错：" + ex.Message;
                return -1;
            }
        }


        // 创建数据目录，并复制进基本内容
        int dp2OPAC_CreateNewDataDir(
            out string strError)
        {
            strError = "";

            string strZipFileName = Path.Combine(this.DataDir, "opac_data.zip");

            // 要求在 opac_data.zip 内准备要安装的数据文件(初次安装而不是升级安装)
            try
            {
                using (ZipFile zip = ZipFile.Read(strZipFileName))
                {
                    foreach (ZipEntry e in zip)
                    {
                        e.Extract(this.UserDir, ExtractExistingFileAction.OverwriteSilently);
                    }
                }
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                return -1;
            }
            return 0;
        }

        // 创建/修改 opac.xml 文件
        // return:
        //      -1  error
        //      0   succeed
        public int dp2OPAC_createXml(string strDataDir,
            string strOpacUserName,
            string strOpacPassword,
            // string strSupervisorRights,
            // string strLibraryName,
            out string strError)
        {
            strError = "";

            string strFilename = PathUtil.MergePath(strDataDir, "opac.xml");
            XmlDocument dom = new XmlDocument();
            try
            {
                dom.Load(strFilename);
            }
            catch (FileNotFoundException)
            {
                strError = "文件 " + strFilename + " 没有找到";
                return -1;
            }
            catch (Exception ex)
            {
                strError = "加载文件 " + strFilename + " 到 XMLDOM 时出错：" + ex.Message;
                return -1;
            }

            XmlNode nodeRmsServer = dom.DocumentElement.SelectSingleNode("libraryServer");
            if (nodeRmsServer == null)
            {
                nodeRmsServer = dom.CreateElement("libraryServer");
                dom.DocumentElement.AppendChild(nodeRmsServer);
            }

            DomUtil.SetAttr(nodeRmsServer,
                "url",
                LibraryHost.default_single_url);
            DomUtil.SetAttr(nodeRmsServer,
                 "username",
                 strOpacUserName);

            string strPassword = Cryptography.Encrypt(strOpacPassword, "dp2circulationpassword");
            DomUtil.SetAttr(nodeRmsServer,
                "password",
                strPassword);

            // 报表目录
            DomUtil.SetAttr(nodeRmsServer,
     "reportDir",
     Path.Combine(this.LibraryDataDir, "upload/reports"));

            dom.Save(strFilename);
            return 0;
        }

        #endregion

        private void MenuItem_setupOpacDataAppDir_Click(object sender, EventArgs e)
        {
            string strError = "";
            // 安装 dp2OPAC 的数据目录
            // parameters:
            //      bAutoSetup  是否自动安装。自动安装时，如果已经存在数据文件，则不会再次安装。否则会强行重新安装，但安装前会出现对话框警告
            int nRet = SetupOpacDataAndAppDir(
                false,
                out strError);
            if (nRet == -1 || nRet == 0)
                goto ERROR1;

            MessageBox.Show(this, "dp2OPAC 数据目录和应用程序目录安装成功");
            return;
        ERROR1:
            MessageBox.Show(this, strError);
        }

        Process _processIIsExpress = null;

        void CloseIIsExpress(bool bWait = true)
        {
            if (_processIIsExpress != null)
            {
                try
                {
                    _processIIsExpress.Kill();
                    if (bWait == true)
                        _processIIsExpress.WaitForExit();
                    _processIIsExpress.Dispose();
                }
                catch
                {
                }

                _processIIsExpress = null;
            }
        }

        // return:
        //      -1  出错
        //      0   程序文件不存在
        //      1   成功启动
        int StartIIsExpress(string strSite,
            bool bHide,
            out string strError)
        {
            strError = "";

            CloseIIsExpress();

            string fileName = Path.Combine(
Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
"iis express\\iisexpress.exe");

            if (File.Exists(fileName) == false)
            {
                strError = "文件 "+fileName+" 不存在， IIS Express 启动失败";
                return 0;
            }

            string arguments = "";


            if (string.IsNullOrEmpty(strSite) == false)
            {
                arguments = "/site:" + strSite + " /systray:true";
            }

            ProcessStartInfo startinfo = new ProcessStartInfo();
            startinfo.FileName = fileName;
            startinfo.Arguments = arguments;
            if (bHide == true)
            {
                // 此二行会导致 statis.aspx 停顿
                //startinfo.RedirectStandardOutput = true;
                //startinfo.RedirectStandardError = true;

                startinfo.UseShellExecute = false;
                startinfo.CreateNoWindow = true;
            }

            Process process = new Process();
            process.StartInfo = startinfo;
            process.EnableRaisingEvents = true;

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                strError = "运行 IIS Express 异常: " + ex.Message;
                return -1;
            }
            _processIIsExpress = process;
            return 1;
        }

        private void MenuItem_startIISExpress_Click(object sender, EventArgs e)
        {
#if NO
            if (_processIIsExpress != null)
            {
                _processIIsExpress.Kill();
                _processIIsExpress.WaitForExit();
                _processIIsExpress.Dispose();
                _processIIsExpress = null;
            }

            string fileName = Path.Combine(
Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
"iis express\\iisexpress");

            // string fileName = "\"%programfiles%/iis express\\iisexpress\"";
            string arguments = "/site:dp2Site";
            // string arguments = "/path:" + this.OpacAppDir + " /port:8081";
            _processIIsExpress = Process.Start(fileName, arguments);
#endif
            string strError = "";

            bool bHide = true;
            if (Control.ModifierKeys == Keys.Control)
                bHide = false;
            // return:
            //      -1  出错
            //      0   程序文件不存在
            //      1   成功启动
            int nRet = StartIIsExpress("dp2Site", bHide, out strError);
            if (nRet == 1)
                AppendSectionTitle("IIS Express 启动成功");
            else
            {
                AppendSectionTitle("IIS Express 启动失败: " + strError);
                MessageBox.Show(this, strError);
            }
        }

        private void MenuItem_registerWebApp_Click(object sender, EventArgs e)
        {
            string strError = "";
            int nRet = RegisterWebApp(out strError);
            if (nRet == -1)
                goto ERROR1;

            MessageBox.Show(this, "注册成功");
            return;
        ERROR1:
            MessageBox.Show(this, strError);
        }

        int RegisterWebApp(out string strError)
        {
            strError = "";

            string fileName = Path.Combine(
Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
"iis express/appcmd");

            List<string> lines = new List<string>();

            // 创建新的 Site
            lines.Add("add site /name:dp2Site /bindings:\"http/:8081:\" /physicalPath:\"" + this.dp2SiteDir + "\"");

            // 允许任何 IP 域名访问本站点
            // lines.Add("set site \"WebSite1\" /bindings:http/:8080:");

            // 创建应用程序
            lines.Add("delete app \"dp2Site/dp2OPAC\"");
            lines.Add("add app /site.name:dp2Site /path:/dp2OPAC /physicalPath:" + this.OpacAppDir);

            // 创建 AppPool
            lines.Add("delete apppool \"dp2OPAC\"");
            lines.Add("add apppool /name:dp2OPAC");
            // 修改 AppPool 特性： .NET 4.0
            lines.Add("set apppool \"dp2OPAC\" /managedRuntimeVersion:v4.0");
            // 修改 AppPool 特性： Integrated
            lines.Add("set apppool \"dp2OPAC\" /managedPipelineMode:Integrated");

            // 修改 AppPool 特性： disallowOverlappingRotation
            lines.Add("set apppool \"dp2OPAC\" /recycling.disallowOverlappingRotation:true");

            // 使用这个 AppPool
            lines.Add("set app \"dp2Site/dp2OPAC\" /applicationPool:dp2OPAC");

            // 确保 MyDocuments 里面的 IISExpress 和 My WebSites 目录创建


            // return:
            //      -1  出错
            //      0   程序文件不存在
            //      1   成功启动
            int nRet = StartIIsExpress("", true, out strError);
            if (nRet != 1)
                return -1;

            Thread.Sleep(3000);
            CloseIIsExpress();

            AppendSectionTitle("开始注册");
            try
            {
                int i = 0;
                foreach (string arguments in lines)
                {
                    ProcessStartInfo info = new ProcessStartInfo()
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };

                    AppendString("\r\n" + (i + 1).ToString() + ")\r\n" + fileName + " " + arguments + "\r\n");

                    // Process.Start(fileName, arguments).WaitForExit();
                    using (Process process = Process.Start(info))
                    {

                        process.OutputDataReceived += new DataReceivedEventHandler(
            (s, e1) =>
            {
                AppendString(e1.Data + "\r\n");
            }
        );
                        process.ErrorDataReceived += new DataReceivedEventHandler((s, e1) =>
                        {
                            AppendString("error:" + e1.Data + "\r\n");
                        }
                        );
                        process.BeginOutputReadLine();
                        while (true)
                        {
                            Application.DoEvents();
                            if (process.WaitForExit(500) == true)
                                break;
                        }
                        // 显示残余的文字
#if NO
                        while (!process.StandardOutput.EndOfStream)
                        {
                            Application.DoEvents();
                            Thread.Sleep(1);
                        }
#endif
                        // process.CancelOutputRead();
                    }

                    for (int j = 0; j < 10; j++)
                    {
                        Application.DoEvents();
                        Thread.Sleep(1);
                    }

                    i++;
                }
            }
            finally
            {
                AppendSectionTitle("结束注册");
            }

            // TODO：需要重新启动 IIS Express
            return 0;
        }

        private void MenuItem_iisExpressVersion_Click(object sender, EventArgs e)
        {
            string strError = "";

            string fileName = Path.Combine(
Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
"iis express\\iisexpress.exe");
            if (File.Exists(fileName) == false)
            {
                strError = "文件 " + fileName + " 不存在";
                goto ERROR1;
            }

            try
            {
                FileVersionInfo version = FileVersionInfo.GetVersionInfo(fileName);
                MessageBox.Show(this, version.FileMajorPart.ToString());
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                goto ERROR1;
            }
            return;
        ERROR1:
            MessageBox.Show(this, strError);
        }

        // 安装 dp2OPAC
        private void MenuItem_installDp2Opac_Click(object sender, EventArgs e)
        {
            string strError = "";
            int nRet = 0;

            AppendSectionTitle("开始安装 dp2OPAC");

            bool bForce = false;
            if (Control.ModifierKeys == Keys.Control)
            {
                AppendString("强制安装\r\n");
                bForce = true;
            }

            if (bForce == false)
            {
                if (Environment.OSVersion.Version.Major >= 6)
                {
                }
                else
                {
                    strError = "当前 Windows 操作系统版本太低，无法安装使用 IIS Express 8.0";
                    goto ERROR1;
                }
            }

            // 首先安装 IIS Express 8
            string fileName = Path.Combine(
Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
"iis express\\iisexpress.exe");

            if (bForce == false)
            {
                if (File.Exists(fileName) == true
                    && FileVersionInfo.GetVersionInfo(fileName).FileMajorPart >= 8)
                {
                } 
                else
                {

                    // 安装 IIS Express 8
                    strError = "需要先安装 IIS Express 8.0。\r\n\r\n安装完 IIS Express 后，请重新执行本命令";
                    AppendString(strError + "\r\n");

                    this.AppInfo.SetBoolean("OPAC", "installed", false);
                    this.AppInfo.Save();

                    MessageBox.Show(this, strError);
                    Process.Start("http://www.microsoft.com/en-us/download/details.aspx?id=34679");
                    return;
                }
            }

#if NO
            _messageBar = new MessageBar();
            _messageBar.TopMost = false;
            _messageBar.Font = this.Font;
            _messageBar.BackColor = SystemColors.Info;
            _messageBar.ForeColor = SystemColors.InfoText;
            _messageBar.Text = "dp2Library XE";
            _messageBar.MessageText = "正在安装 dp2OPAC，请等待 ...";
            _messageBar.StartPosition = FormStartPosition.CenterScreen;
            // _messageBar.TopMost = true;
            _messageBar.Show(this);
            _messageBar.Update();
#endif
            this._floatingMessage.Text = "正在安装 dp2OPAC，请等待 ...";

            Application.DoEvents();

            try
            {
                // 安装 dp2OPAC 的数据目录
                // parameters:
                //      bAutoSetup  是否自动安装。自动安装时，如果已经存在数据文件，则不会再次安装。否则会强行重新安装，但安装前会出现对话框警告
                nRet = SetupOpacDataAndAppDir(
                    false,
                    out strError);
                if (nRet == -1 || nRet == 0)
                    goto ERROR1;

                nRet = RegisterWebApp(out strError);
                if (nRet == -1)
                    goto ERROR1;

                this.AppInfo.SetBoolean("OPAC", "installed", true);
                this.AppInfo.Save();

            }
            finally
            {
#if NO
                _messageBar.Close();
                _messageBar = null;
#endif
                this._floatingMessage.Text = "";
            }

            string strInformation = "dp2OPAC 安装完成。\r\n\r\n在本机可以使用 " + localhost_opac_url + " 访问";
            AppendString(strInformation + "\r\n");

            // 检查当前超级用户帐户是否为空密码
            // return:
            //      -1  检查中出错
            //      0   空密码
            //      1   已经设置了密码
            nRet = CheckNullPassword(out strError);
            if (nRet == -1)
                MessageBox.Show(this, "检查超级用户密码的过程出错: " + strError);

            if (nRet == 0)
            {
                MessageBox.Show(this, strInformation);

                MessageBox.Show(this, "当前超级用户 " + this.SupervisorUserName + " 的密码为空，如果启动 dp2OPAC，其他人将可能通过浏览器冒用此账户。\r\n\r\n请(使用 dp2circulation (内务前端))为此账户设置密码，然后重新启动 dp2libraryXE。\r\n\r\n为确保安全，本次未启动 dp2OPAC",
                    "dp2library XE 警告",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            else
            {
                // MessageBox.Show(this, "即将启动 IIS Express。不要关闭这个窗口");
                // return:
                //      -1  出错
                //      0   程序文件不存在
                //      1   成功启动
                nRet = StartIIsExpress("dp2Site", true, out strError);
                if (nRet != 1)
                    goto ERROR1;
                MessageBox.Show(this, strInformation);
                Process.Start(localhost_opac_url);
            }

            AppendSectionTitle("结束安装 dp2OPAC");
            return;
        ERROR1:
            AppendString(strError + "\r\n");

            this.AppInfo.SetBoolean("OPAC", "installed", false);
            this.AppInfo.Save();

            MessageBox.Show(this, strError);
        }


        // download IIS Express 8.0
        // http://www.microsoft.com/en-us/download/details.aspx?id=34679


        #region console
        /// <summary>
        /// 将浏览器控件中已有的内容清除，并为后面输出的纯文本显示做好准备
        /// </summary>
        /// <param name="webBrowser">浏览器控件</param>
        public static void ClearForPureTextOutputing(WebBrowser webBrowser)
        {
            HtmlDocument doc = webBrowser.Document;

            if (doc == null)
            {
                webBrowser.Navigate("about:blank");
                doc = webBrowser.Document;
            }

            string strHead = "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Transitional//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd\"><html xmlns=\"http://www.w3.org/1999/xhtml\"><head>"
                // + "<link rel='stylesheet' href='"+strCssFileName+"' type='text/css'>"
    + "<style media='screen' type='text/css'>"
    + "body { font-family:Microsoft YaHei; background-color:#555555; color:#eeeeee; } "
    + "</style>"
    + "</head><body>";

            doc = doc.OpenNew(true);
            doc.Write(strHead + "<pre style=\"font-family:Consolas; \">");  // Calibri
        }

        /// <summary>
        /// 将 HTML 信息输出到控制台，显示出来。
        /// </summary>
        /// <param name="strText">要输出的 HTML 字符串</param>
        public void WriteToConsole(string strText)
        {
            WriteHtml(this.webBrowser1, strText);
        }

        /// <summary>
        /// 将文本信息输出到控制台，显示出来
        /// </summary>
        /// <param name="strText">要输出的文本字符串</param>
        public void WriteTextToConsole(string strText)
        {
            WriteHtml(this.webBrowser1, HttpUtility.HtmlEncode(strText));
        }

        /// <summary>
        /// 向一个浏览器控件中追加写入 HTML 字符串
        /// 不支持异步调用
        /// </summary>
        /// <param name="webBrowser">浏览器控件</param>
        /// <param name="strHtml">HTML 字符串</param>
        public static void WriteHtml(WebBrowser webBrowser,
    string strHtml)
        {

            HtmlDocument doc = webBrowser.Document;

            if (doc == null)
            {
                webBrowser.Navigate("about:blank");
                doc = webBrowser.Document;
#if NO
                webBrowser.DocumentText = "<h1>hello</h1>";
                doc = webBrowser.Document;
                Debug.Assert(doc != null, "");
#endif
            }

            // doc = doc.OpenNew(true);
            doc.Write(strHtml);
        }


        void AppendSectionTitle(string strText)
        {
            AppendCrLn();
            AppendString("*** " + strText + " ***\r\n");
            AppendCurrentTime();
            AppendCrLn();
        }

        void AppendCurrentTime()
        {
            AppendString("*** " + DateTime.Now.ToString() + " ***\r\n");
        }

        void AppendCrLn()
        {
            AppendString("\r\n");
        }

        // 线程安全
        void AppendString(string strText)
        {
            if (this.webBrowser1.InvokeRequired)
            {
                this.webBrowser1.Invoke(new Action<string>(AppendString), strText);
                return;
            }
            this.WriteTextToConsole(strText);
            ScrollToEnd();
        }

        void ScrollToEnd()
        {
            this.webBrowser1.Document.Window.ScrollTo(
                0,
                this.webBrowser1.Document.Body.ScrollRectangle.Height);
        }


        #endregion

        // 检查当前超级用户帐户是否为空密码
        // return:
        //      -1  检查中出错
        //      0   空密码
        //      1   已经设置了密码
        int CheckNullPassword(out string strError)
        {
            strError = "";

            int nRet = PrepareSearch();
            try
            {
                EnableControls(false);

                Stop.OnStop += new StopEventHandler(this.DoStop);
                Stop.Initial("正在检查密码 ...");
                Stop.BeginLoop();

                try
                {
                    // return:
                    //      -1  error
                    //      0   登录未成功
                    //      1   登录成功
                    long lRet = Channel.Login(this.SupervisorUserName,
                        "",
                        "type=worker",
                        out strError);
                    if (lRet == -1)
                        return -1;

                    if (lRet == 0)
                    {
                        strError = "已经设置了密码";
                        return 1;
                    }

                    strError = "密码为空，危险";
                    return 0;
                }
                finally
                {
                    Stop.EndLoop();
                    Stop.OnStop -= new StopEventHandler(this.DoStop);
                    Stop.Initial("");

                    EnableControls(true);
                }
            }
            finally
            {
                EndSearch();
            }
        }

        private void MenuItem_test_Click(object sender, EventArgs e)
        {
            int nRet = PrepareSearch();
            try
            {
                EnableControls(false);

                Stop.OnStop += new StopEventHandler(this.DoStop);
                Stop.Initial("testing ...");
                Stop.BeginLoop();

                try
                {
                    BiblioDbFromInfo[] infos = null;
                    string strError = "";
                    long lRet = Channel.ListDbFroms(Stop,
                        "biblio",
                        "zh",
                        out infos,
                        out strError);
#if NO
                    if (lRet == -1)
                        return -1; ;
                    return (int)lRet;
#endif
                }
                finally
                {
                    Stop.EndLoop();
                    Stop.OnStop -= new StopEventHandler(this.DoStop);
                    Stop.Initial("");

                    EnableControls(true);
                }
            }
            finally
            {
                EndSearch();
            }
        }

        private void MenuItem_openDp2OPACHomePage_Click(object sender, EventArgs e)
        {
            bool bInstalled = this.AppInfo.GetBoolean("OPAC", "installed", false);
            if (bInstalled == false)
                MessageBox.Show(this, "dp2OPAC 尚未安装");
            else
                Process.Start(localhost_opac_url);
        }

        private void MenuItem_updateDp2Opac_Click(object sender, EventArgs e)
        {
            string strError = "";
            int nRet = 0;

            AppendSectionTitle("开始升级 dp2OPAC");

            bool bForce = false;
            if (Control.ModifierKeys == Keys.Control)
            {
                AppendString("强制安装\r\n");
                bForce = true;
            }

            if (bForce == false)
            {
                if (Environment.OSVersion.Version.Major >= 6)
                {
                }
                else
                {
                    strError = "当前 Windows 操作系统版本太低，无法安装使用 IIS Express 8.0";
                    goto ERROR1;
                }
            }

            string fileName = Path.Combine(
                this.OpacAppDir, "book.aspx");

            if (bForce == false)
            {
                if (File.Exists(fileName) == true)
                {
                }
                else
                {
                    strError = "尚未安装 dp2OPAC";
                    goto ERROR1;
                }
            }

#if NO
            _messageBar = new MessageBar();
            _messageBar.TopMost = false;
            _messageBar.Font = this.Font;
            _messageBar.BackColor = SystemColors.Info;
            _messageBar.ForeColor = SystemColors.InfoText;
            _messageBar.Text = "dp2Library XE";
            _messageBar.MessageText = "正在升级 dp2OPAC，请等待 ...";
            _messageBar.StartPosition = FormStartPosition.CenterScreen;
            // _messageBar.TopMost = true;
            _messageBar.Show(this);
            _messageBar.Update();
#endif
            this._floatingMessage.Text = "正在升级 dp2OPAC，请等待 ...";

            Application.DoEvents();

            try
            {
                nRet = dp2OPAC_UpdateAppDir(true, out strError);
                if (nRet == -1)
                    goto ERROR1;
            }
            finally
            {
#if NO
                _messageBar.Close();
                _messageBar = null;
#endif
                this._floatingMessage.Text = "";
            }

            AppendSectionTitle("结束升级 dp2OPAC");
            return;
        ERROR1:
            AppendString(strError + "\r\n");
            MessageBox.Show(this, strError);
        }
    }

    /*
     * 删除一个 App
     * D:\Program Files\IIS Express>appcmd delete app /app.name:WebSite1/dp2OPAC
APP 对象“WebSite1/dp2OPAC”已删除

     * 添加一个 AppPool
     * D:\Program Files\IIS Express>appcmd add apppool /name:dp2OPAC
APPPOOL 对象“dp2OPAC”已添加
     * 
     * 察看 AppPool 特性
     * D:\Program Files\IIS Express>appcmd list apppool "dp2OPAC" /text:*
APPPOOL
  APPPOOL.NAME:"dp2OPAC"
  PipelineMode:"Integrated"
  RuntimeVersion:""
  state:"Unknown"
  [add]
    name:"dp2OPAC"
    queueLength:"1000"
    autoStart:"true"
    enable32BitAppOnWin64:"false"
    managedRuntimeVersion:""
    managedRuntimeLoader:"v4.0"
    enableConfigurationOverride:"true"
    managedPipelineMode:"Integrated"
    CLRConfigFile:""
    passAnonymousToken:"true"
    startMode:"OnDemand"
    [processModel]
      identityType:"ApplicationPoolIdentity"
      userName:""
      password:""
      loadUserProfile:"false"
      setProfileEnvironment:"true"
      logonType:"LogonBatch"
      manualGroupMembership:"false"
      idleTimeout:"00:20:00"
      maxProcesses:"1"
      shutdownTimeLimit:"00:01:30"
      startupTimeLimit:"00:01:30"
      pingingEnabled:"true"
      pingInterval:"00:00:30"
      pingResponseTime:"00:01:30"
      logEventOnProcessModel:"IdleTimeout"
    [recycling]
      disallowOverlappingRotation:"false"
      disallowRotationOnConfigChange:"false"
      logEventOnRecycle:"Time, Memory, PrivateMemory"
      [periodicRestart]
        memory:"0"
        privateMemory:"0"
        requests:"0"
        time:"1.05:00:00"
        [schedule]
    [failure]
      loadBalancerCapabilities:"HttpLevel"
      orphanWorkerProcess:"false"
      orphanActionExe:""
      orphanActionParams:""
      rapidFailProtection:"true"
      rapidFailProtectionInterval:"00:05:00"
      rapidFailProtectionMaxCrashes:"5"
      autoShutdownExe:""
      autoShutdownParams:""
    [cpu]
      limit:"0"
      action:"NoAction"
      resetInterval:"00:05:00"
      smpAffinitized:"false"
      smpProcessorAffinityMask:"4294967295"
      smpProcessorAffinityMask2:"4294967295"
      processorGroup:"0"
      numaNodeAssignment:"MostAvailableMemory"
      numaNodeAffinityMode:"Soft"

     * 
     * 
     * D:\Program Files\IIS Express>appcmd set apppool "dp2OPAC" /managedRuntimeVersion:v4.0
APPPOOL 对象“dp2OPAC”已更改

     * D:\Program Files\IIS Express>appcmd set apppool "dp2OPAC" /managedPipelineMode:Integrated
APPPOOL 对象“dp2OPAC”已更改
     * 
     * D:\Program Files\IIS Express>appcmd set apppool "dp2OPAC" /recycling.disallowOverlappingRotation:true
APPPOOL 对象“dp2OPAC”已更改
     * 
     * D:\Program Files\IIS Express>appcmd set app "WebSite1/dp2OPAC" /applicationPool:dp2OPAC
APP 对象“WebSite1/dp2OPAC”已更改
     * 
     * 
     * D:\Program Files\IIS Express>appcmd list site
SITE "WebSite1" (id:1,bindings:http/:8080:localhost,state:Unknown)

D:\Program Files\IIS Express>appcmd set site "WebSite1" /bindings:http/*:8080:

D:\Program Files\IIS Express>appcmd list site
SITE "WebSite1" (id:1,bindings:http/*:8080:,state:Unknown)

D:\Program Files\IIS Express>
     * 
     * 
     * 
     * */


}
