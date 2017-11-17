using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Timers;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using System.Threading.Tasks;
using System.Windows.Controls;
using NLog;
using Torch;
using Torch.API;
using Torch.API.Plugins;
using System.Reflection;
using VRage.Utils;
using Sandbox.Common.ObjectBuilders;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.ObjectBuilders;
using Sandbox.Game.EntityComponents;
using System.Text;
using VRage;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using Sandbox.Game.Gui;
using Ingame = Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces.Terminal;
using ProtoBuf;
using SpaceEngineers.Game.ModAPI;
/*
 * Mod by rexxar.
 * 
 * As usual, you're free to use this mod, dissect, reverse engineer, print and set it on fire,
 * so long as you give credit where it's due.
 * 
 * Simplistic server linking mostly to prove a point that it can be done in a mod. If you think
 * it's neat, you can buy me some caffeine at https://paypal.me/rexxar
 * 
 * This mod works by using the clients as messengers. Since servers can't talk to each other, we
 * serialize the grid and some faction data, timestamp it, sign it with a password, then calculate
 * the MD5 hash. This data is then sent to the client and stored on disk locally. When the client
 * spawns into the target server, it sends the hashed grid data back to the server, which verifies
 * it hasn't been tampered with.
 * 
 * This solution isn't 100% foolproof, but it's more than secure enough for this task.
 * 
 * In order to get around faction limitations, factions are recreated on the target server. We kind
 * of implicitly trust clients here, if they say they were in faction [ASD] then we believe them
 * and just add them to faction on the target server.
 */

namespace ServerJump
{

    public class ServerJumpClass : TorchPluginBase, IWpfPlugin
    {
        public Settings Config => _config?.Data;
        private UserControl _control;
        private Persistent<Settings> _config;
        public UserControl GetControl() => _control ?? (_control = new JumpGateControl(this));




        public static readonly Logger Log = LogManager.GetLogger("ServerJump");
        

        private const string HELP_TEXT = "Use !join to find a server to join, then '!join #' to join that server. !hub will take you back to the hub. !countown will hide the countdown timer.";
        private const string MODERATOR_HELP = HELP_TEXT + " '!spectate #' will take you to a match server without your ship, only available to moderators.";
        private const string ADMIN_HELP = MODERATOR_HELP + " !endjoin ends the join timer. !endmatch ends the match timer.";
        private static bool _init;
        public static bool Debug;
        public static ServerJumpClass Instance;

        private readonly HashSet<IMyEntity> _entityCache = new HashSet<IMyEntity>();
        private readonly Random _random = new Random();
        private bool _countdown = true;
        public DateTime? LobbyTime;
        public DateTime MatchStart;
        public DateTime? MatchTime;
        public Dictionary<int, ServerItem> Servers = new Dictionary<int, ServerItem>();

