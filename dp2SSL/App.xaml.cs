﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Runtime.Remoting;
using System.Speech.Synthesis;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Diagnostics;
using System.Xml;
using System.IO;

using dp2SSL.Models;
using System.Text;
using System.Windows.Input;

using DigitalPlatform;
using DigitalPlatform.Core;
using DigitalPlatform.IO;
using DigitalPlatform.LibraryClient;
using DigitalPlatform.RFID;
using DigitalPlatform.Text;
using static DigitalPlatform.IO.BarcodeCapture;
using DigitalPlatform.Face;
using DigitalPlatform.WPF;

namespace dp2SSL
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application, INotifyPropertyChanged
    {
        public static event UpdatedEventHandler Updated = null;

        public static event LineFeedEventHandler LineFeed = null;
        public static event CharFeedEventHandler CharFeed = null;

        // 主要的通道池，用于当前服务器
        public LibraryChannelPool _channelPool = new LibraryChannelPool();

        CancellationTokenSource _cancelRefresh = new CancellationTokenSource();

        CancellationTokenSource _cancelProcessMonitor = new CancellationTokenSource();


        Mutex myMutex;

        ErrorTable _errorTable = null;

        #region 属性

        public event PropertyChangedEventHandler PropertyChanged;

        void OnPropertyChanged(string name)
        {
            if (this.PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(name));
        }

        private string _error = null;   // "test error line asdljasdkf; ;jasldfjasdjkf aasdfasdf";

        public string Error
        {
            get => _error;
            set
            {
                if (_error != value)
                {
                    _error = value;
                    OnPropertyChanged("Error");
                }
            }
        }

        private string _number = null;

        public string Number
        {
            get => _number;
            set
            {
                if (_number != value)
                {
                    _number = value;
                    OnPropertyChanged("Number");
                }
            }
        }

        #endregion

        protected async override void OnStartup(StartupEventArgs e)
        {
            bool aIsNewInstance = false;
            myMutex = new Mutex(true, "{75BAF3F0-FF7F-46BB-9ACD-8FE7429BF291}", out aIsNewInstance);
            if (!aIsNewInstance)
            {
                MessageBox.Show("dp2SSL 不允许重复启动");
                App.Current.Shutdown();
                return;
            }

            if (DetectVirus.DetectXXX() || DetectVirus.DetectGuanjia())
            {
                MessageBox.Show("dp2SSL 被木马软件干扰，无法启动");
                System.Windows.Application.Current.Shutdown();
                return;
            }

            _errorTable = new ErrorTable((s) =>
            {
                this.Error = s;
            });

            WpfClientInfo.TypeOfProgram = typeof(App);
            if (StringUtil.IsDevelopMode() == false)
                WpfClientInfo.PrepareCatchException();

            WpfClientInfo.Initial("dp2SSL");
            base.OnStartup(e);

            this._channelPool.BeforeLogin += new DigitalPlatform.LibraryClient.BeforeLoginEventHandle(Channel_BeforeLogin);
            this._channelPool.AfterLogin += new AfterLoginEventHandle(Channel_AfterLogin);

            // InitialFingerPrint();

            // 后台自动检查更新
            var task = Task.Run(() =>
            {
                NormalResult result = WpfClientInfo.InstallUpdateSync();
                if (result.Value == -1)
                    OutputHistory("自动更新出错: " + result.ErrorInfo, 2);
                else if (result.Value == 1)
                {
                    OutputHistory(result.ErrorInfo, 1);
                    Updated?.Invoke(this, new UpdatedEventArgs { Message = result.ErrorInfo });
                    // MessageBox.Show(result.ErrorInfo);
                }
                else if (string.IsNullOrEmpty(result.ErrorInfo) == false)
                    OutputHistory(result.ErrorInfo, 0);
            });

#if REMOVED
            // 用于重试初始化指纹环境的 Timer
            // https://stackoverflow.com/questions/13396582/wpf-user-control-throws-design-time-exception
            _timer = new System.Threading.Timer(
    new System.Threading.TimerCallback(timerCallback),
    null,
    TimeSpan.FromSeconds(10),
    TimeSpan.FromSeconds(60));
#endif
            FingerprintManager.Base.Name = "指纹中心";
            FingerprintManager.Url = App.FingerprintUrl;
            FingerprintManager.SetError += FingerprintManager_SetError;
            WpfClientInfo.WriteInfoLog("FingerprintManager.Start()");
            FingerprintManager.Start(_cancelRefresh.Token);

            FaceManager.Base.Name = "人脸中心";
            FaceManager.Url = App.FaceUrl;
            FaceManager.SetError += FaceManager_SetError;
            FaceManager.Start(_cancelRefresh.Token);

            // 自动删除以前残留在 UserDir 中的全部临时文件
            // 用 await 是需要删除完以后再返回，这样才能让后面的 PageMenu 页面开始使用临时文件目录
            await Task.Run(() =>
            {
                DeleteLastTempFiles();
            });

            StartProcessManager();

            BeginCheckServerUID(_cancelRefresh.Token);

            // 
            InitialShelfCfg();

            RfidManager.Base.Name = "RFID 中心";
            RfidManager.EnableBase2();
            RfidManager.Url = App.RfidUrl;
            // RfidManager.AntennaList = "1|2|3|4";    // TODO: 从 shelf.xml 中归纳出天线号范围
            RfidManager.SetError += RfidManager_SetError;
            RfidManager.ListTags += RfidManager_ListTags;

            RfidManager.ListLocks += ShelfData.RfidManager_ListLocks;

            // 2019/12/17
            // 智能书柜一开始假定全部门关闭，所以不需要对任何图书读卡器进行盘点
            if (App.Function == "智能书柜")
                RfidManager.ReaderNameList = "";

            WpfClientInfo.WriteInfoLog("FingerprintManager.Start()");
            RfidManager.Start(_cancelRefresh.Token);
            if (App.Function == "智能书柜")
            {
                WpfClientInfo.WriteInfoLog("RfidManager.StartBase2()");
                RfidManager.StartBase2(_cancelRefresh.Token);
            }

            _barcodeCapture.InputLine += _barcodeCapture_inputLine;
            //_barcodeCapture.InputChar += _barcodeCapture_InputChar;
            _barcodeCapture.Handled = _pauseBarcodeScan == 0;   // 是否把处理过的字符吞掉
            _barcodeCapture.Start();

            InputMethod.SetPreferredImeState(App.Current.MainWindow, InputMethodState.Off);
        }

        public void InitialShelfCfg()
        {
            if (App.Function == "智能书柜")
            {
                try
                {
                    ShelfData.InitialShelf();
                }
                catch(FileNotFoundException)
                {
                    this.SetError("cfg", $"尚未配置 shelf.xml 文件");
                }
                catch (Exception ex)
                {
                    this.SetError("cfg", $"InitialShelf() 出现异常:{ex.Message}");
                }
            }
        }

        private void _barcodeCapture_InputChar(CharInput input)
        {
            if (_pauseBarcodeScan > 0)
            {
                Debug.WriteLine("pauseBarcodeScan");
                return;
            }

            CharFeed?.Invoke(this, new CharFeedEventArgs { CharInput = input });
        }

        class LastBarcode
        {
            public string Barcode { get; set; }
            public DateTime Time { get; set; }
        }

        LastBarcode _lastBarcode = null;
        static TimeSpan _repeatLimit = TimeSpan.FromSeconds(3);

        private void _barcodeCapture_inputLine(BarcodeCapture.StringInput input)
        {
            if (_pauseBarcodeScan > 0)
            {
                Debug.WriteLine("pauseBarcodeScan");
                return;
            }

            Debug.WriteLine($"input.Barcode='{input.Barcode}'");

            {
                string line = input.Barcode.TrimEnd(new char[] { '\r', '\n' });
                Debug.WriteLine($"line feed. line='{line}'");
                if (string.IsNullOrEmpty(line) == false)
                {
                    // 检查和上次输入是否重复
                    if (_lastBarcode != null
                        && _lastBarcode.Barcode == line
                        && DateTime.Now - _lastBarcode.Time <= _repeatLimit)
                    {
                        Debug.WriteLine("密集重复输入被忽略");
                        // App.CurrentApp.Speak("重复扫入被忽略");
                        _lastBarcode = new LastBarcode { Barcode = line, Time = DateTime.Now };
                        return;
                    }

                    _lastBarcode = new LastBarcode { Barcode = line, Time = DateTime.Now };
                    // 触发一次输入
                    LineFeed?.Invoke(this, new LineFeedEventArgs { Text = line });
                }
            }
        }

        public static void PauseBarcodeScan()
        {
            _pauseBarcodeScan++;
            _barcodeCapture.Handled = _pauseBarcodeScan == 0;
        }

        public static void ContinueBarcodeScan()
        {
            _pauseBarcodeScan--;
            _barcodeCapture.Handled = _pauseBarcodeScan == 0;
        }

        StringBuilder _line = new StringBuilder();
        static BarcodeCapture _barcodeCapture = new BarcodeCapture();
        // 是否暂停接收输入
        static int _pauseBarcodeScan = 0;

        // 单独的线程，监控 server UID 关系
        public void BeginCheckServerUID(CancellationToken token)
        {
            // 刚开始 5 分钟内频繁检查
            DateTime start = DateTime.Now;

            var task1 = Task.Run(() =>
            {
                try
                {
                    while (token.IsCancellationRequested == false)
                    {
                        var result = PageSetting.CheckServerUID();
                        if (result.Value == -1)
                            SetError("uid", result.ErrorInfo);
                        else
                            SetError("uid", null);

                        if (DateTime.Now - start < TimeSpan.FromMinutes(5))
                            Task.Delay(TimeSpan.FromSeconds(5)).Wait(token);
                        else
                            Task.Delay(TimeSpan.FromMinutes(5)).Wait(token);
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            });
        }

        public void StartProcessManager()
        {
            // 停止前一次的 monitor
            if (_cancelProcessMonitor != null)
            {
                _cancelProcessMonitor.Cancel();
                _cancelProcessMonitor.Dispose();

                _cancelProcessMonitor = new CancellationTokenSource();
            }

            if (ProcessMonitor == true)
            {
                List<ProcessInfo> infos = new List<ProcessInfo>();
                if (string.IsNullOrEmpty(App.FaceUrl) == false
                    && ProcessManager.IsIpcUrl(App.FaceUrl))
                    infos.Add(new ProcessInfo
                    {
                        Name = "人脸中心",
                        ShortcutPath = "DigitalPlatform/dp2 V3/dp2-人脸中心",
                        MutexName = "{E343F372-13A0-482F-9784-9865B112C042}"
                    });
                if (string.IsNullOrEmpty(App.RfidUrl) == false
                    && ProcessManager.IsIpcUrl(App.RfidUrl))
                    infos.Add(new ProcessInfo
                    {
                        Name = "RFID中心",
                        ShortcutPath = "DigitalPlatform/dp2 V3/dp2-RFID中心",
                        MutexName = "{CF1B7B4A-C7ED-4DB8-B5CC-59A067880F92}"
                    });
                if (string.IsNullOrEmpty(App.FingerprintUrl) == false
                    && ProcessManager.IsIpcUrl(App.FingerprintUrl))
                    infos.Add(new ProcessInfo
                    {
                        Name = "指纹中心",
                        ShortcutPath = "DigitalPlatform/dp2 V3/dp2-指纹中心",
                        MutexName = "{75FB942B-5E25-4228-9093-D220FFEDB33C}"
                    });
                ProcessManager.Start(infos,
                    (info, text) =>
                    {
                        WpfClientInfo.Log?.Info($"{info.Name} {text}");
                    },
                    _cancelProcessMonitor.Token);
            }
        }

        void DeleteLastTempFiles()
        {
            try
            {
                PathUtil.ClearDir(WpfClientInfo.UserTempDir);
            }
            catch (Exception ex)
            {
                this.AddErrors("global", new List<string> { $"清除上次遗留的临时文件时出现异常: {ex.Message}" });
            }
        }

        private void FaceManager_SetError(object sender, SetErrorEventArgs e)
        {
            SetError("face", e.Error);
        }

        private void RfidManager_SetError(object sender, SetErrorEventArgs e)
        {
            SetError("rfid", e.Error);
            // 2019/12/15
            // 注意这里的错误信息可能会洪水般冲来，可能会把磁盘空间占满
            //if (e.Error != null)
            //    WpfClientInfo.WriteErrorLog($"RfidManager 出错: {e.Error}");
        }

        private void FingerprintManager_SetError(object sender, SetErrorEventArgs e)
        {
            SetError("fingerprint", e.Error);
        }

        // TODO: 如何显示后台任务执行信息? 可以考虑只让管理者看到
        public void OutputHistory(string strText, int nWarningLevel = 0)
        {
            // OutputText(DateTime.Now.ToShortTimeString() + " " + strText, nWarningLevel);
        }

        // 注：Windows 关机或者重启的时候，会触发 OnSessionEnding 事件，但不会触发 OnExit 事件
        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            LibraryChannelManager.Log?.Debug("OnSessionEnding() called");
            WpfClientInfo.Finish();
            LibraryChannelManager.Log?.Debug("End WpfClientInfo.Finish()");

            _cancelRefresh?.Cancel();
            _cancelProcessMonitor?.Cancel();

            // 最后关灯
            RfidManager.TurnShelfLamp("*", "turnOff");

            base.OnSessionEnding(e);
        }

        // 注：Windows 关机或者重启的时候，会触发 OnSessionEnding 事件，但不会触发 OnExit 事件
        protected async override void OnExit(ExitEventArgs e)
        {
            _barcodeCapture.Stop();
            _barcodeCapture.InputLine -= _barcodeCapture_inputLine;
            //_barcodeCapture.InputChar -= _barcodeCapture_InputChar;

            try
            {
                await PageMenu.PageShelf?.Submit(true);
            }
            catch (NullReferenceException)
            {

            }

            LibraryChannelManager.Log?.Debug("OnExit() called");
            WpfClientInfo.Finish();
            LibraryChannelManager.Log?.Debug("End WpfClientInfo.Finish()");

            _cancelRefresh?.Cancel();
            _cancelProcessMonitor?.Cancel();

            // EndFingerprint();

            this._channelPool.BeforeLogin -= new DigitalPlatform.LibraryClient.BeforeLoginEventHandle(Channel_BeforeLogin);
            this._channelPool.AfterLogin -= new AfterLoginEventHandle(Channel_AfterLogin);
            this._channelPool.Close();

            // 最后关灯
            RfidManager.TurnShelfLamp("*", "turnOff");

            base.OnExit(e);
        }

        public static App CurrentApp
        {
            get
            {
                return ((App)Application.Current);
            }
        }

        public void ClearChannelPool()
        {
            this._channelPool.Clear();
        }

        public static string dp2ServerUrl
        {
            get
            {
                return WpfClientInfo.Config.Get("global", "dp2ServerUrl", "");
            }
        }

        public static string dp2UserName
        {
            get
            {
                return WpfClientInfo.Config.Get("global", "dp2UserName", "");
            }
        }

        public static string RfidUrl
        {
            get
            {
                return WpfClientInfo.Config?.Get("global", "rfidUrl", "");
            }
        }

        public static string FingerprintUrl
        {
            get
            {
                return WpfClientInfo.Config?.Get("global", "fingerprintUrl", "");
            }
        }

        public static string FaceUrl
        {
            get
            {
                return WpfClientInfo.Config?.Get("global", "faceUrl", "");
            }
        }

        public static bool FullScreen
        {
            get
            {
                return WpfClientInfo.Config?.GetInt("global", "fullScreen", 1) == 1 ? true : false;
            }
        }

        public static bool AutoTrigger
        {
            get
            {
                return (bool)WpfClientInfo.Config?.GetBoolean("ssl_operation", "auto_trigger", false);
            }
        }

        // 身份读卡器是否竖向放置
        public static bool PatronReaderVertical
        {
            get
            {
                return (bool)WpfClientInfo.Config?.GetBoolean("ssl_operation", "patron_info_lasting", false);
            }
        }

        /*
        public static bool PatronInfoDelayClear
        {
            get
            {
                return (bool)WpfClientInfo.Config?.GetBoolean("ssl_operation", "patron_info_delay_clear", false);
            }
        }
        */

        public static bool EnablePatronBarcode
        {
            get
            {
                return (bool)WpfClientInfo.Config?.GetBoolean("ssl_operation", "enable_patron_barcode", false);
            }
        }

        public static bool ProcessMonitor
        {
            get
            {
                if (WpfClientInfo.Config == null)
                    return true;

                return (bool)WpfClientInfo.Config?.GetBoolean("global",
                    "process_monitor",
                    true);
            }
        }

        /*
        public static string ShelfLocation
        {
            get
            {
                return WpfClientInfo.Config?.Get("shelf",
                    "location",
                    "");
            }
        }
        */

        public static string Function
        {
            get
            {
                return WpfClientInfo.Config?.Get("global",
    "function",
    "自助借还");
            }
        }

        public static string CardNumberConvertMethod
        {
            get
            {
                return WpfClientInfo.Config?.Get("global",
    "card_number_convert_method",
    "十六进制");
            }
        }

        public static bool DetectBookChange
        {
            get
            {
                if (WpfClientInfo.Config == null)
                    return true;
                return (bool)WpfClientInfo.Config?.GetBoolean("shelf_operation",
    "detect_book_change",
    true);
            }
        }

        public static string dp2Password
        {
            get
            {
                return DecryptPasssword(WpfClientInfo.Config.Get("global", "dp2Password", ""));
            }
        }

        public static void SetLockingPassword(string password)
        {
            string strSha1 = Cryptography.GetSHA1(password + "_ok");
            WpfClientInfo.Config.Set("global", "lockingPassword", strSha1);
        }

        public static bool MatchLockingPassword(string password)
        {
            string sha1 = WpfClientInfo.Config.Get("global", "lockingPassword", "");
            string current_sha1 = Cryptography.GetSHA1(password + "_ok");
            if (sha1 == current_sha1)
                return true;
            return false;
        }

        public static bool IsLockingPasswordEmpty()
        {
            string sha1 = WpfClientInfo.Config.Get("global", "lockingPassword", "");
            return (string.IsNullOrEmpty(sha1));
        }

        static string EncryptKey = "dp2ssl_client_password_key";

        public static string DecryptPasssword(string strEncryptedText)
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

        public static string EncryptPassword(string strPlainText)
        {
            return Cryptography.Encrypt(strPlainText, EncryptKey);
        }

        #region LibraryChannel

        public class Account
        {
            public string UserName { get; set; }
            public string Password { get; set; }
            public string LibraryCodeList { get; set; } // 馆代码列表

            public static bool IsGlobalUser(string strLibraryCodeList)
            {
                if (strLibraryCodeList == "*" || string.IsNullOrEmpty(strLibraryCodeList) == true)
                    return true;
                return false;
            }

            public static bool MatchLibraryCode(string strLibraryCode, string strLocationLibraryCode)
            {
                if (IsGlobalUser(strLibraryCode) == true)
                    return true;
                if (strLibraryCode == strLocationLibraryCode)
                    return true;
                return false;
            }
        }

        Dictionary<string, Account> _accounts = new Dictionary<string, Account>();

        public Account FindAccount(string userName)
        {
            if (_accounts.ContainsKey(userName) == false)
                return null;
            return _accounts[userName];
        }

        public void SetAccount(string userName, string password, string libraryCode)
        {
            Account account = null;
            if (_accounts.ContainsKey(userName) == false)
            {
                account = new Account
                {
                    UserName = userName,
                    Password = password,
                    LibraryCodeList = libraryCode,
                };
                _accounts[userName] = account;
            }
            else
            {
                account = _accounts[userName];
                account.Password = password;
            }
        }

        public void RemoveAccount(string userName)
        {
            if (_accounts.ContainsKey(userName))
                _accounts.Remove(userName);
        }

        internal void Channel_BeforeLogin(object sender,
DigitalPlatform.LibraryClient.BeforeLoginEventArgs e)
        {
            LibraryChannel channel = sender as LibraryChannel;
            if (e.FirstTry == true)
            {
                // TODO: 从工作人员用户名密码记载里面检查，如果是工作人员账户，则 ...
                Account account = FindAccount(channel.UserName);
                if (account != null)
                {
                    e.UserName = account.UserName;
                    e.Password = account.Password;
                }
                else
                {
                    e.UserName = dp2UserName;

                    // e.Password = this.DecryptPasssword(e.Password);
                    e.Password = dp2Password;

#if NO
                    strPhoneNumber = AppInfo.GetString(
        "default_account",
        "phoneNumber",
        "");
#endif

                    bool bIsReader = false;

                    string strLocation = "";

                    e.Parameters = "location=" + strLocation;
                    if (bIsReader == true)
                        e.Parameters += ",type=reader";
                }

                e.Parameters += ",client=dp2ssl|" + WpfClientInfo.ClientVersion;

                if (String.IsNullOrEmpty(e.UserName) == false)
                    return; // 立即返回, 以便作第一次 不出现 对话框的自动登录
                else
                {
                    e.ErrorInfo = "尚未配置 dp2library 服务器用户名";
                    e.Cancel = true;
                }
            }

            // e.ErrorInfo = "尚未配置 dp2library 服务器用户名";
            e.Cancel = true;
        }

        string _currentUserName = "";

        public string ServerUID = "";

        internal void Channel_AfterLogin(object sender, AfterLoginEventArgs e)
        {
            LibraryChannel channel = sender as LibraryChannel;
            _currentUserName = channel.UserName;
            //_currentUserRights = channel.Rights;
            //_currentLibraryCodeList = channel.LibraryCodeList;
        }

        object _syncRoot_channelList = new object();
        List<LibraryChannel> _channelList = new List<LibraryChannel>();

        public void AbortAllChannel()
        {
            lock (_syncRoot_channelList)
            {
                foreach (LibraryChannel channel in _channelList)
                {
                    if (channel != null)
                        channel.Abort();
                }
            }
        }

        // parameters:
        //      style    风格。如果为 GUI，表示会自动添加 Idle 事件，并在其中执行 Application.DoEvents
        public LibraryChannel GetChannel(string strUserName = "")
        {
            string strServerUrl = dp2ServerUrl;

            if (string.IsNullOrEmpty(strUserName))
                strUserName = dp2UserName;

            LibraryChannel channel = this._channelPool.GetChannel(strServerUrl, strUserName);
            lock (_syncRoot_channelList)
            {
                _channelList.Add(channel);
            }
            // TODO: 检查数组是否溢出
            return channel;
        }

        public void ReturnChannel(LibraryChannel channel)
        {
            this._channelPool.ReturnChannel(channel);
            lock (_syncRoot_channelList)
            {
                _channelList.Remove(channel);
            }
        }

        SpeechSynthesizer m_speech = new SpeechSynthesizer();
        string m_strSpeakContent = "";

        public void Speak(string strText, bool bError = false)
        {
            if (this.m_speech == null)
                return;

            //if (strText == this.m_strSpeakContent)
            //    return; // 正在说同样的句子，不必打断

            this.m_strSpeakContent = strText;

            try
            {
                this.m_speech.SpeakAsyncCancelAll();
                this.m_speech.SpeakAsync(strText);
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // TODO: 如何报错?
            }
        }

        public void SpeakSequence(string strText, bool bError = false)
        {
            if (this.m_speech == null)
                return;

            this.m_strSpeakContent = strText;
            try
            {
                // this.m_speech.SpeakAsyncCancelAll();
                this.m_speech.SpeakAsync(strText);
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // TODO: 如何报错?
            }
        }

        protected override void OnActivated(EventArgs e)
        {
            ContinueBarcodeScan();

            // 单独线程执行，避免阻塞 OnActivated() 返回
            Task.Run(() =>
            {
                FingerprintManager.EnableSendkey(false);
                RfidManager.EnableSendkey(false);
            });
            base.OnActivated(e);
        }

        protected override void OnDeactivated(EventArgs e)
        {
            PauseBarcodeScan();

            // Speak("DeActivated");
            base.OnDeactivated(e);
        }

        #endregion

        public void AddErrors(string type, List<string> errors)
        {
            DateTime now = DateTime.Now;
            List<string> results = new List<string>();
            foreach (string error in errors)
            {
                results.Add($"{now.ToShortTimeString()} {error}");
            }

            _errorTable.SetError(type, StringUtil.MakePathList(results, "; "));
        }

        public void SetError(string type, string error)
        {
            /*
            if (type == "face" && error != null)
            {
                Debug.Assert(false, "");
            }
            */

            _errorTable.SetError(type, error);
        }

        public void ClearErrors(string type)
        {
            // _errors.Clear();
            _errorTable.SetError(type, "");
        }

        public event TagChangedEventHandler TagChanged = null;
        // public event SetErrorEventHandler TagSetError = null;

        private void RfidManager_ListTags(object sender, ListTagsEventArgs e)
        {
            // 标签总数显示
            // this.Number = e.Result?.Results?.Count.ToString();
            if (e.Result.Results != null)
            {
                TagList.Refresh(sender as BaseChannel<IRfid>,
                    e.ReaderNameList,
                    e.Result.Results,
                        (add_books, update_books, remove_books, add_patrons, update_patrons, remove_patrons) =>
                        {
                            TagChanged?.Invoke(sender, new TagChangedEventArgs
                            {
                                AddBooks = add_books,
                                UpdateBooks = update_books,
                                RemoveBooks = remove_books,
                                AddPatrons = add_patrons,
                                UpdatePatrons = update_patrons,
                                RemovePatrons = remove_patrons
                            });
                        },
                        (type, text) =>
                        {
                            RfidManager.TriggerSetError(this, new SetErrorEventArgs { Error = text });
                            // TagSetError?.Invoke(this, new SetErrorEventArgs { Error = text });
                        });

                // 标签总数显示 图书+读者卡
                this.Number = $"{TagList.Books.Count}:{TagList.Patrons.Count}";
            }
        }

        public static void SetSize(Window window, string style)
        {
            var mainWindows = App.CurrentApp.MainWindow;
            if (style == "tall")
            {
                window.Width = Math.Min(700, mainWindows.ActualWidth * 0.95);
                window.Height = Math.Min(900, mainWindows.ActualHeight * .95);
            }
            else if (style == "middle")
            {
                window.Width = Math.Min(700, mainWindows.ActualWidth * 0.95);
                window.Height = Math.Min(500, mainWindows.ActualHeight * .95);
            }
            else if (style == "wide")
            {
                window.Width = Math.Min(1000, mainWindows.ActualWidth * 0.95);
                window.Height = Math.Min(700, mainWindows.ActualHeight * .95);
            }
            else
            {
                window.Width = Math.Min(700, mainWindows.ActualWidth * 0.95);
                window.Height = Math.Min(500, mainWindows.ActualHeight * .95);
            }
        }
    }

    public delegate void TagChangedEventHandler(object sender,
TagChangedEventArgs e);

    /// <summary>
    /// 设置标签变化事件的参数
    /// </summary>
    public class TagChangedEventArgs : EventArgs
    {
        public List<TagAndData> AddBooks { get; set; }
        public List<TagAndData> UpdateBooks { get; set; }
        public List<TagAndData> RemoveBooks { get; set; }

        public List<TagAndData> AddPatrons { get; set; }
        public List<TagAndData> UpdatePatrons { get; set; }
        public List<TagAndData> RemovePatrons { get; set; }
    }

    public delegate void LineFeedEventHandler(object sender,
LineFeedEventArgs e);

    /// <summary>
    /// 条码枪输入一行文字的事件的参数
    /// </summary>
    public class LineFeedEventArgs : EventArgs
    {
        public string Text { get; set; }
    }


    public delegate void CharFeedEventHandler(object sender,
CharFeedEventArgs e);

    /// <summary>
    /// 条码枪输入一行文字的事件的参数
    /// </summary>
    public class CharFeedEventArgs : EventArgs
    {
        public CharInput CharInput { get; set; }
    }

    public delegate void UpdatedEventHandler(object sender,
UpdatedEventArgs e);

    /// <summary>
    /// 升级完成的事件的参数
    /// </summary>
    public class UpdatedEventArgs : EventArgs
    {
        public string Message { get; set; }
    }
}
