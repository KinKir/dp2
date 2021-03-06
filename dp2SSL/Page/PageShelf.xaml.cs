﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml;
using System.IO;

using dp2SSL.Models;
using static dp2SSL.LibraryChannelUtil;
using dp2SSL.Dialog;

using DigitalPlatform;
using DigitalPlatform.Core;
using DigitalPlatform.LibraryClient;
using DigitalPlatform.RFID;
using DigitalPlatform.Interfaces;
using DigitalPlatform.Text;
using DigitalPlatform.IO;
using DigitalPlatform.LibraryClient.localhost;
using DigitalPlatform.LibraryServer;
using DigitalPlatform.Xml;
using static dp2SSL.App;
using DigitalPlatform.Face;
using DigitalPlatform.WPF;

namespace dp2SSL
{
    /// <summary>
    /// PageShelf.xaml 的交互逻辑
    /// </summary>
    public partial class PageShelf : Page, INotifyPropertyChanged
    {
        LayoutAdorner _adorner = null;
        AdornerLayer _layer = null;

        // EntityCollection _entities = new EntityCollection();
        Patron _patron = new Patron();
        object _syncRoot_patron = new object();

        public string Mode { get; set; }    // 运行模式。空/initial

        public PageShelf()
        {
            InitializeComponent();

            _patronErrorTable = new ErrorTable((e) =>
            {
                _patron.Error = e;
            });

            Loaded += PageShelf_Loaded;
            Unloaded += PageShelf_Unloaded;

            this.DataContext = this;

            // this.booksControl.SetSource(_entities);
            this.patronControl.DataContext = _patron;
            this.patronControl.InputFace += PatronControl_InputFace;

            this._patron.PropertyChanged += _patron_PropertyChanged;

            this.doorControl.OpenDoor += DoorControl_OpenDoor;

            App.CurrentApp.PropertyChanged += CurrentApp_PropertyChanged;



            // this.error.Text = "test";
        }

        // parameters:
        //      mode    空字符串或者“initial”
        public PageShelf(string mode) : this()
        {
            this.Mode = mode;
        }


        private async void PageShelf_Loaded(object sender, RoutedEventArgs e)
        {
            // _firstInitial = false;

            FingerprintManager.SetError += FingerprintManager_SetError;
            FingerprintManager.Touched += FingerprintManager_Touched;

            App.CurrentApp.TagChanged += CurrentApp_TagChanged;

            // RfidManager.ListLocks += RfidManager_ListLocks;
            ShelfData.OpenCountChanged += CurrentApp_OpenCountChanged;
            ShelfData.DoorStateChanged += ShelfData_DoorStateChanged;
            //ShelfData.BookChanged += ShelfData_BookChanged;

            RfidManager.ClearCache();
            // 注：将来也许可以通过(RFID 以外的)其他方式输入图书号码
            if (string.IsNullOrEmpty(RfidManager.Url))
                this.SetGlobalError("rfid", "尚未配置 RFID 中心 URL");

            _layer = AdornerLayer.GetAdornerLayer(this.mainGrid);
            _adorner = new LayoutAdorner(this);

            {
                List<string> style = new List<string>();
                if (string.IsNullOrEmpty(App.RfidUrl) == false)
                    style.Add("rfid");
                if (string.IsNullOrEmpty(App.FingerprintUrl) == false)
                    style.Add("fingerprint");
                if (string.IsNullOrEmpty(App.FaceUrl) == false)
                    style.Add("face");
                this.patronControl.SetStartMessage(StringUtil.MakePathList(style));
            }

            /*
            try
            {
                RfidManager.LockCommands = DoorControl.GetLockCommands();
            }
            catch (Exception ex)
            {
                this.SetGlobalError("cfg", $"获得门锁命令时出错:{ex.Message}");
            }
            */

            // 要在初始化以前设定好
            // RfidManager.AntennaList = GetAntennaList();

            // _patronReaderName = GetPatronReaderName();

            App.Updated += App_Updated;

            App.LineFeed += App_LineFeed;
            App.CharFeed += App_CharFeed;

            if (Mode == "initial" || ShelfData.FirstInitialized == false)
            {
                try
                {
                    // TODO: 可否放到 App 的初始化阶段? 这样好处是菜单画面就可以看到有关数量显示了
                    // await InitialShelfEntities();

                    await Task.Run(async () =>
                    {
                        // 初始化之前开灯，让使用者感觉舒服一些(感觉机器在活动状态)
                        RfidManager.TurnShelfLamp("*", "turnOn");

                        await InitialShelfEntities();

                        // 初始化完成之后，应该是全部门关闭状态，还没有人开始使用，则先关灯，进入等待使用的状态
                        RfidManager.TurnShelfLamp("*", "turnOff");
                    });


                }
                catch (Exception ex)
                {
                    this.SetGlobalError("initial", $"InitialShelfEntities() 出现异常: {ex.Message}");
                    WpfClientInfo.WriteErrorLog($"InitialShelfEntities() 出现异常: {ExceptionUtil.GetDebugText(ex)}");
                }
            }

            // InputMethod.Current.ImeState = InputMethodState.Off;
#if DOOR_MONITOR

            if (ShelfData.DoorMonitor == null)
            {
                ShelfData.DoorMonitor = new DoorMonitor();
                /*
                ShelfData.DoorMonitor.Start(async (door) =>
                    {
                        ShelfData.RefreshInventory(door);
                        SaveDoorActions(door);
                        await SubmitCheckInOut();   // "silence"
                    },
                    new CancellationToken());
                    */
                // 不使用独立线程。而是借用 getLockState 的线程来处理
                ShelfData.DoorMonitor.Initialize(async (door, clearOperator) =>
                {
                    ShelfData.RefreshInventory(door);
                    SaveDoorActions(door, clearOperator);
                    // door.Operator = null;
                    await SubmitCheckInOut();   // "silence"
                });
            }
#endif
        }

