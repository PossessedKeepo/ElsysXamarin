using System;
using System.Linq;
using System.Xml.Linq;

namespace HWConfig
{
    class ElsysConfig
    {
        public enum ConfigNodeType
        {
            cntElsysConfig,
            cntHardware,
            cntMBNet,
            cntRS485,
            cntNetGroup,
            cntDeviceRS,
            cntDeviceIP,
            cntInput,
            cntOut,
            cntOutGroup,
            cntDoor,
            cntTurn,
            cntGate,
            cntReader
        }

        public static class DevTypes
        {
            public static int dtDoor = 3;
            public static int dtTurn = 22;
            public static int dtGate = 4;
            public static int dtZone = 12;
            public static int dtPart = 13;
            public static int dtOut = 10;
            public static int dtCU = 5;
            public static int dtReader = 19;
            public static int dtPartGroup = 27;
            public static int dtMBNet = 28;
            public static int dtNetGroup = 40;
        }

        private XDocument Config;
        string ConfigPath;
        string ConfigFileName;
        string ExportFileName;
        public string ConfigFullPath;
        private int UniIndex;
        public string ConfigGUID;
        public XDocument DeviceConfig;

        public ElsysConfig()
        {
            UniIndex = 0;
            ConfigGUID = System.Guid.NewGuid().ToString().ToUpper();
            Config = new XDocument(
              new XElement("ElsysConfig",
                new XAttribute("ConfigNodeType", (int)ConfigNodeType.cntElsysConfig),
                new XElement("Hardware",
                  new XAttribute("ConfigNodeType", (int)ConfigNodeType.cntHardware),
                  new XAttribute("Name", "Конфигурация оборудования"),
                  new XAttribute("GUID", System.Guid.NewGuid().ToString().ToUpper()),
                  new XAttribute("ExchangeMode", 2),
                  new XAttribute("CardSize", 6),
                  new XAttribute("UNIINDEX", UniIndex))));

            ConfigPath = System.IO.Directory.GetCurrentDirectory();
            ConfigFileName = "ElsysConfig.xml";
            ExportFileName = "SDKDevTree.xml";
            ConfigFullPath = String.Format("{0}\\{1}", ConfigPath, ConfigFileName);
        }

        public static int GetDevTypeByConfigNode(XElement aConfigNode)
        {
            int devtype = 0;

            if (aConfigNode.Name == "MBDev") devtype = DevTypes.dtCU;
            else
            if (aConfigNode.Name == "MBNet") devtype = DevTypes.dtMBNet;
            else
            if (aConfigNode.Name == "NetGroup") devtype = DevTypes.dtNetGroup;
            else
            if (aConfigNode.Name == "Input") devtype = DevTypes.dtZone;
            else
            if (aConfigNode.Name == "Out") devtype = DevTypes.dtOut;
            else
            if (aConfigNode.Name == "OutGroup") devtype = DevTypes.dtOut;
            else
            if (aConfigNode.Name == "Door") devtype = DevTypes.dtDoor;
            else
            if (aConfigNode.Name == "Reader") devtype = DevTypes.dtReader;
            else
            if (aConfigNode.Name == "Part") devtype = DevTypes.dtPart;
            else
            if (aConfigNode.Name == "GlobalPart") devtype = DevTypes.dtPart;
            else
            if (aConfigNode.Name == "PartGroup") devtype = DevTypes.dtPartGroup;

            if (devtype == DevTypes.dtDoor)
            {
                if (aConfigNode.Element("DoorType") != null)
                {
                    int doorType = int.Parse(aConfigNode.Element("DoorType").Value);
                    if (doorType == 1)
                        devtype = DevTypes.dtTurn;
                    else
                    if (doorType == 2)
                        devtype = DevTypes.dtGate;
                }
            }

            return devtype;
        }

        public XElement HWRootElement()
        {
            return Config.Root.Element("Hardware");
        }

        public void SaveToFile()
        {
            HWRootElement().Attribute("UNIINDEX").Value = UniIndex.ToString();
            HWRootElement().Attribute("GUID").Value = System.Guid.NewGuid().ToString().ToUpper();
            Config.Save(ConfigFullPath);
        }

        private int GetNewMBNetAddr()
        {
            int retval = 0;
            var UsedAddr = Config.Root.Element("Hardware").Elements("MBNet").Attributes("Address").ToDictionary(attr => int.Parse(attr.Value));
            for (int i = 1; i <= 254; i++)
            {
                if (!UsedAddr.ContainsKey(i))
                {
                    retval = i;
                    break;
                }
            }
            return retval;
        }

        private XElement AddMBNet()
        {
            XElement retval = null;
            var addr = GetNewMBNetAddr();
            if (addr > 0)
            {
                retval = new XElement("MBNet",
                  new XAttribute("ConfigNodeType", (int)ConfigNodeType.cntMBNet),
                  new XAttribute("Name", String.Format("КСК {0}", addr)),
                  new XAttribute("Address", addr),
                  new XAttribute("IP", "192.168.127.254"),
                  new XAttribute("MBNetType", "Slave"),
                  new XAttribute("Version", 770),
                  new XAttribute("SubnetMask", "255.255.255.0"),
                  new XAttribute("ExchangeEnabled", true),
                  new XAttribute("TranslateILocks", false),
                  new XAttribute("ID", ++UniIndex)
                  );
                Config.Root.Element("Hardware").Add(retval);
            }
            return retval;
        }

        private XElement AddRS485(XElement aMBNet)
        {
            XElement retval = null;
            if (aMBNet.Elements("RS485").Count() == 0)
            {
                retval = new XElement("RS485",
                  new XAttribute("ConfigNodeType", (int)ConfigNodeType.cntRS485),
                  new XAttribute("Name", "Линия связи RS485"),
                  new XAttribute("BaudRate", 19200),
                  new XAttribute("Multimaster", false),
                  new XAttribute("APB", false),
                  new XAttribute("ForceAPB", false),
                  new XAttribute("NoCheckAPB", false),
                  new XAttribute("TransmissionDelay", 0),
                  new XAttribute("ReadInterval", 0),
                  new XAttribute("ResponseTimeout", 0),
                  new XAttribute("MaxErrorCount", 0),
                  new XAttribute("ID", ++UniIndex)
                  );
                aMBNet.Add(retval);
            }
            return retval;
        }