        public void Save()
        {
            _config.Save();
        }
        

     
        public void  SomeLog(string text)
        {
#if DEBUG
            ServerJump.ServerJumpClass.Log.Info(text);
#else
            ServerJump.ServerJumpClass.Log.Debug(text);
#endif
        }
        public override void Init(ITorchBase torch)
        {
            base.Init(torch);
            _config = Persistent<Settings>.Load(Path.Combine(StoragePath, "ServerJump.cfg"));

           /* foreach (var plugin in torch.Plugins)
            {
                if (plugin.Id == Guid.Parse("17f44521-b77a-4e85-810f-ee73311cf75d"))
                {
                    //   concealment = plugin;
                    // ReflectMethodRevealAll = plugin.GetType().GetMethod("RevealAll", BindingFlags.Public | BindingFlags.Instance);
                }
            }*/
           SomeLog("Init SERVERLINK");
            //_concealedAabbTree = new MyDynamicAABBTreeD(MyConstants.GAME_PRUNING_STRUCTURE_AABB_EXTENSION);
        }
        public void HandleChatCommand(ulong steamId, string command)
        {
            MyPromoteLevel level = MyAPIGateway.Session.GetUserPromoteLevel(steamId);
            ServerJumpClass.Instance.SomeLog($"Got chat command from {steamId} : {command}");

            if (command.Equals("!join", StringComparison.CurrentCultureIgnoreCase))
            {
                InitJump(steamId);

                return;
            }

            if (command.StartsWith("!join", StringComparison.CurrentCultureIgnoreCase))
            {
                int ind = command.IndexOf(" ");
                if (ind == -1)
                {
                    Communication.SendServerChat(steamId, "Couldn't parse your server selection!");
                    return;
                }

                string numtex = command.Substring(ind);
                ServerItem server;
                int num;
                if (!int.TryParse(numtex, out num))
                {
                    Communication.SendServerChat(steamId, "Couldn't parse your server selection!");
                    return;
                }

                if (!Servers.TryGetValue(num - 1, out server))
                {
                    Communication.SendServerChat(steamId, $"Couldn't find server {num}");
                    return;
                }

                /*
                if (!server.CanJoin)
                {
                    Communication.SendServerChat(steamId, "Sorry, this server is not open to new members. Please try another.");
                    return;
                }*/
                //TODO USE STEAM NETWORK FOR COUNT PLAYERS

                IMyPlayer player = Utilities.GetPlayerBySteamId(steamId);

                var block = player?.Controller?.ControlledEntity?.Entity as IMyCubeBlock;
                IMyCubeGrid grid = block?.CubeGrid;
                if (grid == null)
                {
                    Communication.SendServerChat(steamId, "Can't find your ship. Make sure you're seated in the ship you want to take with you.");
                    return;
                }

                var blocks = new List<IMySlimBlock>();
                grid.GetBlocks(blocks);

               /* if (blocks.Count > Settings.MaxBlockCount)
                {
                    Communication.SendServerChat(steamId, $"Your ship has {blocks.Count} blocks. The limit for this server is {Settings.MaxBlockCount}");
                    return;
                }*/ //TODO store block limit in custom gate data

                byte[] payload = Utilities.SerializeAndSign(grid, Utilities.GetPlayerBySteamId(steamId), block.Position);
                Communication.SegmentAndSend(Communication.MessageType.ClientGridPart, payload, MyAPIGateway.Multiplayer.ServerId, steamId);

               // server.Join(steamId);

                var timer = new Timer(10000);
                timer.AutoReset = false;
                timer.Elapsed += (a, b) => MyAPIGateway.Utilities.InvokeOnGameThread(() => grid.Close());
                timer.Start();
                return;
            }
            
            if (level >= MyPromoteLevel.Moderator)
            {
                if (command.StartsWith("!spectate", StringComparison.CurrentCultureIgnoreCase))
                {
                    int ind = command.IndexOf(" ");
                    if (ind == -1)
                    {
                        Communication.SendServerChat(steamId, "Couldn't parse your server selection!");
                        return;
                    }

                    string numtex = command.Substring(ind);
                    ServerItem server;
                    int num;
                    if (!int.TryParse(numtex, out num))
                    {
                        Communication.SendServerChat(steamId, "Couldn't parse your server selection!");
                        return;
                    }

                    if (!Servers.TryGetValue(num - 1, out server))
                    {
                        Communication.SendServerChat(steamId, $"Couldn't find server {num}");
                        return;
                    }

                    ServerJumpClass.Instance.SomeLog("DeleteFileInLocalStorage");
                    //  MyAPIGateway.Utilities.DeleteFileInLocalStorage("Ship.bin", typeof(ServerJump));
                    Communication.RedirectClient(steamId, server.IP);
                    return;
                }
            }

            if (level >= MyPromoteLevel.Admin)
            {
                                if (command.Equals("!reload", StringComparison.CurrentCultureIgnoreCase))
                {
                    //Settings.LoadSettings();
                    Communication.SendServerChat(steamId, "Okay.");
                    return;
                }

                if (command.Equals("!save", StringComparison.CurrentCultureIgnoreCase))
                {
                    // Settings.SaveSettings();
                    Communication.SendServerChat(steamId, "Okay.");
                    return;
                }

                if (command.StartsWith("!reset", StringComparison.CurrentCultureIgnoreCase))
                {
                    int ind = command.IndexOf(" ");
                    if (ind == -1)
                    {
                        foreach (var s in Servers.Values)
                            s.Reset();
                        Communication.SendServerChat(steamId, "Reset all servers");
                        return;
                    }

                    string numtex = command.Substring(ind);
                    ServerItem server;
                    int num;
                    if (!int.TryParse(numtex, out num))
                    {
                        Communication.SendServerChat(steamId, "Couldn't parse your server selection!");
                        return;
                    }

                    if (Servers.TryGetValue(num - 1, out server))
                    {
                        server.Reset();
                        Communication.SendServerChat(steamId, $"Reset server {num}");
                    }
                }
            }

            if (command.Equals("!help", StringComparison.CurrentCultureIgnoreCase))
            {
                if (level >= MyPromoteLevel.Admin)
                    Communication.SendServerChat(steamId, ADMIN_HELP);
                else
                {
                    if (level >= MyPromoteLevel.Moderator)
                        Communication.SendServerChat(steamId, MODERATOR_HELP);
                    else
                        Communication.SendServerChat(steamId, HELP_TEXT);
                }
            }
        }

        public override void Update()
        {
            try
            {
                if (MyAPIGateway.Session == null)
                    return;
                if (!_init)
                    Initialize();
                if (_config == null)
                {
                    ServerJumpClass.Instance.SomeLog("LinkMod Settings not defined on this server! Link mod will not work!");
                    ServerJumpClass.Instance.SomeLog("Settings not defined on server! Link mod will not work!");
                    return;
                }
            }
            catch (Exception ex)
            {
                ServerJumpClass.Instance.SomeLog($"Exception during update:\r\n{ex}");
            }
        }