        private void App_Updated(object sender, UpdatedEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                this.updateInfo.Text = e.Message;
                if (string.IsNullOrEmpty(this.updateInfo.Text) == false)
                    this.updateInfo.Visibility = Visibility.Visible;
                else
                    this.updateInfo.Visibility = Visibility.Collapsed;
            }));
        }

        static string ToString(BarcodeCapture.CharInput input)
        {
            return $"{input.Key} string='{input.KeyChar}'";
        }

        private void App_CharFeed(object sender, CharFeedEventArgs e)
        {
            // 用来观察单个击键
            SetGlobalError("charinput", ToString(e.CharInput));
        }

        private async void App_LineFeed(object sender, LineFeedEventArgs e)
        {
            // 扫入一个条码
            string barcode = e.Text.ToUpper();
            // 检查防范空字符串，和使用工作人员方式(~开头)的字符串
            if (string.IsNullOrEmpty(barcode) || barcode.StartsWith("~"))
                return;

            // return:
            //      false   没有成功
            //      true    成功
            SetPatronInfo(new GetMessageResult { Message = barcode });

            // resut.Value
            //      -1  出错
            //      0   没有填充
            //      1   成功填充
            var result = await FillPatronDetail();
            if (result.Value == 1)
                Welcome();
        }

        object _syncRoot_save = new object();

        // 门状态变化。从这里触发提交
        private async void ShelfData_DoorStateChanged(object sender, DoorStateChangedEventArgs e)
        {
            if (e.NewState == "close")
            {
                // 2019/12/15
                // 补做一次 inventory，确保不会漏掉 RFID 变动信息
                //WpfClientInfo.WriteInfoLog($"++incWaiting() door '{e.Door.Name}' state changed");
                e.Door.IncWaiting();  // inventory 期间显示等待动画
                try
                {
                    // TODO: 这里用 await 是否不太合适？
                    await Task.Run(() =>
                    {
                        ShelfData.RefreshInventory(e.Door);
                    });
                }
                finally
                {
                    e.Door.DecWaiting();
                    //WpfClientInfo.WriteInfoLog($"--decWaiting() door '{e.Door.Name}' state changed");
                }

                lock (_syncRoot_save)
                {
                    SaveDoorActions(e.Door, true);

                    /*
                    // testing
                    // 先保存一套 actions
                    List<ActionInfo> temp = new List<ActionInfo>();
                    temp.AddRange(ShelfData.Actions);
                    */

                    // e.Door.Operator = null; // 清掉门上的操作者名字
                }

                // 注: 调用完成后门控件上的 +- 数字才会消失
                var task = SubmitCheckInOut("");

                /*
                // testing
                ShelfData.Actions.AddRange(temp);
                await SubmitCheckInOut("");
                */
            }

            if (e.NewState == "open")
            {
                // ShelfData.ProcessOpenCommand(e.Door, e.Comment);

                // e.Door.Waiting--;
            }

#if DOOR_MONITOR

            // 取消状态变化监控
            ShelfData.DoorMonitor.RemoveMessages(e.Door);
#endif
        }

        static string GetPartialName(string buttonName)
        {
            if (buttonName == "count")
                return "全部图书";
            if (buttonName == "add")
                return "新放入";
            if (buttonName == "remove")
                return "新取出";
            if (buttonName == "errorCount")
                return "状态错误的图书";
            return buttonName;
        }

        private void ShowBookInfo(object sender, OpenDoorEventArgs e)
        {
            // 书柜外的读卡器触发观察图书信息对话框
            // if (e.Door.Type == "free" && e.Adds != null && e.Adds.Count > 0)
            {
                BookInfoWindow bookInfoWindow = null;

                EntityCollection collection = null;
                if (e.ButtonName == "count")
                    collection = e.Door.AllEntities;
                else if (e.ButtonName == "add")
                    collection = e.Door.AddEntities;
                else if (e.ButtonName == "remove")
                    collection = e.Door.RemoveEntities;
                else if (e.ButtonName == "errorCount")
                    collection = e.Door.ErrorEntities;

                Application.Current.Dispatcher.Invoke(new Action(() =>
                {
                    bookInfoWindow = new BookInfoWindow();
                    bookInfoWindow.TitleText = $"{e.Door.Name} {GetPartialName(e.ButtonName)}";
                    bookInfoWindow.Owner = Application.Current.MainWindow;
                    bookInfoWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    App.SetSize(bookInfoWindow, "wide");
                    //bookInfoWindow.Width = Math.Min(1000, this.ActualWidth);
                    //bookInfoWindow.Height = Math.Min(700, this.ActualHeight);
                    bookInfoWindow.Closed += BookInfoWindow_Closed;
                    bookInfoWindow.SetBooks(collection);
                    bookInfoWindow.Show();
                    AddLayer();
                }));
            }
        }

        private void BookInfoWindow_Closed(object sender, EventArgs e)
        {
            RemoveLayer();
        }

        // 当前读者卡状态是否 OK?
        static bool IsPatronOK(Patron patron, string action, out string message)
        {
            message = "";

            // 如果 UID 为空，而 Barcode 有内容，也是 OK 的。这是指纹的场景
            if (string.IsNullOrEmpty(patron.UID) == true
                && string.IsNullOrEmpty(patron.Barcode) == false)
                return true;

            // UID 和 Barcode 都不为空。这是 15693 和 14443 读者卡的场景
            if (string.IsNullOrEmpty(patron.UID) == false
    && string.IsNullOrEmpty(patron.Barcode) == false)
                return true;

            string debug_info = $"uid:[{patron.UID}],barcode:[{patron.Barcode}]";
            if (action == "open")
            {
                // 提示信息要考虑到应用了指纹的情况
                if (string.IsNullOrEmpty(App.FingerprintUrl) == false)
                    message = $"请先刷读者卡，或扫入一次指纹，然后再开门\r\n({debug_info})";
                else
                    message = $"请先刷读者卡，然后再开门\r\n({debug_info})";
            }
            else
            {
                // 调试用
                message = $"读卡器上的当前读者卡状态不正确。无法进行 {action} 操作\r\n({debug_info})";
            }
            return false;
        }

        void DisplayError(ref ProgressWindow progress,
    string message,
    string color = "red")
        {
            if (progress == null)
                return;
            MemoryDialog(progress);
            var temp = progress;
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                temp.MessageText = message;
                temp.BackColor = color;
                temp = null;
            }));
            progress = null;
        }

        void DisplayMessage(ProgressWindow progress,
            string message,
            string color = "")
        {
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                progress.MessageText = message;
                if (string.IsNullOrEmpty(color) == false)
                    progress.BackColor = color;
            }));
        }

        List<Window> _dialogs = new List<Window>();

        void CloseDialogs()
        {
            // 确保 page 关闭时对话框能自动关闭
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                foreach (var window in _dialogs)
                {
                    window.Close();
                }
            }));
        }

        void MemoryDialog(Window dialog)
        {
            _dialogs.Add(dialog);
        }

        delegate string Delegate_process(ProgressWindow progress);

        void ProcessBox(string start_message,
            Delegate_process func)
        {
            ProgressWindow progress = null;

            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                progress = new ProgressWindow();
                progress.MessageText = start_message;
                progress.Owner = Application.Current.MainWindow;
                progress.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                progress.Closed += Progress_Closed;
                //if (StringUtil.IsInList("button_ok", style))
                //    progress.okButton.Content = "确定";
                progress.Show();
                AddLayer();
            }));

            string result_message = func?.Invoke(progress);

            if (string.IsNullOrEmpty(result_message) == false)
                DisplayError(ref progress, result_message, "red");
            else
            {
                Application.Current.Dispatcher.Invoke(new Action(() =>
                {
                    progress.Close();
                }));
            }
        }

        void ErrorBox(string message,
            string color = "red",
            string style = "")
        {
            ProgressWindow progress = null;

            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                progress = new ProgressWindow();
                progress.MessageText = "正在处理，请稍候 ...";
                progress.Owner = Application.Current.MainWindow;
                progress.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                App.SetSize(progress, "tall");
                //progress.Width = Math.Min(700, this.ActualWidth);
                //progress.Height = Math.Min(900, this.ActualHeight);
                progress.Closed += Progress_Closed;
                if (StringUtil.IsInList("button_ok", style))
                    progress.okButton.Content = "确定";
                progress.Show();
                AddLayer();
            }));


            if (StringUtil.IsInList("auto_close", style))
            {
                DisplayMessage(progress, message, color);

                Task.Run(() =>
                {
                    // TODO: 显示倒计时计数？
                    Task.Delay(TimeSpan.FromSeconds(1)).Wait();
                    Application.Current.Dispatcher.Invoke(new Action(() =>
                    {
                        progress.Close();
                    }));
                });
            }
            else
                DisplayError(ref progress, message, color);
        }

        // 点门控件触发开门
        private async void DoorControl_OpenDoor(object sender, OpenDoorEventArgs e)
        {
            // 观察图书详情
            if (string.IsNullOrEmpty(e.ButtonName) == false)
            {
                ShowBookInfo(sender, e);
                return;
            }

            // 没有门锁的门
            if (string.IsNullOrEmpty(e.Door.LockPath))
            {
                ErrorBox("没有门锁");
                return;
            }

            // 检查门锁是否已经是打开状态?
            if (e.Door.State == "open")
            {
                App.CurrentApp.Speak("已经打开");
                ErrorBox("已经打开", "yellow", "auto_close,button_ok");
                return;
            }

            if (e.Door.Waiting > 0)
            {
                // 正在开门中，要放弃重复开门的动作
                App.CurrentApp.Speak("正在打开，请稍等");   // 打开或者关闭都会造成这个状态
                return;
            }

            // TODO: 这里最好锁定
            Patron current_patron = null;

            lock (_syncRoot_patron)
            {
                current_patron = _patron.Clone();
            }


            // TODO: 提前到这里这里清除读者信息?

            // 以前积累的 _adds 和 _removes 要先处理，处理完再开门

            // 先检查当前是否具备读者身份？
            // 检查读者卡状态是否 OK
            if (IsPatronOK(current_patron, "open", out string check_message) == false)
            {
                if (string.IsNullOrEmpty(check_message))
                    check_message = $"(读卡器上的)当前读者卡状态不正确。无法进行开门操作";

                ErrorBox(check_message);
                return;
            }

            var person = new Operator
            {
                PatronBarcode = current_patron.Barcode,
                PatronName = current_patron.PatronName
            };
            string libraryCodeOfDoor = ShelfData.GetLibraryCode(e.Door.ShelfNo);

            // 检查读者记录状态
            if (person.IsWorker == false)
            {
                XmlDocument readerdom = new XmlDocument();
                readerdom.LoadXml(current_patron.Xml);
                // return:
                //      -1  检查过程出错
                //      0   状态不正常
                //      1   状态正常
                int nRet = LibraryServerUtil.CheckPatronState(readerdom,
                    out string strError);
                if (nRet != 1)
                {
                    ErrorBox(strError);
                    return;
                }

                // 检查读者所在分馆是否和打算打开的门的 shelfNo 参数矛盾
                string libraryCodeOfPatron = DomUtil.GetElementText(readerdom.DocumentElement, "libraryCode");
                if (libraryCodeOfDoor != libraryCodeOfPatron)
                {
                    ErrorBox($"权限不足，无法开门。\r\n\r\n详情: 读者 {_patron.PatronName} 所属馆代码 '{libraryCodeOfPatron}' 和门所属馆代码 '{libraryCodeOfDoor}' 不同");
                    return;
                }
            }
            else
            {
                // 对于工作人员也要检查其分馆是否和门的分馆矛盾

                var account = App.CurrentApp.FindAccount(person.GetWorkerAccountName());
                if (account == null)
                {
                    ErrorBox($"FindAccount('{person.GetWorkerAccountName()}') return null");
                    return;
                }

                if (Account.IsGlobalUser(account.LibraryCodeList) == false
                    && StringUtil.IsInList(libraryCodeOfDoor, account.LibraryCodeList) == false)
                {
                    ErrorBox($"权限不足，无法开门。\r\n\r\n详情: 工作人员 {person.GetWorkerAccountName()} 所属馆代码 '{account.LibraryCodeList}' 无法管辖门所属馆代码 '{libraryCodeOfDoor}'");
                    return;
                }
            }

            // MessageBox.Show(e.Name);

            // TODO: 显示一个模式对话框挡住界面，直到收到门状态变化的信号再自动关闭对话框。这样可以防止开门瞬间、还没有收到开门信号的时候用户突然点 home 按钮回到主菜单(因为这样会突破“主菜单界面不允许处在开门状态”的规则)
            ProgressWindow progress = null;
#if REMOVED
            bool cancelled = false;
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                progress = new ProgressWindow();
                // progress.TitleText = "初始化智能书柜";
                progress.MessageText = $"{_patron.PatronName} 正在开门，请稍候 ...";
                progress.Owner = Application.Current.MainWindow;
                progress.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                progress.Closed += (s1, e1) =>
                {
                    RemoveLayer();
                    cancelled = true;
                };
                App.SetSize(progress, "middle");

                //progress.Width = Math.Min(700, this.ActualWidth);
                //progress.Height = Math.Min(500, this.ActualHeight);
                // progress.okButton.Content = "取消";
                progress.okButton.Visibility = Visibility.Collapsed;
                progress.Show();
                AddLayer();
            }));
