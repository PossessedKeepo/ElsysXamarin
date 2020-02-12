using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Xml.Linq;
using System.IO;
using ElsysSDK2;
using HWConfig;
using Android.Content.Res;

namespace DutyPoll
{
    public enum TInitState
    {
        istNone,
        istDevTree,
        istDevices,
        istInitTimeZones,
        istInitAccessLevels,
        istInitCards,
        istInitHolidays,
        istLoadAPBZones,
        istLoadAPBDoors,
        istInitDeviceAPB,
        istInitMBNetAPB,
        istInitMBNetCards,
        istInitMBNetOPS,
        istInitCUOPS,
        istChangeOPS,
        istChangeAccessLevels,
        istChangeTimeZones,
        istChangeCards,
        istChangeHolidays,
        istFinished
    }

    public enum TChangeStatus
    {
        cstNotStarted = 0,
        cstStarted = 1,
        cstStartedErr = 2,
        cstFinishedErr = 5,
        cstFinishedOK = 6
    }

    public enum TChangeDevices
    {
        cdAllDevices = 0,
        cdDevices,
        cdReaders
    }

    class TInitData
    {
        public TInitState InitState;
        public XDocument DevTree;
        public XDocument TimeZones;
        public XDocument AccessLevels;
        public XDocument Cards;
        public XDocument Holidays;
        public XDocument ChangesInfo;
        public XDocument APBConfig;
        public XDocument OPSConfig;
        public XDocument MBNetOPSConfigNew;
        public XDocument MBOPSConfigNew;
        public string ConfigPath;

        public XElement SDKDevTree;
        public XElement ChangeItemsDevices;
        public XElement ChangeItemsTimeZones;
        public XElement ChangeItemsAccessLevels;
        public XElement ChangeItemsCards;
        public XElement ChangeItemsOPS;

        public XElement ChangeItemsHolidays;
        public XElement InitDevList;

        public bool NeedClearConfig;
        public bool NeedClearChanges;

        public XElement ControlCmds;

        public int CommandID;
        public bool CommandCommitted;
        public int CommandCount;
        public int TotalCommandID;
        public int TotalCommandCount;

        public XElement CommandData;
        public int ChangeID;
        public XElement ChangeItems;
        public Dictionary<int, TChangeStatus> ChangesStatus;

        // дполнительные опции считывателей, которые должны устанавливаться ПО верхнего уровня
        public bool UseCardRetentionEvent;
        public bool UseMNOPS;

        public TInitData()
        {
            InitState = TInitState.istNone;
            NeedClearConfig = false;
            CommandID = 0;
            ChangeID = 0;
            TotalCommandID = 0;
            TotalCommandCount = 0;
            ChangesStatus = new Dictionary<int, TChangeStatus>();
            ControlCmds = new XElement("ControlCmds", "");
        }
    }

    class TPollTask
    {
        private bool Terminated;
        private bool Connection;
        private string ServerIP;
        private HttpClient HTTPClient;
        private HttpResponseMessage HTTPResponse;
        private CancellationTokenSource CancelTokenSource;
        private string RequestUri;
        private byte[] Content;
        private XElement XContent;
        private bool WriteLog;
        private bool ShowDevStates;
        private string ConfigPath;
        private FileStream LogWriter;
        private int LogFileNumber;
        private DateTime BeginInitTime;

        private int InitDeviceListCount = 80;
        private int InitDeviceChangeCount = 10; //200; todo
        private int InitUserChangeCount = 10; // todo
        private int InitAPBZoneCount = 1; // todo
        private int InitAPBDoorCount = 1; // todo
        private int InitAPBCount = 1; // todo
        private int InitMBNEtCardCount = 500; // todo
        private int InitMBNEtOPSCount = 100; // todo
        private int InitCUOPSCount = 100; // todo
        private int InitPartListCount = 100;
        private int InitPartGroupListCount = 100;
        private int InitChangeOPSCount = 1; // todo

        private ElsysEvents Events;
        private ElsysStates States;

        private TimeSpan TimeCorrection;
        private string Password;
        XDocument DevTree;
        Dictionary<int, XElement> DevList;
        string ConfigGUID;

        private uint CID;
        private uint SID;
        private uint CIDResp;
        private uint CommandID;

        private bool NeedInit;



        TInitData InitData;


        public delegate void MessageHandler(string aMessage, string aDT);
        public event MessageHandler OnMessage;

        public delegate void TerminateHandler();
        public event TerminateHandler OnTerminate;

        public delegate void OnInitProgress(int aPercent);
        public event OnInitProgress OnInit;


        public void ClearConfig()
        {
            lock (InitData)
            {
                InitData.NeedClearConfig = true;
            }
        }

        public void SendCommand(int aID, int aDevtype, int aCommand)
        {
            lock (InitData)
            {
                InitData.ControlCmds.Add(new XElement("ControlCmd",
                  new XElement("DevID", aID),
                  new XElement("DevType", aDevtype),
                  new XElement("Action", aCommand),
                  new XElement("ID", IncCommandID())));
            }
        }

        public void SendCommand(int aID, int aDevtype, int aCommand, DateTime aDateTime)
        {
            lock (InitData)
            {
                InitData.ControlCmds.Add(new XElement("ControlCmd",
                  new XElement("DevID", aID),
                  new XElement("DevType", aDevtype),
                  new XElement("Action", aCommand),
                  new XElement("DateTime", aDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")),
                  new XElement("ID", IncCommandID())));
            }
        }

        public void BreakInit()
        {
            lock (InitData)
            {
                InitData.NeedClearChanges = true;
            }
        }

        public void InitDevTree(XDocument aDevTree, XDocument aOPSConfig, bool aUseCardRetentionEvent, bool aUseMNOPS)
        {
            lock (InitData)
            {
                if (InitData.InitState == TInitState.istNone)
                {
                    ClearInitData();
                    InitData.UseCardRetentionEvent = aUseCardRetentionEvent;
                    InitData.UseMNOPS = aUseMNOPS;
                    BeginInitTime = DateTime.Now;
                    InitData.DevTree = aDevTree;
                    InitData.OPSConfig = aOPSConfig;
                    InitData.InitState = TInitState.istDevTree;
                    PrepareDevTreeItems();
                    PrepareCommandData();
                    PrepareCommandTotalCounter();
                    OnInit(0);
                }
            }
        }

        public void InitDevices(XDocument aDevTree, bool aUseCardRetentionEvent, bool aUseMNOPS)
        {
            lock (InitData)
            {
                if (InitData.InitState == TInitState.istNone)
                {
                    ClearInitData();
                    InitData.UseCardRetentionEvent = aUseCardRetentionEvent;
                    InitData.UseMNOPS = aUseMNOPS;
                    BeginInitTime = DateTime.Now;
                    InitData.InitState = TInitState.istDevices;
                    InitData.DevTree = aDevTree;
                    PrepareChangesInfo();
                    PrepareDeviceChangeItems();
                    PrepareCommandData();
                    PrepareCommandTotalCounter();
                    OnInit(0);
                }
            }
        }

        private void PrepareAPBChangesItems()
        {
            if (InitData.InitState == TInitState.istInitDeviceAPB)
                InitData.InitDevList = PrepareInitDevList();
            else
            if (InitData.InitState == TInitState.istInitMBNetAPB)
                InitData.InitDevList = PrepareInitMBNetList();
        }

        public void InitDeviceAPB(XDocument aDevTree)
        {
            lock (InitData)
            {
                if (InitData.InitState == TInitState.istNone)
                {
                    ClearInitData();
                    BeginInitTime = DateTime.Now;
                    InitData.InitState = TInitState.istInitDeviceAPB;
                    InitData.DevTree = aDevTree;
                    PrepareChangesInfo();
                    PrepareAPBChangesItems();
                    PrepareCommandTotalCounter();
                    PrepareCommandData();
                    OnInit(0);
                }
            }
        }

        public void InitMBNetAPB(XDocument aDevTree)
        {
            lock (InitData)
            {
                if (InitData.InitState == TInitState.istNone)
                {
                    ClearInitData();
                    BeginInitTime = DateTime.Now;
                    InitData.InitState = TInitState.istInitMBNetAPB;
                    InitData.DevTree = aDevTree;
                    PrepareChangesInfo();
                    PrepareAPBChangesItems();
                    PrepareCommandTotalCounter();
                    PrepareCommandData();
                    OnInit(0);
                }
            }
        }


        private void PrepareDeviceChangeItems()
        {
            InitData.ChangeItemsDevices = ElsysConfig.GetSDKDevices(InitData.DevTree);
        }

        private void PrepareDevTreeItems()
        {
            InitData.SDKDevTree = ElsysConfig.ExportForSDK(InitData.DevTree, InitData.OPSConfig, InitData.UseCardRetentionEvent, InitData.UseMNOPS);
        }

        public TPollTask(AssetManager assets)
        {
            Terminated = true;
            TimeCorrection = new TimeSpan(0, 0, 0);
            InitData = new TInitData();
            Events = new ElsysEvents(assets);
            States = new ElsysStates(assets);
            NeedInit = false;
            DevList = new Dictionary<int, XElement>();
            WriteLog = false;
            LogFileNumber = 0;
        }

        ~TPollTask()
        {
            if (LogWriter != null)
            {
                LogWriter.Close();
                LogWriter = null;
            }

            if (HTTPClient != null)
            {
                HTTPClient.Dispose();
                HTTPClient = null;
            }
            CancelTokenSource = null;
        }

        public void SetWrightLog(bool aWriteLog)
        {
            WriteLog = aWriteLog;
            if (!WriteLog)
            {
                if (LogWriter != null)
                {
                    LogWriter.Close();
                    LogWriter = null;
                }
            }
        }

        public void SetShowDevStates(bool aShowDevStates)
        {
            ShowDevStates = aShowDevStates;
        }

        private void LoadDevList(XDocument aDevTree, XDocument aOPSConfig)
        {
            DevList.Clear();
            foreach (var item in DevTree.Root.Element("Hardware").Descendants())
            {
                if (item.Name == "ID")
                {
                    DevList.Add(int.Parse(item.Value), item.Parent);
                }
                else
                if (item.Attribute("ID") != null)
                    DevList.Add(int.Parse(item.Attribute("ID").Value), item);
            }

            foreach (var mbnet in aOPSConfig.Root.Element("MBNets").Elements("MBNet"))
            {
                if (mbnet.Element("GlobalParts") != null)
                    foreach (var item in mbnet.Element("GlobalParts").Elements("GlobalPart"))
                        DevList.Add(int.Parse(item.Element("PartID").Value), item);

                if (mbnet.Element("PartGroups") != null)
                    foreach (var item in mbnet.Element("PartGroups").Elements("PartGroup"))
                        DevList.Add(int.Parse(item.Element("GroupID").Value), item);
            }

            ConfigGUID = aDevTree.Root.Element("Hardware").Attribute("GUID").Value.Replace("-", "");
        }

        private void UpdateOPSConfig(XDocument aMNOPSConfigNew, XDocument aMBOPSConfigNew)
        {
            if (aMNOPSConfigNew != null)
            {
                try
                {
                    foreach (var group in aMNOPSConfigNew.Root.Descendants("OPSGroup").Where(e => e.Attribute("Change") != null))
                    {
                        group.Attribute("Change").Remove();
                    }
                    aMNOPSConfigNew.Save(string.Format("{0}\\{1}", ConfigPath, "OPSConfigMN.xml"));
                }
                catch (Exception)
                {
                    ////MessageBox.Show("Не удалось обновить файл конфигурации ОПС КСК!");
                }
            }

            if (aMBOPSConfigNew != null)
            {
                try
                {
                    aMBOPSConfigNew.Save(string.Format("{0}\\{1}", ConfigPath, "OPSConfigCU.xml"));
                }
                catch (Exception)
                {
                    ////MessageBox.Show("Не удалось обновить файл конфигурации ОПС КСК!");
                }
            }
        }

        private void UpdateConfigFile(string aFileName)
        {
            try
            {
                XDocument newConfig = XDocument.Load(string.Format("{0}\\{1}_New.xml", InitData.ConfigPath, aFileName));
                newConfig.Save(string.Format("{0}\\{1}.xml", ConfigPath, aFileName));
            }
            catch (Exception)
            {
                ////MessageBox.Show(string.Format("Не удалось обновить файл {0}!", aFileName));
            }
        }

        private void UpdateUserConfig()
        {
            if (InitData.ChangeItemsHolidays != null)
                UpdateConfigFile("Holidays");
            if (InitData.ChangeItemsTimeZones != null)
                UpdateConfigFile("TimeZones");
            if (InitData.ChangeItemsCards != null)
                UpdateConfigFile("SKDCards");
            if (InitData.ChangeItemsAccessLevels != null)
                UpdateConfigFile("AccessLevels");
        }

        private string GetFileForMove()
        {
            while (File.Exists(string.Format("{0}\\ProtLog_{1}.xml", ConfigPath, LogFileNumber)))
            {
                LogFileNumber++;
            }
            return string.Format("{0}\\ProtLog_{1}.xml", ConfigPath, LogFileNumber);
        }

        public void WriteToLog(XElement aXElement)
        {
            if (WriteLog)
            {
                string fileName = string.Format("{0}\\{1}", ConfigPath, "ProtLog.xml");
                if (File.Exists(fileName))
                {
                    FileInfo file = new FileInfo(fileName);
                    if (file.Length > 0x2000000)
                    {
                        if (LogWriter != null)
                        {
                            LogWriter.Close();
                            LogWriter = null;
                            File.Move(fileName, GetFileForMove());
                            File.Delete(fileName);
                        }
                    }
                }

                if (LogWriter == null)
                    LogWriter = new FileStream(fileName, FileMode.Append, FileAccess.Write);

                aXElement.Save(LogWriter);

            }
        }

        public bool Start(string aConfigPath, string aServerIP, string aPassword, XDocument aDevTree, XDocument aOPSConfig,
          bool aWriteLog, bool aShowStates)
        {
            if (aServerIP != "")
            {
                ConfigPath = aConfigPath;
                ServerIP = aServerIP;
                Password = aPassword;
                DevTree = aDevTree;
                WriteLog = aWriteLog;
                ShowDevStates = aShowStates;

                LoadDevList(aDevTree, aOPSConfig);

                HTTPClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(15000) };
                HTTPResponse = null;
                CancelTokenSource = new CancellationTokenSource();
                RequestUri = String.Format("http://{0}{1}", ServerIP, Protocol.URL);

                Connection = false;
                CID = 10000;
                SID = 0;
                CommandID = 10000;
                Terminated = false;
                OnMessage?.Invoke("Начало опроса", "");
                NextPoll();
                return true;
            }
            else
            { return false; }
        }

        private void TerminatePoll()
        {
            if (CancelTokenSource != null)
            {
                CancelTokenSource.Cancel();
                CancelTokenSource.Dispose();
            }
            if (HTTPClient != null)
            {
                HTTPClient.CancelPendingRequests();
                HTTPClient.Dispose();
            }

            OnTerminate();
        }

        private void NextPoll()
        {
            if (Terminated)
            {
                TerminatePoll();
            }
            else
            {
                PrepareRequest();
                SendRequestAsync();
            }
        }

        private uint IncCID()
        {
            if (CID < 0x80000000)
                CID++;
            else
                CID = 1;
            return CID;
        }

        private uint IncCommandID()
        {
            if (CommandID < 0x80000000)
                CommandID++;
            else
                CommandID = 1;
            return CommandID;
        }

        private void PrepareRequest()
        {
            string Nonce = Protocol.GetNonce();
            DateTime now = DateTime.Now;
            string CreationTime = (now.ToUniversalTime() + TimeCorrection).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            if (Math.Abs(TimeCorrection.TotalSeconds) > 5)
            {
                OnMessage("Синхронизация времени", "");
                XContent = Protocol.GetXContent(IncCID(), SID, now);
            }
            else
            {
                lock (InitData)
                {
                    if (InitData.NeedClearConfig)
                    {
                        XContent = Protocol.GetXContentClearConfig(IncCID(), SID);
                        InitData.NeedClearConfig = false;
                    }
                    else
                    if (InitData.NeedClearChanges)
                    {
                        XContent = Protocol.GetXContentClearChanges(IncCID(), SID);
                        InitData.NeedClearChanges = false;
                    }
                    else
                    if (InitData.ControlCmds.Elements().Count() > 0)
                    {
                        XContent = Protocol.GetXContentControlCmds(IncCID(), SID, InitData.ControlCmds);
                        InitData.ControlCmds.Elements().Remove();
                    }
                    else
                    if ((InitData.InitState != TInitState.istNone) && (InitData.InitState != TInitState.istFinished))
                    {
                        PrepareInitData(InitData.InitState);
                        XContent = Protocol.GetXContent(IncCID(), SID, InitData.CommandData);
                    }
                    else
                    {
                        XContent = Protocol.GetXContent(IncCID(), SID);
                    }
                }
            }
            Content = Encoding.UTF8.GetBytes(XContent.ToString());

            string Digest = Protocol.GetDigest(Nonce, Password, Content, CreationTime);

            HTTPClient.DefaultRequestHeaders.Clear();
            HTTPClient.DefaultRequestHeaders.Add("ECNC-Auth", String.Format("Nonce=\"{0}\", Created=\"{1}\", Digest=\"{2}\"", Nonce, CreationTime, Digest));
            HTTPClient.DefaultRequestHeaders.Date = now.ToUniversalTime();
            HTTPClient.DefaultRequestHeaders.ConnectionClose = true;
        }