        private int GetNewNGAddr()
        {
            int retval = 0;
            var UsedAddr = Config.Root.Element("Hardware").Descendants("NetGroup").Attributes("Address").ToDictionary(attr => int.Parse(attr.Value));
            for (int i = 1; i <= 254; i++)
            {
                if (!UsedAddr.ContainsKey(i))
                {
                    retval = i;
                    break;
                }
            }
            return retval;
        }

        private XElement AddNetGroup(XElement aMBNet)
        {
            XElement retval = null;
            var addr = GetNewNGAddr();
            if (addr > 0)
            {
                retval = new XElement("NetGroup",
                  new XAttribute("ConfigNodeType", (int)ConfigNodeType.cntNetGroup),
                  new XAttribute("Name", String.Format("Сетевая группа {0}", addr)),
                  new XAttribute("Address", addr),
                  new XAttribute("ExchangeMNAddr", 0),
                  new XAttribute("ForceAPB", false),
                  new XAttribute("NoCheckAPB", false),
                  new XAttribute("ExchangeMode", 2),
                  new XAttribute("EnabledMN", true),
                  new XAttribute("TransmissionDelay", 0),
                  new XAttribute("ResponseTimeout", 0),
                  new XAttribute("ID", ++UniIndex)
                  );
                aMBNet.Add(retval);
            }

            return retval;
        }

        private int GetNewDeviceAddr(XElement aParent)
        {
            int retval = 0;
            var UsedAddr = aParent.Elements("Device").Elements("MBDev").Elements("Addr").ToDictionary(addr => int.Parse(addr.Value));
            for (int i = 1; i < 64; i++)
            {
                if (!UsedAddr.ContainsKey(i))
                {
                    retval = i;
                    break;
                }
            }
            return retval;
        }

        private bool CheckNewDeviceAddr(XElement aParent, int aNewAddr)
        {
            var UsedAddr = aParent.Elements("Device").Elements("MBDev").Elements("Addr").ToDictionary(addr => int.Parse(addr.Value));
            if ((aNewAddr < 1) || (aNewAddr >= 64) || UsedAddr.ContainsKey(aNewAddr))
                return false;
            else return true;
        }

        private XElement AddDeviceRS_Gen(XElement aRS)
        {
            XElement retval = null;
            var addr = GetNewDeviceAddr(aRS);
            if (addr > 0)
            {
                retval = new XElement(DeviceConfig.Root);

                var ambn = aRS.Parent.Attribute("Address").Value;
                var amb = addr.ToString();
                retval.Element("MBDev").Element("Addr").Value = retval.Element("MBDev").Element("Addr").Value.Replace("%amb%", amb);
                retval.Element("MBDev").Element("Name").Value = retval.Element("MBDev").Element("Name").Value.Replace("%ambn%", ambn).Replace("%amb%", amb);
                retval.Element("MBDev").Add(new XElement("ID", ++UniIndex));

                foreach (var input in retval.Element("Inputs").Elements("Input"))
                {
                    input.Element("Name").Value = input.Element("Name").Value.Replace("%ambn%", ambn).Replace("%amb%", amb);
                    input.Add(new XElement("ID", ++UniIndex));
                }

                foreach (var _out in retval.Element("Outs").Elements("Out"))
                {
                    _out.Element("Name").Value = _out.Element("Name").Value.Replace("%ambn%", ambn).Replace("%amb%", amb);
                    _out.Add(new XElement("ID", ++UniIndex));
                }

                foreach (var outgroup in retval.Element("OutGroups").Elements("OutGroup"))
                {
                    outgroup.Element("Name").Value = outgroup.Element("Name").Value.Replace("%ambn%", ambn).Replace("%amb%", amb);
                    outgroup.Add(new XElement("ID", ++UniIndex));
                }

                foreach (var door in retval.Element("Doors").Elements("Door"))
                {
                    door.Element("Name").Value = door.Element("Name").Value.Replace("%ambn%", ambn).Replace("%amb%", amb);
                    door.Add(new XElement("ID", ++UniIndex));
                }

                foreach (var reader in retval.Element("Readers").Elements("Reader"))
                {
                    reader.Element("Name").Value = reader.Element("Name").Value.Replace("%ambn%", ambn).Replace("%amb%", amb);
                    reader.Add(new XElement("ID", ++UniIndex));
                }

                foreach (var part in retval.Element("Parts").Elements("Part"))
                {
                    part.Element("Name").Value = part.Element("Name").Value.Replace("%ambn%", ambn).Replace("%amb%", amb);
                    part.Add(new XElement("ID", ++UniIndex));
                }

                aRS.Add(retval);
            }
            return retval;
        }