#endif
            bool succeed = false;
            //WpfClientInfo.WriteInfoLog($"++incWaiting() door '{e.Door.Name}' open door");
            e.Door.IncWaiting();

            try
            {
                // 2019/12/16
                // 开门点击动作重入
                if (e.Door.Waiting > 1)
                {
                    App.CurrentApp.Speak("正在开门中，请稍等");
                    return;
                }

                // 2019/12/21
                if (e.Door.Operator != null)
                {
                    WpfClientInfo.WriteInfoLog($"开门前发现门 {e.Door.Name} 的 Operator 不为空(为 '{e.Door.Operator.ToString()}')，所以补做一次 Inventory");
                    // 补做一次 inventory，确保不会漏掉 RFID 变动信息
                    try
                    {
                        await Task.Run(() =>
                        {
                            ShelfData.RefreshInventory(e.Door);
                            SaveDoorActions(e.Door, false);
                            // TODO: 是否 Submit? Submit 窗口可能会干扰原本的开门流程
                        });
                    }
                    catch (Exception ex)
                    {
                        WpfClientInfo.WriteErrorLog($"对门 {e.Door.Name} 补做 Inventory 出现异常: {ExceptionUtil.GetDebugText(ex)}");
                    }
                }

                e.Door.Operator = person;

                // 把发出命令的瞬间安排在 getLockState 之前的间隙。就是说不和 getLockState 重叠
                //lock (RfidManager.SyncRoot)
                //{
                // 开始监控这个门的状态变化。如果超过一定时间没有得到开门状态，则主动补一次 submit 动作
                // ShelfData.DoorMonitor?.BeginMonitor(e.Door);

                /*
                // TODO: 是否这里要等待开门信号到来时候再给门赋予操作者身份？因为过早赋予身份，可能会破坏一个姗姗来迟的早先一个关门动作信号的提交动作
                // 给门赋予操作者身份
                ShelfData.PushCommand(e.Door, person, RfidManager.LockHeartbeat);
                */

                long startTicks = RfidManager.LockHeartbeat;

                var result = RfidManager.OpenShelfLock(e.Door.LockPath);
                if (result.Value == -1)
                {
                    //MessageBox.Show(result.ErrorInfo);
                    DisplayError(ref progress, result.ErrorInfo);
                    e.Door.Operator = null;
                    /*
                    ShelfData.PopCommand(e.Door, "cancelled");
                    */
#if DOOR_MONITOR

                    // 取消监控
                    ShelfData.DoorMonitor?.RemoveMessages(e.Door);
#endif
                    return;
                }
                //}

                // TODO: 加锁。避免和 Clone() 互相干扰
                // 如果读者信息区没有被固定，则门开后会自动清除读者信息区
                if (PatronFixed == false)
                    PatronClear();

                // 开门动作会中断延迟任务
                CancelDelayClearTask();

                // 一旦成功，门的 waiting 状态会在 PopCommand 的同时被改回 false
                // succeed = true;

                // 等待确认收到开门信号
                await Task.Run(async () =>
                {
                    DateTime start = DateTime.Now;
                    while (e.Door.State != "open"
                    // && cancelled == false
                    )
                    {
                        // 超时。补一次开门和关门提交动作
                        // if (DateTime.Now - start >= TimeSpan.FromSeconds(5))
                        if (RfidManager.LockHeartbeat - startTicks >= 3)
                        {
                            App.CurrentApp.Speak("超时补做提交");
                            WpfClientInfo.WriteInfoLog($"超时情况下，对门 {e.Door.Name} 补做一次 submit");

                            ShelfData.RefreshInventory(e.Door);
                            SaveDoorActions(e.Door, true);
                            await SubmitCheckInOut();   // "silence"
                            break;
                        }

                        Thread.Sleep(500);
                    }
                });
            }
            finally
            {
                if (progress != null)
                {
                    Application.Current.Dispatcher.Invoke(new Action(() =>
                    {
                        if (progress != null)
                            progress.Close();
                    }));
                }

                if (succeed == false)
                {
                    e.Door.DecWaiting();
                    //WpfClientInfo.WriteInfoLog($"--decWaiting() door '{e.Door.Name}' on cancel");
                }
            }
        }

        private void CurrentApp_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Error")
            {
                OnPropertyChanged(e.PropertyName);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        void OnPropertyChanged(string name)
        {
            if (this.PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(name));
        }

        public string Error
        {
            get
            {
                return App.CurrentApp.Error;
            }
        }


        private void CurrentApp_OpenCountChanged(object sender, OpenCountChangedEventArgs e)
        {
            // 当所有门都关闭后，即便是固定了的读者信息也要自动被清除。此举是避免读者忘了清除固定的读者信息
            if (e.OldCount > 0 && e.NewCount == 0)
                PatronClear();
#if NO
            // 如果从有门打开的状态变为全部门都关闭的状态，要尝试提交一次出纳请求
            if (e.OldCount > 0 && e.NewCount == 0)
            {
                // await SubmitCheckInOut("clearPatron,verifyDoorClosing");  // 要求检查门是否全关闭
                SaveActions();
                PatronClear(false);  // 确保在没有可提交内容的情况下也自动清除读者信息

                await SubmitCheckInOut("verifyDoorClosing");
            }
#endif
        }

#if NO
        int _openCount = 0; // 当前处于打开状态的门的个数

        private void RfidManager_ListLocks(object sender, ListLocksEventArgs e)
        {
            if (e.Result.Value == -1)
                return;

            // bool triggerAllClosed = false;
            {
                int count = 0;
                foreach (var state in e.Result.States)
                {
                    if (state.State == "open")
                        count++;
                    var result = DoorItem.SetLockState(_doors, state);
                    if (result.LockName != null && result.OldState != null && result.NewState != null)
                    {
                        if (result.NewState != result.OldState)
                        {
                            if (result.NewState == "open")
                                App.CurrentApp.Speak($"{result.LockName} 打开");
                            else
                                App.CurrentApp.Speak($"{result.LockName} 关闭");
                        }
                    }
                }

                //if (_openCount > 0 && count == 0)
                //    triggerAllClosed = true;

                SetOpenCount(count);
            }
        }

        // 设置打开门数量
        void SetOpenCount(int count)
        {
            int oldCount = _openCount;

            _openCount = count;

            // 打开门的数量发生变化
            if (oldCount != _openCount)
            {
                /*
                if (_openCount == 0)
                {
                    // 关闭图书读卡器(只使用读者证读卡器)
                    if (string.IsNullOrEmpty(_patronReaderName) == false
                        && RfidManager.ReaderNameList != _patronReaderName)
                    {
                        RfidManager.ReaderNameList = _patronReaderName;
                        RfidManager.ClearCache();
                    }
                }
                else
                {
                    // 打开图书读卡器(同时也使用读者证读卡器)
                    if (RfidManager.ReaderNameList != "*")
                    {
                        RfidManager.ReaderNameList = "*";
                        RfidManager.ClearCache();
                    }
                }*/
                if (oldCount > 0 && count == 0)
                {
                    // TODO: 如果从有门打开的状态变为全部门都关闭的状态，要尝试提交一次出纳请求
                    // if (triggerAllClosed)
                    {
                        SubmitCheckInOut();
                        PatronClear(false);  // 确保在没有可提交内容的情况下也自动清除读者信息
                    }
                }

            }
        }
#endif

        /*
        LockChanged SetLockState(LockState state)
        {
            return this.doorControl.SetLockState(state);
        }
        */

        private async void PageShelf_Unloaded(object sender, RoutedEventArgs e)
        {
            App.LineFeed -= App_LineFeed;
            App.CharFeed -= App_CharFeed;

            App.Updated -= App_Updated;

            CancelDelayClearTask();

            // 提交尚未提交的取出和放入
            // PatronClear(true);
            SaveAllActions();
            await Submit(true);

            RfidManager.SetError -= RfidManager_SetError;

            App.CurrentApp.TagChanged -= CurrentApp_TagChanged;
            //ShelfData.BookChanged -= ShelfData_BookChanged;

            FingerprintManager.Touched -= FingerprintManager_Touched;
            FingerprintManager.SetError -= FingerprintManager_SetError;

            // RfidManager.ListLocks -= RfidManager_ListLocks;
            ShelfData.OpenCountChanged -= CurrentApp_OpenCountChanged;
            ShelfData.DoorStateChanged -= ShelfData_DoorStateChanged;

            if (_progressWindow != null)
                _progressWindow.Close();
            // 确保 page 关闭时对话框能自动关闭
            CloseDialogs();
            PatronClear();
        }

        // 从指纹阅读器获取消息(第一阶段)
        private async void FingerprintManager_Touched(object sender, TouchedEventArgs e)
        {
            // return:
            //      false   没有成功
            //      true    成功
            SetPatronInfo(e.Result);

            // resut.Value
            //      -1  出错
            //      0   没有填充
            //      1   成功填充
            var result = await FillPatronDetail();
            if (result.Value == 1)
                Welcome();
#if NO
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                _patron.IsFingerprintSource = true;
                _patron.Barcode = "test1234";
            }));
#endif
        }

        // 从指纹阅读器(或人脸)获取消息(第一阶段)
        // return:
        //      false   没有成功
        //      true    成功
        bool SetPatronInfo(GetMessageResult result)
        {
            if (ClosePasswordDialog() == true)
            {
                // 这次刷卡的作用是取消了上次登录
                return false;
            }

            if (result.Value == -1)
            {
                SetPatronError("fingerprint", $"指纹中心出错: {result.ErrorInfo}, 错误码: {result.ErrorCode}");
                if (_patron.IsFingerprintSource)
                    PatronClear();    // 只有当面板上的读者信息来源是指纹仪时，才清除面板上的读者信息
                return false;
            }
            else
            {
                // 清除以前残留的报错信息
                SetPatronError("fingerprint", "");
            }

            if (result.Message == null)
                return false;

            PatronClear();
            _patron.IsFingerprintSource = true;
            _patron.PII = result.Message;
            return true;
        }

        private void FingerprintManager_SetError(object sender, SetErrorEventArgs e)
        {
            SetGlobalError("fingerprint", e.Error);
        }

        private void RfidManager_SetError(object sender, SetErrorEventArgs e)
        {
            SetGlobalError("rfid", e.Error);
            /*
            if (e.Error == null)
            {
                // 恢复正常
            }
            else
            {
                // 进入错误状态
                if (_rfidState != "error")
                {
                    await ClearBooksAndPatron(null);
                }

                _rfidState = "error";
            }
            */
        }