        private void CheckConfigGUID(string aConfigGUID)
        {
            if ((CID == 10001) || (!NeedInit))
            {
                if ((aConfigGUID == "") || (aConfigGUID != ConfigGUID))
                {
                    if (aConfigGUID == "") OnMessage("Конфигурация SDK отсутствует!", "");

                    if ((aConfigGUID) != ConfigGUID)
                        OnMessage("Необходимо выполнить инициализацию!", "");

                    NeedInit = true;
                }
                else
                {
                    NeedInit = false;
                }
            }
        }

        private async void SendRequestAsync()
        {
            try
            {
                if (XContent != null)
                {
                    WriteToLog(new XElement("Client", new XAttribute("LocalTime", DateTime.Now.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")), XContent));
                }

                HTTPResponse = await HTTPClient.PostAsync(RequestUri, new ByteArrayContent(Content), CancelTokenSource.Token);
            }
            catch
            {
                HTTPResponse = null;
            }
            HandleResponse();
            NextPoll();
        }

        private void HandleError(string aError)
        {
            OnMessage(string.Format("Ошибка протокола обмена: {0}", aError), "");
        }

        //todo процедура только для отладки
        private void CheckInit()
        {
            if ((InitData.InitState == TInitState.istLoadAPBZones) ||
                (InitData.InitState == TInitState.istLoadAPBDoors))
            {
                XElement initResponse = new XElement("UpdAPBConfigResponse",
                  new XElement("CmdNo", InitData.TotalCommandID),
                  new XElement("Result", true));
                HandleLoadAPB(initResponse);
            }

            if ((InitData.InitState == TInitState.istInitDeviceAPB) ||
                (InitData.InitState == TInitState.istInitMBNetAPB))
            {
                XElement initResponse = new XElement("ChangesResponse",
                  new XElement("Result", true),
                  new XElement("ErrCode", 0));
                HandleChangesResponse(initResponse);
            }

            if (InitData.InitState == TInitState.istInitMBNetCards)
            {
                XElement initResponse = new XElement("ChangesResponse",
                  new XElement("Result", true),
                  new XElement("ErrCode", 0));
                HandleChangesResponse(initResponse);
            }

            if (InitData.InitState == TInitState.istFinished)
            {
                XElement changesResults = new XElement("ChangesResults",
                  new XElement("Changes", ""));
                foreach (var changeID in InitData.ChangesStatus.Keys)
                {
                    changesResults.Element("Changes").Add(new XElement("Change",
                      new XElement("ID", changeID),
                      new XElement("Status", (int)TChangeStatus.cstFinishedOK)));
                }
                HandleChangesResult(changesResults);
            }
        }

        private void HandleResponse()
        {
            bool connection = false;
            if (!CancelTokenSource.IsCancellationRequested)
                if (HTTPResponse != null)
                    if ((HTTPResponse.StatusCode == HttpStatusCode.OK) || (HTTPResponse.StatusCode == HttpStatusCode.Unauthorized))
                    {
                        connection = true;
                        if (HTTPResponse.Headers.Date.HasValue)
                            TimeCorrection = HTTPResponse.Headers.Date.Value - DateTime.Now.ToUniversalTime();

                        try
                        {
                            XDocument Content = XDocument.Parse(HTTPResponse.Content.ReadAsStringAsync().Result);
                            if (Content.Root != null)
                            {
                                WriteToLog(new XElement("MBNet", new XAttribute("LocalTime", DateTime.Now.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")), Content.Root));
                                var BodyNodes = Content.Element("Envelope").Element("Body").Elements();
                                foreach (var node in BodyNodes)
                                {
                                    if (node.Name == "CIDResp") uint.TryParse(node.Value, out CIDResp);
                                    if (node.Name == "SID") uint.TryParse(node.Value, out SID);
                                    if (node.Name == "Events") HandleEvents(node);
                                    if (node.Name == "DevStates") HandleDevStates(node);
                                    if (node.Name == "OnlineStatus") HandleOnlineStatus(node);
                                    if (node.Name == "UpdSysConfigResponse") HandleInitDevTree(node);
                                    if (node.Name == "UpdAPBConfigResponse") HandleLoadAPB(node);
                                    if (node.Name == "ChangesResults") HandleChangesResult(node);
                                    if (node.Name == "ChangesResponse") HandleChangesResponse(node);
                                    if (node.Name == "ErrCode") HandleError(node.Value);
                                    if (node.Name == "ConfigGUID") CheckConfigGUID(node.Value);
                                    if (node.Name == "ConnectedDevices") HandleConnectedDevices(node);
                                    if (node.Name == "DisconnectedDevices") HandleDisconnectedDevices(node);
                                    if (node.Name == "ConnectedMBNets") HandleConnectedMBNets(node);
                                    if (node.Name == "DisconnectedMBNets") HandleDisconnectedMBNets(node);
                                    if (node.Name == "ControlCmdsResponse") HandleControlCmdsResponse(node);
                                    if (node.Name == "NumericalHWParams") HandleNumericalHWParams(node);
                                }
                                //todo здесь нужно проверять наличие требуемого узла, чтобы завершать инициализацию при отсутствии ответов
                                //CheckInit();
                            }
                        }
                        catch
                        {
                        }
                    }

            if (Connection != connection)
            {
                Connection = connection;

                if (Connection)
                    OnMessage?.Invoke("Восстановление связи", "");
                else
                    OnMessage?.Invoke("Потеря связи", "");
            }
        }

        private void PrepareInitData(TInitState aInitState)
        {
            switch (aInitState)
            {
                case TInitState.istDevTree:
                    PrepareInitDevTree();
                    break;
                case TInitState.istDevices:
                    PrepareInitDevices();
                    break;
                case TInitState.istInitTimeZones:
                case TInitState.istInitAccessLevels:
                case TInitState.istInitCards:
                case TInitState.istInitHolidays:
                    PrepareInitUsers();
                    break;
                case TInitState.istLoadAPBZones:
                    PrepareLoadAPBZones();
                    break;
                case TInitState.istLoadAPBDoors:
                    PrepareLoadAPBDoors();
                    break;
                case TInitState.istInitDeviceAPB:
                    PrepareInitAPB("InitCU", "DeviceID", "InitCUAPB");
                    break;
                case TInitState.istInitMBNetAPB:
                    PrepareInitAPB("InitMN", "MBNetID", "InitMNAPB");
                    break;
                case TInitState.istInitMBNetCards:
                    PrepareLoadMBNetCards();
                    break;
                case TInitState.istInitMBNetOPS:
                    PrepareLoadMBNetOPS();
                    break;
                case TInitState.istInitCUOPS:
                    PrepareLoadCUOPS();
                    break;
                case TInitState.istChangeOPS:
                    PrepareLoadChangeOPS();
                    break;
                case TInitState.istChangeTimeZones:
                case TInitState.istChangeAccessLevels:
                case TInitState.istChangeCards:
                case TInitState.istChangeHolidays:
                    PrepareChangeUsers();
                    break;
                default:
                    break;
            }
        }

        private void PrepareInitDevices()
        {
            bool commandCommitted;
            lock (InitData)
            {
                commandCommitted = InitData.CommandCommitted;
            }

            if (!commandCommitted) return;

            InitData.TotalCommandID++;
            InitData.CommandID++;
            InitData.ChangeID++;
            UpdateChangesInfo();
            InitData.ChangesStatus.Add(InitData.ChangeID, TChangeStatus.cstNotStarted);
            InitData.CommandCommitted = false;


            InitData.CommandData = new XElement("Changes",
              new XElement("Change",
                new XElement("ChangeType", "InitCU"),
                new XElement("ID", InitData.ChangeID),
                new XElement("Items", "")));

            if (InitData.CommandCount == 1)
            {
                InitData.CommandData.Element("Change").Element("Items").Add(InitData.ChangeItems.Elements());
            }
            else
            {
                int itemCounter = InitDeviceChangeCount;
                XElement changeItem;
                if (InitData.ChangeItems.Elements().Count() > 0)
                    changeItem = InitData.ChangeItems.Elements().First();
                else
                    changeItem = null;

                while ((changeItem != null) && (itemCounter != 0))
                {
                    InitData.CommandData.Element("Change").Element("Items").Add(changeItem);
                    changeItem.Remove();

                    itemCounter--;
                    if (InitData.ChangeItems.Elements().Count() > 0)
                        changeItem = InitData.ChangeItems.Elements().First();
                    else
                        changeItem = null;
                }
            }

            if (InitData.CommandID == InitData.CommandCount)
            {
                InitData.InitState = NextInitStage();
                PrepareCommandData();
            }
        }

        private void PrepareInitDevTree()
        {
            InitData.TotalCommandID++;
            InitData.CommandID++;
            InitData.ChangeID++;

            // одна команда
            if (InitData.CommandCount == 1)
            {
                InitData.CommandData = new XElement("UpdSysConfig",
                  new XElement("CmdNo", InitData.CommandID),
                  new XElement("CmdCount", InitData.CommandCount),
                  InitData.SDKDevTree.Elements());
            }
            else
            {
                //первая команда
                if (InitData.CommandID == 1)
                {
                    InitData.CommandData = new XElement("UpdSysConfig",
                      new XElement("CmdNo", InitData.CommandID),
                      new XElement("CmdCount", InitData.CommandCount),
                      new XElement(InitData.SDKDevTree.Element("ConfigGUID")),
                      new XElement(InitData.SDKDevTree.Element("CommonSettings")),
                      new XElement(InitData.SDKDevTree.Element("MBNets")));

                    if (InitData.SDKDevTree.Element("Lines") != null)
                        InitData.CommandData.Add(InitData.SDKDevTree.Element("Lines"));

                }
                // следующие команды
                else
                {
                    InitData.CommandData = new XElement("UpdSysConfig",
                      new XElement("CmdNo", InitData.CommandID),
                      new XElement("CmdCount", InitData.CommandCount),
                      new XElement(InitData.SDKDevTree.Element("ConfigGUID")));

                    if (InitData.SDKDevTree.Element("Devices").Elements().Count() > 0)
                    {
                        var devices = new XElement("Devices", "");

                        int DeviceCounter = InitDeviceListCount;
                        XElement device;
                        if (InitData.SDKDevTree.Element("Devices").Elements().Count() > 0)
                            device = InitData.SDKDevTree.Element("Devices").Elements().First();
                        else
                            device = null;

                        while ((device != null) && (DeviceCounter != 0))
                        {
                            devices.Add(device);
                            device.Remove();

                            DeviceCounter--;
                            if (InitData.SDKDevTree.Element("Devices").Elements().Count() > 0)
                                device = InitData.SDKDevTree.Element("Devices").Elements().First();
                            else
                                device = null;
                        }
                        InitData.CommandData.Add(devices);
                    }
                    else

                    if (InitData.SDKDevTree.Element("MNParts").Elements().Count() > 0)
                    {
                        var parts = new XElement("MNParts", "");

                        int partCounter = InitPartListCount;
                        XElement part;
                        if (InitData.SDKDevTree.Element("MNParts").Elements().Count() > 0)
                            part = InitData.SDKDevTree.Element("MNParts").Elements().First();
                        else
                            part = null;

                        while ((part != null) && (partCounter != 0))
                        {
                            parts.Add(part);
                            part.Remove();

                            partCounter--;
                            if (InitData.SDKDevTree.Element("Devices").Elements().Count() > 0)
                                part = InitData.SDKDevTree.Element("Devices").Elements().First();
                            else
                                part = null;
                        }
                        InitData.CommandData.Add(parts);
                    }
                    else

                    if (InitData.SDKDevTree.Element("MNPartGroups").Elements().Count() > 0)
                    {
                        var groups = new XElement("MNPartGroups", "");

                        int groupCounter = InitPartListCount;
                        XElement group;
                        if (InitData.SDKDevTree.Element("MNPartGroups").Elements().Count() > 0)
                            group = InitData.SDKDevTree.Element("MNPartGroups").Elements().First();
                        else
                            group = null;

                        while ((group != null) && (groupCounter != 0))
                        {
                            groups.Add(group);
                            group.Remove();

                            groupCounter--;
                            if (InitData.SDKDevTree.Element("MNPartGroups").Elements().Count() > 0)
                                group = InitData.SDKDevTree.Element("MNPartGroups").Elements().First();
                            else
                                group = null;
                        }
                        InitData.CommandData.Add(groups);
                    }
                }
            }
        }

        private void PrepareLoadAPBZones()
        {
            InitData.TotalCommandID++;
            InitData.CommandID++;

            // одна команда
            if (InitData.CommandCount == 1)
            {
                InitData.CommandData = new XElement("UpdAPBConfig",
                  new XElement("APBConfigGUID", InitData.APBConfig.Root.Element("ConfigGUID").Value),
                  new XElement("CmdNo", InitData.TotalCommandID),
                  new XElement("CmdCount", InitData.TotalCommandCount),
                  new XElement(new XElement("APBSettings",
                    InitData.APBConfig.Root.Element("APBSettings").Element("APBZones"))));
            }
            else
            {
                InitData.CommandData = new XElement("UpdAPBConfig",
                  new XElement("APBConfigGUID", InitData.APBConfig.Root.Element("ConfigGUID").Value),
                  new XElement("CmdNo", InitData.TotalCommandID),
                  new XElement("CmdCount", InitData.TotalCommandCount),
                  new XElement(new XElement("APBSettings", "")));

                var apbzones = new XElement("APBZones", "");

                int ZonesCounter = InitAPBZoneCount;
                XElement zone;
                if (InitData.APBConfig.Root.Element("APBSettings").Element("APBZones").Elements().Count() > 0)
                    zone = InitData.APBConfig.Root.Element("APBSettings").Element("APBZones").Elements().First();
                else
                    zone = null;

                while ((zone != null) && (ZonesCounter != 0))
                {
                    var s1 = zone.Element("Name").Value;
                    var temp = Encoding.UTF8.GetBytes(s1.ToString());
                    var temp2 = Encoding.ASCII.GetBytes(s1.ToString());
                    apbzones.Add(zone);
                    zone.Remove();

                    ZonesCounter--;
                    if (InitData.APBConfig.Root.Element("APBSettings").Element("APBZones").Elements().Count() > 0)
                        zone = InitData.APBConfig.Root.Element("APBSettings").Element("APBZones").Elements().First();
                    else
                        zone = null;
                }
                InitData.CommandData.Element("APBSettings").Add(apbzones);
            }
        }

        private void PrepareLoadAPBDoors()
        {
            InitData.TotalCommandID++;
            InitData.CommandID++;

            // одна команда
            if (InitData.CommandCount == 1)
            {
                InitData.CommandData = new XElement("UpdAPBConfig",
                  new XElement("APBConfigGUID", InitData.APBConfig.Root.Element("ConfigGUID").Value),
                  new XElement("CmdNo", InitData.TotalCommandID),
                  new XElement("CmdCount", InitData.TotalCommandCount),
                  new XElement(new XElement("APBSettings",
                    InitData.APBConfig.Root.Element("APBSettings").Element("APBDoors"))));
            }
            else
            {
                InitData.CommandData = new XElement("UpdAPBConfig",
                  new XElement("APBConfigGUID", InitData.APBConfig.Root.Element("ConfigGUID").Value),
                  new XElement("CmdNo", InitData.TotalCommandID),
                  new XElement("CmdCount", InitData.TotalCommandCount),
                  new XElement(new XElement("APBSettings", "")));

                var apbdoors = new XElement("APBDoors", "");

                int DoorsCounter = InitAPBDoorCount;
                XElement door;
                if (InitData.APBConfig.Root.Element("APBSettings").Element("APBDoors").Elements().Count() > 0)
                    door = InitData.APBConfig.Root.Element("APBSettings").Element("APBDoors").Elements().First();
                else
                    door = null;

                while ((door != null) && (DoorsCounter != 0))
                {
                    apbdoors.Add(door);
                    door.Remove();

                    DoorsCounter--;
                    if (InitData.APBConfig.Root.Element("APBSettings").Element("APBDoors").Elements().Count() > 0)
                        door = InitData.APBConfig.Root.Element("APBSettings").Element("APBDoors").Elements().First();
                    else
                        door = null;
                }
                InitData.CommandData.Element("APBSettings").Add(apbdoors);
            }
        }

        private void PrepareInitAPB(string aChangeType, string aItemName, string aActionName)
        {
            bool commandCommitted;
            lock (InitData)
            {
                commandCommitted = InitData.CommandCommitted;
            }

            if (!commandCommitted) return;

            InitData.TotalCommandID++;
            InitData.CommandID++;
            InitData.ChangeID++;
            UpdateChangesInfo();
            InitData.ChangesStatus.Add(InitData.ChangeID, TChangeStatus.cstNotStarted);
            InitData.CommandCommitted = false;

            InitData.CommandData = new XElement("Changes",
              new XElement("Change",
                new XElement("ChangeType", aChangeType),
                new XElement("ID", InitData.ChangeID),
                new XElement("Items", "")));

            int itemCounter = InitAPBCount;
            XElement devItem;
            if (InitData.InitDevList.Elements().Count() > 0)
                devItem = InitData.InitDevList.Elements().First();
            else
                devItem = null;

            while ((devItem != null) && (itemCounter != 0))
            {
                var changeItem = new XElement("Item",
                    new XElement(aItemName, devItem.Value),
                    new XElement("Action", aActionName));

                InitData.CommandData.Element("Change").Element("Items").Add(changeItem);
                devItem.Remove();

                itemCounter--;
                if (InitData.InitDevList.Elements().Count() > 0)
                    devItem = InitData.InitDevList.Elements().First();
                else
                    devItem = null;
            }

        }

        private void PrepareLoadMBNetCards()
        {
            bool commandCommitted;
            lock (InitData)
            {
                commandCommitted = InitData.CommandCommitted;
            }

            if (!commandCommitted) return;

            InitData.TotalCommandID++;
            InitData.CommandID++;
            InitData.ChangeID++;
            UpdateChangesInfo();
            InitData.ChangesStatus.Add(InitData.ChangeID, TChangeStatus.cstNotStarted);
            InitData.CommandCommitted = false;

            InitData.CommandData = new XElement("Changes",
              new XElement("Change",
                new XElement("ChangeType", "InitMN"),
                new XElement("ID", InitData.ChangeID),
                new XElement("Items", "")));

            if (InitData.CommandCount == 1)
            {
                InitData.CommandData.Element("Change").Element("Items").Add(InitData.ChangeItems.Elements());
            }
            else
            {
                int itemCounter = InitMBNEtCardCount;
                XElement changeItem;
                if (InitData.ChangeItems.Elements().Count() > 0)
                    changeItem = InitData.ChangeItems.Elements().First();
                else
                    changeItem = null;

                while ((changeItem != null) && (itemCounter != 0))
                {
                    InitData.CommandData.Element("Change").Element("Items").Add(changeItem);
                    changeItem.Remove();

                    itemCounter--;
                    if (InitData.ChangeItems.Elements().Count() > 0)
                        changeItem = InitData.ChangeItems.Elements().First();
                    else
                        changeItem = null;
                }
            }
        }

        private void PrepareLoadMBNetOPS()
        {
            bool commandCommitted;
            lock (InitData)
            {
                commandCommitted = InitData.CommandCommitted;
            }

            if (!commandCommitted) return;

            InitData.TotalCommandID++;
            InitData.CommandID++;
            InitData.ChangeID++;
            UpdateChangesInfo();
            InitData.ChangesStatus.Add(InitData.ChangeID, TChangeStatus.cstNotStarted);
            InitData.CommandCommitted = false;

            InitData.CommandData = new XElement("Changes",
              new XElement("Change",
                new XElement("ChangeType", "InitMNOPS"),
                new XElement("ID", InitData.ChangeID),
                new XElement("Items", "")));

            if (InitData.CommandCount == 1)
            {
                InitData.CommandData.Element("Change").Element("Items").Add(InitData.ChangeItems.Elements());
            }
            else
            {
                int itemCounter = InitMBNEtOPSCount;
                XElement changeItem;
                if (InitData.ChangeItems.Elements().Count() > 0)
                    changeItem = InitData.ChangeItems.Elements().First();
                else
                    changeItem = null;

                while ((changeItem != null) && (itemCounter != 0))
                {
                    InitData.CommandData.Element("Change").Element("Items").Add(changeItem);
                    changeItem.Remove();

                    itemCounter--;
                    if (InitData.ChangeItems.Elements().Count() > 0)
                        changeItem = InitData.ChangeItems.Elements().First();
                    else
                        changeItem = null;
                }
            }
        }

        private void PrepareLoadChangeOPS()
        {
            bool commandCommitted;
            lock (InitData)
            {
                commandCommitted = InitData.CommandCommitted;
            }

            if (!commandCommitted) return;

            InitData.TotalCommandID++;
            InitData.CommandID++;
            InitData.ChangeID++;
            UpdateChangesInfo();
            InitData.ChangesStatus.Add(InitData.ChangeID, TChangeStatus.cstNotStarted);
            InitData.CommandCommitted = false;

            InitData.CommandData = new XElement("Changes",
              new XElement("Change",
                new XElement("ChangeType", "ChangeOPS"),
                new XElement("ID", InitData.ChangeID),
                new XElement("Items", "")));

            if (InitData.CommandCount == 1)
            {
                InitData.CommandData.Element("Change").Element("Items").Add(InitData.ChangeItems.Elements());
            }
            else
            {
                int itemCounter = InitChangeOPSCount;
                XElement changeItem;
                if (InitData.ChangeItems.Elements().Count() > 0)
                    changeItem = InitData.ChangeItems.Elements().First();
                else
                    changeItem = null;

                while ((changeItem != null) && (itemCounter != 0))
                {
                    InitData.CommandData.Element("Change").Element("Items").Add(changeItem);
                    changeItem.Remove();

                    itemCounter--;
                    if (InitData.ChangeItems.Elements().Count() > 0)
                        changeItem = InitData.ChangeItems.Elements().First();
                    else
                        changeItem = null;
                }
            }
        }

        private void PrepareLoadCUOPS()
        {
            bool commandCommitted;
            lock (InitData)
            {
                commandCommitted = InitData.CommandCommitted;
            }

            if (!commandCommitted) return;

            InitData.TotalCommandID++;
            InitData.CommandID++;
            InitData.ChangeID++;
            UpdateChangesInfo();
            InitData.ChangesStatus.Add(InitData.ChangeID, TChangeStatus.cstNotStarted);
            InitData.CommandCommitted = false;

            InitData.CommandData = new XElement("Changes",
              new XElement("Change",
                new XElement("ChangeType", "InitCU"),
                new XElement("ID", InitData.ChangeID),
                new XElement("Items", "")));

            if (InitData.CommandCount == 1)
            {
                InitData.CommandData.Element("Change").Element("Items").Add(InitData.ChangeItems.Elements());
            }
            else
            {
                int itemCounter = InitCUOPSCount;
                XElement changeItem;
                if (InitData.ChangeItems.Elements().Count() > 0)
                    changeItem = InitData.ChangeItems.Elements().First();
                else
                    changeItem = null;

                while ((changeItem != null) && (itemCounter != 0))
                {
                    InitData.CommandData.Element("Change").Element("Items").Add(changeItem);
                    changeItem.Remove();

                    itemCounter--;
                    if (InitData.ChangeItems.Elements().Count() > 0)
                        changeItem = InitData.ChangeItems.Elements().First();
                    else
                        changeItem = null;
                }
            }
        }


        private void PrepareInitUsers()
        {
            bool commandCommitted;
            lock (InitData)
            {
                commandCommitted = InitData.CommandCommitted;
            }

            if (!commandCommitted) return;

            InitData.TotalCommandID++;
            InitData.CommandID++;
            InitData.ChangeID++;
            UpdateChangesInfo();
            InitData.ChangesStatus.Add(InitData.ChangeID, TChangeStatus.cstNotStarted);
            InitData.CommandCommitted = false;

            InitData.CommandData = new XElement("Changes",
              new XElement("Change",
                new XElement("ChangeType", "InitPersDB"),
                new XElement("ID", InitData.ChangeID),
                new XElement(InitData.InitDevList),
                new XElement("Items", "")));

            if (InitData.CommandCount == 1)
            {
                InitData.CommandData.Element("Change").Element("Items").Add(InitData.ChangeItems.Elements());
            }
            else
            {
                int itemCounter = InitUserChangeCount;
                XElement changeItem;
                if (InitData.ChangeItems.Elements().Count() > 0)
                    changeItem = InitData.ChangeItems.Elements().First();
                else
                    changeItem = null;

                while ((changeItem != null) && (itemCounter != 0))
                {
                    InitData.CommandData.Element("Change").Element("Items").Add(changeItem);
                    changeItem.Remove();

                    itemCounter--;
                    if (InitData.ChangeItems.Elements().Count() > 0)
                        changeItem = InitData.ChangeItems.Elements().First();
                    else
                        changeItem = null;
                }
            }
        }

        private void PrepareChangeUsers()
        {
            bool commandCommitted;
            lock (InitData)
            {
                commandCommitted = InitData.CommandCommitted;
            }

            if (!commandCommitted) return;

            InitData.TotalCommandID++;
            InitData.CommandID++;
            InitData.ChangeID++;
            UpdateChangesInfo();
            InitData.ChangesStatus.Add(InitData.ChangeID, TChangeStatus.cstNotStarted);
            InitData.CommandCommitted = false;

            InitData.CommandData = new XElement("Changes",
              new XElement("Change",
                new XElement("ChangeType", "ChangePersDB"),
                new XElement("ID", InitData.ChangeID),
                new XElement("Items", "")));

            if (InitData.CommandCount == 1)
            {
                InitData.CommandData.Element("Change").Element("Items").Add(InitData.ChangeItems.Elements());
            }
            else
            {
                int itemCounter = InitUserChangeCount;
                XElement changeItem;
                if (InitData.ChangeItems.Elements().Count() > 0)
                    changeItem = InitData.ChangeItems.Elements().First();
                else
                    changeItem = null;

                while ((changeItem != null) && (itemCounter != 0))
                {
                    InitData.CommandData.Element("Change").Element("Items").Add(changeItem);
                    changeItem.Remove();

                    itemCounter--;
                    if (InitData.ChangeItems.Elements().Count() > 0)
                        changeItem = InitData.ChangeItems.Elements().First();
                    else
                        changeItem = null;
                }
            }
        }

        private void HandleInitDevTree(XElement aInitResponce)
        {
            lock (InitData)
            {
                if ((int.Parse(aInitResponce.Element("CmdNo").Value) != InitData.CommandID) ||
                    (bool.Parse(aInitResponce.Element("Result").Value) == false))
                {
                    OnMessage("Инициализация завершена с ошибкой", "");
                    TimeSpan timeSpan = DateTime.Now - BeginInitTime;
                    OnMessage(string.Format("Время инициализации: {0}", timeSpan.ToString()), "");

                    InitData.InitState = TInitState.istNone;
                    OnInit(100);
                }
                else
                if (InitData.CommandID < InitData.CommandCount)
                {
                    OnInit((InitData.CommandID * 100) / InitData.CommandCount);
                }
                else
                {
                    OnMessage("Успешное завершение инициализации", "");
                    TimeSpan timeSpan = DateTime.Now - BeginInitTime;
                    OnMessage(string.Format("Время инициализации: {0}", timeSpan.ToString()), "");
                    InitData.InitState = TInitState.istNone;
                    LoadDevList(InitData.DevTree, InitData.OPSConfig);
                    OnInit(100);
                    NeedInit = false;
                }
            }
        }

        private void HandleLoadAPB(XElement aInitResponce)
        {
            lock (InitData)
            {
                if ((int.Parse(aInitResponce.Element("CmdNo").Value) != InitData.TotalCommandID) ||
                    (bool.Parse(aInitResponce.Element("Result").Value) == false))
                {
                    OnMessage("Инициализация завершена с ошибкой", "");
                    TimeSpan timeSpan = DateTime.Now - BeginInitTime;
                    OnMessage(string.Format("Время инициализации: {0}", timeSpan.ToString()), "");

                    InitData.InitState = TInitState.istNone;
                    OnInit(100);
                }
                else
              if (InitData.TotalCommandID < InitData.TotalCommandCount)
                {
                    OnInit((InitData.TotalCommandID * 100) / InitData.TotalCommandCount);

                    lock (InitData)
                    {
                        if (InitData.CommandID == InitData.CommandCount)
                        {
                            InitData.InitState = NextInitStage();
                            PrepareCommandData();
                        }
                    }
                }
                else
                {
                    OnMessage("Успешное завершение инициализации", "");
                    TimeSpan timeSpan = DateTime.Now - BeginInitTime;
                    OnMessage(string.Format("Время инициализации: {0}", timeSpan.ToString()), "");
                    InitData.InitState = TInitState.istNone;
                    OnInit(100);
                }
            }
        }

        private void OnChangeStatus(int aChangeID, TChangeStatus aStatus)
        {
            if (InitData.ChangesStatus.ContainsKey(aChangeID))
            {
                switch (aStatus)
                {
                    case TChangeStatus.cstNotStarted:
                    case TChangeStatus.cstStarted:
                        InitData.ChangesStatus[aChangeID] = aStatus;
                        break;
                    case TChangeStatus.cstStartedErr:
                    case TChangeStatus.cstFinishedErr:
                    case TChangeStatus.cstFinishedOK:
                        InitData.ChangesStatus.Remove(aChangeID);

                        if (aStatus != TChangeStatus.cstFinishedOK)
                        {
                            lock (InitData)
                            {
                                if ((InitData.InitState == TInitState.istInitTimeZones) ||
                                    (InitData.InitState == TInitState.istInitAccessLevels) ||
                                    (InitData.InitState == TInitState.istInitCards) ||
                                    (InitData.InitState == TInitState.istInitHolidays) ||
                                    (InitData.InitState == TInitState.istChangeTimeZones) ||
                                    (InitData.InitState == TInitState.istChangeAccessLevels) ||
                                    (InitData.InitState == TInitState.istChangeCards) ||
                                    (InitData.InitState == TInitState.istChangeHolidays) ||
                                    (InitData.InitState == TInitState.istFinished)
                                    )
                                {
                                    OnMessage("Инициализация завершена с ошибкой", "");
                                    InitData.InitState = TInitState.istNone;
                                }
                            }
                        }
                        else
                        {
                            lock (InitData)
                            {
                                if (InitData.ChangesStatus.Count == 0)
                                {
                                    if (InitData.InitState == TInitState.istFinished)
                                    {
                                        OnMessage("Успешное завершение инициализации", "");
                                        TimeSpan timeSpan = DateTime.Now - BeginInitTime;
                                        OnMessage(string.Format("Время инициализации: {0}", timeSpan.ToString()), "");
                                        UpdateOPSConfig(InitData.MBNetOPSConfigNew, InitData.MBOPSConfigNew);
                                        UpdateUserConfig();
                                        OnInit(100);
                                        InitData.InitState = TInitState.istNone;

                                    }
                                }
                            }
                        }

                        break;
                    default:
                        break;
                }
            }
        }

        private void HandleChangesResult(XElement aChangesResults)
        {
            if (aChangesResults.Element("Changes") != null)
            {
                foreach (var change in aChangesResults.Element("Changes").Elements("Change"))
                {
                    OnChangeStatus(int.Parse(change.Element("ID").Value), (TChangeStatus)int.Parse(change.Element("Status").Value));
                }
            }
        }

        private void HandleConnectedDevices(XElement aChangesResults)
        {
            foreach (var item in aChangesResults.Elements())
            {
                string deviceName = GetDevNameByID(int.Parse(item.Value));
                string MessageText = Events.GetMessageText(ElsysConfig.DevTypes.dtCU, 11);
                OnMessage(string.Format("\"{0}\" {1}", deviceName, MessageText), "");
            }
        }

        private void HandleConnectedMBNets(XElement aConnectedMBNets)
        {
            foreach (var item in aConnectedMBNets.Elements())
            {
                string mbnetName = GetDevNameByID(int.Parse(item.Value));
                string MessageText = Events.GetMessageText(ElsysConfig.DevTypes.dtMBNet, 11);
                OnMessage(string.Format("\"{0}\" {1}", mbnetName, MessageText), "");
            }
        }

        private void HandleDisconnectedDevices(XElement aChangesResults)
        {
            foreach (var item in aChangesResults.Elements())
            {
                string deviceName = GetDevNameByID(int.Parse(item.Value));
                string MessageText = Events.GetMessageText(ElsysConfig.DevTypes.dtCU, 10);
                OnMessage(string.Format("\"{0}\" {1}", deviceName, MessageText), "");
            }
        }

        private void HandleDisconnectedMBNets(XElement aDisconnectedMBNets)
        {
            foreach (var item in aDisconnectedMBNets.Elements())
            {
                string mbnetName = GetDevNameByID(int.Parse(item.Value));
                string MessageText = Events.GetMessageText(ElsysConfig.DevTypes.dtMBNet, 10);
                OnMessage(string.Format("\"{0}\" {1}", mbnetName, MessageText), "");
            }
        }

        private void HandleControlCmdsResponse(XElement aControlCmdsResponse)
        {
            bool result = bool.Parse(aControlCmdsResponse.Element("Result").Value);
            int errorCode = 0;
            if (aControlCmdsResponse.Element("ErrCode") != null)
                errorCode = int.Parse(aControlCmdsResponse.Element("ErrCode").Value);
            if (result)
                OnMessage("Успешное выполнение команды.", "");
            else
                OnMessage(string.Format("Ошибка выполнения команды: {0}", errorCode), "");
        }

        private void HandleNumericalHWParams(XElement aControlCmdsResponse)
        {
            foreach (var item in aControlCmdsResponse.Elements())
            {
                int deviceID = int.Parse(item.Element("ID").Value);
                int cardCount = int.Parse(item.Element("CardCount").Value);
                int cardTmCount = int.Parse(item.Element("CardTmCount").Value);
                int tzItemCount = int.Parse(item.Element("TZItemCount").Value);
                int alItemCount = int.Parse(item.Element("ALItemCount").Value);
                int holidayCount = int.Parse(item.Element("HolidayCount").Value);
                string deviceName = GetDevNameByID(deviceID);
                OnMessage(string.Format("\"{0}\": постоянных карт - {1}, временных карт - {2}, временных блоков - {3}, элементов УД - {4}, праздников - {5}",
                  deviceName, cardCount, cardTmCount, tzItemCount, alItemCount, holidayCount), "");
            }
        }

        private void HandleChangesResponse(XElement aChangesResponse)
        {
            bool result = bool.Parse(aChangesResponse.Element("Result").Value);
            int errcode = 0;
            bool busy = false;
            if (aChangesResponse.Element("ErrCode") != null)
                errcode = int.Parse(aChangesResponse.Element("ErrCode").Value);
            if (aChangesResponse.Element("Busy") != null)
                busy = bool.Parse(aChangesResponse.Element("Busy").Value);

            if (result)
            {
                if (InitData.TotalCommandID < InitData.TotalCommandCount)
                    OnInit((InitData.TotalCommandID * 100) / InitData.TotalCommandCount);
                else
                    OnInit(100);

                lock (InitData)
                {
                    InitData.CommandCommitted = true;
                    if (InitData.CommandID == InitData.CommandCount)
                    {
                        InitData.InitState = NextInitStage();
                        PrepareCommandData();
                    }
                }
            }
            else
            {
                if (!(busy && (errcode == 0)))
                {
                    OnMessage("Инициализация завершена с ошибкой: " + errcode.ToString(), "");
                    TimeSpan timeSpan = DateTime.Now - BeginInitTime;
                    OnMessage(string.Format("Время инициализации: {0}", timeSpan.ToString()), "");
                    lock (InitData)
                    {
                        InitData.InitState = TInitState.istNone;
                    }
                    OnInit(100);
                }
            }
        }

        private string CompleteMessageText(string aMessageText, XElement aEvent)
        {
            if (aEvent.Element("CardNo") != null)
                aMessageText = aMessageText.Replace("%CardNo", aEvent.Element("CardNo").Value);
            else
            if (aEvent.Element("PinCode") != null)
                aMessageText = aMessageText.Replace("%PinCode", aEvent.Element("PinCode").Value);
            else
            {
                aMessageText = aMessageText.Replace("%CardNo", "");
                aMessageText = aMessageText.Replace("%PinCode", "");

            }
            return aMessageText;
        }

        public void HandleEvents(XElement aEvents)
        {
            foreach (var item in aEvents.Elements())
            {
                string EventDateTime = "Время отсутствует!";
                if (item.Element("DateTime") != null)
                    EventDateTime = DateTime.Parse(item.Element("DateTime").Value).ToString();
                string DevName = GetDevNameByID(int.Parse(item.Element("ID").Value));
                int devtype = int.Parse(item.Element("DevType").Value);

                string MessageText = Events.GetMessageText(devtype, int.Parse(item.Element("EventCode").Value));
                MessageText = CompleteMessageText(MessageText, item);
                if (item.Element("DevState") != null)
                {
                    int StateCode = int.Parse(item.Element("DevState").Value);
                    string DevState = States.GetStateText(devtype, StateCode);
                    if (DevState != "")
                        MessageText = string.Format("{0} (Состояние: {1})", MessageText, DevState);
                    else
                        OnMessage(string.Format("\"{0}\" Состояние не найдено: тип устройства - {1} код состояния - {2}", DevName, devtype, StateCode), EventDateTime);
                }
                OnMessage(string.Format("\"{0}\" {1}", DevName, MessageText), EventDateTime);
            }
        }

        public void HandleDevStates(XElement aDevStates)
        {
            foreach (var item in aDevStates.Elements())
            {
                int ID = int.Parse(item.Element("ID").Value);
                int StateCode = int.Parse(item.Element("DevState").Value);
                string DevName = GetDevNameByID(ID);
                int DevType = GetDevTypeByID(ID);
                string DevState = States.GetStateText(DevType, StateCode);
                if (DevState == "")
                    OnMessage(string.Format("\"{0}\" Состояние не найдено: тип устройства - {1} код состояния - {2}", DevName, DevType, StateCode), "");
                else
                {
                    if (ShowDevStates)
                        OnMessage(string.Format("\"{0}\" Состояние: {1}", DevName, DevState), "");
                }
            }
        }

        private List<bool> HexToBitList(string aHex)
        {
            List<bool> bitList = new List<bool>();

            for (int i = 0; i < aHex.Length / 2; i++)
            {
                var hex = aHex.Substring(i * 2, 2);
                byte newByte = byte.Parse(hex, System.Globalization.NumberStyles.HexNumber);
                for (byte j = 0; j < 8; j++)
                {
                    byte mask = (byte)(1 << j);
                    if ((newByte & mask) > 0)
                        bitList.Add(true);
                    else
                        bitList.Add(false);
                }
            }
            return bitList;
        }
        private XElement GetMBNetByAddr(int aMBNetAddr)
        {
            XElement mbnet = null;
            foreach (var item in DevTree.Root.Descendants("MBNet"))
            {
                if (int.Parse(item.Attribute("Address").Value) == aMBNetAddr)
                {
                    mbnet = item;
                    break;
                }
            }
            return mbnet;
        }

        private XElement GetDeviceByID(int aID)
        {
            if (DevList.ContainsKey(aID))
                return DevList[aID];
            else
                return null;
        }

        private XElement GetDeviceByAddr(int aLineID, int aDeviceAddr)
        {
            XElement device = null;
            var line = GetDeviceByID(aLineID);
            if (line != null)
            {
                foreach (var dev in line.Elements("Device"))
                {
                    if (int.Parse(dev.Element("MBDev").Element("Addr").Value) == aDeviceAddr)
                    {
                        device = dev;
                        break;
                    }
                }
            }
            return device;
        }

        public void HandleOnlineStatus(XElement aOnlineStatus)
        {
            if (!ShowDevStates)
                return;

            List<bool> existMBNets = HexToBitList(aOnlineStatus.Element("MBNets").Value);
            List<bool> onlineMBNets = HexToBitList(aOnlineStatus.Element("OnlineMBNets").Value);
            for (int i = 0; i < existMBNets.Count(); i++)
            {
                if (existMBNets[i])
                {
                    var mbnet = GetMBNetByAddr(i + 1);
                    if (onlineMBNets[i])
                        OnMessage(string.Format("\"{0}\" OnlineStatus: {1}", mbnet.Attribute("Name").Value, "Восстановление связи"), "");
                    else
                        OnMessage(string.Format("\"{0}\" OnlineStatus: {1}", mbnet.Attribute("Name").Value, "Потеря связи"), "");
                }
            }

            if (aOnlineStatus.Element("Lines") != null)
            {
                foreach (var line in aOnlineStatus.Element("Lines").Elements())
                {
                    int lineID = int.Parse(line.Element("LineID").Value);
                    List<bool> existDevices = HexToBitList(line.Element("Devices").Value);
                    List<bool> onlineDevices = HexToBitList(line.Element("OnlineDevices").Value);
                    for (int i = 0; i < existDevices.Count(); i++)
                    {
                        if (existDevices[i])
                        {
                            var dev = GetDeviceByAddr(lineID, i + 1);
                            if (onlineDevices[i])
                                OnMessage(string.Format("\"{0}\" OnlineStatus: {1}", dev.Element("MBDev").Element("Name").Value, "Восстановление связи"), "");
                            else
                                OnMessage(string.Format("\"{0}\" OnlineStatus: {1}", dev.Element("MBDev").Element("Name").Value, "Потеря связи"), "");
                        }
                    }
                }
            }
        }

        private string GetDevNameByID(int aID)
        {
            if (DevList.ContainsKey(aID))
            {
                if (DevList[aID].Attribute("Name") != null)
                    return DevList[aID].Attribute("Name").Value;
                else
                if (DevList[aID].Element("Name") != null)
                    return DevList[aID].Element("Name").Value;
                else
                    return "Неизвестное устройство " + aID.ToString();
            }
            else
                return "Неизвестное устройство " + aID.ToString();
        }

        private int GetDevTypeByID(int aID)
        {
            int devtype = 0;
            if (DevList.ContainsKey(aID))
            {
                var configNode = DevList[aID];
                devtype = ElsysConfig.GetDevTypeByConfigNode(configNode);
            }
            return devtype;
        }

        public void Stop()
        {
            lock (InitData)
            {
                InitData.InitState = TInitState.istNone;
                Terminated = true;
            }
            if (LogWriter != null)
            {
                LogWriter.Close();
                LogWriter = null;
            }
        }

        private void PrepareChangesInfo()
        {
            if (File.Exists(string.Format("{0}\\{1}", ConfigPath, "ChangesInfo.xml")))
            {
                try
                {
                    InitData.ChangesInfo = XDocument.Load(string.Format("{0}\\{1}", ConfigPath, "ChangesInfo.xml"));
                    InitData.ChangeID = int.Parse(InitData.ChangesInfo.Root.Element("ChangesIndex").Value);
                }
                catch (Exception)
                {
                    InitData.ChangesInfo = null;
                }

            }

            if (InitData.ChangesInfo == null)
            {
                InitData.ChangesInfo = new XDocument(new XElement("ChangesInfo", new XElement("ChangesIndex", InitData.ChangeID)));
                InitData.ChangesInfo.Save(string.Format("{0}\\{1}", ConfigPath, "ChangesInfo.xml"));
            }
        }

        private void UpdateChangesInfo()
        {
            InitData.ChangesInfo.Root.Element("ChangesIndex").Value = InitData.ChangeID.ToString();
            InitData.ChangesInfo.Save(string.Format("{0}\\{1}", ConfigPath, "ChangesInfo.xml"));
        }

        public void InitUsers(string aConfigPath, XDocument aDevTree, XDocument aTimeZones, XDocument aAccessLevels, XDocument aCards, XDocument aHolidays)
        {
            lock (InitData)
            {
                if (InitData.InitState == TInitState.istNone)
                {
                    ClearInitData();
                    BeginInitTime = DateTime.Now;
                    InitData.InitState = TInitState.istInitAccessLevels;
                    InitData.DevTree = aDevTree;
                    InitData.TimeZones = aTimeZones;
                    InitData.AccessLevels = aAccessLevels;
                    InitData.Cards = aCards;
                    InitData.Holidays = aHolidays;
                    PrepareChangesInfo();
                    PrepareUserChangeItems();
                    PrepareCommandTotalCounter();
                    PrepareCommandData();
                    OnInit(0);
                }
            }
        }

        public void InitMBNetCards(string aConfigPath, XDocument aDevTree, XDocument aCards)
        {
            lock (InitData)
            {
                if (InitData.InitState == TInitState.istNone)
                {
                    ClearInitData();
                    BeginInitTime = DateTime.Now;
                    InitData.InitState = TInitState.istInitMBNetCards;
                    InitData.DevTree = aDevTree;
                    InitData.Cards = aCards;
                    PrepareChangesInfo();
                    PrepareMBNetCardChangeItems();
                    PrepareCommandTotalCounter();
                    PrepareCommandData();
                    OnInit(0);
                }
            }
        }

        public void InitMBNetOPS(string aConfigPath, XDocument aMBNetOPSConfig)
        {
            lock (InitData)
            {
                if (InitData.InitState == TInitState.istNone)
                {
                    ClearInitData();
                    BeginInitTime = DateTime.Now;
                    InitData.InitState = TInitState.istInitMBNetOPS;
                    InitData.OPSConfig = aMBNetOPSConfig;
                    PrepareChangesInfo();
                    PrepareMBNetOPSChangeItems();
                    PrepareCommandTotalCounter();
                    PrepareCommandData();
                    OnInit(0);
                }
            }
        }

        public void InitCUOPS(string aConfigPath, XDocument aCUOPSConfig)
        {
            lock (InitData)
            {
                if (InitData.InitState == TInitState.istNone)
                {
                    ClearInitData();
                    BeginInitTime = DateTime.Now;
                    InitData.InitState = TInitState.istInitCUOPS;
                    InitData.OPSConfig = aCUOPSConfig;
                    PrepareChangesInfo();
                    PrepareCUOPSChangeItems();
                    PrepareCommandTotalCounter();
                    PrepareCommandData();
                    OnInit(0);
                }
            }
        }

        public void ChangeMBNetOPSConfig(string aConfigPath, XDocument aOPSConfigOld, XDocument aMBNetOPSConfigNew)
        {
            lock (InitData)
            {
                if (InitData.InitState == TInitState.istNone)
                {
                    ClearInitData();
                    BeginInitTime = DateTime.Now;
                    InitData.InitState = TInitState.istChangeOPS;
                    InitData.OPSConfig = aOPSConfigOld;
                    InitData.MBNetOPSConfigNew = aMBNetOPSConfigNew;
                    if (PrepareMBNetChangeOPSItems())
                    {
                        PrepareChangesInfo();
                        PrepareCommandTotalCounter();
                        PrepareCommandData();
                        OnInit(0);
                    }
                    else
                    {
                        InitData.InitState = TInitState.istNone;
                    }
                }
            }
        }

        public void ChangeMBOPSConfig(string aConfigPath, XDocument aOPSConfigOld, XDocument aMBOPSConfigNew)
        {
            lock (InitData)
            {
                if (InitData.InitState == TInitState.istNone)
                {
                    ClearInitData();
                    BeginInitTime = DateTime.Now;
                    InitData.InitState = TInitState.istChangeOPS;
                    InitData.OPSConfig = aOPSConfigOld;
                    InitData.MBOPSConfigNew = aMBOPSConfigNew;
                    if (PrepareMBChangeOPSItems())
                    {
                        PrepareChangesInfo();
                        PrepareCommandTotalCounter();
                        PrepareCommandData();
                        OnInit(0);
                    }
                    else
                    {
                        InitData.InitState = TInitState.istNone;
                    }
                }
            }
        }

        private bool HolidaysIsChanged()
        {
            bool retval = false;
            try
            {
                XDocument oldConfig = XDocument.Load(string.Format("{0}\\{1}", InitData.ConfigPath, "Holidays.xml"));
                XDocument newConfig = XDocument.Load(string.Format("{0}\\{1}", InitData.ConfigPath, "Holidays_New.xml"));

                if (oldConfig.Root.Descendants("Holiday").Count() == newConfig.Root.Descendants("Holiday").Count())
                {
                    foreach (var oldItem in oldConfig.Root.Descendants("Holiday"))
                    {
                        XElement newItem = newConfig.Root.Descendants("Holiday").Where(e => (e.Attribute("Date").Value == oldItem.Attribute("Date").Value) &&
                          e.Attribute("Type").Value == oldItem.Attribute("Type").Value).FirstOrDefault();
                        if (newItem == null)
                        {
                            retval = true;
                            break;
                        }
                    }
                }
                else
                    retval = true;
            }
            catch (Exception)
            {
                //MessageBox.Show("Не удалось получить изменения праздников.");
            }
            return retval;
        }

        private bool CheckEqual(XElement aItem1, XElement aItem2)
        {
            bool retval = true;
            if (aItem1.Attributes().Count() == aItem2.Attributes().Count())
            {
                foreach (var attr1 in aItem1.Attributes())
                {
                    var attr2 = aItem2.Attributes().Where(a => a.Name == attr1.Name).FirstOrDefault();
                    if (attr2 != null)
                    {
                        if (attr1.Value != attr2.Value)
                            retval = false;
                    }
                    else
                        retval = false;

                    if (!retval)
                        break;
                }
            }
            else retval = false;

            return retval;
        }

        private bool CardsIsChanged(XElement aNewCards, XElement aUpdateCards, XElement aDeleteCards)
        {
            bool retval = false;
            try
            {
                XDocument oldConfig = XDocument.Load(string.Format("{0}\\{1}", InitData.ConfigPath, "SKDCards.xml"));
                XDocument newConfig = XDocument.Load(string.Format("{0}\\{1}", InitData.ConfigPath, "SKDCards_New.xml"));
                foreach (var newItem in newConfig.Root.Descendants("Card"))
                {
                    XElement oldItem = oldConfig.Root.Descendants("Card").Where(e => (e.Attribute("CardNo").Value == newItem.Attribute("CardNo").Value)).FirstOrDefault();
                    if (oldItem == null)
                        aNewCards.Add(newItem);
                    else
                    {
                        if (!CheckEqual(oldItem, newItem))
                            aUpdateCards.Add(newItem);
                    }
                }

                foreach (var oldItem in oldConfig.Root.Descendants("Card"))
                {
                    XElement newItem = newConfig.Root.Descendants("Card").Where(e => (e.Attribute("CardNo").Value == oldItem.Attribute("CardNo").Value)).FirstOrDefault();
                    if (newItem == null)
                        aDeleteCards.Add(oldItem);
                }

                retval = (aNewCards.Elements().Count() > 0) || (aUpdateCards.Elements().Count() > 0) || (aDeleteCards.Elements().Count() > 0);

            }
            catch (Exception)
            {
                //MessageBox.Show("Не удалось получить изменения карт.");
            }

            return retval;
        }

        private bool TimeZonesIsChanged(XElement aNewTimeZones, XElement aUpdateTimeZones, XElement aDeleteTimeZones)
        {
            bool retval = false;
            try
            {
                XDocument oldConfig = XDocument.Load(string.Format("{0}\\{1}", InitData.ConfigPath, "TimeZones.xml"));
                XDocument newConfig = XDocument.Load(string.Format("{0}\\{1}", InitData.ConfigPath, "TimeZones_New.xml"));
                foreach (var newZone in newConfig.Root.Elements("TimeZone"))
                {
                    XElement oldZone = oldConfig.Root.Elements("TimeZone").Where(e => (e.Attribute("ID").Value == newZone.Attribute("ID").Value)).FirstOrDefault();
                    if (oldZone == null)
                        aNewTimeZones.Add(newZone);
                    else
                    {
                        if (newZone.Elements().Count() == oldZone.Elements().Count())
                        {
                            for (int i = 0; i < newZone.Elements().Count(); i++)
                            {
                                if (!CheckEqual(oldZone.Elements().ElementAt(i), newZone.Elements().ElementAt(i)))
                                {
                                    aUpdateTimeZones.Add(newZone);
                                    break;
                                }
                            }
                        }
                        else
                            aUpdateTimeZones.Add(newZone);
                    }
                }

                foreach (var oldZone in oldConfig.Root.Elements("TimeZone"))
                {
                    XElement newZone = newConfig.Root.Elements("TimeZone").Where(e => (e.Attribute("ID").Value == oldZone.Attribute("ID").Value)).FirstOrDefault();
                    if (newZone == null)
                        aDeleteTimeZones.Add(oldZone);
                }

                retval = (aNewTimeZones.Elements().Count() > 0) || (aUpdateTimeZones.Elements().Count() > 0) || (aDeleteTimeZones.Elements().Count() > 0);

            }
            catch (Exception)
            {
                //MessageBox.Show("Не удалось получить изменения временных зон.");
            }

            return retval;
        }

        private void GetUpdateCards(XElement aUpdateCards, XElement aAccessLevelIDs)
        {
            try
            {
                XDocument newConfig = XDocument.Load(string.Format("{0}\\{1}", InitData.ConfigPath, "SKDCards_New.xml"));
                foreach (var id in aAccessLevelIDs.Elements())
                {
                    aUpdateCards.Add(newConfig.Root.Elements("Card").Where(e => e.Attribute("ALNo").Value == id.Value).ToList());
                }
            }
            catch (Exception)
            {
            }
        }

        private void GetUpdateTimeBlocks(XElement aUpdateTimeBlocks, XElement aTimeBlockIDs)
        {
            try
            {
                XDocument newConfig = XDocument.Load(string.Format("{0}\\{1}", InitData.ConfigPath, "TimeZones_New.xml"));
                foreach (var id in aTimeBlockIDs.Elements())
                {
                    aUpdateTimeBlocks.Add(newConfig.Root.Elements("TimeZone").Where(e => e.Attribute("ID").Value == id.Value).ToList());
                }
            }
            catch (Exception)
            {
            }
        }

        private bool AccessLevelsIsChanged(XElement aNewAccessLevels, XElement aUpdateAccessLevels, XElement aDeleteAccessLevels,
                XElement updateCards, XElement updateTimeBlocks)
        {
            bool retval = false;
            try
            {
                XDocument oldConfig = XDocument.Load(string.Format("{0}\\{1}", InitData.ConfigPath, "AccessLevels.xml"));
                XDocument newConfig = XDocument.Load(string.Format("{0}\\{1}", InitData.ConfigPath, "AccessLevels_New.xml"));
                var accessLevelIDs = new XElement("AccessLevelIDs");
                var updatedTimeBlockIDs = new XElement("TimeBlockIDs");

                foreach (var newAL in newConfig.Root.Elements("AccessLevel"))
                {
                    XElement oldAL = oldConfig.Root.Elements("AccessLevel").Where(e => (e.Attribute("ID").Value == newAL.Attribute("ID").Value)).FirstOrDefault();
                    if (oldAL == null)
                    {
                        aNewAccessLevels.Add(newAL);
                        // запоминаем ID новых непустых уровней доступа
                        if (newAL.Elements().Count() > 0)
                        {
                            accessLevelIDs.Add(new XElement("ID", newAL.Attribute("ID").Value));
                        }
                    }
                    else
                    {
                        if (newAL.Elements().Count() == oldAL.Elements().Count())
                        {
                            for (int i = 0; i < newAL.Elements().Count(); i++)
                            {
                                if (!CheckEqual(oldAL.Elements().ElementAt(i), newAL.Elements().ElementAt(i)))
                                {
                                    aUpdateAccessLevels.Add(newAL);
                                    break;
                                }
                            }
                        }
                        else
                        {
                            aUpdateAccessLevels.Add(newAL);
                            // запоминаем ID уровней доступа, в которых появился первый элемент  
                            if (oldAL.Elements().Count() == 0)
                                accessLevelIDs.Add(new XElement("ID", oldAL.Attribute("ID").Value));
                        }

                        // запоминаем ID временного блока при добавлении элемента УД
                        foreach (var item in newAL.Elements())
                        {
                            var al = oldAL.Elements().Where(e => e.Attribute("ReaderID").Value == item.Attribute("ReaderID").Value).FirstOrDefault();
                            if (al == null)
                            {
                                if (updatedTimeBlockIDs.Elements().Where(e => e.Value == item.Attribute("TimeZoneID").Value).FirstOrDefault() == null)
                                    updatedTimeBlockIDs.Add(new XElement("TimeZoneID", item.Attribute("TimeZoneID").Value));
                            }
                        }
                    }
                }

                foreach (var oldAL in oldConfig.Root.Elements("AccessLevel"))
                {
                    XElement newAL = newConfig.Root.Elements("AccessLevel").Where(e => (e.Attribute("ID").Value == oldAL.Attribute("ID").Value)).FirstOrDefault();
                    if (newAL == null)
                        aDeleteAccessLevels.Add(oldAL);
                }

                //Пустые УД переносим в список для удаления
                foreach (var item in aUpdateAccessLevels.Elements())
                {
                    if (item.Elements().Count() == 0)
                        aDeleteAccessLevels.Add(item);
                }

                //Удаляем пустые УД из списков обновления
                aUpdateAccessLevels.Elements().Where(e => e.Elements().Count() == 0).Remove();
                aNewAccessLevels.Elements().Where(e => e.Elements().Count() == 0).Remove();

                retval = (aNewAccessLevels.Elements().Count() > 0) || (aUpdateAccessLevels.Elements().Count() > 0) || (aDeleteAccessLevels.Elements().Count() > 0);
                if (retval)
                {
                    //Формируем списки обновления карт и временных блоков, связанных с изменениями уровней доступа
                    GetUpdateCards(updateCards, accessLevelIDs);
                    GetUpdateTimeBlocks(updateTimeBlocks, updatedTimeBlockIDs);
                }
            }
            catch (Exception)
            {
                //MessageBox.Show("Не удалось получить изменения временных зон.");
            }

            return retval;
        }

        private XElement PrepareChangeDevList(TChangeDevices aChangeDevices, List<int> aIDs)
        {
            XElement changeDevices;

            switch (aChangeDevices)
            {
                case TChangeDevices.cdAllDevices:
                    changeDevices = new XElement("ForAllDevices", "true");
                    break;
                case TChangeDevices.cdDevices:
                    changeDevices = new XElement("Devices", "");
                    break;
                case TChangeDevices.cdReaders:
                    changeDevices = new XElement("Readers", "");
                    break;
                default:
                    changeDevices = new XElement("ForAllDevices", "true");
                    break;
            }

            if (aChangeDevices != TChangeDevices.cdAllDevices)
                foreach (var id in aIDs)
                    changeDevices.Add(new XElement("ID", id));

            return changeDevices;
        }

        private bool PrepareHolidayChangeItems(TChangeDevices aChangeDevices, List<int> aIDs)
        {
            bool holidaysIsChanged = HolidaysIsChanged();

            if (holidaysIsChanged)
            {
                InitData.InitDevList = PrepareChangeDevList(aChangeDevices, aIDs);
                XDocument holidaysNew = XDocument.Load(string.Format("{0}\\{1}", InitData.ConfigPath, "Holidays_New.xml"));

                InitData.ChangeItemsHolidays = new XElement("Items",
                  new XElement("Item",
                    new XElement("Action", "UpdHolidays"),
                    InitData.InitDevList,
                    new XElement("Items", "")));


                foreach (var holiday in holidaysNew.Root.Elements("Holiday"))
                {
                    InitData.ChangeItemsHolidays.Element("Item").Element("Items").Add(
                      new XElement("Item",
                          new XElement("Date", holiday.Attribute("Date").Value),
                          new XElement("Type", holiday.Attribute("Type").Value)));
                }
            }
            return holidaysIsChanged;
        }

        private void ClearInitData()
        {
            InitData.CommandCommitted = true;
            InitData.DevTree = null;
            InitData.ConfigPath = "";
            InitData.OPSConfig = null;
            InitData.MBOPSConfigNew = null;
            InitData.MBNetOPSConfigNew = null;
            InitData.ChangesStatus.Clear();
            InitData.ChangeItemsHolidays = null;
            InitData.ChangeItemsTimeZones = null;
            InitData.ChangeItemsCards = null;
            InitData.ChangeItemsAccessLevels = null;
        }

        public void ChangeHolidays(XDocument aDevTree, string aUserConfigPath, TChangeDevices aChangeDevices, List<int> aIDs)
        {

            lock (InitData)
            {
                if (InitData.InitState == TInitState.istNone)
                {
                    ClearInitData();
                    BeginInitTime = DateTime.Now;
                    InitData.InitState = TInitState.istChangeHolidays;
                    InitData.DevTree = aDevTree;
                    InitData.ConfigPath = aUserConfigPath;
                    if (PrepareHolidayChangeItems(aChangeDevices, aIDs))
                    {
                        PrepareChangesInfo();
                        PrepareCommandTotalCounter();
                        PrepareCommandData();
                        OnInit(0);
                    }
                    else
                    {
                        InitData.InitState = TInitState.istNone;
                        //MessageBox.Show("Изменения в праздниках отсутствуют.");
                    }
                }
            }
        }

        private void AddTimeZonesForUpdate(XElement aInitDevList, XElement aTimeZones)
        {
            bool NoCheckUnique = InitData.ChangeItemsTimeZones == null;

            foreach (var timeZone in aTimeZones.Elements())
            {
                var changeItem = new XElement("Item",
                  aInitDevList,
                  new XElement("Action", "UpdTB"),
                  new XElement("TmBlockID", timeZone.Attribute("ID").Value),
                  new XElement("TmBlockNo", timeZone.Attribute("ID").Value),
                  new XElement("Intervals", ""));

                foreach (var timeInterval in timeZone.Elements("TimeInterval"))
                {
                    changeItem.Element("Intervals").Add(
                      new XElement("TimeInterval",
                        new XElement("StartTime", timeInterval.Attribute("StartTime").Value),
                        new XElement("EndTime", timeInterval.Attribute("EndTime").Value),
                        new XElement("ActiveDays", timeInterval.Attribute("ActiveDays").Value),
                        new XElement("ActiveHolidays", timeInterval.Attribute("ActiveHolidays").Value),
                        new XElement("Period", timeInterval.Attribute("Period").Value),
                        new XElement("StartDate", timeInterval.Attribute("StartDate").Value),
                        new XElement("EndDate", timeInterval.Attribute("EndDate").Value),
                        new XElement("NoUsingHolidays", timeInterval.Attribute("NoUsingHolidays").Value)
                        ));
                }

                if (InitData.ChangeItemsTimeZones == null)
                    InitData.ChangeItemsTimeZones = new XElement("Items", "");

                if (NoCheckUnique || (changeItem.Element("Action").Value != "UpdTB"))
                    InitData.ChangeItemsTimeZones.Add(changeItem);
                else
                {
                    if (InitData.ChangeItemsTimeZones.Elements().Where(e => (e.Element("Action").Value == changeItem.Element("Action").Value) &&
                      (e.Element("TmBlockID").Value == changeItem.Element("TmBlockID").Value)).FirstOrDefault() == null)
                        InitData.ChangeItemsTimeZones.Add(changeItem);

                }
            }
        }

        private bool PrepareTimeZonesChangeItems(TChangeDevices aChangeDevices, List<int> aIDs)
        {
            var newTimeZones = new XElement("TimeZones");
            var updateTimeZones = new XElement("TimeZones");
            var deleteTimeZones = new XElement("TimeZones");

            bool timeZonesIsChanged = TimeZonesIsChanged(newTimeZones, updateTimeZones, deleteTimeZones);
            if (timeZonesIsChanged)
            {
                InitData.InitDevList = PrepareChangeDevList(aChangeDevices, aIDs);
                InitData.ChangeItemsTimeZones = new XElement("Items", "");

                foreach (var timeZone in deleteTimeZones.Elements())
                {
                    var changeItem = new XElement("Item",
                      new XElement("Action", "DelTB"),
                      InitData.InitDevList,
                      new XElement("TmBlockNo", timeZone.Attribute("ID").Value),
                      new XElement("TmBlockID", timeZone.Attribute("ID").Value));
                    InitData.ChangeItemsTimeZones.Add(changeItem);
                }

                AddTimeZonesForUpdate(InitData.InitDevList, updateTimeZones);
                AddTimeZonesForUpdate(InitData.InitDevList, newTimeZones);
            }

            return timeZonesIsChanged;
        }

        public void ChangeTimeZones(XDocument aDevTree, string aUserConfigPath, TChangeDevices aChangeDevices, List<int> aIDs)
        {

            lock (InitData)
            {
                if (InitData.InitState == TInitState.istNone)
                {
                    ClearInitData();
                    BeginInitTime = DateTime.Now;
                    InitData.InitState = TInitState.istChangeTimeZones;
                    InitData.DevTree = aDevTree;
                    InitData.ConfigPath = aUserConfigPath;
                    if (PrepareTimeZonesChangeItems(aChangeDevices, aIDs))
                    {
                        PrepareChangesInfo();
                        PrepareCommandTotalCounter();
                        PrepareCommandData();
                        OnInit(0);
                    }
                    else
                    {
                        InitData.InitState = TInitState.istNone;
                        //MessageBox.Show("Изменения временных зон отсутствуют.");
                    }
                }
            }
        }

        private bool PrepareALChangeItems(TChangeDevices aChangeDevices, List<int> aIDs)
        {
            var newAccessLevels = new XElement("AccessLevels");
            var updateAccessLevels = new XElement("AccessLevels");
            var deleteAccessLevels = new XElement("AccessLevels");
            var updateCards = new XElement("Cards");
            var updateTimeBlocks = new XElement("TimeBlocks");

            bool ALIsChanged = AccessLevelsIsChanged(newAccessLevels, updateAccessLevels, deleteAccessLevels, updateCards, updateTimeBlocks);
            if (ALIsChanged)
            {
                InitData.InitDevList = PrepareChangeDevList(aChangeDevices, aIDs);
                InitData.ChangeItemsAccessLevels = new XElement("Items", "");

                foreach (var accessLevel in deleteAccessLevels.Elements())
                {
                    var changeItem = new XElement("Item",
                      new XElement("Action", "DelAL"),
                      InitData.InitDevList,
                      new XElement("ALNo", accessLevel.Attribute("ID").Value),
                      new XElement("ALID", accessLevel.Attribute("ID").Value)
                      );
                    InitData.ChangeItemsAccessLevels.Add(changeItem);
                }

                foreach (var accessLevel in updateAccessLevels.Elements())
                {
                    var changeItem = new XElement("Item",
                      new XElement("Action", "UpdAL"),
                      new XElement("ForAllDevices", (aChangeDevices == TChangeDevices.cdAllDevices).ToString().ToLower()),
                      new XElement("ALItems", ""));

                    foreach (var accessLevelItem in accessLevel.Elements("AccessLevelItem"))
                    {
                        changeItem.Element("ALItems").Add(
                          new XElement("ALItem",
                            new XElement("TmBlockNo", accessLevelItem.Attribute("TimeZoneID").Value),
                            new XElement("TmBlockID", accessLevelItem.Attribute("TimeZoneID").Value),
                            new XElement("RdrID", accessLevelItem.Attribute("ReaderID").Value)));
                    }

                    changeItem.Add(new XElement("ALNo", accessLevel.Attribute("ID").Value),
                      new XElement("ALID", accessLevel.Attribute("ID").Value));


                    InitData.ChangeItemsAccessLevels.Add(changeItem);
                }

                foreach (var accessLevel in newAccessLevels.Elements())
                {
                    var changeItem = new XElement("Item",
                      new XElement("Action", "UpdAL"),
                      new XElement("ForAllDevices", (aChangeDevices == TChangeDevices.cdAllDevices).ToString().ToLower()),
                      new XElement("ALItems", ""));

                    foreach (var accessLevelItem in accessLevel.Elements("AccessLevelItem"))
                    {
                        changeItem.Element("ALItems").Add(
                          new XElement("ALItem",
                            new XElement("TmBlockNo", accessLevelItem.Attribute("TimeZoneID").Value),
                            new XElement("TmBlockID", accessLevelItem.Attribute("TimeZoneID").Value),
                            new XElement("RdrID", accessLevelItem.Attribute("ReaderID").Value)));
                    }

                    changeItem.Add(new XElement("ALNo", accessLevel.Attribute("ID").Value),
                      new XElement("ALID", accessLevel.Attribute("ID").Value));

                    InitData.ChangeItemsAccessLevels.Add(changeItem);
                }

                // Обновление карт и временных блоков при изменении УД
                AddCardForUpdate(InitData.InitDevList, updateCards);
                AddTimeZonesForUpdate(InitData.InitDevList, updateTimeBlocks);

            }
            return ALIsChanged;
        }

        public void ChangeALs(XDocument aDevTree, string aUserConfigPath, TChangeDevices aChangeDevices, List<int> aIDs)
        {
            lock (InitData)
            {
                if (InitData.InitState == TInitState.istNone)
                {
                    ClearInitData();
                    BeginInitTime = DateTime.Now;
                    InitData.InitState = TInitState.istChangeAccessLevels;
                    InitData.DevTree = aDevTree;
                    InitData.ConfigPath = aUserConfigPath;
                    if (PrepareALChangeItems(aChangeDevices, aIDs))
                    {
                        PrepareChangesInfo();
                        PrepareCommandTotalCounter();
                        PrepareCommandData();
                        OnInit(0);
                    }
                    else
                    {
                        InitData.InitState = TInitState.istNone;
                        //MessageBox.Show("Изменения в уровнях доступа отсутствуют.");
                    }
                }
            }
        }

        private TInitState PrepareUserConfigChangeItems(TChangeDevices aChangeDevices, List<int> aIDs)
        {
            TInitState retval = TInitState.istNone;
            if (PrepareHolidayChangeItems(aChangeDevices, aIDs))
                retval = TInitState.istChangeHolidays;
            if (PrepareCardsChangeItems(aChangeDevices, aIDs))
                retval = TInitState.istChangeCards;
            if (PrepareTimeZonesChangeItems(aChangeDevices, aIDs))
                retval = TInitState.istChangeTimeZones;
            if (PrepareALChangeItems(aChangeDevices, aIDs))
                retval = TInitState.istChangeAccessLevels;
            return retval;
        }

        public void ChangeUserConfig(XDocument aDevTree, string aUserConfigPath, TChangeDevices aChangeDevices, List<int> aIDs)
        {
            lock (InitData)
            {
                if (InitData.InitState == TInitState.istNone)
                {
                    ClearInitData();
                    InitData.DevTree = aDevTree;
                    InitData.ConfigPath = aUserConfigPath;
                    InitData.InitState = PrepareUserConfigChangeItems(aChangeDevices, aIDs);
                    if (InitData.InitState != TInitState.istNone)
                    {
                        BeginInitTime = DateTime.Now;
                        PrepareChangesInfo();
                        PrepareCommandTotalCounter();
                        PrepareCommandData();
                        OnInit(0);
                    }
                    else
                    {
                        //MessageBox.Show("Изменения в базе данных пропусков отсутствуют.");
                    }
                }
            }
        }

        private void AddCardForUpdate(XElement aInitDevList, XElement aCards)
        {
            foreach (var card in aCards.Elements())
            {
                bool NoCheckUnique = InitData.ChangeItemsCards == null;

                var changeItem = new XElement("Item",
                  new XElement("Action", "UpdCard"),
                  aInitDevList,
                  new XElement("CardNo", card.Attribute("CardNo").Value),
                  new XElement("ALNo", card.Attribute("ALNo").Value),
                  new XElement("PINCode", card.Attribute("PINCode").Value),
                  new XElement("IsTmpCard", card.Attribute("IsTmpCard").Value),
                  new XElement("NoAPB", card.Attribute("NoAPB").Value),
                  new XElement("Options", card.Attribute("Options").Value),
                  new XElement("Priv", card.Attribute("Priv").Value),
                  new XElement("CardAction", card.Attribute("CardAction").Value),
                  new XElement("StartDate", card.Attribute("StartDate").Value),
                  new XElement("EndDate", card.Attribute("EndDate").Value),
                  new XElement("DevParams", ""));

                foreach (var devParam in card.Elements("DevParam"))
                {
                    changeItem.Element("DevParams").Add(
                      new XElement("DevParamsItem",
                        new XElement("ID", devParam.Attribute("ID").Value),
                        new XElement("Options", devParam.Attribute("Options").Value),
                        new XElement("Priv", devParam.Attribute("Priv").Value),
                        new XElement("CardAction", devParam.Attribute("CardAction").Value)
                        ));
                }

                if (InitData.ChangeItemsCards == null)
                    InitData.ChangeItemsCards = new XElement("Items", "");

                if (NoCheckUnique || (changeItem.Element("Action").Value != "UpdCard"))
                    InitData.ChangeItemsCards.Add(changeItem);
                else
                {
                    if (InitData.ChangeItemsCards.Elements().Where(e => (e.Element("Action").Value == changeItem.Element("Action").Value) &&
                      (e.Element("CardNo").Value == changeItem.Element("CardNo").Value)).FirstOrDefault() == null)
                        InitData.ChangeItemsCards.Add(changeItem);
                }
            }
        }

        private bool PrepareCardsChangeItems(TChangeDevices aChangeDevices, List<int> aIDs)
        {
            var newCards = new XElement("Cards");
            var updateCards = new XElement("Cards");
            var deleteCards = new XElement("Cards");

            bool cardsIsChanged = CardsIsChanged(newCards, updateCards, deleteCards);
            if (cardsIsChanged)
            {
                InitData.InitDevList = PrepareChangeDevList(aChangeDevices, aIDs);
                InitData.ChangeItemsCards = new XElement("Items", "");

                foreach (var card in deleteCards.Elements())
                {
                    var changeItem = new XElement("Item",
                      new XElement("Action", "DelCard"),
                      InitData.InitDevList,
                      new XElement("CardNo", card.Attribute("CardNo").Value));
                    InitData.ChangeItemsCards.Add(changeItem);
                }

                AddCardForUpdate(InitData.InitDevList, updateCards);
                AddCardForUpdate(InitData.InitDevList, newCards);
            }
            return cardsIsChanged;
        }

        public void ChangeCards(XDocument aDevTree, string aUserConfigPath, TChangeDevices aChangeDevices, List<int> aIDs)
        {

            lock (InitData)
            {
                if (InitData.InitState == TInitState.istNone)
                {
                    ClearInitData();
                    BeginInitTime = DateTime.Now;
                    InitData.InitState = TInitState.istChangeCards;
                    InitData.DevTree = aDevTree;
                    InitData.ConfigPath = aUserConfigPath;
                    if (PrepareCardsChangeItems(aChangeDevices, aIDs))
                    {
                        PrepareChangesInfo();
                        PrepareCommandTotalCounter();
                        PrepareCommandData();
                        OnInit(0);
                    }
                    else
                    {
                        InitData.InitState = TInitState.istNone;
                        //MessageBox.Show("Изменения в картах доступа отсутствуют.");
                    }
                }
            }
        }

        private void PrepareCommandTotalCounter()
        {
            InitData.TotalCommandID = 0;
            switch (InitData.InitState)
            {
                case TInitState.istDevTree:
                    InitData.TotalCommandCount = InitData.CommandCount;
                    break;
                case TInitState.istDevices:
                    InitData.TotalCommandCount = GetChangesCommandCount(InitData.ChangeItemsDevices, InitDeviceChangeCount);
                    break;
                case TInitState.istInitTimeZones:
                case TInitState.istInitAccessLevels:
                case TInitState.istInitCards:
                case TInitState.istInitHolidays:
                case TInitState.istChangeTimeZones:
                case TInitState.istChangeAccessLevels:
                case TInitState.istChangeCards:
                case TInitState.istChangeHolidays:
                    InitData.TotalCommandCount = GetChangesCommandCount(InitData.ChangeItemsTimeZones, InitUserChangeCount) +
                                                 GetChangesCommandCount(InitData.ChangeItemsAccessLevels, InitUserChangeCount) +
                                                 GetChangesCommandCount(InitData.ChangeItemsCards, InitUserChangeCount) +
                                                 GetChangesCommandCount(InitData.ChangeItemsHolidays, InitUserChangeCount);
                    break;

                case TInitState.istLoadAPBZones:
                case TInitState.istLoadAPBDoors:
                    InitData.TotalCommandCount = GetChangesCommandCount(InitData.APBConfig.Root.Element("APBSettings").Element("APBZones"), InitAPBZoneCount) +
                                                 GetChangesCommandCount(InitData.APBConfig.Root.Element("APBSettings").Element("APBDoors"), InitAPBDoorCount);
                    break;

                case TInitState.istInitDeviceAPB:
                    InitData.TotalCommandCount = GetChangesCommandCount(InitData.InitDevList, InitAPBCount);
                    break;
                case TInitState.istInitMBNetCards:
                    InitData.TotalCommandCount = GetChangesCommandCount(InitData.ChangeItemsCards, InitMBNEtCardCount);
                    break;
                case TInitState.istInitMBNetOPS:
                    InitData.TotalCommandCount = GetChangesCommandCount(InitData.ChangeItemsOPS, InitMBNEtOPSCount);
                    break;
                case TInitState.istInitCUOPS:
                    InitData.TotalCommandCount = GetChangesCommandCount(InitData.ChangeItemsOPS, InitCUOPSCount);
                    break;
                case TInitState.istChangeOPS:
                    InitData.TotalCommandCount = GetChangesCommandCount(InitData.ChangeItemsOPS, InitChangeOPSCount);
                    break;
                default:
                    break;
            }
        }

        private TInitState NextInitStage()
        {
            TInitState retval = TInitState.istNone;
            switch (InitData.InitState)
            {
                case TInitState.istDevTree:
                    retval = TInitState.istFinished;
                    break;
                case TInitState.istDevices:
                    retval = TInitState.istFinished;
                    break;
                case TInitState.istInitAccessLevels:
                    InitData.ChangeItemsAccessLevels = null;
                    retval = TInitState.istInitTimeZones;
                    break;
                case TInitState.istInitTimeZones:
                    InitData.ChangeItemsTimeZones = null;
                    retval = TInitState.istInitCards;
                    break;
                case TInitState.istInitCards:
                    InitData.ChangeItemsCards = null;
                    retval = TInitState.istInitHolidays;
                    break;
                case TInitState.istInitHolidays:
                    InitData.ChangeItemsHolidays = null;
                    retval = TInitState.istFinished;
                    break;
                case TInitState.istLoadAPBZones:
                    retval = TInitState.istLoadAPBDoors;
                    break;
                case TInitState.istLoadAPBDoors:
                case TInitState.istFinished:
                    retval = TInitState.istFinished;
                    break;
                case TInitState.istInitDeviceAPB:
                case TInitState.istInitMBNetAPB:
                case TInitState.istInitMBNetCards:
                    retval = TInitState.istFinished;
                    break;
                case TInitState.istInitMBNetOPS:
                    retval = TInitState.istFinished;
                    break;
                case TInitState.istInitCUOPS:
                    retval = TInitState.istFinished;
                    break;
                case TInitState.istChangeOPS:
                    retval = TInitState.istFinished;
                    break;
                case TInitState.istChangeAccessLevels:
                    if (InitData.ChangeItemsTimeZones != null)
                        retval = TInitState.istChangeTimeZones;
                    else
                    if (InitData.ChangeItemsCards != null)
                        retval = TInitState.istChangeCards;
                    else
                    if (InitData.ChangeItemsHolidays != null)
                        retval = TInitState.istChangeHolidays;
                    else
                        retval = TInitState.istFinished;
                    break;
                case TInitState.istChangeTimeZones:
                    if (InitData.ChangeItemsCards != null)
                        retval = TInitState.istChangeCards;
                    else
                    if (InitData.ChangeItemsHolidays != null)
                        retval = TInitState.istChangeHolidays;
                    else
                        retval = TInitState.istFinished;
                    break;
                case TInitState.istChangeCards:
                    if (InitData.ChangeItemsHolidays != null)
                        retval = TInitState.istChangeHolidays;
                    else
                        retval = TInitState.istFinished;
                    break;
                case TInitState.istChangeHolidays:
                    retval = TInitState.istFinished;
                    break;
                default:
                    break;
            }
            return retval;
        }

        private int GetChangesCommandCount(XElement aChangeItems, int aInitChangeItemCount)
        {
            int retval = 0;
            if (aChangeItems != null)
            {
                int changeItemsSize = Encoding.UTF8.GetByteCount(aChangeItems.ToString());

                /*
                if (changeItemsSize <= 0x40000)
                {
                  retval = 1;
                }
                else
                {
                  int changeItemsCount = aChangeItems.Elements().Count();
                  if ((changeItemsCount % InitChangeItemCount) == 0)
                    retval = changeItemsCount / InitChangeItemCount;
                  else
                    retval = changeItemsCount / InitChangeItemCount + 1;
                }
                */

                int changeItemsCount = aChangeItems.Elements().Count();
                if ((changeItemsCount % aInitChangeItemCount) == 0)
                    retval = changeItemsCount / aInitChangeItemCount;
                else
                    retval = changeItemsCount / aInitChangeItemCount + 1;

            }
            return retval;
        }

        private void PrepareCommandData()
        {
            InitData.CommandID = 0;
            switch (InitData.InitState)
            {
                case TInitState.istDevTree:
                    int SDKDevTreeSize = Encoding.UTF8.GetByteCount(InitData.SDKDevTree.ToString());
                    //TODO временно для отладки нескольких пакетов инициализации
                    /*
                    if (SDKDevTreeSize <= 0x40000)
                      InitData.CommandCount = 1;
                    else
                      InitData.CommandCount = InitData.SDKDevTree.Root.Element("Devices").Elements().Count() / InitDeviceCount + 1;
                      */
                    InitData.CommandCount = 1;
                    int deviceCount = InitData.SDKDevTree.Element("Devices").Elements().Count();
                    if ((deviceCount % InitDeviceListCount) == 0)
                        InitData.CommandCount += deviceCount / InitDeviceListCount;
                    else
                        InitData.CommandCount += deviceCount / InitDeviceListCount + 1;

                    if (InitData.SDKDevTree.Element("MNParts") != null)
                    {
                        int partCount = InitData.SDKDevTree.Element("MNParts").Elements().Count();
                        if ((partCount % InitPartListCount) == 0)
                            InitData.CommandCount += partCount / InitPartListCount;
                        else
                            InitData.CommandCount += partCount / InitPartListCount + 1;
                    }

                    if (InitData.SDKDevTree.Element("MNPartGroups") != null)
                    {
                        int groupCount = InitData.SDKDevTree.Element("MNPartGroups").Elements().Count();
                        if ((groupCount % InitPartGroupListCount) == 0)
                            InitData.CommandCount += groupCount / InitPartGroupListCount;
                        else
                            InitData.CommandCount += groupCount / InitPartGroupListCount + 1;
                    }
                    break;
                case TInitState.istDevices:
                    InitData.ChangeItems = InitData.ChangeItemsDevices;
                    InitData.CommandCount = GetChangesCommandCount(InitData.ChangeItems, InitDeviceChangeCount);
                    break;
                case TInitState.istInitAccessLevels:
                case TInitState.istChangeAccessLevels:
                    InitData.ChangeItems = InitData.ChangeItemsAccessLevels;
                    InitData.CommandCount = GetChangesCommandCount(InitData.ChangeItems, InitUserChangeCount);
                    break;
                case TInitState.istInitTimeZones:
                case TInitState.istChangeTimeZones:
                    InitData.ChangeItems = InitData.ChangeItemsTimeZones;
                    InitData.CommandCount = GetChangesCommandCount(InitData.ChangeItems, InitUserChangeCount);
                    break;
                case TInitState.istInitCards:
                case TInitState.istChangeCards:
                    InitData.ChangeItems = InitData.ChangeItemsCards;
                    InitData.CommandCount = GetChangesCommandCount(InitData.ChangeItems, InitUserChangeCount);
                    break;
                case TInitState.istInitHolidays:
                case TInitState.istChangeHolidays:
                    InitData.ChangeItems = InitData.ChangeItemsHolidays;
                    InitData.CommandCount = GetChangesCommandCount(InitData.ChangeItems, InitUserChangeCount);
                    break;
                case TInitState.istLoadAPBZones:
                    InitData.ChangeItems = InitData.APBConfig.Root.Element("APBSettings").Element("APBZones");
                    InitData.CommandCount = GetChangesCommandCount(InitData.ChangeItems, InitAPBZoneCount);
                    break;
                case TInitState.istLoadAPBDoors:
                    InitData.ChangeItems = InitData.APBConfig.Root.Element("APBSettings").Element("APBDoors");
                    InitData.CommandCount = GetChangesCommandCount(InitData.ChangeItems, InitAPBDoorCount);
                    break;
                case TInitState.istInitDeviceAPB:
                case TInitState.istInitMBNetAPB:
                    InitData.ChangeItems = InitData.InitDevList;
                    InitData.CommandCount = GetChangesCommandCount(InitData.InitDevList, InitAPBCount);
                    break;
                case TInitState.istInitMBNetCards:
                    InitData.ChangeItems = InitData.ChangeItemsCards;
                    InitData.CommandCount = GetChangesCommandCount(InitData.ChangeItems, InitMBNEtCardCount);
                    break;
                case TInitState.istInitMBNetOPS:
                    InitData.ChangeItems = InitData.ChangeItemsOPS;
                    InitData.CommandCount = GetChangesCommandCount(InitData.ChangeItems, InitMBNEtOPSCount);
                    break;
                case TInitState.istInitCUOPS:
                    InitData.ChangeItems = InitData.ChangeItemsOPS;
                    InitData.CommandCount = GetChangesCommandCount(InitData.ChangeItems, InitCUOPSCount);
                    break;
                case TInitState.istChangeOPS:
                    InitData.ChangeItems = InitData.ChangeItemsOPS;
                    InitData.CommandCount = GetChangesCommandCount(InitData.ChangeItems, InitChangeOPSCount);
                    break;
                default:
                    InitData.ChangeItems = null;
                    InitData.CommandCount = 0;
                    break;
            }
        }

        private void PrepareUserChangeItems()
        {
            InitData.InitDevList = PrepareInitDevList();
            InitData.ChangeItemsTimeZones = new XElement("Items", ChangeItems_TimeZones().Elements());
            InitData.ChangeItemsAccessLevels = new XElement("Items", ChangeItems_AccessLevels().Elements());
            InitData.ChangeItemsCards = new XElement("Items", ChangeItems_Cards().Elements());
            InitData.ChangeItemsHolidays = new XElement("Items", ChangeItems_Holidays().Elements());
        }

        private void PrepareMBNetCardChangeItems()
        {
            InitData.InitDevList = PrepareInitMBNetList();
            InitData.ChangeItemsCards = new XElement("Items", ChangeItems_MBNetCards().Elements());
        }

        private void PrepareMBNetOPSChangeItems()
        {
            InitData.InitDevList = PrepareInitOPSMBNetList();
            InitData.ChangeItemsOPS = new XElement("Items", ChangeItems_MBNetOPSForInit().Elements());
        }

        private bool PrepareMBNetChangeOPSItems()
        {
            InitData.InitDevList = PrepareInitOPSMBNetList(); //TODO наверно не нужен здесь
            InitData.ChangeItemsOPS = new XElement("Items", ChangeItems_MBNetOPSChange().Elements());
            if (InitData.ChangeItemsOPS.Elements().Count() > 0)
                return true;
            else
                return false;
        }

        private bool PrepareMBChangeOPSItems()
        {
            InitData.ChangeItemsOPS = new XElement("Items", ChangeItems_CUOPSChange().Elements());
            if (InitData.ChangeItemsOPS.Elements().Count() > 0)
                return true;
            else
                return false;
        }

        private void PrepareCUOPSChangeItems()
        {
            //InitData.InitDevList = PrepareInitOPSMBNetList();
            InitData.ChangeItemsOPS = new XElement("Items", ChangeItems_CUOPS().Elements());
        }

        private XElement PrepareInitDevList()
        {
            XElement initDevices = new XElement("Devices", "");
            foreach (var device in InitData.DevTree.Descendants("MBDev"))
            {
                initDevices.Add(device.Element("ID"));
            }
            return initDevices;
        }

        private XElement PrepareInitMBNetList()
        {
            XElement initMBNets = new XElement("InitMBNets", "");
            foreach (var mbnet in InitData.DevTree.Descendants("MBNet"))
            {
                initMBNets.Add(new XElement("ID", mbnet.Attribute("ID").Value));
            }
            return initMBNets;
        }

        private XElement PrepareInitOPSMBNetList()
        {
            XElement initMBNets = new XElement("InitMBNets", "");
            XElement mbnet = InitData.OPSConfig.Root.Element("MBNets").Elements().First();
            if (mbnet != null)
                initMBNets.Add(new XElement("ID", mbnet.Element("MBNetID").Value));
            return initMBNets;
        }

        private XElement PrepareChangeOPSMBNetList()
        {
            XElement initMBNets = new XElement("InitMBNets", "");
            XElement mbnet = InitData.OPSConfig.Root.Element("MBNets").Elements().First();
            if (mbnet != null)
                initMBNets.Add(new XElement("ID", mbnet.Element("MBNetID").Value));
            return initMBNets;
        }

        private XElement ChangeItems_TimeZones()
        {
            XElement changeItems = new XElement("ChangeItems", "");
            changeItems.Add(new XElement("Item", new XElement("Action", "DelAllTB")));

            foreach (var timeZone in InitData.TimeZones.Root.Elements("TimeZone"))
            {
                var changeItem = new XElement("Item",
                  new XElement("Action", "AddTB"),
                  new XElement("TmBlockID", timeZone.Attribute("ID").Value),
                  new XElement("TmBlockNo", timeZone.Attribute("ID").Value),
                  new XElement("Intervals", ""));

                foreach (var timeInterval in timeZone.Elements("TimeInterval"))
                {
                    changeItem.Element("Intervals").Add(
                      new XElement("TimeInterval",
                        new XElement("StartTime", timeInterval.Attribute("StartTime").Value),
                        new XElement("EndTime", timeInterval.Attribute("EndTime").Value),
                        new XElement("ActiveDays", timeInterval.Attribute("ActiveDays").Value),
                        new XElement("ActiveHolidays", timeInterval.Attribute("ActiveHolidays").Value),
                        new XElement("Period", timeInterval.Attribute("Period").Value),
                        new XElement("StartDate", timeInterval.Attribute("StartDate").Value),
                        new XElement("EndDate", timeInterval.Attribute("EndDate").Value),
                        new XElement("NoUsingHolidays", timeInterval.Attribute("NoUsingHolidays").Value)
                        ));
                }
                changeItems.Add(changeItem);
            }
            return changeItems;
        }

        private XElement ChangeItems_AccessLevels()
        {
            XElement changeItems = new XElement("ChangeItems", "");
            changeItems.Add(new XElement("Item", new XElement("Action", "DelAllAL")));

            foreach (var accessLevel in InitData.AccessLevels.Root.Elements("AccessLevel"))
            {
                var changeItem = new XElement("Item",
                  new XElement("Action", "AddAL"),
                  new XElement("ALItems", ""),
                  new XElement("ALNo", accessLevel.Attribute("ID").Value),
                  new XElement("ALID", accessLevel.Attribute("ID").Value));

                foreach (var accessLevelItem in accessLevel.Elements("AccessLevelItem"))
                {
                    changeItem.Element("ALItems").Add(
                      new XElement("ALItem",
                        new XElement("TmBlockNo", accessLevelItem.Attribute("TimeZoneID").Value),
                        new XElement("TmBlockID", accessLevelItem.Attribute("TimeZoneID").Value),
                        new XElement("RdrID", accessLevelItem.Attribute("ReaderID").Value)));
                }

                changeItems.Add(changeItem);
            }
            return changeItems;
        }

        private XElement ChangeItems_Cards()
        {
            XElement changeItems = new XElement("ChangeItems", "");
            changeItems.Add(new XElement("Item", new XElement("Action", "DelAllCards")));

            foreach (var card in InitData.Cards.Root.Elements("Card"))
            {
                var changeItem = new XElement("Item",
                  new XElement("Action", "AddCard"),
                  new XElement("CardNo", card.Attribute("CardNo").Value),
                  new XElement("ALNo", card.Attribute("ALNo").Value),
                  new XElement("PINCode", card.Attribute("PINCode").Value),
                  new XElement("IsTmpCard", card.Attribute("IsTmpCard").Value),
                  new XElement("NoAPB", card.Attribute("NoAPB").Value),
                  new XElement("Options", card.Attribute("Options").Value),
                  new XElement("Priv", card.Attribute("Priv").Value),
                  new XElement("CardAction", card.Attribute("CardAction").Value),
                  new XElement("StartDate", card.Attribute("StartDate").Value),
                  new XElement("EndDate", card.Attribute("EndDate").Value),
                  new XElement("DevParams", ""));

                foreach (var devParam in card.Elements("DevParam"))
                {
                    changeItem.Element("DevParams").Add(
                      new XElement("DevParamsItem",
                        new XElement("ID", devParam.Attribute("ID").Value),
                        new XElement("Options", devParam.Attribute("Options").Value),
                        new XElement("Priv", devParam.Attribute("Priv").Value),
                        new XElement("CardAction", devParam.Attribute("CardAction").Value)
                        ));
                }

                changeItems.Add(changeItem);
            }
            return changeItems;
        }

        private XElement ChangeItems_Holidays()
        {
            XElement changeItems = new XElement("ChangeItems", "");
            changeItems.Add(new XElement("Item", new XElement("Action", "DelAllHolidays")));
            var changeItem = new XElement("Item", new XElement("Action", "AddHolidays"), new XElement("Items", ""));
            foreach (var holiday in InitData.Holidays.Root.Elements("Holiday"))
            {
                changeItem.Element("Items").Add(
                  new XElement("Item",
                    new XElement("Date", holiday.Attribute("Date").Value),
                    new XElement("Type", holiday.Attribute("Type").Value)));
            }
            changeItems.Add(changeItem);
            return changeItems;
        }

        private XElement ChangeItems_MBNetCards()
        {
            XElement changeItems = new XElement("ChangeItems", "");
            foreach (var mbNetID in InitData.InitDevList.Elements())
            {
                changeItems.Add(new XElement("Item",
                  new XElement("MBNetID", mbNetID.Value),
                  new XElement("Action", "DeleteAllMNCards")));

                // TODO большие массивы разбивать на блоки
                var changeItem = new XElement("Item",
                  new XElement("MBNetID", mbNetID.Value),
                  new XElement("Action", "AddMNCards"),
                  new XElement("Cards", ""));

                foreach (var card in InitData.Cards.Root.Elements("Card"))
                {
                    changeItem.Element("Cards").Add(new XElement("CardNo", card.Attribute("CardNo").Value));
                }
                changeItems.Add(changeItem);

            }
            return changeItems;
        }

        private XElement PrepareInitCUUsers(XElement aDevice)
        {
            XElement changeItems = new XElement("ChangeItems", "");
            if (aDevice.Element("Users") != null)
            {
                changeItems.Add(new XElement("Item",
                  new XElement("Action", "AddOPSUsers"),
                  aDevice.Element("DeviceID"),
                  aDevice.Element("Users")));
            }
            return changeItems;
        }

        private XElement PrepareClearOPS(XElement aMBNet)
        {
            XElement changeItems = new XElement("ChangeItems",
            new XElement("Item",
              aMBNet.Element("MBNetID"),
              new XElement("Action", "ClearOPS")));
            return changeItems;
        }

        private XElement PrepareInitParts(XElement aMBNet)
        {
            XElement changeItems = new XElement("ChangeItems", "");
            if ((aMBNet.Element("LocalParts") != null) || (aMBNet.Element("GlobalParts") != null))
            {
                var changeItem = new XElement("Item",
                  aMBNet.Element("MBNetID"),
                  new XElement("Action", "AddParts"));
                if (aMBNet.Element("LocalParts") != null)
                    changeItem.Add(aMBNet.Element("LocalParts"));
                if (aMBNet.Element("GlobalParts") != null)
                    changeItem.Add(aMBNet.Element("GlobalParts"));
                changeItem.Descendants("Name").Remove();
                changeItem.Descendants("CUAddr").Remove();
                changeItem.Descendants("PartAddr").Remove();
                changeItems.Add(changeItem);
            }
            return changeItems;
        }

        private XElement PrepareInitPartGroups(XElement aMBNet)
        {
            XElement changeItems = new XElement("ChangeItems", "");
            if (aMBNet.Element("PartGroups") != null)
            {
                var changeItem = new XElement("Item",
                  aMBNet.Element("MBNetID"),
                  new XElement("Action", "AddPartGroups"),
                  aMBNet.Element("PartGroups"));
                changeItem.Descendants("Name").Remove();

                changeItems.Add(changeItem);
            }
            return changeItems;
        }

        private XElement PrepareInitReaders(XElement aMBNet)
        {
            XElement changeItems = new XElement("ChangeItems", "");

            if (aMBNet.Element("Readers") != null)
            {
                changeItems.Add(new XElement("Item",
                  aMBNet.Element("MBNetID"),
                  new XElement("Action", "AddReaders"),
                  aMBNet.Element("Readers")));
            }
            return changeItems;
        }

        private XElement PrepareInitPCNOuts(XElement aMBNet)
        {
            XElement changeItems = new XElement("ChangeItems", "");

            if (aMBNet.Element("PCNOuts") != null)
            {
                var changeItem = new XElement("Item",
                  aMBNet.Element("MBNetID"),
                  new XElement("Action", "AddPCNOuts"),
                  new XElement("PCNOuts", ""));

                foreach (var pcnOut in aMBNet.Element("PCNOuts").Elements())
                {
                    var pcnOutProps = new XElement(pcnOut);
                    pcnOutProps.Element("Parts").Remove();
                    changeItem.Element("PCNOuts").Add(pcnOutProps);
                }
                changeItems.Add(changeItem);

                changeItem = new XElement("Item",
                  aMBNet.Element("MBNetID"),
                  new XElement("Action", "AddPCNItems"),
                  new XElement("PCNOuts", ""));

                foreach (var pcnOut in aMBNet.Element("PCNOuts").Elements())
                {
                    var pcnItems = new XElement(pcnOut);
                    pcnItems.Element("Program").Remove();
                    pcnItems.Element("Delay").Remove();
                    pcnItems.Element("Plus").Remove();
                    pcnItems.Element("Minus").Remove();
                    pcnItems.Element("Repeat").Remove();
                    pcnItems.Element("Unit").Remove();
                    changeItem.Element("PCNOuts").Add(pcnItems);
                }
                changeItems.Add(changeItem);
            }
            return changeItems;
        }

        private XElement PrepareInitPartContent(XElement aMBNet)
        {
            XElement changeItems = new XElement("ChangeItems", "");
            if (aMBNet.Element("PartContent") != null)
            {
                changeItems.Add(new XElement("Item",
                  aMBNet.Element("MBNetID"),
                  new XElement("Action", "AddOPSZones"),
                  new XElement("Parts", aMBNet.Element("PartContent").Elements())));
            }
            return changeItems;
        }

        private XElement PrepareInitPartGroupContent(XElement aMBNet)
        {
            XElement changeItems = new XElement("ChangeItems", "");
            if (aMBNet.Element("PartGroupContent") != null)
            {
                changeItems.Add(new XElement("Item",
                  aMBNet.Element("MBNetID"),
                  new XElement("Action", "AddPartGroupItems"),
                  new XElement("PartGroups", aMBNet.Element("PartGroupContent").Elements())));
            }
            return changeItems;
        }

        private XElement PrepareInitIndDevs(XElement aMBNet)
        {
            XElement changeItems = new XElement("ChangeItems", "");
            if (aMBNet.Element("IndDevs") != null)
            {
                var changeItem = new XElement("Item",
                  aMBNet.Element("MBNetID"),
                  new XElement("Action", "AddIndDevs"),
                  new XElement("IndDevs", ""));

                foreach (var indDev in aMBNet.Element("IndDevs").Elements())
                {
                    var indDevProps = new XElement(indDev);
                    if (indDev.Element("Indicators") != null)
                        indDevProps.Element("Indicators").Remove();
                    changeItem.Element("IndDevs").Add(indDevProps);
                }
                changeItems.Add(changeItem);

                changeItem = new XElement("Item",
                  aMBNet.Element("MBNetID"),
                  new XElement("Action", "AddIndDevItems"),
                  new XElement("IndDevs", ""));

                foreach (var indDev in aMBNet.Element("IndDevs").Elements())
                {
                    if (indDev.Element("Indicators") != null)
                    {
                        var indicators = new XElement(indDev);
                        indicators.Element("IndDevType").Remove();
                        changeItem.Element("IndDevs").Add(indicators);
                    }
                }
                changeItems.Add(changeItem);
            }
            return changeItems;
        }

        private XElement PrepareInitOPSGroups(XElement aMBNet)
        {
            XElement changeItems = new XElement("ChangeItems", "");
            var changeItem = new XElement("Item",
              aMBNet.Element("MBNetID"),
              new XElement("Action", "ClearOPSUsersAndGroups"));
            changeItems.Add(changeItem);

            if (aMBNet.Element("OPSGroups") != null)
            {
                changeItem = new XElement("Item",
                  aMBNet.Element("MBNetID"),
                  new XElement("Action", "AddOPSGroups"),
                  new XElement("OPSGroups", ""));

                foreach (var opsGroup in aMBNet.Element("OPSGroups").Elements())
                {
                    var opsParts = new XElement(opsGroup);
                    opsParts.Element("Users").Remove();
                    changeItem.Element("OPSGroups").Add(opsParts);
                }
                changeItems.Add(changeItem);

                changeItem = new XElement("Item",
                  aMBNet.Element("MBNetID"),
                  new XElement("Action", "AddOPSUsers"),
                  new XElement("OPSGroups", ""));

                foreach (var opsGroup in aMBNet.Element("OPSGroups").Elements())
                {
                    var opsUsers = new XElement(opsGroup);
                    opsUsers.Element("Parts").Remove();
                    changeItem.Element("OPSGroups").Add(opsUsers);
                }
                changeItems.Add(changeItem);
            }
            return changeItems;
        }

        private XElement PrepareApplyOPS(XElement aMBNet)
        {
            XElement changeItems = new XElement("ChangeItems",
              new XElement("Item",
                aMBNet.Element("MBNetID"),
                new XElement("Action", "ApplyOPS")));
            return changeItems;
        }

        private void CompareOPSGroups()
        {
            XElement newMBNets = InitData.MBNetOPSConfigNew.Root.Element("MBNets");
            XElement oldMBNets = InitData.OPSConfig.Root.Element("MBNets");
            foreach (var newMBNet in newMBNets.Elements())
            {
                XElement oldMBNet = oldMBNets.Descendants("MBNet").Where(e => e.Element("MBNetID").Value == newMBNet.Element("MBNetID").Value).FirstOrDefault();
                if (oldMBNet != null)
                {
                    oldMBNet.Add(new XAttribute("Changed", false));
                    var newOPSGroups = newMBNet.Element("OPSGroups");
                    var oldOPSGroups = oldMBNet.Element("OPSGroups");
                    foreach (var newOPSGroup in newOPSGroups.Elements())
                    {
                        XElement oldOPSGroup = oldOPSGroups.Descendants("OPSGroup").Where(e => e.Element("OPSGroupID").Value == newOPSGroup.Element("OPSGroupID").Value).FirstOrDefault();

                        if (oldOPSGroup != null)
                        {
                            newOPSGroup.Add(new XAttribute("Change", "Update"));
                            oldOPSGroup.Add(new XAttribute("Change", "Update"));
                        }
                        else
                            newOPSGroup.Add(new XAttribute("Change", "New"));
                    }
                }
            }
        }

        private XElement AddDeleteMNOPSGroupsItems(XElement aOldMBNet, XElement aDeleteGroups)
        {
            XElement changeItems = new XElement("ChangeItems",
              new XElement("Item",
                aOldMBNet.Element("MBNetID"),
                new XElement("Action", "DeleteMNOPSGroups"),
                aDeleteGroups
              ));
            return changeItems;
        }

        private XElement AddUpdateMNOPSGroupItems(XElement aOldMBNet, XElement aUpdateGroups)
        {
            XElement changeItems = new XElement("ChangeItems");
            foreach (var updateGroup in aUpdateGroups.Elements())
            {
                changeItems.Add(new XElement("Item",
                  aOldMBNet.Element("MBNetID"),
                  new XElement("Action", "ClearMNOPSGroup"),
                  updateGroup.Element("OPSGroupID")));

                changeItems.Add(new XElement("Item",
                  aOldMBNet.Element("MBNetID"),
                  new XElement("Action", "UpdateMNOPSGroup"),
                  new XElement("OPSGroup",
                    updateGroup.Element("OPSGroupID"),
                    updateGroup.Element("Parts"))));
            }

            return changeItems;
        }

        private XElement AddNewMNOPSGroupItems(XElement aOldMBNet, XElement aNewGroups)
        {
            XElement changeItems = new XElement("ChangeItems");
            foreach (var newGroup in aNewGroups.Elements())
            {
                changeItems.Add(new XElement("Item",
                  aOldMBNet.Element("MBNetID"),
                  new XElement("Action", "UpdateMNOPSGroup"),
                  new XElement("OPSGroup",
                    newGroup.Element("OPSGroupID"),
                    newGroup.Element("Parts"))));
            }

            return changeItems;
        }

        private XElement AddDeleteMNOPSUsersItems(XElement aOldMBNet, XElement aDeleteUsers)
        {
            XElement changeItems = new XElement("ChangeItems",
              new XElement("Item",
                aOldMBNet.Element("MBNetID"),
                new XElement("Action", "DeleteMNOPSUsers"),
                aDeleteUsers));
            return changeItems;
        }

        private XElement AddNewMNOPSUsersItems(XElement aOldMBNet, XElement aNewUsers)
        {
            XElement changeItems = new XElement("ChangeItems",
              new XElement("Item",
                aOldMBNet.Element("MBNetID"),
                new XElement("Action", "AddMNOPSUsers"),
                aNewUsers));
            return changeItems;
        }

        private XElement PrepareChangeOPS(XElement aOldMBNet)
        {
            XElement changeItems = new XElement("ChangeItems");

            XElement newMBNet = InitData.MBNetOPSConfigNew.Root.Element("MBNets").Elements().Where(e => e.Element("MBNetID").Value == aOldMBNet.Element("MBNetID").Value).FirstOrDefault();
            if (newMBNet == null)
                return changeItems;

            // списки для формирования изменений групп ОПС
            XElement deleteGroups = new XElement("OPSGroups");
            XElement updateGroups = new XElement("OPSGroups");
            XElement newGroups = new XElement("OPSGroups");
            XElement needUpdateGroups = new XElement("OPSGroups");

            deleteGroups.Add(aOldMBNet.Descendants("OPSGroupID").Where(e => e.Parent.Attribute("Change") == null));
            newGroups.Add(newMBNet.Descendants("OPSGroup").Where(e => e.Attribute("Change").Value == "New"));
            updateGroups.Add(newMBNet.Descendants("OPSGroup").Where(e => e.Attribute("Change").Value == "Update"));

            // группы, у которых изменился состав разделов
            foreach (var updateGroup in updateGroups.Elements())
            {
                string groupID = updateGroup.Element("OPSGroupID").Value;
                XElement oldGroup = aOldMBNet.Descendants("OPSGroup").Where(e => e.Element("OPSGroupID").Value == groupID).FirstOrDefault();
                if (oldGroup != null)
                {
                    var updateParts = updateGroup.Element("Parts");
                    var oldParts = oldGroup.Element("Parts");

                    bool needUpdate = false;
                    if (updateParts.Elements().Count() != oldParts.Elements().Count())
                        needUpdate = true;
                    else
                        foreach (var part in updateParts.Elements())
                        {
                            XElement oldPart = oldParts.Descendants("Part").Where(e => e.Element("PartID").Value == part.Element("PartID").Value).FirstOrDefault();
                            if (oldPart == null)
                                needUpdate = true;
                            else
                              if (oldPart.Element("CanDisarm").Value != part.Element("CanDisarm").Value)
                                needUpdate = true;

                            if (needUpdate) break;
                        }

                    if (needUpdate)
                        needUpdateGroups.Add(updateGroup);
                }
            }

            // списки для формирования изменений пользователей ОПС
            XElement deleteUsers = new XElement("Users");
            XElement newUsers = new XElement("Users");
            // добавление карт при обновлении групп
            foreach (var updateGroup in updateGroups.Elements())
            {
                string groupID = updateGroup.Element("OPSGroupID").Value;
                XElement oldGroup = aOldMBNet.Descendants("OPSGroup").Where(e => e.Element("OPSGroupID").Value == groupID).FirstOrDefault();
                if (oldGroup != null)
                {
                    var updateUsers = updateGroup.Element("Users");
                    var oldUsers = oldGroup.Element("Users");
                    foreach (var cardNo in updateUsers.Elements())
                    {
                        XElement oldUser = oldUsers.Descendants("CardNo").Where(e => e.Value == cardNo.Value).FirstOrDefault();
                        // если пользователь не найден в старой конфигурации, то его добавить 
                        if (oldUser == null)
                            newUsers.Add(new XElement("User", cardNo, updateGroup.Element("OPSGroupID")));
                    }
                }
            }
            //удаление карт при обновлении групп ОПС 
            foreach (var oldGroup in aOldMBNet.Descendants("OPSGroup"))
            {
                string groupID = oldGroup.Element("OPSGroupID").Value;
                if (oldGroup.Attribute("Change") != null)
                    if (oldGroup.Attribute("Change").Value == "Update")
                    {
                        XElement updateGroup = updateGroups.Descendants("OPSGroup").Where(e => e.Element("OPSGroupID").Value == groupID).FirstOrDefault();
                        foreach (var cardNo in oldGroup.Element("Users").Elements())
                        {
                            XElement newUser = updateGroup.Descendants("CardNo").Where(e => e.Value == cardNo.Value).FirstOrDefault();
                            // если пользователь не найден в новой конфигурации, то его надо удалить
                            if (newUser == null)
                                deleteUsers.Add(cardNo);
                        }
                    }
            }
            //добавление карт при добавлении групп ОПС
            foreach (var newGroup in newGroups.Elements("OPSGroup"))
            {
                foreach (var cardNo in newGroup.Element("Users").Elements())
                {
                    newUsers.Add(new XElement("User", cardNo, newGroup.Element("OPSGroupID")));
                }
            }

            if ((deleteGroups.Elements().Count() > 0) ||
              (needUpdateGroups.Elements().Count() > 0) ||
              (newGroups.Elements().Count() > 0) ||
              (deleteUsers.Elements().Count() > 0) ||
              (newUsers.Elements().Count() > 0))
            {
                aOldMBNet.Attribute("Changed").SetValue(true);
                //формируем списки изменений
                if (deleteGroups.Elements().Count() > 0) changeItems.Add(AddDeleteMNOPSGroupsItems(aOldMBNet, deleteGroups).Elements());
                if (needUpdateGroups.Elements().Count() > 0) changeItems.Add(AddUpdateMNOPSGroupItems(aOldMBNet, needUpdateGroups).Elements());
                if (newGroups.Elements().Count() > 0) changeItems.Add(AddNewMNOPSGroupItems(aOldMBNet, newGroups).Elements());
                if (deleteUsers.Elements().Count() > 0) changeItems.Add(AddDeleteMNOPSUsersItems(aOldMBNet, deleteUsers).Elements());
                if (newUsers.Elements().Count() > 0) changeItems.Add(AddNewMNOPSUsersItems(aOldMBNet, newUsers).Elements());
            }

            return changeItems;

        }

        private XElement ChangeItems_MBNetOPSForInit()
        {
            XElement changeItems = new XElement("ChangeItems", "");
            XElement initMBNet = InitData.OPSConfig.Root.Element("MBNets").Elements().First();
            if (initMBNet != null)
            {
                //TODO большие массивы разбивать на блоки, пока передаем данные одного типа в одном элементе  
                changeItems.Add(PrepareClearOPS(initMBNet).Elements());
                changeItems.Add(PrepareInitParts(initMBNet).Elements());
                changeItems.Add(PrepareInitPartGroups(initMBNet).Elements());
                changeItems.Add(PrepareInitReaders(initMBNet).Elements());
                changeItems.Add(PrepareInitPCNOuts(initMBNet).Elements());
                changeItems.Add(PrepareInitPartContent(initMBNet).Elements());
                changeItems.Add(PrepareInitPartGroupContent(initMBNet).Elements());
                changeItems.Add(PrepareInitIndDevs(initMBNet).Elements());
                changeItems.Add(PrepareInitOPSGroups(initMBNet).Elements());
                changeItems.Add(PrepareApplyOPS(initMBNet).Elements());
            }
            return changeItems;
        }

        private XElement ChangeItems_MBNetOPSChange()
        {
            XElement changeItems = new XElement("ChangeItems", "");
            // Внимание! Помечаем группы ОПС для обновления конфигурации !только существующих! в старой конфигурации КСК
            CompareOPSGroups();

            //Формируем изменения !только для существующих! в старой конфигурации КСК
            foreach (var oldMBNet in InitData.OPSConfig.Root.Element("MBNets").Elements())
            {
                if (oldMBNet.Attribute("Changed") != null)
                    changeItems.Add(PrepareChangeOPS(oldMBNet).Elements());
            }
            return changeItems;
        }

        private XElement ChangeItems_CUOPSChange()
        {
            // списки для формирования изменений групп ОПС
            XElement deleteUsers = new XElement("Users");
            XElement updateUsers = new XElement("Users");

            XElement changeItems = new XElement("ChangeItems", "");
            foreach (var oldDevice in InitData.OPSConfig.Root.Element("Devices").Elements())
            {
                XElement newDevice = InitData.MBOPSConfigNew.Root.Element("Devices").Descendants("Device").Where(e => e.Element("DeviceID").Value == oldDevice.Element("DeviceID").Value).FirstOrDefault();
                if (newDevice != null)
                {
                    foreach (var oldUser in oldDevice.Element("Users").Elements("User"))
                    {
                        if (newDevice.Element("Users").Elements("User").Where(e => e.Element("CardNo").Value == oldUser.Element("CardNo").Value).FirstOrDefault() == null)
                            deleteUsers.Add(oldUser.Element("CardNo"));
                    }

                    foreach (var newUser in newDevice.Element("Users").Elements("User"))
                    {
                        var oldUser = oldDevice.Element("Users").Elements("User").Where(e => e.Element("CardNo").Value == newUser.Element("CardNo").Value).FirstOrDefault();
                        if (oldUser != null)
                        {
                            if ((oldUser.Element("Parts").Value != newUser.Element("Parts").Value) || (oldUser.Element("CanDisarm").Value != newUser.Element("CanDisarm").Value))
                                updateUsers.Add(newUser);
                        }
                        else
                            updateUsers.Add(newUser);
                    }
                }

                if (deleteUsers.Elements().Count() > 0)
                {
                    changeItems.Add(new XElement("Item",
                      new XElement("Action", "DeleteCUOPSUsers"),
                      oldDevice.Element("DeviceID"),
                      deleteUsers));
                }

                if (updateUsers.Elements().Count() > 0)
                {
                    changeItems.Add(new XElement("Item",
                      new XElement("Action", "AddCUOPSUsers"),
                      oldDevice.Element("DeviceID"),
                      updateUsers));
                }
            }

            return changeItems;
        }

        private XElement ChangeItems_ChangeOPS()
        {
            XElement changeItems = new XElement("ChangeItems", "");
            return changeItems;

        }

        private XElement ChangeItems_CUOPS()
        {
            XElement changeItems = new XElement("ChangeItems", "");
            XElement initCU = InitData.OPSConfig.Root.Element("Devices").Elements().First();
            if (initCU != null)
            {
                changeItems.Add(new XElement("Item",
                   new XElement("Action", "DeleteAllOPSUsers"),
                   initCU.Element("DeviceID")));

                //TODO большие массивы разбивать на блоки 
                changeItems.Add(PrepareInitCUUsers(initCU).Elements());

            }
            return changeItems;
        }

        public void LoadAPBZones(XDocument aAPBConfig)
        {
            lock (InitData)
            {
                if (InitData.InitState == TInitState.istNone)
                {
                    BeginInitTime = DateTime.Now;
                    InitData.InitState = TInitState.istLoadAPBZones;
                    InitData.APBConfig = aAPBConfig;
                    InitData.MBOPSConfigNew = null;
                    InitData.MBNetOPSConfigNew = null;
                    PrepareCommandData();
                    PrepareCommandTotalCounter();
                    OnInit(0);
                }
            }
        }
    }
}
