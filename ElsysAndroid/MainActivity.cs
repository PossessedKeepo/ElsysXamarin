using System;
using System.Linq;
using System.Xml.Linq;
using Android.App;
using Android.OS;
using Android.Support.V7.App;
using Android.Views;
using Android.Widget;
using DutyPoll;

namespace ElsysAndroid
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme.NoActionBar", MainLauncher = true)]
    public class MainActivity : AppCompatActivity
    {
        private TPollTask PollTask;
        private XDocument aDevTree;

        private string configFolder = "Configs";
        private string configFile = "Configs/ElsysConfig.xml";
        private string opsConfigFile = "Configs/OPSConfigMN.xml";

        private string serverIp = "192.168.1.21";
        private string password = "12345678";

        private bool chLog = false;
        private bool chShowDevStates = false;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_main);

            PollTask = new TPollTask(Assets);

            Android.Support.V7.Widget.Toolbar toolbar = FindViewById<Android.Support.V7.Widget.Toolbar>(Resource.Id.toolbar);
            SetSupportActionBar(toolbar);
            FindViewById<Button>(Resource.Id.start_button).Click += Start;
            FindViewById<Button>(Resource.Id.impulse_button).Click += Impulse;
        }

        public override bool OnCreateOptionsMenu(IMenu menu)
        {
            MenuInflater.Inflate(Resource.Menu.menu_main, menu);
            return true;
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            int id = item.ItemId;
            if (id == Resource.Id.action_settings)
            {
                return true;
            }

            return base.OnOptionsItemSelected(item);
        }

        private void SendCommand(int aID, int aDevtype, int aCommand)
        {
            PollTask.SendCommand(aID, aDevtype, aCommand);
        }

        private void Impulse(object sender, EventArgs eventArgs)
        {
            var Outs = aDevTree.Root.Descendants("Outs").Elements("Out").ToList();
            SendCommand(int.Parse(Outs[0].Element("ID").Value), HWConfig.ElsysConfig.DevTypes.dtOut, ElsysSDK2.ElsysCommands.Outs.Impulse);
        }

        private void Start(object sender, EventArgs eventArgs)
        {
            try
            {
                var DevTree = XDocument.Load(Assets.Open(configFile));
                var mbnetOPSConfig = XDocument.Load(Assets.Open(opsConfigFile));

                if (PollTask.Start(configFolder, serverIp, password, DevTree, mbnetOPSConfig, chLog, chShowDevStates))
                {
                    //tbStart.Enabled = false;
                    //tbStop.Enabled = true;
                    //miInit.Enabled = true;
                    //tbClear.Enabled = true;
                    //LoadListsForControl(DevTree, mbnetOPSConfig);
                    aDevTree = DevTree;
                }
                else
                {
                    PollTask = null;
                }
            }
            catch (Exception)
            {
                //MessageBox.Show("Не удалось открыть файл конфигурации!");
            }
        }
    }
}