#if NO
        async Task<NormalResult> Update(
            BaseChannel<IRfid> channel_param,
            List<Entity> update_entities,
            CancellationToken token)
        {
            if (update_entities.Count > 0)
            {
                try
                {
                    BaseChannel<IRfid> channel = channel_param;
                    if (channel == null)
                        channel = RfidManager.GetChannel();
                    try
                    {
                        await FillBookFields(channel, update_entities, token);
                    }
                    finally
                    {
                        if (channel_param == null)
                            RfidManager.ReturnChannel(channel);
                    }
                }
                catch (Exception ex)
                {
                    string error = $"填充图书信息时出现异常: {ex.Message}";
                    SetGlobalError("rfid", error);
                    return new NormalResult { Value = -1, ErrorInfo = error };
                }

                // 自动检查 EAS 状态
                // CheckEAS(update_entities);
            }
            return new NormalResult();
        }

#endif

        // 设置全局区域错误字符串
        void SetGlobalError(string type, string error)
        {
            /*
            if (error != null && error.StartsWith("未"))
                throw new Exception("test");
                */
            App.CurrentApp?.SetError(type, error);
        }

        // 第二阶段：填充图书信息的 PII 和 Title 字段
        async Task FillBookFields(BaseChannel<IRfid> channel,
            List<Entity> entities,
            CancellationToken token)
        {
            try
            {
                foreach (Entity entity in entities)
                {
                    if (token.IsCancellationRequested)
                        return;

                    if (entity.FillFinished == true)
                        continue;

                    // 获得 PII
                    // 注：如果 PII 为空，文字中要填入 "(空)"
                    if (string.IsNullOrEmpty(entity.PII))
                    {
                        if (entity.TagInfo == null)
                            continue;

                        Debug.Assert(entity.TagInfo != null);

                        // Exception:
                        //      可能会抛出异常 ArgumentException TagDataException
                        LogicChip chip = LogicChip.From(entity.TagInfo.Bytes,
(int)entity.TagInfo.BlockSize,
"" // tag.TagInfo.LockStatus
);
                        string pii = chip.FindElement(ElementOID.PII)?.Text;
                        entity.PII = PageBorrow.GetCaption(pii);
                    }

                    // 获得 Title
                    // 注：如果 Title 为空，文字中要填入 "(空)"
                    if (string.IsNullOrEmpty(entity.Title)
                        && string.IsNullOrEmpty(entity.PII) == false && entity.PII != "(空)")
                    {
                        GetEntityDataResult result = await
                            Task<GetEntityDataResult>.Run(() =>
                            {
                                return GetEntityData(entity.PII);
                            });

                        if (result.Value == -1)
                        {
                            entity.SetError(result.ErrorInfo);
                            continue;
                        }
                        entity.Title = PageBorrow.GetCaption(result.Title);
                        entity.SetData(result.ItemRecPath, result.ItemXml);
                    }

                    entity.SetError(null);
                    entity.FillFinished = true;
                }

                // booksControl.SetBorrowable();
            }
            catch (Exception ex)
            {
                SetGlobalError("current", $"FillBookFields exception: {ex.Message}");
            }
        }

        // 初始化时列出当前馆藏地应有的全部图书
        // 本函数中，只给 Entity 对象里面设置好了 PII，其他成员尚未设置
        static void FillLocationBooks(EntityCollection entities,
            string location,
            CancellationToken token)
        {
            var channel = App.CurrentApp.GetChannel();
            try
            {
                long lRet = channel.SearchItem(null,
                    "<全部>",
                    location,
                    5000,
                    "馆藏地点",
                    "exact",
                    "zh",
                    "shelfResultset",
                    "",
                    "",
                    out string strError);
                if (lRet == -1)
                    throw new ChannelException(channel.ErrorCode, strError);

                string strStyle = "id,cols,format:@coldef:*/barcode|*/borrower";

                ResultSetLoader loader = new ResultSetLoader(channel,
                    null,
                    "shelfResultset",
                    strStyle,
                    "zh");
                foreach (DigitalPlatform.LibraryClient.localhost.Record record in loader)
                {
                    token.ThrowIfCancellationRequested();
                    string pii = record.Cols[0];
                    Application.Current.Dispatcher.Invoke(new Action(() =>
                    {
                        entities.Add(pii);
                    }));
                }
            }
            finally
            {
                App.CurrentApp.ReturnChannel(channel);
            }
        }

        private async void CurrentApp_TagChanged(object sender, TagChangedEventArgs e)
        {
            // TODO: 对已经拿走的读者卡，用 TagList.ClearTagTable() 清除它的缓存内容

            // 读者。不再精细的进行增删改跟踪操作，而是笼统地看 TagList.Patrons 集合即可
            var task = RefreshPatrons();

            await ShelfData.ChangeEntities((BaseChannel<IRfid>)sender,
                e,
                () =>
                {
                    // 如果图书数量有变动，要自动清除挡在前面的残留的对话框
                    CloseDialogs();
                });

            // "initial" 模式下，立即合并到 _all。等关门时候一并提交请求
            // TODO: 不过似乎此时有语音提示放入、取出，似乎更显得实用一些？
            if (this.Mode == "initial")
            {
                var adds = ShelfData.Adds; // new List<Entity>(ShelfData.Adds);
                /*
                foreach (var entity in adds)
                {
                    ShelfData.Add("all", entity);

                    ShelfData.Remove("adds", entity);
                    ShelfData.Remove("removes", entity);
                }
                */
                {
                    ShelfData.Add("all", adds);

                    ShelfData.Remove("adds", adds);
                    ShelfData.Remove("removes", adds);
                }

                // List<Entity> removes = new List<Entity>(ShelfData.Removes);
                var removes = ShelfData.Removes;
                /*
                foreach (var entity in removes)
                {
                    ShelfData.Remove("all", entity);

                    ShelfData.Remove("adds", entity);
                    ShelfData.Remove("removes", entity);
                }
                */
                {
                    ShelfData.Remove("all", removes);

                    ShelfData.Remove("adds", removes);
                    ShelfData.Remove("removes", removes);
                }

                ShelfData.RefreshCount();
            }
        }

        bool _initialCancelled = false;

#if OLD_VERSION
        // 初始化开始前，要先把 RfidManager.ReaderNameList 设置为 "*"
        // 初始化完成前，先不要允许(开关门变化导致)修改 RfidManager.ReaderNameList
        async Task InitialShelfEntities()
        {
            if (ShelfData.FirstInitialized)
                return;

            this.doorControl.Visibility = Visibility.Collapsed;
            _initialCancelled = false;

            ProgressWindow progress = null;
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                progress = new ProgressWindow();
                progress.TitleText = "初始化智能书柜";
                progress.MessageText = "正在初始化图书信息，请稍候 ...";
                progress.Owner = Application.Current.MainWindow;
                progress.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                progress.Closed += Progress_Cancelled;
                App.SetSize(progress, "tall");
                // progress.Width = Math.Min(700, this.ActualWidth);
                // progress.Height = Math.Min(900, this.ActualHeight);
                progress.okButton.Content = "取消";
                progress.Show();
                AddLayer();
            }));
            this.doorControl.Visibility = Visibility.Hidden;

            try
            {
                var initial_result = await ShelfData.InitialShelfEntities(
                    (s) =>
                    {
                        DisplayMessage(progress, s, "green");
                    },
                    () =>
                    {
                        return _initialCancelled;
                    });

                if (_initialCancelled)
                    return;

                // 2019/11/29
                // 先报告一次标签数据错误
                if (initial_result.Warnings?.Count > 0)
                {
                    ErrorBox(StringUtil.MakePathList(initial_result.Warnings, "\r\n"));
                }

#if NO
                // TODO: 出现“正在初始化”的对话框。另外需要注意如果 DataReady 信号永远来不了怎么办
                await Task.Run(() =>
                {
                    TagList.DataReady = false;
                    // TODO: 是否一开始主动把 RfidManager ReaderNameList 设置为 "*"?
                    while (true)
                    {
                        if (TagList.DataReady == true)
                            return true;
                        Thread.Sleep(100);
                    }
                });

                _all.Clear();
                var books = TagList.Books;
                foreach (var tag in books)
                {
                    _all.Add(NewEntity(tag));
                }

                // DoorItem.DisplayCount(_all, _adds, _removes, App.CurrentApp.Doors);
                ShelfData.RefreshCount();
#endif
                // 把门显示出来。因为此时需要看到是否关门的状态
                this.doorControl.Visibility = Visibility.Visible;
                this.doorControl.InitializeButtons(ShelfData.ShelfCfgDom, ShelfData.Doors);

                // 检查门是否为关闭状态？
                // 注意 RfidManager 中门锁启动需要一定时间。状态可能是：尚未初始化/有门开着/门都关了
                await Task.Run(() =>
                {
                    while (ShelfData.OpeningDoorCount > 0)
                    {
                        if (_initialCancelled)
                            break;
                        DisplayMessage(progress, "请关闭全部柜门，以完成初始化", "yellow");
                        Thread.Sleep(1000);
                    }
                });

                if (_initialCancelled)
                    return;

                // 此时门是关闭状态。让读卡器切换到节省盘点状态
                ShelfData.RefreshReaderNameList();

                // TODO: 如何显示还书操作中的报错信息? 看了报错以后点继续?
                // result.Value
                //      -1  出错
                //      0   没有必要处理
                //      1   已经处理
                var result = TryReturn(progress, ShelfData.All);
                if (result.MessageDocument != null
                    && result.MessageDocument.ErrorCount > 0)
                {
                    string speak = "";
                    {
                        Application.Current.Dispatcher.Invoke(new Action(() =>
                        {
                            progress.BackColor = "yellow";
                            progress.MessageDocument = result.MessageDocument.BuildDocument(18, out speak);
                            if (result.MessageDocument.ErrorCount > 0)
                                progress = null;
                        }));
                    }
                    if (string.IsNullOrEmpty(speak) == false)
                        App.CurrentApp.Speak(speak);
                }

                if (_initialCancelled)
                    return;

                /*
                if (_initialCancelled == false)
                {
                    this.doorControl.Visibility = Visibility.Visible;
                }
                */

                /*
                var manage_result = RfidManager.ManageReader(ShelfData.DoorReaderName, "CloseRFTransmitter");
                if (manage_result.Value == -1)
                    this.SetGlobalError("InitialShelfEntities", $"ManageReader() 出错: {manage_result.ErrorInfo}");
                    */
                SelectAntenna();
            }
            finally
            {
                // _firstInitial = true;   // 第一次初始化已经完成
                RemoveLayer();

                if (_initialCancelled == false)
                {
                    // PageMenu.MenuPage.shelf.Visibility = Visibility.Visible;

                    if (progress != null)
                    {
                        progress.Closed -= Progress_Cancelled;
                        Application.Current.Dispatcher.Invoke(new Action(() =>
                        {
                            if (progress != null)
                                progress.Close();
                        }));
                    }

                    SetGlobalError("initial", null);
                    this.Mode = ""; // 从初始化模式转为普通模式
                }
                else
                {
                    ShelfData.FirstInitialized = false;

                    // PageMenu.MenuPage.shelf.Visibility = Visibility.Collapsed;

                    // TODO: 页面中央大字显示“书柜初始化失败”。重新进入页面时候应该自动重试初始化
                    SetGlobalError("initial", "智能书柜初始化失败。请检查读卡器和门锁参数配置，重新进行初始化 ...");
                    /*
                    ProgressWindow error = null;
                    Application.Current.Dispatcher.Invoke(new Action(() =>
                    {
                        error = new ProgressWindow();
                        error.Owner = Application.Current.MainWindow;
                        error.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                        error.Closed += Error_Closed;
                        error.Show();
                        AddLayer();
                    }));
                    DisplayError(ref error, "智能书柜初始化失败。请检查读卡器和门锁参数配置，重新进行初始化 ...");
                    */
                }
            }

            // TODO: 初始化中断后，是否允许切换到菜单和设置画面？(只是不让进入书架画面)
        }