        private XElement AddDeviceIP_Gen(XElement aNG)
        {
            XElement retval = null;
            var addr = GetNewDeviceAddr(aNG);
            if (addr > 0)
            {
                retval = new XElement(DeviceConfig.Root);

                var ambn = "{" + aNG.Parent.Attribute("Address").Value + "}";
                var amb = addr.ToString();
                retval.Element("MBDev").Element("Addr").Value = retval.Element("MBDev").Element("Addr").Value.Replace("%amb%", amb);
                retval.Element("MBDev").Element("Name").Value = retval.Element("MBDev").Element("Name").Value.Replace("%ambn%", ambn).Replace("%amb%", amb);
                retval.Element("MBDev").Add(new XElement("IP",
                  new XElement("EVersion", 516),
                  new XElement("IP", "192.168.127.254"),
                  new XElement("SubnetMask", "255.255.255.0"),
                  new XElement("Gateway", "0.0.0.0"),
                  new XElement("DelayTime", 100),
                  new XElement("TimeoutTime", 300),
                  new XElement("MaxErrCount", 20),
                  new XElement("MaxSendCount", 20),
                  new XElement("ExchangeEnabled", true)));

                retval.Element("MBDev").Add(new XElement("ID", ++UniIndex));

                foreach (var input in retval.Element("Inputs").Elements("Input"))
                {
                    input.Element("Name").Value = input.Element("Name").Value.Replace("%ambn%", ambn).Replace("%amb%", amb);
                    input.Add(new XElement("ID", ++UniIndex));
                }

                foreach (var _out in retval.Element("Outs").Elements("Out"))
                {
                    _out.Element("Name").Value = _out.Element("Name").Value.Replace("%ambn%", ambn).Replace("%amb%", amb);
                    _out.Add(new XElement("ID", ++UniIndex));
                }

                foreach (var outgroup in retval.Element("OutGroups").Elements("OutGroup"))
                {
                    outgroup.Element("Name").Value = outgroup.Element("Name").Value.Replace("%ambn%", ambn).Replace("%amb%", amb);
                    outgroup.Add(new XElement("ID", ++UniIndex));
                }

                foreach (var door in retval.Element("Doors").Elements("Door"))
                {
                    door.Element("Name").Value = door.Element("Name").Value.Replace("%ambn%", ambn).Replace("%amb%", amb);
                    door.Add(new XElement("ID", ++UniIndex));
                }

                foreach (var reader in retval.Element("Readers").Elements("Reader"))
                {
                    reader.Element("Name").Value = reader.Element("Name").Value.Replace("%ambn%", ambn).Replace("%amb%", amb);
                    reader.Add(new XElement("ID", ++UniIndex));
                }

                foreach (var part in retval.Element("Parts").Elements("Part"))
                {
                    part.Element("Name").Value = part.Element("Name").Value.Replace("%ambn%", ambn).Replace("%amb%", amb);
                    part.Add(new XElement("ID", ++UniIndex));
                }

                aNG.Add(retval);
            }
            return retval;
        }

        private XElement AddDeviceRS(XElement aRS, XDocument aDeviceConfig, int aNewAddr)
        {
            XElement retval = null;
            if (aNewAddr == 0)
                aNewAddr = GetNewDeviceAddr(aRS);
            else
            if (CheckNewDeviceAddr(aRS, aNewAddr) == false)
                aNewAddr = 0;

            if (aNewAddr > 0)
            {
                UpdateDevAddr(aDeviceConfig, aRS.Parent.Attribute("Address").Value, aNewAddr.ToString());
                retval = new XElement(aDeviceConfig.Root);
                aRS.Add(retval);
            }
            return retval;
        }

        private XElement AddDeviceIP(XElement aNG, XDocument aDeviceConfig, int aNewAddr)
        {
            XElement retval = null;
            if (aNewAddr == 0)
                aNewAddr = GetNewDeviceAddr(aNG);
            else
            if (CheckNewDeviceAddr(aNG, aNewAddr) == false)
                aNewAddr = 0;

            if (aNewAddr > 0)
            {
                UpdateDevAddr(aDeviceConfig, "{" + aNG.Attribute("Address").Value + "}", aNewAddr.ToString());
                retval = new XElement(aDeviceConfig.Root);
                retval.Add(new XElement("IP",
                      new XElement("EVersion", 516),
                      new XElement("IP", "192.168.127.254"),
                      new XElement("Gateway", "0.0.0.0"),
                      new XElement("SubnetMask", "255.255.255.0"),
                      new XElement("DelayTime", 100),
                      new XElement("TimeoutTime", 300),
                      new XElement("MaxErrCount", 20),
                      new XElement("MaxSendCount", 20),
                      new XElement("ExchangeEnabled", true)
                      ));
                aNG.Add(retval);
            }
            return retval;
        }

        private XElement AddDeviceRS(XElement aRS)
        {
            XElement retval = null;
            var addr = GetNewDeviceAddr(aRS);
            if (addr > 0)
            {
                retval = new XElement("Device",
                  new XElement("MBDev",
                    new XElement("Addr", addr),
                    new XElement("Name", string.Format("Контроллер {0}.{1}", aRS.Parent.Attribute("Address").Value, addr)),
                    new XElement("MBType", 1),
                    new XElement("Version", 580),
                    new XElement("CodeSize", 3),
                    new XElement("DBParams",
                      new XElement("PinUsed", "false"),
                      new XElement("XBType", "XB32"),
                      new XElement("EvBufCount", 30574),
                      new XElement("MaxCardCount", 33718),
                      new XElement("MaxCardTmCount", 0),
                      new XElement("ExtMemoryUsing", true),
                      new XElement("ALItemCount", 3600),
                      new XElement("TZItemCount", 1800)),
                    new XElement("APB",
                      new XElement("LocalAPBUsed", "false"),
                      new XElement("GlobalAPBUsed", 1),
                      new XElement("NightReset", 0),
                      new XElement("UseTMAPB", "false"),
                      new XElement("TMAPBTime", "false")),
                    new XElement("Options",
                      new XElement("LockCardTime", "false"),
                      new XElement("TamperUsed", 1),
                      new XElement("PWFailUsed", 0),
                      new XElement("CheckBattery", "false"),
                      new XElement("ReaderInterface", "false"),
                      new XElement("UsePINAsN1000", "false"),
                      new XElement("InitDevStatesAfterReset", "false"),
                      new XElement("KeyType", "false"),
                      new XElement("RingPL", "false")),
                    new XElement("UseGUO", "false"),
                    new XElement("UseALwithGUO", "false"),
                    new XElement("CDPIndControlWay", 0),
                    new XElement("ID", ++UniIndex)
                    ));
                aRS.Add(retval);
            }
            return retval;
        }

