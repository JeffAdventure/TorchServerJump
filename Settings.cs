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
        private string _password;
        private double _spawnradius;


        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }


        public string Password
        {
            get => _password;
            set { _password = value; OnPropertyChanged(); }
        }

        public double SpawnRadius
        {
            get => _spawnradius;
            set { _spawnradius = value; OnPropertyChanged(); }
        }
    }
}