#else
        public void InitialDoorControl()
        {
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                // 把门显示出来
                this.doorControl.Visibility = Visibility.Visible;
                this.doorControl.InitializeButtons(ShelfData.ShelfCfgDom, ShelfData.Doors);
            }));
        }

        // 新版本的首次填充图书信息的函数
        async Task InitialShelfEntities()
        {
            if (ShelfData.FirstInitialized)
                return;

            // 尚未配置 shelf.xml
            if (ShelfData.ShelfCfgDom == null)
                return;

            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                this.doorControl.Visibility = Visibility.Collapsed;
            }));

            _initialCancelled = false;

            ProgressWindow progress = null;
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                progress = new ProgressWindow();
                progress.TitleText = "初始化智能书柜";
                progress.MessageText = "正在初始化图书信息，请稍候 ...";
                progress.Owner = Application.Current.MainWindow;
                progress.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                progress.Closed += Progress_Cancelled;
                App.SetSize(progress, "tall");
                // progress.Width = Math.Min(700, this.ActualWidth);
                // progress.Height = Math.Min(900, this.ActualHeight);
                progress.okButton.Content = "取消";
                progress.Show();
                AddLayer();
            }));

            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                // 把门显示出来。因为此时需要看到是否关门的状态
                this.doorControl.Visibility = Visibility.Visible;
                this.doorControl.InitializeButtons(ShelfData.ShelfCfgDom, ShelfData.Doors);
            }));

            // 等待锁控就绪
            var lock_result = await ShelfData.WaitLockReady(
                (s) =>
                {
                    DisplayMessage(progress, s, "green");
                },
                () =>
                {
                    return _initialCancelled;
                })
                .ConfigureAwait(false);
            if (lock_result.Value == -1)
                return;

            try
            {

                // 检查门是否为关闭状态？
                // 注意 RfidManager 中门锁启动需要一定时间。状态可能是：尚未初始化/有门开着/门都关了
                await Task.Run(() =>
                {
                    while (ShelfData.OpeningDoorCount > 0)
                    {
                        if (_initialCancelled)
                            break;
                        DisplayMessage(progress, "请关闭全部柜门，以完成初始化", "yellow");
                        Thread.Sleep(1000);
                    }
                });

                if (_initialCancelled)
                    return;

                // 此时门是关闭状态。让读卡器切换到节省盘点状态
                ShelfData.RefreshReaderNameList();

                // TODO: 填充 RFID 图书标签信息
                var initial_result = await ShelfData.newVersion_InitialShelfEntities(
    (s) =>
    {
        DisplayMessage(progress, s, "green");
        // Thread.Sleep(1000);
    },
    () =>
    {
        return _initialCancelled;
    }).ConfigureAwait(false);

                if (_initialCancelled)
                    return;

                // 2019/11/29
                // 先报告一次标签数据错误
                if (initial_result.Warnings?.Count > 0)
                {
                    ErrorBox(StringUtil.MakePathList(initial_result.Warnings, "\r\n"));
                }

                DisplayMessage(progress, "尝试还书和上架 ...", "green");

                WpfClientInfo.WriteInfoLog("首次初始化尝试还书和上架开始");
                // TODO: 如何显示还书操作中的报错信息? 看了报错以后点继续?
                // result.Value
                //      -1  出错
                //      0   没有必要处理
                //      1   已经处理
                var result = TryReturn(progress, ShelfData.All);
                WpfClientInfo.WriteInfoLog("首次初始化尝试还书和上架结束");

                if (result.MessageDocument != null
                    && result.MessageDocument.ErrorCount > 0)
                {
                    string speak = "";
                    {
                        Application.Current.Dispatcher.Invoke(new Action(() =>
                        {
                            progress.BackColor = "yellow";
                            progress.MessageDocument = result.MessageDocument.BuildDocument(18, out speak);
                            if (result.MessageDocument.ErrorCount > 0)
                                progress = null;
                        }));
                    }
                    if (string.IsNullOrEmpty(speak) == false)
                        App.CurrentApp.Speak(speak);

                    _initialCancelled = true;   // 表示初始化失败
                }

                if (_initialCancelled)
                    return;

                SelectAntenna();
            }
            finally
            {
                // _firstInitial = true;   // 第一次初始化已经完成
                Application.Current.Dispatcher.Invoke(new Action(() =>
                {
                    RemoveLayer();
                }));

                if (_initialCancelled == false)
                {
                    // PageMenu.MenuPage.shelf.Visibility = Visibility.Visible;

                    if (progress != null)
                    {
                        progress.Closed -= Progress_Cancelled;
                        Application.Current.Dispatcher.Invoke(new Action(() =>
                        {
                            if (progress != null)
                                progress.Close();
                        }));
                    }

                    SetGlobalError("initial", null);
                    this.Mode = ""; // 从初始化模式转为普通模式
                }
                else
                {
                    ShelfData.FirstInitialized = false;

                    // PageMenu.MenuPage.shelf.Visibility = Visibility.Collapsed;

                    // TODO: 页面中央大字显示“书柜初始化失败”。重新进入页面时候应该自动重试初始化
                    SetGlobalError("initial", "智能书柜初始化失败。请检查读卡器和门锁参数配置，重新进行初始化 ...");
                    /*
                    ProgressWindow error = null;
                    Application.Current.Dispatcher.Invoke(new Action(() =>
                    {
                        error = new ProgressWindow();
                        error.Owner = Application.Current.MainWindow;
                        error.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                        error.Closed += Error_Closed;
                        error.Show();
                        AddLayer();
                    }));
                    DisplayError(ref error, "智能书柜初始化失败。请检查读卡器和门锁参数配置，重新进行初始化 ...");
                    */
                }
            }

            // TODO: 初始化中断后，是否允许切换到菜单和设置画面？(只是不让进入书架画面)
        }


#endif

#if NO
        // 故意选择用到的天线编号加一的天线(用 GetTagInfo() 实现)
        string SelectAntenna()
        {
            StringBuilder text = new StringBuilder();
            List<string> errors = new List<string>();
            List<AntennaList> table = ShelfData.GetAntennaTable();
            foreach (var list in table)
            {
                if (list.Antennas == null || list.Antennas.Count == 0)
                    continue;
                uint antenna = (uint)(list.Antennas[list.Antennas.Count - 1] + 1);
                text.Append($"readerName[{list.ReaderName}], antenna[{antenna}]\r\n");
                var manage_result = RfidManager.SelectAntenna(list.ReaderName, antenna);
                if (manage_result.Value == -1)
                    errors.Add($"SelectAntenna() 出错: {manage_result.ErrorInfo}");
            }
            if (errors.Count > 0)
                this.SetGlobalError("InitialShelfEntities", $"ManageReader() 出错: {StringUtil.MakePathList(errors, ";")}");

            return text.ToString();
        }