        private XElement AddDeviceIP(XElement aNG)
        {
            XElement retval = null;
            var addr = GetNewDeviceAddr(aNG);
            if (addr > 0)
            {
                retval = new XElement("Device",
                  new XElement("MBDev",
                    new XElement("Addr", addr),
                    new XElement("Name", string.Format("Контроллер {0}.{1}", "{" + aNG.Attribute("Address").Value + "}", addr)),
                    new XElement("MBType", 1),
                    new XElement("Version", 580),
                    new XElement("CodeSize", 3),
                    new XElement("DBParams",
                      new XElement("PinUsed", "false"),
                      new XElement("XBType", "XB32"),
                      new XElement("EvBufCount", 30574),
                      new XElement("MaxCardCount", 33718),
                      new XElement("MaxCardTmCount", 0),
                      new XElement("ExtMemoryUsing", true),
                      new XElement("ALItemCount", 3600),
                      new XElement("TZItemCount", 1800)),
                    new XElement("APB",
                      new XElement("LocalAPBUsed", "false"),
                      new XElement("GlobalAPBUsed", 1),
                      new XElement("NightReset", 0),
                      new XElement("UseTMAPB", "false"),
                      new XElement("TMAPBTime", "false")),
                    new XElement("Options",
                      new XElement("LockCardTime", "false"),
                      new XElement("TamperUsed", 1),
                      new XElement("PWFailUsed", 0),
                      new XElement("CheckBattery", "false"),
                      new XElement("ReaderInterface", "false"),
                      new XElement("UsePINAsN1000", "false"),
                      new XElement("InitDevStatesAfterReset", "false"),
                      new XElement("KeyType", "false"),
                      new XElement("RingPL", "false")),
                    new XElement("UseGUO", "false"),
                    new XElement("UseALwithGUO", "false"),
                    new XElement("CDPIndControlWay", 0),
                    new XElement("IP",
                      new XElement("EVersion", 516),
                      new XElement("IP", "192.168.127.254"),
                      new XElement("SubnetMask", "255.255.255.0"),
                      new XElement("Gateway", "0.0.0.0"),
                      new XElement("DelayTime", 100),
                      new XElement("TimeoutTime", 300),
                      new XElement("MaxErrCount", 20),
                      new XElement("MaxSendCount", 20),
                      new XElement("ExchangeEnabled", true)
                      ),
                    new XElement("ID", ++UniIndex)
                    ));
                aNG.Add(retval);
            }
            return retval;
        }

        private string GetSubdevXmlTag(ConfigNodeType aConfigNodeType)
        {
            string retval = "";
            switch (aConfigNodeType)
            {
                case ConfigNodeType.cntElsysConfig:
                    retval = "ElsysConfig";
                    break;
                case ConfigNodeType.cntHardware:
                    retval = "Hardware";
                    break;
                case ConfigNodeType.cntMBNet:
                    retval = "MBNet";
                    break;
                case ConfigNodeType.cntRS485:
                    retval = "RS485";
                    break;
                case ConfigNodeType.cntNetGroup:
                    retval = "NetGroup";
                    break;
                case ConfigNodeType.cntDeviceRS:
                    retval = "Device";
                    break;
                case ConfigNodeType.cntDeviceIP:
                    retval = "Device";
                    break;
                case ConfigNodeType.cntInput:
                    retval = "Input";
                    break;
                case ConfigNodeType.cntOut:
                    retval = "Out";
                    break;
                case ConfigNodeType.cntOutGroup:
                    retval = "OutGroup";
                    break;
                case ConfigNodeType.cntDoor:
                    retval = "Door";
                    break;
                case ConfigNodeType.cntTurn:
                    retval = "Turn";
                    break;
                case ConfigNodeType.cntGate:
                    retval = "Gate";
                    break;
                case ConfigNodeType.cntReader:
                    retval = "Reader";
                    break;
                default:
                    break;
            }
            return retval;
        }

        private int GetMaxAddr(ConfigNodeType aConfigNodeType)
        {
            int retval = 0;
            // todo с учетом типа контроллера
            switch (aConfigNodeType)
            {
                case ConfigNodeType.cntMBNet:
                    retval = 254;
                    break;
                case ConfigNodeType.cntRS485:
                    retval = 1;
                    break;
                case ConfigNodeType.cntNetGroup:
                    retval = 254;
                    break;
                case ConfigNodeType.cntDeviceRS:
                    retval = 63;
                    break;
                case ConfigNodeType.cntDeviceIP:
                    retval = 63;
                    break;
                case ConfigNodeType.cntInput:
                    retval = 8;
                    break;
                case ConfigNodeType.cntOut:
                    retval = 4;
                    break;
                case ConfigNodeType.cntOutGroup:
                    retval = 4;
                    break;
                case ConfigNodeType.cntDoor:
                    retval = 4;
                    break;
                case ConfigNodeType.cntTurn:
                    retval = 1;
                    break;
                case ConfigNodeType.cntGate:
                    retval = 1;
                    break;
                case ConfigNodeType.cntReader:
                    retval = 4;
                    break;
                default:
                    break;
            }
            return retval;
        }

        private int GetNewSubdevAddr(XElement aParent, ConfigNodeType aConfigNodeType)
        {
            int retval = 0;
            var UsedAddr = aParent.Elements(GetSubdevXmlTag(aConfigNodeType)).Attributes("Address").ToDictionary(attr => int.Parse(attr.Value));
            var MaxAddr = GetMaxAddr(aConfigNodeType);
            if (UsedAddr != null)
            {
                for (int i = 1; i <= MaxAddr; i++)
                {
                    if (!UsedAddr.ContainsKey(i))
                    {
                        retval = i;
                        break;
                    }
                }
            }
            return retval;
        }