        public override void Dispose()
        {
            if(MyAPIGateway.Utilities != null) MyAPIGateway.Utilities.MessageEntered -= MessageEntered;
            Communication.UnregisterHandlers();
        }

        private void Initialize()
        {
            Instance = this;
            _init = true;
            ServerJumpClass.Instance.SomeLog("LinkMod initialized.start");
            MyAPIGateway.Utilities.MessageEntered += MessageEntered;
            Communication.RegisterHandlers();
            ServerJumpClass.Instance.SomeLog("LinkMod initialized.end");
        }



        private void MessageEntered(string messageText, ref bool sendToOthers)
        {
            if (messageText.Equals("!!countdown", StringComparison.CurrentCultureIgnoreCase))
            {
                sendToOthers = false;
                _countdown = !_countdown;
                MyAPIGateway.Utilities.GetObjectiveLine().Hide();
                return;
            }

            if (messageText.StartsWith("!!"))
            {
                sendToOthers = false;
                Communication.SendClientChat(messageText);
            }
        }


        public void InitJump(ulong steamId)
        {
            ServerJumpClass.Instance.SomeLog("InitJump() start");

            Communication.SendServerChat(steamId, "InitJump start!");
            IMyPlayer player;
            player = Utilities.GetPlayerBySteamId(steamId);
            Communication.SendServerChat(steamId, "InitJump start!" + player.SteamUserId);
            //MyAPIGateway.Utilities.InvokeOnGameThread(() => { });
            var block = player?.Controller?.ControlledEntity?.Entity as IMyCubeBlock;
            IMyCubeGrid grid = block?.CubeGrid;
            if (grid == null)
            {
                Communication.SendServerChat(steamId, "Can't find your ship. Make sure you're seated in the ship you want to take with you.");
                return;
            }
            CheckBeforeJump(block.CubeGrid.GetPosition(), steamId, grid);
           /* byte[] payload = Utilities.SerializeAndSign(grid, Utilities.GetPlayerBySteamId(steamId), block.Position);
                Communication.SegmentAndSend(Communication.MessageType.ClientGridPart, payload, MyAPIGateway.Multiplayer.ServerId, steamId);
                Communication.SendServerChat(steamId, "redirect to 178.210.32.201:27016!");
                Communication.RedirectClient(steamId, "178.210.32.201:27016");
                var timer = new Timer(10000);
                timer.AutoReset = false; 
                timer.Elapsed += (a, b) => MyAPIGateway.Utilities.InvokeOnGameThread(() => grid.Close());
                timer.Start();*/
         
            //  
        }
        private void CheckBeforeJump(Vector3D gridpos, ulong steamId, IMyCubeGrid gridtotp)
        {
            ServerJumpClass.Instance.SomeLog("CheckBeforeJump() start");
            bool HyperDrive = false;
            bool GateLink = false;
            string IP = "0.0.0.0:27016";
            Communication.SendServerChat(steamId, "CheckBeforeJump start!" + gridpos);
            List<IMyEntity> allentities = new List<IMyEntity>();
            var tmpsphere = new BoundingSphereD(gridpos, 3100);
            allentities = MyAPIGateway.Entities.GetEntitiesInSphere(ref tmpsphere);
            Communication.SendServerChat(steamId, "GetTopMostEntitiesInSphere :" + allentities.Count);
            List<IMyCubeGrid> allentitieslist = allentities.OfType<IMyCubeGrid>().ToList();
            Communication.SendServerChat(steamId, ".OfType<IMyCubeGrid>() :" + allentitieslist.Count);
            if (allentitieslist.Count >= 2)
            {
                foreach (IMyCubeGrid grid in allentitieslist)
                {
                    if (grid?.GridSizeEnum == MyCubeSize.Small || grid.Physics == null ||grid.MarkedForClose || grid.Closed )
                        continue;
                    // Logging.Instance.WriteLine($"grid.Name = " + grid.Name);
                    Communication.SendServerChat(steamId, "grid.WorldVolume :" + grid.WorldVolume.Radius);
                    var blocks = new List<IMySlimBlock>();

                    var tmpspher2e = new BoundingSphereD(grid.GetPosition(), grid.WorldVolume.Radius * 2 ); 
                     blocks = grid.GetBlocksInsideSphere(ref tmpspher2e);

                    Communication.SendServerChat(steamId, " GetBlocks :" + blocks.Count);

                    foreach (IMySlimBlock block in blocks)
                    {
                         try
                        {
                        // Communication.SendServerChat(steamId, " block :" + block.GetObjectBuilderCubeBlock().SubtypeName);
                        if (block.FatBlock == null) continue;
                        if (block.FatBlock.BlockDefinition.SubtypeId  == "HyperDrive" &&
                            ((block.FatBlock.GetObjectBuilderCubeBlock() as MyObjectBuilder_JumpDrive).StoredPower >= (block.FatBlock as MyJumpDrive).BlockDefinition.PowerNeededForJump))
                            {
                                HyperDrive = true;
                                Communication.SendServerChat(steamId, "  HyperDrive = true; :");
                                //   Logging.Instance.WriteLine($"found HyperDrive !" + tmp2);
                            }
                            if (block.FatBlock.BlockDefinition.SubtypeId == "JumpGateLink")
                            {
                                GateLink = true;
                            IP = (block.FatBlock as IMyTerminalBlock)?.CustomData.ToString();
                            Communication.SendServerChat(steamId, "  GateLink = true; :");
                            }
                            if (HyperDrive && GateLink)
                            {
                              //  MyAPIGateway.Utilities.InvokeOnGameThread(() => { 
                                byte[] payload = Utilities.SerializeAndSign(gridtotp, 
                                    Utilities.GetPlayerBySteamId(steamId), 
                                    (Utilities.GetPlayerBySteamId(steamId)?.Controller?.ControlledEntity?.Entity as IMyCubeBlock).Position);

                                Communication.SegmentAndSend(Communication.MessageType.ClientGridPart, payload, MyAPIGateway.Multiplayer.ServerId, steamId);
                                //Communication.SendServerChat(steamId, "redirect to 178.210.32.201:27016!");
                                Communication.RedirectClient(steamId, IP);//"178.210.32.201:27016"
                                ServerJumpClass.Instance.SomeLog("[Jump] Client: " + Utilities.GetPlayerBySteamId(steamId).DisplayName + " steamId: " +steamId + " Grid: " + grid.DisplayName + " To: " + IP);
                                var timer = new Timer(8000);
                                timer.AutoReset = false;
                                timer.Elapsed += (a, b) => MyAPIGateway.Utilities.InvokeOnGameThread(() => gridtotp.Close());
                                timer.Start();
                                //});
                                return;
                            }

                        }
                        catch { Communication.SendServerChat(steamId, "  catch foreach (IMySlimBlock block in blocks) "); }
                    }
                }
                Communication.SendServerChat(steamId, "HyperDrive = " + HyperDrive + "GateLink = " + GateLink);
                return;
            }
            else { Communication.SendServerChat(steamId, "allentitieslist.Count >=2!"); }
           
            return;
        }