#endif

        // 故意选择用到的天线编号加一的天线(用 ListTags() 实现)
        string SelectAntenna()
        {
            StringBuilder text = new StringBuilder();
            List<string> errors = new List<string>();
            List<AntennaList> table = ShelfData.GetAntennaTable();
            foreach (var list in table)
            {
                if (list.Antennas == null || list.Antennas.Count == 0)
                    continue;
                // uint antenna = (uint)(list.Antennas[list.Antennas.Count - 1] + 1);
                int first_antenna = list.Antennas[0];
                text.Append($"readerName[{list.ReaderName}], antenna[{first_antenna}]\r\n");
                var result = RfidManager.CallListTags($"{list.ReaderName}:{first_antenna}", "");
                if (result.Value == -1)
                    errors.Add($"CallListTags() 出错: {result.ErrorInfo}");
            }
            if (errors.Count > 0)
                this.SetGlobalError("InitialShelfEntities", $"SelectAntenna() 出错: {StringUtil.MakePathList(errors, ";")}");
            return text.ToString();
        }

        private void Error_Closed(object sender, EventArgs e)
        {
            RemoveLayer();
        }

        // 初始化被中途取消
        private void Progress_Cancelled(object sender, EventArgs e)
        {
            _initialCancelled = true;
        }

        // 刷新读者信息
        // TODO: 当读者信息更替时，要检查前一个读者是否有 _adds 和 _removes 队列需要提交，先提交，再刷成后一个读者信息
        async Task RefreshPatrons()
        {
            //_lock_refreshPatrons.EnterWriteLock();
            try
            {
                var patrons = TagList.Patrons;
                if (patrons.Count == 1)
                    _patron.IsRfidSource = true;

                if (_patron.IsFingerprintSource)
                {
                    // 指纹仪来源
                    // CloseDialogs();
                }
                else
                {

                    if (patrons.Count >= 1 && ClosePasswordDialog() == true)
                    {
                        // 这次刷卡的作用是取消了上次登录
                        return;
                    }

                    // RFID 来源
                    if (patrons.Count == 1)
                    {

                        if (_patron.Fill(patrons[0].OneTag) == false)
                            return;

                        SetPatronError("rfid_multi", "");   // 2019/5/22

                        // 2019/5/29
                        // resut.Value
                        //      -1  出错
                        //      0   没有填充
                        //      1   成功填充
                        var result = await FillPatronDetail();
                        if (result.Value == 1)
                            Welcome();
                    }
                    else
                    {
                        // 拿走 RFID 读者卡时，不要清除读者信息。也就是说和指纹做法一样

                        // PatronClear(false); // 不需要 submit


                        // SetPatronError("getreaderinfo", "");

                        if (patrons.Count > 1)
                        {
                            // 读卡器上放了多张读者卡
                            SetPatronError("rfid_multi", $"读卡器上放了多张读者卡({patrons.Count})。请拿走多余的");
                        }
                        else
                        {
                            SetPatronError("rfid_multi", "");   // 2019/5/20
                        }
                    }
                }
                SetGlobalError("patron", "");
            }
            catch (Exception ex)
            {
                SetGlobalError("patron", $"RefreshPatrons() 出现异常: {ex.Message}");
            }
            finally
            {
                //_lock_refreshPatrons.ExitWriteLock();
            }
        }

        public static string HexToDecimal(string hex_string)
        {
            var bytes = Element.FromHexString(hex_string);
            return BitConverter.ToUInt32(bytes, 0).ToString();
        }

        // 填充读者信息的其他字段(第二阶段)
        // resut.Value
        //      -1  出错
        //      0   没有填充
        //      1   成功填充
        async Task<NormalResult> FillPatronDetail(bool force = false)
        {
            // 已经填充过了
            if (_patron.PatronName != null
                && force == false)
                return new NormalResult();

            // 开灯
            ShelfData.TurnLamp("~", "on");

            string pii = _patron.PII;

            // TODO: 判断 PII 是否为工作人员账户名
            if (string.IsNullOrEmpty(pii) == false
                && Operator.IsPatronBarcodeWorker(pii))
            {
                ClearBorrowedEntities();

                // 出现登录对话框，要求输入密码登录验证
                var login_result = await WorkerLogin(pii);
                if (login_result.Value == -1)
                {
                    PatronClear();
                    return login_result;
                }
                return new NormalResult { Value = 1 };
            }

            if (string.IsNullOrEmpty(pii))
            {
                if (App.CardNumberConvertMethod == "十进制")
                    pii = HexToDecimal(_patron.UID);  // 14443A 卡的 UID
                else
                    pii = _patron.UID;  // 14443A 卡的 UID
            }

            if (string.IsNullOrEmpty(pii))
            {
                ClearBorrowedEntities();
                return new NormalResult();
            }

            // TODO: 先显示等待动画

            // return.Value:
            //      -1  出错
            //      0   读者记录没有找到
            //      1   成功
            GetReaderInfoResult result = await
                Task<GetReaderInfoResult>.Run(() =>
                {
                    return GetReaderInfo(pii);
                });

            if (result.Value != 1)
            {
                ClearBorrowedEntities();

                string error = $"读者 '{pii}': {result.ErrorInfo}";
                SetPatronError("getreaderinfo", error);
                return new NormalResult
                {
                    Value = -1,
                    ErrorInfo = error
                };
            }

            SetPatronError("getreaderinfo", "");

            //if (string.IsNullOrEmpty(_patron.State) == true)
            //    OpenDoor();

            // TODO: 出现一个半透明(倒计时)提示对话框，提示可以开门了。如果书柜只有一个门，则直接打开这个门？

            if (force)
                _patron.PhotoPath = "";
            // string old_photopath = _patron.PhotoPath;
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                _patron.SetPatronXml(result.RecPath, result.ReaderXml, result.Timestamp);
                this.patronControl.SetBorrowed(result.ReaderXml);
            }));

            // 显示在借图书列表
            List<Entity> entities = new List<Entity>();
            foreach (Entity entity in this.patronControl.BorrowedEntities)
            {
                entities.Add(entity);
            }
            if (entities.Count > 0)
            {
                try
                {
                    BaseChannel<IRfid> channel = RfidManager.GetChannel();
                    try
                    {
                        await FillBookFields(channel, entities, new CancellationToken());
                    }
                    finally
                    {
                        RfidManager.ReturnChannel(channel);
                    }
                }
                catch (Exception ex)
                {
                    string error = $"填充读者信息时出现异常: {ex.Message}";
                    SetGlobalError("rfid", error);
                    return new NormalResult { Value = -1, ErrorInfo = error };
                }
            }
#if NO
            // 装载图象
            if (old_photopath != _patron.PhotoPath)
            {
                Task.Run(()=> {
                    LoadPhoto(_patron.PhotoPath);
                });
            }