        private string GetSubdevName(ConfigNodeType aConfigNodeType)
        {
            string retval = "";
            switch (aConfigNodeType)
            {
                case ConfigNodeType.cntElsysConfig:
                    retval = "Конфигурация";
                    break;
                case ConfigNodeType.cntHardware:
                    retval = "Конфигурация оборудования";
                    break;
                case ConfigNodeType.cntMBNet:
                    retval = "КСК";
                    break;
                case ConfigNodeType.cntRS485:
                    retval = "Линия связи RS485";
                    break;
                case ConfigNodeType.cntNetGroup:
                    retval = "Сетевая группа";
                    break;
                case ConfigNodeType.cntDeviceRS:
                    retval = "Контроллер";
                    break;
                case ConfigNodeType.cntDeviceIP:
                    retval = "Контроллер";
                    break;
                case ConfigNodeType.cntInput:
                    retval = "Вход";
                    break;
                case ConfigNodeType.cntOut:
                    retval = "Выход";
                    break;
                case ConfigNodeType.cntOutGroup:
                    retval = "Группа выходов";
                    break;
                case ConfigNodeType.cntDoor:
                    retval = "Дверь";
                    break;
                case ConfigNodeType.cntTurn:
                    retval = "Турникет";
                    break;
                case ConfigNodeType.cntGate:
                    retval = "Ворота";
                    break;
                case ConfigNodeType.cntReader:
                    retval = "Считыватель";
                    break;
                default:
                    break;
            }
            return retval;

        }

        private int GetSubdevType(ConfigNodeType aConfigNodeType)
        {
            int retval = 0;
            switch (aConfigNodeType)
            {
                case ConfigNodeType.cntMBNet:
                    retval = 5;
                    break;
                case ConfigNodeType.cntDeviceRS:
                case ConfigNodeType.cntDeviceIP:
                    retval = 5;
                    break;
                case ConfigNodeType.cntInput:
                    retval = 12;
                    break;
                case ConfigNodeType.cntOut:
                case ConfigNodeType.cntOutGroup:
                    retval = 10;
                    break;
                case ConfigNodeType.cntDoor:
                    retval = 3;
                    break;
                case ConfigNodeType.cntTurn:
                    retval = 22;
                    break;
                case ConfigNodeType.cntGate:
                    retval = 4;
                    break;
                case ConfigNodeType.cntReader:
                    retval = 19;
                    break;
                default:
                    break;
            }
            return retval;

        }

        public XElement AddConfigNode(ConfigNodeType aConfigNodeType, XElement aParentNode, XDocument aDeviceConfig, int aNewAddr)
        {
            XElement retval = null;
            switch (aConfigNodeType)
            {
                case ConfigNodeType.cntDeviceRS:
                    retval = AddDeviceRS(aParentNode, aDeviceConfig, aNewAddr);
                    break;
                case ConfigNodeType.cntDeviceIP:
                    retval = AddDeviceIP(aParentNode, aDeviceConfig, aNewAddr);
                    break;
                default:
                    break;
            }

            return retval;
        }

        public XElement AddConfigNode(ConfigNodeType aConfigNodeType, XElement aParentNode)
        {
            XElement retval = null;
            switch (aConfigNodeType)
            {
                case ConfigNodeType.cntMBNet:
                    retval = AddMBNet();
                    break;
                case ConfigNodeType.cntRS485:
                    retval = AddRS485(aParentNode);
                    break;
                case ConfigNodeType.cntNetGroup:
                    retval = AddNetGroup(aParentNode);
                    break;
                case ConfigNodeType.cntDeviceRS:
                    retval = AddDeviceRS(aParentNode);
                    break;
                case ConfigNodeType.cntDeviceIP:
                    retval = AddDeviceIP(aParentNode);
                    break;
                default:
                    break;
            }

            return retval;
        }

        public void DeleteConfigNode(XElement aNode)
        {
            aNode.Remove();
            SaveToFile();
        }

        public void LoadFromFile(string aFileName)
        {
            ConfigPath = System.IO.Path.GetDirectoryName(aFileName);
            ConfigFileName = System.IO.Path.GetFileNameWithoutExtension(aFileName);
            ExportFileName = string.Format("{0}{1}", ConfigFileName, "_SDK.xml");
            ConfigFullPath = aFileName;
            Config = XDocument.Load(ConfigFullPath);
            UniIndex = int.Parse(HWRootElement().Attribute("UNIINDEX").Value);
        }

        public void ExportForSDK()
        {
            XDocument mbnetOPSConfig = null;

            /*
            try
            {
              mbnetOPSConfig = XDocument.Load(@"D:\Views_svn\Hardware\trunk\SKUD_Elsys2_Firmware\DebugUtils\ElsysSDK2\Конфигурации\OPSConfigMN.xml");
            }
            catch (Exception)
            {
              MessageBox.Show("Не удалось открыть файл конфигурации OPSConfigMN.xml");
            }
            */

            var sdkDevTree = ExportForSDK(Config, mbnetOPSConfig, false, false);
            sdkDevTree.Save(String.Format("{0}\\{1}", ConfigPath, ExportFileName));
        }

        public static XElement GetSDKDevices(XDocument aDevTree)
        {
            XElement SDKDevices = new XElement("SDKDevices", "");
            foreach (var device in aDevTree.Root.Descendants("Device"))
            {
                XElement deviceChangeItem = new XElement("Item",
                  new XElement("Action", "InitDevice"),
                  new XElement("DeviceID", device.Element("MBDev").Element("ID").Value),
                  new XElement(device));

                SDKDevices.Add(deviceChangeItem);
            }
            return SDKDevices;
        }

