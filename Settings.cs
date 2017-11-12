using System.IO;
using System.Xml.Serialization;
using Torch;
using System;
using System.Collections.Generic;
namespace ServerJump
{
    public class Settings : ViewModel
    {
        private bool _enabled = true;
        private string _typeid = "Reactor";
        private string _subtypeid = "na";
        [System.Xml.Serialization.XmlIgnoreAttribute]
        private ulong _checkInterval = 1739;

        [System.Xml.Serialization.XmlIgnoreAttribute]
        public DateTime CommandRunTime;

        public DateTime LastExecCommandTime;
        private string _commandTime = "21:21";
        private ulong _damageamount = 1;



     
        public List<string> BattleIPs = new List<string> { "1.2.3.4" };
        public int BattleTime;
        public bool Hub;
        public string HubIP;
        public int JoinTime;
        public int MaxBlockCount;
        public int MaxPlayerCount;

        public string Password;

        public double SpawnRadius;


        public string CommandTime
        {
            get => _commandTime;
            set
            {   _commandTime = value;
                CommandRunTime = DateTime.Parse(_commandTime);
            }
        }
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }

        public ulong DamageAmount
        {
            get => _damageamount;
            set { _damageamount = value; OnPropertyChanged(); }
        }

        public ulong CheckInterval //1 min?
        {
            get => _checkInterval;
            set { _checkInterval = value; OnPropertyChanged(); }
        }

        public string TypeID
        {
            get => _typeid;
            set { _typeid = value; OnPropertyChanged(); }
        }

        public string SubTypeID
        {
            get => _subtypeid;
            set { _subtypeid = value; OnPropertyChanged(); }
        }

        public void Save(string path)
        {
            var xmlSerializer = new XmlSerializer(typeof(Settings));
            using (var fileStream = File.Open(path, FileMode.OpenOrCreate))
            {
                xmlSerializer.Serialize(fileStream, this);
            }
        }

        public static Settings LoadOrCreate(string path)
        {
            if (!File.Exists(path))
                return new Settings();

            var xmlSerializer = new XmlSerializer(typeof(Settings));
            Settings result;
            using (var fileStream = File.OpenRead(path))
            {
                result = (Settings)xmlSerializer.Deserialize(fileStream);
            }
            return result;
        }
    }
}