        /// <summary>
        ///     Cleans up match servers during the match
        /// </summary>
        /// 
        /*
        private void ProcessCleanup()
        {
            _entityCache.Clear();
            MyAPIGateway.Entities.GetEntities(_entityCache);

            MyAPIGateway.Parallel.ForEach(_entityCache, entity =>
                                                        {
                                                            if (entity.Closed || entity.MarkedForClose)
                                                                return;

                                                            var floating = entity as IMyFloatingObject;
                                                            if (floating != null)
                                                            {
                                                                MyAPIGateway.Utilities.InvokeOnGameThread(() => floating.Close());
                                                                return;
                                                            }

                                                            var grid = entity as IMyCubeGrid;
                                                            if (grid?.Physics == null)
                                                                return;

                                                            var blocks = new List<IMySlimBlock>();
                                                            grid.GetBlocks(blocks);

                                                            if (blocks.Count < 5)
                                                            {
                                                                MyAPIGateway.Utilities.InvokeOnGameThread(() => grid.Close());
                                                                return;
                                                            }

                                                            if (blocks.All(s => s.FatBlock == null))
                                                            {
                                                                MyAPIGateway.Utilities.InvokeOnGameThread(() => grid.Close());
                                                                return;
                                                            }

                                                            ulong id = 0;
                                                            if (grid.BigOwners.Count > 0)
                                                                id = MyAPIGateway.Players.TryGetSteamId(grid.BigOwners[0]);

                                                            Vector3D pos = grid.GetPosition();
                                                            if (pos.LengthSquared() > (Settings.SpawnRadius + 1000) * (Settings.SpawnRadius + 1000))
                                                            {
                                                                if (id != 0)
                                                                    Communication.SendNotification(id, "You have left the battle zone! Turn back now or face consequences!");
                                                            }

                                                            if (pos.LengthSquared() > (Settings.SpawnRadius + 2000) * (Settings.SpawnRadius + 2000))
                                                            {
                                                                MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                                                                                                          {
                                                                                                              IMySlimBlock b = blocks[_random.Next(blocks.Count)];
                                                                                                              Utilities.Explode(b.GetPosition(), 7000f, 22.5, grid, MyExplosionTypeEnum.WARHEAD_EXPLOSION_50);
                                                                                                          });
                                                            }
                                                        });
        }
        */
    }
}
