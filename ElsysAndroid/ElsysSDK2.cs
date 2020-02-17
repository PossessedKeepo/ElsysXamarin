using System;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Xml.Linq;
using Android.Content.Res;

namespace ElsysSDK2
{
    public static class Protocol
    {
        public const string URL = "/xmlapi/std";
        public static string GetNonce()
        {
            byte[] nonce = new byte[20];
            Random random = new Random();
            random.NextBytes(nonce);
            return Convert.ToBase64String(nonce);
        }

        public static byte[] GetContentClearConfig(uint aCID, uint aSIDResp)
        {
            var Content = new XElement("Envelope", new XElement("Body",
              new XElement("ClearSDKMode", true),
              new XElement("CID", aCID),
              new XElement("SIDResp", aSIDResp)));
            return Encoding.UTF8.GetBytes(Content.ToString());
        }

        public static byte[] GetContent(uint aCID, uint aSIDResp)
        {
            var Content = new XElement("Envelope", new XElement("Body", new XElement("CID", aCID), new XElement("SIDResp", aSIDResp)));
            return Encoding.UTF8.GetBytes(Content.ToString());
        }

        public static byte[] GetContent(uint aCID, uint aSIDResp, DateTime aNow)
        {
            var Content = new XElement("Envelope", new XElement("Body",
              new XElement("CID", aCID),
              new XElement("SIDResp", aSIDResp),
              new XElement("SetDateTime",
                new XElement("LocalTime", aNow.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")),
                new XElement("UTCTime", aNow.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")))
              ));
            return Encoding.UTF8.GetBytes(Content.ToString());
        }

        public static byte[] GetContent(uint aCID, uint aSIDResp, XElement aInitData)
        {
            var Content = new XElement("Envelope", new XElement("Body",
              aInitData,
              new XElement("CID", aCID),
              new XElement("SIDResp", aSIDResp)
              ));
            return Encoding.UTF8.GetBytes(Content.ToString());
        }

        public static XElement GetXContentClearChanges(uint aCID, uint aSIDResp)
        {
            var Content = new XElement("Envelope", new XElement("Body",
              new XElement("BreakChanges", true),
              new XElement("CID", aCID),
              new XElement("SIDResp", aSIDResp)));
            return Content;
        }

        public static XElement GetXContentControlCmds(uint aCID, uint aSIDResp, XElement aControlCmds)
        {
            var Content = new XElement("Envelope", new XElement("Body",
              new XElement("CID", aCID),
              new XElement("SIDResp", aSIDResp),
              new XElement(aControlCmds)));
            return Content;
        }

        public static XElement GetXContentClearConfig(uint aCID, uint aSIDResp)
        {
            var Content = new XElement("Envelope", new XElement("Body",
              new XElement("ClearSDKMode", true),
              new XElement("CID", aCID),
              new XElement("SIDResp", aSIDResp)));
            return Content;
        }

        public static XElement GetXContent(uint aCID, uint aSIDResp)
        {
            var Content = new XElement("Envelope", new XElement("Body", new XElement("CID", aCID), new XElement("SIDResp", aSIDResp)));
            return Content;
        }

        public static XElement GetXContent(uint aCID, uint aSIDResp, DateTime aNow)
        {
            var Content = new XElement("Envelope", new XElement("Body",
              new XElement("CID", aCID),
              new XElement("SIDResp", aSIDResp),
              new XElement("SetDateTime",
                new XElement("LocalTime", aNow.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")),
                new XElement("UTCTime", aNow.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")))
              ));
            return Content;
        }

        public static XElement GetXContent(uint aCID, uint aSIDResp, XElement aInitData)
        {
            var Content = new XElement("Envelope", new XElement("Body",
              aInitData,
              new XElement("CID", aCID),
              new XElement("SIDResp", aSIDResp)
              ));
            return Content;
        }

        public static string GetDigest(string aNonce, string aPassword, byte[] aContent, string aCreationTime)
        {
            byte[] SHA1Buffer = Convert.FromBase64String(aNonce);
            SHA1Buffer = SHA1Buffer.Concat(Encoding.ASCII.GetBytes(aCreationTime)).ToArray();
            SHA1Buffer = SHA1Buffer.Concat(Encoding.ASCII.GetBytes("POST")).ToArray();
            SHA1Buffer = SHA1Buffer.Concat(Encoding.ASCII.GetBytes(Protocol.URL)).ToArray();
            SHA1Buffer = SHA1Buffer.Concat(aContent).ToArray();

            HMACSHA1 SHA1 = new HMACSHA1();
            SHA1.Key = Encoding.ASCII.GetBytes(aPassword);
            byte[] Digest = SHA1.ComputeHash(SHA1Buffer);

            return Convert.ToBase64String(Digest);
        }
    }

    public class ElsysEvents
    {
        private XDocument elsysEvents;

        public ElsysEvents(AssetManager assets)
        {
            elsysEvents = XDocument.Load(assets.Open("ElsysEvents.xml"));
        }

        public string GetMessageText(int aDevType, int aMessageCode)
        {
            string retval = "";

            foreach (var devtype in elsysEvents.Root.Elements("devType"))
            {
                if (int.Parse(devtype.Attribute("id").Value) == aDevType)
                {
                    foreach (var message in devtype.Elements("message"))
                    {
                        if (int.Parse(message.Attribute("code").Value) == aMessageCode)
                            retval = message.Attribute("text").Value;
                    }
                }
            }
            return retval;
        }
    }

    public class ElsysStates
    {
        private XDocument elsysStates;

        public ElsysStates(AssetManager assets)
        {
            elsysStates = XDocument.Load(assets.Open("ElsysStates.xml"));
        }
        public string GetStateText(int aDevType, int aStateCode)
        {
            string retval = "";

            foreach (var devtype in elsysStates.Root.Elements("devType"))
            {
                if (int.Parse(devtype.Attribute("id").Value) == aDevType)
                {
                    foreach (var message in devtype.Elements("state"))
                    {
                        if (int.Parse(message.Attribute("code").Value) == aStateCode)
                            retval = message.Attribute("text").Value;
                    }
                }
            }
            return retval;
        }
    }



    public static class ElsysCommands
    {
        public static class Doors
        {
            public static int Open = 0;
            public static int Lock = 1;
            public static int Normal = 2;
            public static int Unlock = 3;
            public static int Confirm = 4; // программная реализация
            public static int Deny = 5; // программная реализация
        }

        public static class Turnstiles
        {
            public static int OpenInp = 0;
            public static int LockInp = 1;
            public static int NormalInp = 2;
            public static int UnlockInp = 3;
            public static int OpenOut = 4;
            public static int LockOut = 5;
            public static int NormalOut = 6;
            public static int UnlockOut = 7;
            public static int LockAll = 8;
            public static int NormalAll = 9;
            public static int UnlockAll = 10;
            public static int Confirm = 11;// программная реализация
            public static int Deny = 12;// программная реализация
        }

        public static class Gates
        {
            public static int Open = 0;   // 0
            public static int Lock = 1;   // 1
            public static int Normal = 2; // 2
            public static int Close = 4;  // 4
            public static int Stop = 5;   // 5
            public static int Confirm = 6;       // программная реализация
            public static int Deny = 7;       // программная реализация
        }

        public static class Outs
        {
            public static int SwitchOff = 0;
            public static int Impulse = 1;
            public static int SwitchOn = 2;
            public static int Invert = 3;
        }

        public static class Readers
        {
            public static int Lock = 0; //0
            public static int Normal = 1; // 1
            public static int Restrict = 2; // 2
            public static int Unrestrict = 3;  //3
        }

        public static class Zones
        {
            public static int Disarm = 0; //0
            public static int Arm = 1; // 1
        }

        public static class Parts
        {
            public static int Disarm = 0; //0
            public static int Arm = 1; // 1
        }

        public static class Devices
        {
            public static int Reset = 0; //0
            public static int ResetAPB = 1; // 1
            public static int ClearConfig = 2; // 2

            public static int ResetState = 4; // 4
            public static int ResetPersCounter = 5; // 5

            public static int GetStates = 100;
            public static int GetNumericalInfo = 101;
            public static int RestoreEvents = 102;
        }
    }
}