        public static XElement ExportForSDK(XDocument aDevTree, XDocument aOPSConfig, bool aUseCardRetentionEvent, bool aUseMNOPS)
        {
            XElement SDKDevTree = new XElement("SDKDevTree",
              new XElement("ConfigGUID", aDevTree.Root.Element("Hardware").Attribute("GUID").Value.ToString().Replace("-", "")),
              new XElement("CommonSettings",
                new XElement("ExchangeMode", aDevTree.Root.Element("Hardware").Attribute("ExchangeMode").Value),
                new XElement("CardSize", aDevTree.Root.Element("Hardware").Attribute("CardSize").Value),
                new XElement("ExtendedSettings", "")),
                new XElement("MBNets", ""));

            // Список КСК
            foreach (var mbnet in aDevTree.Root.Element("Hardware").Elements("MBNet"))
            {
                if (mbnet.Attribute("MBNetType").Value == "External")
                {
                    XElement mbext = new XElement("MBNet",
                      new XElement("IPAddr", mbnet.Attribute("IP").Value),
                      new XElement("MBNetType", mbnet.Attribute("MBNetType").Value),
                      new XElement("MNAddr", mbnet.Attribute("Address").Value),
                      new XElement("ID", mbnet.Attribute("ID").Value),
                      new XElement("VersionNo", mbnet.Attribute("Version").Value),
                      new XElement("SubnetMask", mbnet.Attribute("SubnetMask").Value));
                    SDKDevTree.Element("MBNets").Add(mbext);
                    continue;
                }

                XElement mb = new XElement("MBNet",
                  new XElement("IPAddr", mbnet.Attribute("IP").Value),
                  new XElement("MBNetType", mbnet.Attribute("MBNetType").Value),
                  new XElement("MNAddr", mbnet.Attribute("Address").Value),
                  new XElement("ID", mbnet.Attribute("ID").Value),
                  new XElement("Name", mbnet.Attribute("Name").Value),
                  new XElement("VersionNo", mbnet.Attribute("Version").Value),
                  new XElement("SubnetMask", mbnet.Attribute("SubnetMask").Value),
                  new XElement("ExchangeEnabled", mbnet.Attribute("ExchangeEnabled").Value.ToLower()),
                  new XElement("TranslateILocks", mbnet.Attribute("TranslateILocks").Value.ToLower()),
                  new XElement("Lines", ""));

                foreach (var rs485 in mbnet.Elements("RS485"))
                {
                    XElement line = new XElement("Line",
                      new XElement("MasterMNAddr", mbnet.Attribute("Address").Value),
                      new XElement("ExchangeMNAddr", mbnet.Attribute("Address").Value),
                      new XElement("ID", rs485.Attribute("ID").Value),
                      new XElement("Name", rs485.Attribute("Name").Value),
                      new XElement("RS485", true),
                      new XElement("BaudRate", rs485.Attribute("BaudRate").Value),
                      new XElement("Multimaster", rs485.Attribute("Multimaster").Value),
                      new XElement("ForceAPB", rs485.Attribute("ForceAPB").Value.ToLower()),
                      new XElement("NoCheckAPB", rs485.Attribute("NoCheckAPB").Value.ToLower()),
                      new XElement("ExtendedSettings",
                        new XElement("TransmissionDelay", rs485.Attribute("TransmissionDelay").Value),
                        new XElement("ReadInterval", rs485.Attribute("ReadInterval").Value),
                        new XElement("ResponseTimeout", rs485.Attribute("ResponseTimeout").Value),
                        new XElement("MaxErrorCount", rs485.Attribute("MaxErrorCount").Value))
                      );
                    mb.Element("Lines").Add(line);
                }

                foreach (var netgroup in mbnet.Elements("NetGroup"))
                {
                    XElement line = new XElement("Line",
                    new XElement("MasterMNAddr", mbnet.Attribute("Address").Value),
                    new XElement("ExchangeMNAddr", netgroup.Attribute("ExchangeMNAddr").Value),
                    new XElement("ID", netgroup.Attribute("ID").Value),
                    new XElement("Name", netgroup.Attribute("Name").Value),
                    new XElement("RS485", false),
                    new XElement("NGAddr", netgroup.Attribute("Address").Value),
                    new XElement("ForceAPB", netgroup.Attribute("ForceAPB").Value.ToLower()),
                    new XElement("NoCheckAPB", netgroup.Attribute("NoCheckAPB").Value.ToLower()),
                    new XElement("ExchangeMode", netgroup.Attribute("ExchangeMode").Value.ToLower())/*,
          new XElement("ExtendedSettings",
            new XElement("TransmissionDelay", netgroup.Attribute("TransmissionDelay").Value),
            new XElement("ResponseTimeout", netgroup.Attribute("ResponseTimeout").Value))*/
                    );
                    mb.Element("Lines").Add(line);
                }

                SDKDevTree.Element("MBNets").Add(mb);
            }

            //Список линий СГ
            var lines = new XElement("Lines", "");
            foreach (var netgroup in aDevTree.Root.Element("Hardware").Elements("NetGroup"))
            {
                XElement line = new XElement("Line",
                new XElement("MasterMNAddr", 0),
                new XElement("ExchangeMNAddr", netgroup.Attribute("ExchangeMNAddr").Value),
                new XElement("ID", netgroup.Attribute("ID").Value),
                new XElement("Name", netgroup.Attribute("Name").Value),
                new XElement("RS485", false),
                new XElement("NGAddr", netgroup.Attribute("Address").Value),
                new XElement("ForceAPB", netgroup.Attribute("ForceAPB").Value.ToLower()),
                new XElement("NoCheckAPB", netgroup.Attribute("NoCheckAPB").Value.ToLower()),
                new XElement("ExchangeMode", netgroup.Attribute("ExchangeMode").Value.ToLower()));
                lines.Add(line);
            }

            if (lines.Elements().Count() > 0)
                SDKDevTree.Add(lines);

            // Список контроллеров
            var devices = new XElement("Devices", "");
            foreach (var mbnet in aDevTree.Root.Element("Hardware").Elements("MBNet"))
            {
                if (mbnet.Attribute("MBNetType").Value == "External")
                    continue;

                foreach (var rs485 in mbnet.Elements("RS485"))
                {
                    foreach (var device in rs485.Elements("Device"))
                    {
                        XElement Device;
                        var mbType = int.Parse(device.Element("MBDev").Element("MBType").Value);

                        Device = new XElement("Device",
                          new XElement("ID", device.Element("MBDev").Element("ID").Value),
                          new XElement("Name", device.Element("MBDev").Element("Name").Value),
                          new XElement("CUAddr", device.Element("MBDev").Element("Addr").Value),
                          new XElement("LineID", rs485.Attribute("ID").Value),
                          new XElement("HWType", device.Element("MBDev").Element("MBType").Value),
                          new XElement("VersionNo", device.Element("MBDev").Element("Version").Value));

                        if (mbType <= 3)
                        {
                            Device.Add(
                              new XElement("LocalAPBUsed", device.Element("MBDev").Element("APB").Element("LocalAPBUsed").Value),
                              new XElement("GlobalAPBUsed", device.Element("MBDev").Element("APB").Element("GlobalAPBUsed").Value),
                              new XElement("NightReset", device.Element("MBDev").Element("APB").Element("NightReset").Value),
                              new XElement("UseTMAPB", device.Element("MBDev").Element("APB").Element("UseTMAPB").Value),
                              new XElement("TMAPBTime", device.Element("MBDev").Element("APB").Element("TMAPBTime").Value));
                        }

                        var subdevs = new XElement("SubDevices", "");
                        subdevs.Add(GetSubdevsForSDK(device, aUseCardRetentionEvent, aUseMNOPS).Elements());
                        Device.Add(subdevs);

                        devices.Add(Device);
                    }
                }

                foreach (var netgroup in mbnet.Elements("NetGroup"))
                {
                    foreach (var device in netgroup.Elements("Device"))
                    {
                        XElement Device;
                        var mbType = int.Parse(device.Element("MBDev").Element("MBType").Value);
                        Device = new XElement("Device",
                          new XElement("ID", device.Element("MBDev").Element("ID").Value),
                          new XElement("Name", device.Element("MBDev").Element("Name").Value),
                          new XElement("CUAddr", device.Element("MBDev").Element("Addr").Value),
                          new XElement("LineID", netgroup.Attribute("ID").Value),
                          new XElement("HWType", device.Element("MBDev").Element("MBType").Value),
                          new XElement("VersionNo", device.Element("MBDev").Element("Version").Value),
                          new XElement("IPAddr", device.Element("IP").Element("IP").Value),
                          new XElement("SubnetMask", device.Element("IP").Element("SubnetMask").Value),
                          new XElement("EVersionNo", device.Element("IP").Element("EVersion").Value),
                          new XElement("ExchangeEnabled", device.Element("IP").Element("ExchangeEnabled").Value));

                        if (mbType <= 3)
                        {
                            Device.Add(
                              new XElement("LocalAPBUsed", device.Element("MBDev").Element("APB").Element("LocalAPBUsed").Value),
                              new XElement("GlobalAPBUsed", device.Element("MBDev").Element("APB").Element("GlobalAPBUsed").Value),
                              new XElement("NightReset", device.Element("MBDev").Element("APB").Element("NightReset").Value),
                              new XElement("UseTMAPB", device.Element("MBDev").Element("APB").Element("UseTMAPB").Value),
                              new XElement("TMAPBTime", device.Element("MBDev").Element("APB").Element("TMAPBTime").Value));
                        }

                        var subdevs = new XElement("SubDevices", "");
                        subdevs.Add(GetSubdevsForSDK(device, aUseCardRetentionEvent, aUseMNOPS).Elements());
                        Device.Add(subdevs);

                        devices.Add(Device);
                    }
                }
            }


            foreach (var netgroup in aDevTree.Root.Element("Hardware").Elements("NetGroup"))
            {
                foreach (var device in netgroup.Elements("Device"))
                {
                    XElement Device;
                    var mbType = int.Parse(device.Element("MBDev").Element("MBType").Value);
                    Device = new XElement("Device",
                      new XElement("ID", device.Element("MBDev").Element("ID").Value),
                      new XElement("Name", device.Element("MBDev").Element("Name").Value),
                      new XElement("CUAddr", device.Element("MBDev").Element("Addr").Value),
                      new XElement("LineID", netgroup.Attribute("ID").Value),
                      new XElement("HWType", device.Element("MBDev").Element("MBType").Value),
                      new XElement("VersionNo", device.Element("MBDev").Element("Version").Value),
                      new XElement("IPAddr", device.Element("IP").Element("IP").Value),
                      new XElement("SubnetMask", device.Element("IP").Element("SubnetMask").Value),
                      new XElement("EVersionNo", device.Element("IP").Element("EVersion").Value),
                      new XElement("ExchangeEnabled", device.Element("IP").Element("ExchangeEnabled").Value));

                    if (mbType <= 3)
                    {
                        Device.Add(
                          new XElement("LocalAPBUsed", device.Element("MBDev").Element("APB").Element("LocalAPBUsed").Value),
                          new XElement("GlobalAPBUsed", device.Element("MBDev").Element("APB").Element("GlobalAPBUsed").Value),
                          new XElement("NightReset", device.Element("MBDev").Element("APB").Element("NightReset").Value),
                          new XElement("UseTMAPB", device.Element("MBDev").Element("APB").Element("UseTMAPB").Value),
                          new XElement("TMAPBTime", device.Element("MBDev").Element("APB").Element("TMAPBTime").Value));
                    }

                    var subdevs = new XElement("SubDevices", "");
                    subdevs.Add(GetSubdevsForSDK(device, aUseCardRetentionEvent, aUseMNOPS).Elements());
                    Device.Add(subdevs);

                    devices.Add(Device);
                }
            }

            SDKDevTree.Add(devices);

            //Список разделов
            if (aOPSConfig != null)
            {
                //Список разделов
                var MNParts = new XElement("MNParts");
                foreach (var item in aOPSConfig.Root.Element("MBNets").Elements("MBNet"))
                {
                    var mbnet = new XElement("MBNet",
                      item.Element("MBNetID"),
                      new XElement("Parts", ""));

                    if (item.Element("GlobalParts") != null)
                    {
                        foreach (var part in item.Element("GlobalParts").Elements("GlobalPart"))
                        {
                            mbnet.Element("Parts").Add(new XElement("Part", part.Element("PartID"), part.Element("Name")));
                        }
                    }

                    if (mbnet.Element("Parts").Elements().Count() > 0)
                        MNParts.Add(mbnet);
                }
                if (MNParts.Elements().Count() > 0)
                    SDKDevTree.Add(MNParts);


                //Список групп разделов
                var MNPartGroups = new XElement("MNPartGroups");

                foreach (var item in aOPSConfig.Root.Element("MBNets").Elements("MBNet"))
                {
                    var mbnet = new XElement("MBNet",
                      item.Element("MBNetID"),
                      new XElement("Groups", ""));

                    if (item.Element("PartGroups") != null)
                    {
                        foreach (var part in item.Element("PartGroups").Elements("PartGroup"))
                        {
                            mbnet.Element("Groups").Add(new XElement("Group", part.Element("GroupID"), part.Element("Name")));
                        }
                    }

                    if (mbnet.Element("Groups").Elements().Count() > 0)
                        MNPartGroups.Add(mbnet);

                }
                if (MNPartGroups.Elements().Count() > 0)
                    SDKDevTree.Add(MNPartGroups);
            }

            return SDKDevTree;
        }
        private static XElement GetSubdevsForSDK(XElement aDevice, bool aUseCardRetentionEvent, bool aUseMNOPS)
        {
            var retval = new XElement("SubDevices", "");

            foreach (var items in aDevice.Elements())
            {
                int devtype = 0;
                if (items.Name == "Inputs") devtype = DevTypes.dtZone;
                if (items.Name == "Outs") devtype = DevTypes.dtOut;
                if (items.Name == "OutGroups") devtype = DevTypes.dtOut;
                if (items.Name == "Doors") devtype = DevTypes.dtDoor;
                if (items.Name == "Readers") devtype = DevTypes.dtReader;
                if (items.Name == "Parts") devtype = DevTypes.dtPart;

                if (devtype > 0)
                    foreach (var item in items.Elements())
                    {
                        if (items.Name == "Doors")
                        {
                            if (item.Element("DoorType") != null)
                            {
                                int doorType = int.Parse(item.Element("DoorType").Value);
                                if (doorType == 1)
                                    devtype = DevTypes.dtTurn;
                                else
                                if (doorType == 2)
                                    devtype = DevTypes.dtGate;
                            }
                        }

                        var subdev = new XElement("SubDevice",
                              new XElement("ID", item.Element("ID").Value),
                              new XElement("Name", item.Element("Name").Value),
                              new XElement("Addr", item.Element("Addr").Value),
                              new XElement("DevType", devtype)
                          );

                        if (items.Name == "Readers")
                        {
                            if (item.Element("ParentDoor") != null)
                                subdev.Add(new XElement("ParentDoor", item.Element("ParentDoor").Value));
                            if (item.Element("ReaderRole") != null)
                                subdev.Add(new XElement("IsOutReader", item.Element("ReaderRole").Value));
                            subdev.Add(new XElement("UseCardRetentionEvent", aUseCardRetentionEvent));
                            subdev.Add(new XElement("UseMNOPS", aUseMNOPS));
                        }

                        if (items.Name == "OutGroups")
                        {
                            subdev.Add(new XElement("IsGroup", 1));
                        }

                        retval.Add(subdev);
                    }
            }
            return retval;
        }