#endif
            return new NormalResult { Value = 1 };
        }

        InputPasswordWindows _passwordDialog = null;

        // return:
        //      true    关闭了密码输入窗口
        //      false   其他情况
        bool ClosePasswordDialog()
        {
            bool found = false;
            if (_passwordDialog != null)
            {
                Application.Current.Dispatcher.Invoke(new Action(() =>
                {
                    _passwordDialog.Close();
                    found = true;
                }));
            }

            // 2019/12/18
            // 关闭已经打开的人脸识别视频窗口
            if (CloseRecognitionWindow() == true)
                found = true;

            return found;
        }

        async Task<NormalResult> WorkerLogin(string pii)
        {
            App.CurrentApp.SpeakSequence("请登录");
            string userName = pii.Substring(1);
            string password = "";

            bool closed = false;
            string dialog_result = "";

            ClosePasswordDialog();

            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                App.PauseBarcodeScan();
                _passwordDialog = new InputPasswordWindows();
                _passwordDialog.TitleText = $"请输入工作人员账户 {userName} 的密码并登录";
                _passwordDialog.Owner = App.CurrentApp.MainWindow;
                _passwordDialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                _passwordDialog.Closed += (s, e) =>
                {
                    RemoveLayer();
                    password = _passwordDialog.password.Password;
                    dialog_result = _passwordDialog.Result;
                    _passwordDialog = null;
                    closed = true;
                    App.ContinueBarcodeScan();
                };
                _passwordDialog.Show();
                AddLayer();
            }));

            // 等待对话框关闭
            await Task.Run(() =>
            {
                while (closed == false)
                {
                    Thread.Sleep(500);
                }
            });

            if (dialog_result != "OK")
                return new NormalResult
                {
                    Value = -1,
                    ErrorCode = "cancelled"
                };
            _patron.Barcode = pii;

            // 登录
            {
                LoginResult result = null;
                // 显示一个处理对话框
                ProcessBox("正在登录 ...",
                    (progress) =>
                    {
                        result = LibraryChannelUtil.WorkerLogin(userName, password);
                        if (result.Value != 1)
                            return result.ErrorInfo;
                        return null;
                    });

                if (result.Value != 1)
                    return new NormalResult
                    {
                        Value = -1,
                        ErrorCode = "loginFail",
                        ErrorInfo = result.ErrorInfo,
                    };

                App.CurrentApp.SetAccount(userName, password, result.LibraryCode);
                return new NormalResult();
            }
        }

        public async Task Submit(bool silently = false)
        {
            if (ShelfData.Adds.Count > 0
                || ShelfData.Removes.Count > 0
                || ShelfData.Changes.Count > 0)
            {
                SaveAllActions();
                await SubmitCheckInOut(silently ? "silence" : "");
                // await SubmitCheckInOut("silence");
            }
        }

        // parameters:
        //      submitBefore    是否自动提交前面残留的 _adds 和 _removes ?
        public void PatronClear(/*bool submitBefore*/)
        {
            /*
            // 预先提交一次
            if (submitBefore)
            {
                if (ShelfData.Adds.Count > 0
                    || ShelfData.Removes.Count > 0
                    || ShelfData.Changes.Count > 0)
                {
                    // await SubmitCheckInOut("");    // 不清除 patron
                    SaveActions();
                }
            }
            */
            // 暂时没有想好在什么时机清除 Account 信息
            //if (_patron.Barcode != null && Operator.IsPatronBarcodeWorker(_patron.Barcode))
            //    App.CurrentApp.RemoveAccount(Operator.BuildWorkerAccountName(_patron.Barcode));

            lock (_syncRoot_patron)
            {
                _patron.Clear();
            }


            if (!Application.Current.Dispatcher.CheckAccess())
                Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                PatronFixed = false;
                fixPatron.IsEnabled = false;
                clearPatron.IsEnabled = false;
            }));
            else
            {
                PatronFixed = false;
                fixPatron.IsEnabled = false;
                clearPatron.IsEnabled = false;
            }

            ClearBorrowedEntities();

            // 延迟关灯
            ShelfData.TurnLamp("~", "off,delay");
        }

        void ClearBorrowedEntities()
        {
            if (this.patronControl.BorrowedEntities.Count > 0)
            {
                if (!Application.Current.Dispatcher.CheckAccess())
                    Application.Current.Dispatcher.Invoke(new Action(() =>
                    {
                        this.patronControl.BorrowedEntities.Clear();
                    }));
                else
                    this.patronControl.BorrowedEntities.Clear();
            }
        }

        #region patron 分类报错机制

        // 错误类别 --> 错误字符串
        // 错误类别有：rfid fingerprint getreaderinfo
        ErrorTable _patronErrorTable = null;

        // 设置读者区域错误字符串
        void SetPatronError(string type, string error)
        {
            _patronErrorTable.SetError(type, error);
            // 如果有错误信息，则主动把“清除读者信息”按钮设为可用，以便读者可以随时清除错误信息
            if (_patron.Error != null)
            {
                Application.Current.Dispatcher.Invoke(new Action(() =>
                {
                    clearPatron.IsEnabled = true;
                }));
            }
        }

        #endregion

        bool _visiblityChanged = false;

        private void _patron_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "PhotoPath")
            {
                Task.Run(() =>
                {
                    try
                    {
                        this.patronControl.LoadPhoto(_patron.PhotoPath);
                    }
                    catch (Exception ex)
                    {
                        SetGlobalError("patron", ex.Message);
                    }
                });
            }

            if (e.PropertyName == "UID"
                || e.PropertyName == "Barcode")
            {
                // 如果 patronControl 本来是隐藏状态，但读卡器上放上了读者卡，这时候要把 patronControl 恢复显示
                if ((string.IsNullOrEmpty(_patron.UID) == false || string.IsNullOrEmpty(_patron.Barcode) == false)
                    && this.patronControl.Visibility != Visibility.Visible)
                    Application.Current.Dispatcher.Invoke(new Action(() =>
                    {
                        patronControl.Visibility = Visibility.Visible;
                        _visiblityChanged = true;
                    }));
                // 如果读者卡又被拿走了，则要恢复 patronControl 的隐藏状态
                else if (string.IsNullOrEmpty(_patron.UID) == true && string.IsNullOrEmpty(_patron.Barcode) == true
    && this.patronControl.Visibility == Visibility.Visible
    && _visiblityChanged)
                    Application.Current.Dispatcher.Invoke(new Action(() =>
                    {
                        patronControl.Visibility = Visibility.Collapsed;
                    }));
            }
        }

        /*
        // 开门
        NormalResult OpenDoor()
        {
            // 打开对话框，询问门号
            OpenDoorWindow progress = null;

            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                progress = new OpenDoorWindow();
                // progress.MessageText = "正在处理，请稍候 ...";
                progress.Owner = Application.Current.MainWindow;
                progress.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                progress.Closed += Progress_Closed;
                progress.Show();
                AddLayer();
            }));

            try
            {
                progress = null;

                return new NormalResult();
            }
            finally
            {
                Application.Current.Dispatcher.Invoke(new Action(() =>
                {
                    if (progress != null)
                        progress.Close();
                }));
            }
        }
        */

        private void Progress_Closed(object sender, EventArgs e)
        {
            RemoveLayer();
        }

        void AddLayer()
        {
            try
            {
                _layer.Add(_adorner);
            }
            catch
            {

            }
        }

        void RemoveLayer()
        {
            _layer.Remove(_adorner);
        }

        private void GoHome_Click(object sender, RoutedEventArgs e)
        {
            // 检查全部门是否关闭

            if (ShelfData.OpeningDoorCount > 0)
            {
                ErrorBox("请先关闭全部柜门，才能返回主菜单页面", "yellow", "button_ok");
                return;
            }

            /*
            await Task.Run(() =>
            {
                while (ShelfData.OpeningDoorCount > 0)
                {
                    if (_initialCancelled)
                        break;
                    DisplayMessage(progress, "请先关闭全部柜门，以返回菜单页面", "yellow");
                    Thread.Sleep(1000);
                }
            });
            */

            this.NavigationService.Navigate(PageMenu.MenuPage);
        }

        private void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            // OpenDoor();
        }

        /*
        Operator GetOperator()
        {
            return new Operator
            {
                PatronBarcode = _patron.Barcode,
                PatronName = _patron.PatronName
            };
        }
        */

        // 获得特定门的 Operator
        // parameters:
        //      logNullOperator 是否在错误日志里面记载未找到门的 Operator 的情况？(读者借书时候需要 log，其他时候不需要)
        static Operator GetOperator(Entity entity, bool logNullOperator)
        {
            var doors = DoorItem.FindDoors(ShelfData.Doors, entity.ReaderName, entity.Antenna);
            if (doors.Count == 0)
                return null;
            if (doors.Count > 1)
            {
                WpfClientInfo.WriteErrorLog($"读卡器名 '{entity.ReaderName}' 天线编号 {entity.Antenna} 匹配上 {doors.Count} 个门");
                throw new Exception($"读卡器名 '{entity.ReaderName}' 天线编号 {entity.Antenna} 匹配上 {doors.Count} 个门。请检查 shelf.xml 并修正配置此错误，确保只匹配一个门");
            }

            var person = doors[0].Operator;
            if (person == null)
            {
                if (logNullOperator)
                    WpfClientInfo.WriteErrorLog($"标签 '{entity.UID}' 经查找属于门 '{doors[0].Name}'，但此时门 '{doors[0].Name}' 并没有关联的 Operator 信息");
                return new Operator();
            }
            return person;
        }

        // TODO: 报错信息尝试用 FlowDocument 改造
        // 尝试进行一次还书操作
        // result.Value
        //      -1  出错
        //      0   没有必要处理
        //      1   已经处理
        SubmitResult TryReturn(ProgressWindow progress,
            IReadOnlyCollection<Entity> entities)
        {
            List<ActionInfo> actions = new List<ActionInfo>();
            foreach (var entity in entities)
            {
                actions.Add(new ActionInfo
                {
                    Entity = entity,
                    Action = "return",
                    Operator = GetOperator(entity, false)
                });
                actions.Add(new ActionInfo
                {
                    Entity = entity,
                    Action = "transfer",
                    CurrentShelfNo = ShelfData.GetShelfNo(entity),
                    Operator = GetOperator(entity, false)
                });
            }

            if (actions.Count == 0)
                return new SubmitResult();  // 没有必要处理

            var result = ShelfData.SubmitCheckInOut(
                (min, max, value, text) =>
                {
                    if (progress != null)
                    {
                        Application.Current.Dispatcher.Invoke(new Action(() =>
                        {
                            if (min == -1 && max == -1 && value == -1)
                                progress.ProgressBar.Visibility = Visibility.Collapsed;
                            else
                                progress.ProgressBar.Visibility = Visibility.Visible;

                            //if (text != null)
                            //    progress.MessageText = text;

                            if (min != -1)
                                progress.ProgressBar.Minimum = min;
                            if (max != -1)
                                progress.ProgressBar.Maximum = max;
                            if (value != -1)
                                progress.ProgressBar.Value = value;
                        }));
                    }
                },
                //"", // _patron.Barcode,
                //"", // _patron.PatronName,
                actions);

            return result;
        }

        // 将指定门的暂存的信息保存为 Action。但并不立即提交
        void SaveDoorActions(DoorItem door, bool clearOperator)
        {
            ShelfData.SaveActions((entity) =>
            {
                var results = DoorItem.FindDoors(ShelfData.Doors, entity.ReaderName, entity.Antenna);
                // TODO: 如果遇到 results.Count > 1 是否抛出异常?
                if (results.Count > 1)
                {
                    WpfClientInfo.WriteErrorLog($"读卡器名 '{entity.ReaderName}' 天线编号 {entity.Antenna} 匹配上 {results.Count} 个门");
                    throw new Exception($"读卡器名 '{entity.ReaderName}' 天线编号 {entity.Antenna} 匹配上 {results.Count} 个门。请检查 shelf.xml 并修正配置此错误，确保只匹配一个门");
                }

                if (results.IndexOf(door) != -1)
                {
                    return door.Operator;
                    // return GetOperator(entity);
                }
                return null;
            });

            // 2019/12/21
            if (clearOperator == true && door.State == "close")
                door.Operator = null; // 清掉门上的操作者名字
        }

        // 将所有暂存信息保存为 Action，但并不立即提交
        void SaveAllActions()
        {
            ShelfData.SaveActions((entity) =>
            {
                return GetOperator(entity, true);
            });
        }


        SubmitWindow _progressWindow = null;

        void OpenProgressWindow()
        {
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                if (_progressWindow != null)
                {
                    if (_progressWindow.IsVisible == false)
                        _progressWindow.Show();
                    // 2019/12/22 
                    // 每次刚开始的时候都把颜色恢复初始值，避免受到上次最后颜色的影响
                    _progressWindow.BackColor = "black";
                    return;
                }
                else
                {
                    _progressWindow = new SubmitWindow();
                    _progressWindow.MessageText = "正在处理，请稍候 ...";
                    _progressWindow.Owner = Application.Current.MainWindow;
                    _progressWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    _progressWindow.Closed += _progressWindow_Closed;
                    _progressWindow.IsVisibleChanged += _progressWindow_IsVisibleChanged;
                    // _progressWindow.Next += _progressWindow_Next;
                    App.SetSize(_progressWindow, "tall");
                    //_progressWindow.Width = Math.Min(700, this.ActualWidth);
                    //_progressWindow.Height = Math.Min(900, this.ActualHeight);
                    _progressWindow.Show();
                }
            }));
        }

        private void _progressWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_progressWindow.IsVisible == false)
                RemoveLayer();
            else
                AddLayer();
        }

        private void _progressWindow_Closed(object sender, EventArgs e)
        {
            RemoveLayer();
            _progressWindow = null;
            // _showCount = 0;
        }

        // 向服务器提交 actions 中存储的全部出纳请求
        // parameters:
        //      clearPatron 操作完成后是否自动清除右侧的读者信息
        async Task SubmitCheckInOut(
            string strStyle = "")
        {
            bool silence = false;

            if (StringUtil.IsInList("silence", strStyle))
                silence = true;
            // bool verifyDoorClosing = StringUtil.IsInList("verifyDoorClosing", strStyle);

            // 在本函数内使用。中途可能被修改
            List<ActionInfo> actions = new List<ActionInfo>(ShelfData.Actions);
            if (actions.Count == 0)
                return;  // 没有必要处理

            // 关闭以前残留的对话框
            CloseDialogs();

            {
                // 对涉及到工作人员身份进行典藏移交的 action 进行补充修正
                bool changed = false;
                ShelfData.AskLocationTransfer(actions,
                    (action) =>
                    {
                        var entity = action.Entity;
                        if (action.Action == "transfer")
                        {
                            ShelfData.Remove("all", entity);
                            ShelfData.Remove("adds", entity);
                            ShelfData.Remove("removes", entity);
                            ShelfData.Remove("changes", entity);
                            changed = true;
                        }
                    });

                if (changed)
                    ShelfData.RefreshCount();

                if (actions.Count == 0)
                    return;  // 没有必要处理
            }

            SubmitWindow progress = null;

            if (silence == false)
            {
                OpenProgressWindow();
                progress = _progressWindow;
            }

            try
            {
                var result = ShelfData.SubmitCheckInOut(
                (min, max, value, text) =>
                {
                    if (progress != null)
                    {
                        Application.Current.Dispatcher.Invoke(new Action(() =>
                        {
                            if (min == -1 && max == -1 && value == -1)
                                progress.ProgressBar.Visibility = Visibility.Collapsed;
                            else
                                progress.ProgressBar.Visibility = Visibility.Visible;

                            if (text != null)
                                progress.TitleText = text;

                            if (min != -1)
                                progress.ProgressBar.Minimum = min;
                            if (max != -1)
                                progress.ProgressBar.Maximum = max;
                            if (value != -1)
                                progress.ProgressBar.Value = value;
                        }));
                    }
                },
                actions);

                if (result.Value == -1)
                {
                    _progressWindow?.PushContent(result.ErrorInfo, "red");

                    if (result.ErrorCode == "limitTimeout")
                    {
                        WpfClientInfo.WriteErrorLog("发生一次 limitTimeout 出错");
                    }

                    // 启动自动重试
                    if (result.RetryActions != null)
                        ShelfData.AddRetryActions(result.RetryActions);
                    return;
                }

                if (progress != null && result.Value == 1 && result.MessageDocument != null)
                {
                    Application.Current.Dispatcher.Invoke(new Action(() =>
                    {
                        _progressWindow?.PushContent(result.MessageDocument);
                    }));
                }

                // 启动自动重试
                if (result.RetryActions != null)
                    ShelfData.AddRetryActions(result.RetryActions);
            }
            finally
            {
                Application.Current.Dispatcher.Invoke(new Action(() =>
                {
                    _progressWindow?.ShowContent();
                }));
            }
        }

        static string MakeList(List<string> list)
        {
            StringBuilder text = new StringBuilder();
            int i = 1;
            foreach (string s in list)
            {
                text.Append($"{i++}) {s}\r\n");
            }

            return text.ToString();
        }

        #region 延迟清除读者信息

        DelayAction _delayClearPatronTask = null;

        void CancelDelayClearTask()
        {
            if (_delayClearPatronTask != null)
            {
                _delayClearPatronTask.Cancel.Cancel();
                _delayClearPatronTask = null;
            }

            /*
            // 恢复按钮原有文字
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                this.clearPatron.Content = $"清除读者信息";
            }));
            */
        }

        void BeginDelayClearTask()
        {
            CancelDelayClearTask();
            // TODO: 开始启动延时自动清除读者信息的过程。如果中途门被打开，则延时过程被取消(也就是说读者信息不再会被自动清除)

            Application.Current?.Dispatcher?.Invoke(new Action(() =>
            {
                PatronFixed = false;
            }));
            _delayClearPatronTask = DelayAction.Start(
                20,
                () =>
                {
                    PatronClear();
                },
                (seconds) =>
                {
                    Application.Current?.Dispatcher?.Invoke(new Action(() =>
                    {
                        if (seconds > 0)
                            this.clearPatron.Content = $"({seconds.ToString()} 秒后自动) 清除读者信息";
                        else
                            this.clearPatron.Content = $"清除读者信息";
                    }));
                });
        }


        bool PatronFixed
        {
            get
            {
                return (bool)fixPatron.IsChecked;
            }
            set
            {
                fixPatron.IsChecked = value;
            }
        }

        #endregion

        #region 模拟柜门灯亮灭

        public void SimulateLamp(bool on)
        {
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                if (on)
                    this.lamp.Background = new SolidColorBrush(Colors.White);
                else
                    this.lamp.Background = new SolidColorBrush(Colors.Black);
            }));
        }

        #endregion

        #region 人脸识别功能

        bool _stopVideo = false;

        VideoWindow _videoRecognition = null;

        private async void PatronControl_InputFace(object sender, EventArgs e)
        {
            RecognitionFaceResult result = null;

            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                _videoRecognition = new VideoWindow
                {
                    TitleText = "识别人脸 ...",
                    Owner = Application.Current.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                _videoRecognition.Closed += VideoRecognition_Closed;
                _videoRecognition.Show();
            }));
            _stopVideo = false;
            var task = Task.Run(() =>
            {
                DisplayVideo(_videoRecognition);
            });
            try
            {
                result = await RecognitionFace("");
                if (result.Value == -1)
                {
                    if (result.ErrorCode != "cancelled")
                        SetGlobalError("face", result.ErrorInfo);
                    DisplayError(ref _videoRecognition, result.ErrorInfo);
                    return;
                }

                SetGlobalError("face", null);
            }
            finally
            {
                if (_videoRecognition != null)
                    Application.Current.Dispatcher.Invoke(new Action(() =>
                    {
                        _videoRecognition.Close();
                    }));
            }

            GetMessageResult message = new GetMessageResult
            {
                Value = 1,
                Message = result.Patron,
            };
            // return:
            //      false   没有成功
            //      true    成功
            SetPatronInfo(message);
            SetQuality("");

            // resut.Value
            //      -1  出错
            //      0   没有填充
            //      1   成功填充
            var fill_result = await FillPatronDetail();
            if (fill_result.Value == 1)
                Welcome();
        }

        // 开始启动延时自动清除读者信息的过程。如果中途门被打开，则延时过程被取消(也就是说读者信息不再会被自动清除)
        void Welcome()
        {
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                fixPatron.IsEnabled = true;
                clearPatron.IsEnabled = true;
            }));

            App.CurrentApp.Speak($"欢迎您，{(string.IsNullOrEmpty(_patron.PatronName) ? _patron.Barcode : _patron.PatronName)}");
            BeginDelayClearTask();

            this.doorControl.AnimateDoors();
        }

        void DisplayError(ref VideoWindow videoRegister,
        string message,
        string color = "red")
        {
            if (videoRegister == null)
                return;
            MemoryDialog(videoRegister);
            var temp = videoRegister;
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                temp.MessageText = message;
                temp.BackColor = color;
                temp.okButton.Content = "返回";
                temp = null;
            }));
            videoRegister = null;
        }


        void SetQuality(string text)
        {
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                this.Quality.Text = text;
            }));
        }

        void DisplayVideo(VideoWindow window)
        {
            while (_stopVideo == false)
            {
                var result = FaceManager.GetImage("");
                if (result.ImageData == null)
                {
                    Thread.Sleep(500);
                    continue;
                }
                MemoryStream stream = new MemoryStream(result.ImageData);
                try
                {
                    Application.Current.Dispatcher.Invoke(new Action(() =>
                    {
                        window.SetPhoto(stream);
                    }));
                    stream = null;
                }
                finally
                {
                    if (stream != null)
                        stream.Close();
                }
            }
        }

        private void VideoRecognition_Closed(object sender, EventArgs e)
        {
            FaceManager.CancelRecognitionFace();
            _stopVideo = true;
            RemoveLayer();
            _videoRecognition = null;
        }

        bool CloseRecognitionWindow()
        {
            bool closed = false;
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                if (_videoRecognition != null)
                {
                    _videoRecognition.Close();
                    closed = true;
                }
            }));

            return closed;
        }

        void EnableControls(bool enable)
        {
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                //this.borrowButton.IsEnabled = enable;
                //this.returnButton.IsEnabled = enable;
                this.goHome.IsEnabled = enable;
                this.patronControl.inputFace.IsEnabled = enable;
            }));
        }

        async Task<RecognitionFaceResult> RecognitionFace(string style)
        {
            EnableControls(false);
            try
            {
                return await Task.Run<RecognitionFaceResult>(() =>
                {
                    // 2019/9/6 增加
                    var result = FaceManager.GetState("camera");
                    if (result.Value == -1)
                        return new RecognitionFaceResult
                        {
                            Value = -1,
                            ErrorInfo = result.ErrorInfo,
                            ErrorCode = result.ErrorCode
                        };
                    return FaceManager.RecognitionFace("");
                });
            }
            finally
            {
                EnableControls(true);
            }
        }

        #endregion

        private void ClearPatron_Click(object sender, RoutedEventArgs e)
        {
            CancelDelayClearTask();

            /*
            // 如果柜门没有全部关闭，要提醒先关闭柜门
            if (ShelfData.OpeningDoorCount > 0)
            {
                ErrorBox("请先关闭全部柜门，才能清除读者信息", "yellow", "button_ok");
                return;
            }
            */

            PatronClear();
        }

        private void FixPatron_Checked(object sender, RoutedEventArgs e)
        {
            CancelDelayClearTask();
        }

        private void FixPatron_Unchecked(object sender, RoutedEventArgs e)
        {

        }

        private void CloseRF_Click(object sender, RoutedEventArgs e)
        {
            string result = SelectAntenna();
            MessageBox.Show(result);
        }
    }
}
