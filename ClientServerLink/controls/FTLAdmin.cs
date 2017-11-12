using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.Components;
using VRage.Game;

// This handles server administrative functions
// Some of this code was take from Midspace's Admin helper
namespace Phoenix.FTL
{
    public enum OnOffTriState
    {
        On,
        Off,
        Unset
    }

    public enum ChangeType
    {
        Base,
        Upgrade,
    }

    /* If any of these are matched to upgrade modules, the name must match exactly! */
    public enum ModifierType
    {
        Unset,
        #region Upgrade modules as in Cubeblocks.sbc
        // The names in the region must match exactly with the <UpgradeType> in CubeBlocks.sbc!
        InhibitorRange,
        Spool,
        Accuracy,
        PowerEfficiency,
        Range,
        #endregion
        InhibitorPowerEfficiency,
        LargeRange,
        SmallRange,
        FTLMedFactor,
        FTLSmlFactor,
        SmallMass,
        LargeMass,
        MaxSpool,
        MaxCooldown,
        CooldownMultiplier,
    }

    /// <summary>
    /// This is the mod configuration data stored on disk
    /// </summary>
    public class FTLConfig
    {
        public bool Debug = false;
        public bool BlockStockJump = true;
        public bool AllowPBControl = true;
        public bool AllowGravityWellJump = true;
        public bool FixedFontSize = true;
        public List<MyTuple<ModifierType, float>> BaseValues = new List<MyTuple<ModifierType, float>>();
        public List<MyTuple<ModifierType, float>> Upgrades = new List<MyTuple<ModifierType, float>>();
    }

    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    class FTLAdmin : MySessionComponentBase
    {
        #region Static data
        static bool _isInitialized = false;
        public static FTLAdmin Instance {get; private set;}
        static FTLConfig _config = null;
        public static FTLConfig Configuration
        {
            get
            {
                if (_config == null)
                    _config = new FTLConfig();
                return _config;
            }
            private set
            {
                _config = value;
            }
        }
        #endregion

        private const string ConfigFileName = "Config_{0}.cfg";

        static FTLAdmin()
        {
            _config = new FTLConfig();
        }

        public FTLAdmin()
        {
            Instance = this;
        }

        private void Init()
        {
            _isInitialized = true;
            LoadConfig();
            Logger.Instance.LogDebug("FTLAdmin.Init()");
            MyAPIGateway.Utilities.MessageEntered += Utilities_MessageEntered;
            MyAPIGateway.Multiplayer.RegisterMessageHandler(MessageUtils.MessageId, MessageUtils.HandleMessage);
        }

        public override void LoadData()
        {
            Logger.Instance.Init("FTL");
            Logger.Instance.LogDebug("FTLAdmin.LoadData()");
        }

        public override void UpdateBeforeSimulation()
        {
            if (!_isInitialized && MyAPIGateway.Session != null)
                Init();
        }

        protected override void UnloadData()
        {
            try
            {
                MyAPIGateway.Utilities.MessageEntered -= Utilities_MessageEntered;
                MyAPIGateway.Multiplayer.UnregisterMessageHandler(MessageUtils.MessageId, MessageUtils.HandleMessage);
                Globals.Unload();
            }
            catch (Exception ex) { Logger.Instance.LogException(ex); }

        }

        public override void SaveData()
        {
            SaveConfig();
        }

        static void Utilities_MessageEntered(string messageText, ref bool sendToOthers)
        {
            if (ProcessClientMessage(messageText))
                sendToOthers = false;
        }

        public static bool ProcessClientMessage(string messageText)
        {
            if (!_isInitialized || string.IsNullOrEmpty(messageText))
                return false;

            string invalidOptionErrorText = "Invalid argument supplied. Type /" + Globals.ModName.ToLowerInvariant() + " help to show valid commands and options";

            Logger.Instance.LogDebug("Processing message");

            var commands = messageText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (commands.Length == 0)
                return false;

            var match = Regex.Match(messageText, @"(/" + Globals.ModName.ToLowerInvariant() + @")\s+(?<Key>[^\s]+)((\s+(?<Value>.+))|)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var key = match.Groups["Key"].Value;
                var value = match.Groups["Value"].Value.Trim();

                if (!MyAPIGateway.Session.Player.IsAdmin())
                {
                    MyAPIGateway.Utilities.ShowMessage(Globals.ModName, "You must be a server admin to use this.");
                    return true;
                }

                bool bval = false;
                OnOffTriState onoffval = OnOffTriState.Unset;

                switch (key.ToLowerInvariant())
                {
                    case "upgrades":
                    case "upgrade":
                        // This sets the upgrade values (overrides cubeblocks.sbc)
                        if (!HandleValueChange(ChangeType.Upgrade, value))
                            MyAPIGateway.Utilities.ShowMessage(Globals.ModName, invalidOptionErrorText);
                        break;
                    case "base":
                    case "starting":
                    case "set":
                        // This sets the upgrade values (overrides cubeblocks.sbc)
                        if (value.ToLowerInvariant().Contains("help"))
                        {
                            MyAPIGateway.Utilities.ShowMissionScreen(Globals.ModName + " Admin Help",
                                "/" + Globals.ModName.ToLowerInvariant() + " " + key.ToLowerInvariant() + " <Type> <Modifier>|reset", "",
                                "<type> for base command:\r\n" + 
                                "Accuracy - Starting accuracy value\r\n" +
                                "Spool - Spool multiplier\r\n" +
                                "Range - Range multiplier (in meters)\r\n" +
                                "LargeRange - Starting range for Large ships (meters)\r\n" +
                                "SmallRange - Starting range for Small ships (meters)\r\n" + 
                                "PowerEfficiency - Power multiplier\r\n" + 
                                "InhibitorRange - Starting range for the inhibitor\r\n" + 
                                "InhibitorPowerEfficiency - Power multiplier for the inhibitor\r\n" +
                                "MaxSpool - Absolute max spooling time (seconds)\r\n" +
                                "MaxCooldown - Absolute max cooldown time (seconds)\r\n" +
                                "CooldownMultiplier - Spool time * this is CD\r\n" +
                                "LargeMass - Large grid mass for power calculations (kg)\r\n" +
                                "SmallMass - Small grid mass for power calculations (kg)\r\n" +
                                "\r\n" +
                                //"LargeRange, SmallRange, and InhibitorRange are in meters, all others are percentage as a decimal (0.5)\r\n" +
                                "Use 'reset' as the modifier to reset the value to the default." +
                                ""
                                );
                            break;
                        }
                        if (!HandleValueChange(ChangeType.Base, value))
                            MyAPIGateway.Utilities.ShowMessage(Globals.ModName, invalidOptionErrorText);
                        break;
                    case "fixedfont":
                        bval = true;
                        onoffval = OnOffTriState.Unset;
                        if (bool.TryParse(value, out bval) || OnOffTriState.TryParse(value, true, out onoffval))
                        {
                            if (onoffval != OnOffTriState.Unset)
                                bval = onoffval == OnOffTriState.On ? true : false;

                            var message = new MessageFont() { FixedFontSize = bval };
                            MessageUtils.SendMessageToServer(message);
                        }
                        else
                            MyAPIGateway.Utilities.ShowMessage(Globals.ModName, invalidOptionErrorText);
                        break;
                    case "blockstockjump":
                    case "inhibitstockjump":
                    case "blockstock":
                    case "inhibitstock":
                        bval = true;
                        onoffval = OnOffTriState.Unset;
                        if (bool.TryParse(value, out bval) || OnOffTriState.TryParse(value, true, out onoffval))
                        {
                            if (onoffval != OnOffTriState.Unset)
                                bval = onoffval == OnOffTriState.On ? true : false;

                            var message = new MessageStockJump() { BlockStockJump = bval };
                            MessageUtils.SendMessageToServer(message);
                        }
                        else
                            MyAPIGateway.Utilities.ShowMessage(Globals.ModName, invalidOptionErrorText);
                        break;
                    case "gravitywell":
                    case "gravitywelljump":
                    case "allowgravitywell":
                    case "allowgravitywelljump":
                    case "planetjump":
                    case "allowplanetjump":
                        bval = true;
                        onoffval = OnOffTriState.Unset;
                        if (bool.TryParse(value, out bval) || OnOffTriState.TryParse(value, true, out onoffval))
                        {
                            if (onoffval != OnOffTriState.Unset)
                                bval = onoffval == OnOffTriState.On ? true : false;

                            var message = new MessageGravityWell() { AllowGravityWellJump = bval };
                            MessageUtils.SendMessageToServer(message);
                        }
                        else
                            MyAPIGateway.Utilities.ShowMessage(Globals.ModName, invalidOptionErrorText);
                        break;
                    case "allowpbjump":
                    case "pbjump":
                    case "allowpbcontrol":
                    case "pbcontrol":
                        bval = true;
                        onoffval = OnOffTriState.Unset;
                        if (bool.TryParse(value, out bval) || OnOffTriState.TryParse(value, true, out onoffval))
                        {
                            if (onoffval != OnOffTriState.Unset)
                                bval = onoffval == OnOffTriState.On ? true : false;

                            var message = new MessagePBControl() { AllowPBControl = bval };
                            MessageUtils.SendMessageToServer(message);
                        }
                        else
                            MyAPIGateway.Utilities.ShowMessage(Globals.ModName, invalidOptionErrorText);
                        break;
                    case "debug":
                        bool debug = true;
                        OnOffTriState dbg = OnOffTriState.Unset;
                        if (bool.TryParse(value, out debug) || OnOffTriState.TryParse(value, true, out dbg))
                        {
                            if (dbg != OnOffTriState.Unset)
                                debug = dbg == OnOffTriState.On ? true : false;

                            var message = new MessageDebug() { DebugMode = debug };
                            message.InvokeProcessing();
                            MessageUtils.SendMessageToServer(message);
                        }
                        else
                            MyAPIGateway.Utilities.ShowMessage(Globals.ModName, invalidOptionErrorText);
                        break;
                    case "save":
                        MessageUtils.SendMessageToServer(new MessageSave());
                        break;
                    case "help":
                        MyAPIGateway.Utilities.ShowMissionScreen(Globals.ModName + " Admin Help", 
                            "/" + Globals.ModName.ToLowerInvariant() + " <command> [value]", "", 
                            "Current commands:\r\n" +
                            "base <type> <modifier>|reset - Customize FTL options\r\n" +
                            "inhibitstockjump <on|off|true|false> - Enable/disable inhibiting stock jump drive\r\n" +
                            "gravitywell <on|off|true|false> - Enable/disable jumping inside planetary gravity well\r\n" +
                            "fixedfont <on|off|true|false> - Enable/disable fixed LCD font size\r\n" +
                            "debug <on|off|true|false> - Enable/disable debug logging\r\n" +
                            "save - Save settings\r\n" +
                            "\r\n" +
                            "use '/ftl base help' for details\r\n" +
                            "These commands are server-wide and can only be run by server administrators.\r\n" +
                            "Items with [ ] are optional, < > are required.\r\n" +
                            "<on|off> means pick 'on' or 'off'.\r\n" +
                            "\r\n" +
                            "If problems occur, do '/ftl save' and reload the map.\r\n" +
                            ""
                            );
                        break;
                    default:
                        MyAPIGateway.Utilities.ShowMessage(Globals.ModName, "Invalid command, type /" + Globals.ModName.ToLowerInvariant() + " help to show valid commands");
                        break;
                }
                return true;
            }

            return false;
        }

        static bool HandleValueChange(ChangeType change, string text)
        {
            try
            {
                var match = Regex.Match(text, @"(?<Type>[^\s]+)(\s+(?<Modifier>[0-9.]+|reset))");
                if (match.Success)
                {
                    var stype = match.Groups["Type"].Value;
                    var modifier = match.Groups["Modifier"].Value.Trim();
                    ModifierType type = ModifierType.Unset;

                    if (!Enum.TryParse<ModifierType>(stype, true, out type))
                    {
                        MyAPIGateway.Utilities.ShowMessage(Globals.ModName, "Invalid type: " + stype);
                        return false;
                    }

                    Logger.Instance.LogDebug(string.Format("Type: {0}, Modifier: {1}", type, modifier));
                    if (modifier.ToLowerInvariant() == "reset")
                    {
                        MessageUtils.SendMessageToServer(new MessageValueChange() { ValueType = change, Type = type, Reset = true });
                        return true;
                    }
                    else
                    {
                        float dbl = 0;
                        if (float.TryParse(modifier, out dbl))
                        {
                            MessageUtils.SendMessageToServer(new MessageValueChange() { ValueType = change, Type = type, Modifier = dbl });
                            return true;
                        }
                        else
                            MyAPIGateway.Utilities.ShowMessage(Globals.ModName, "Invalid modifier: " + modifier);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogException(ex);
            }
            return false;
        }

        static void LoadConfig()
        {
            Logger.Instance.LogMessage("FTLAdmin.LoadConfig()");
            try
            {
                var worldname = MyAPIGateway.Session.Name;
                worldname = Regex.Replace(worldname, "[<>:\"/\\|?*]", "");  // Remove invalid filename chars

                System.IO.TextReader reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(string.Format(ConfigFileName, worldname), typeof(FTLAdmin));
                var xmlData = reader.ReadToEnd();
                Configuration = MyAPIGateway.Utilities.SerializeFromXML<FTLConfig>(xmlData);
                reader.Close();

                Logger.Instance.Debug = Configuration.Debug;
            }
            catch(System.IO.FileNotFoundException)
            {
                // Config file doesn't exist, that's fine. Ignore the error.
            }
            catch( Exception ex)
            {
                Logger.Instance.LogMessage("FTLAdmin.LoadConfig() - Error");
                Logger.Instance.LogMessage(ex.GetType().Name);
                Logger.Instance.LogException(ex);
                // Continue Processing
            }
            Globals.Reload();
            FTLData.ReloadAll();
            FTLInhibitor.ReloadAll();
        }

        static public void SaveConfig()
        {
            string filename = string.Empty;

            try
            {
                var worldname = MyAPIGateway.Session.Name;
                worldname = Regex.Replace(worldname, "[<>:\"/\\|?*]", "");  // Remove invloid filename chars

                var xmlData = MyAPIGateway.Utilities.SerializeToXML<FTLConfig>(Configuration);
                filename = MyAPIGateway.Utilities.GamePaths.ModsPath + "\\322067487.sbm_Stargate\\" + string.Format(ConfigFileName, worldname);
                System.IO.TextWriter writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(string.Format(ConfigFileName, worldname), typeof(FTLAdmin));
                writer.Write(xmlData);
                writer.Flush();
                writer.Close();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(filename);
                Logger.Instance.LogException(ex);
            }
        }
    }

    public static class AdminExtensions
    {
        /// <summary>
        /// Creates the objectbuilders in game, and syncs it to the server and all clients.
        /// </summary>
        /// <param name="entities"></param>
        public static void CreateAndSyncEntities(this List<VRage.ObjectBuilders.MyObjectBuilder_EntityBase> entities)
        {
            MyAPIGateway.Entities.RemapObjectBuilderCollection(entities);
            entities.ForEach(item => MyAPIGateway.Entities.CreateFromObjectBuilderAndAdd(item));
            MyAPIGateway.Multiplayer.SendEntitiesCreated(entities);
        }

    }

    //[Serializable]
    //public struct MyMyTuple
    //{
    //    public static MyTuple<T1, T2> Create<T1, T2>(T1 arg1, T2 arg2)
    //    {
    //        return new MyTuple<T1, T2>(arg1, arg2);
    //    }
    //}

    //[StructLayout(LayoutKind.Sequential, Pack = 4)]
    //[Serializable]
    //public struct MyTuple<T1, T2>
    //{
    //    public T1 Item1;
    //    public T2 Item2;

    //    public MyTuple(T1 item1, T2 item2)
    //    {
    //        Item1 = item1;
    //        Item2 = item2;
    //    }
    //}
}
// vim: tabstop=4 expandtab shiftwidth=4 nobackup