        public void UpdateDevAddr(XDocument aDeviceConfig, string aMBNetAddr, string aMBAddr)
        {
            aDeviceConfig.Root.Element("MBDev").Element("Addr").Value = aDeviceConfig.Root.Element("MBDev").Element("Addr").Value.Replace("%amb%", aMBAddr);
            aDeviceConfig.Root.Element("MBDev").Element("Name").Value = aDeviceConfig.Root.Element("MBDev").Element("Name").Value.Replace("%ambn%", aMBNetAddr).Replace("%amb%", aMBAddr);
            aDeviceConfig.Root.Element("MBDev").Add(new XElement("ID", ++UniIndex));

            if (aDeviceConfig.Root.Element("Inputs") != null)
                foreach (var input in aDeviceConfig.Root.Element("Inputs").Elements("Input"))
                {
                    input.Element("Name").Value = input.Element("Name").Value.Replace("%ambn%", aMBNetAddr.ToString()).Replace("%amb%", aMBAddr.ToString());
                    input.Add(new XElement("ID", ++UniIndex));
                }

            if (aDeviceConfig.Root.Element("Outs") != null)
                foreach (var _out in aDeviceConfig.Root.Element("Outs").Elements("Out"))
                {
                    _out.Element("Name").Value = _out.Element("Name").Value.Replace("%ambn%", aMBNetAddr.ToString()).Replace("%amb%", aMBAddr.ToString());
                    _out.Add(new XElement("ID", ++UniIndex));
                }

            if (aDeviceConfig.Root.Element("OutGroups") != null)
                foreach (var outgroup in aDeviceConfig.Root.Element("OutGroups").Elements("OutGroup"))
                {
                    outgroup.Element("Name").Value = outgroup.Element("Name").Value.Replace("%ambn%", aMBNetAddr.ToString()).Replace("%amb%", aMBAddr.ToString());
                    outgroup.Add(new XElement("ID", ++UniIndex));
                }

            if (aDeviceConfig.Root.Element("Doors") != null)
                foreach (var door in aDeviceConfig.Root.Element("Doors").Elements("Door"))
                {
                    door.Element("Name").Value = door.Element("Name").Value.Replace("%ambn%", aMBNetAddr.ToString()).Replace("%amb%", aMBAddr.ToString());
                    door.Add(new XElement("ID", ++UniIndex));
                }

            if (aDeviceConfig.Root.Element("Readers") != null)
                foreach (var reader in aDeviceConfig.Root.Element("Readers").Elements("Reader"))
                {
                    reader.Element("Name").Value = reader.Element("Name").Value.Replace("%ambn%", aMBNetAddr.ToString()).Replace("%amb%", aMBAddr.ToString());
                    reader.Add(new XElement("ID", ++UniIndex));
                }

            if (aDeviceConfig.Root.Element("Parts") != null)
                foreach (var part in aDeviceConfig.Root.Element("Parts").Elements("Part"))
                {
                    part.Element("Name").Value = part.Element("Name").Value.Replace("%ambn%", aMBNetAddr.ToString()).Replace("%amb%", aMBAddr.ToString());
                    part.Add(new XElement("ID", ++UniIndex));
                }

        }
    }
}
