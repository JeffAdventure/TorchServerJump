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
using VRage.Utils;
using Sandbox.Common.ObjectBuilders;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.ObjectBuilders;
using Sandbox.Game.EntityComponents;
using System.Text;
using VRage;
using Sandbox.Game.Entities;
using VRageMath;
using VRage.Game.Entity;
using Sandbox.Game.Gui;


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

namespace ServerLinkMod
{
    // [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Beacon), false, new string[] { "JumpGateLink" })]
    public static class StaticLinkModCoreblock
    {
        public static  List<IMyEntity> HyperDriveList = new List<IMyEntity>();
        public static IMyEntity stJumpGateLink = null;
        public static bool InitedJumpDriveControls  = false;
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Beacon), true, new string[] { "JumpGateLink" })]
    public class LinkModCoreblock : MyGameLogicComponent
    {
        IMyFunctionalBlock JumpGateLink { get; set; }
        public static LinkModCoreblock Instance;
        public List<Vector3D> JumpGatesList = new List<Vector3D>();
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            JumpGateLink = Container.Entity as IMyFunctionalBlock;
            base.Init(objectBuilder);
            Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
            JumpGateLink.OnClose += OnClose;
            StaticLinkModCoreblock.stJumpGateLink = Entity;

        }
        void OnClose(IMyEntity obj)
        {
            if (!(StaticLinkModCoreblock.stJumpGateLink == null)) StaticLinkModCoreblock.stJumpGateLink = null;
            JumpGateLink.OnClose -= OnClose;
            Logging.Instance.WriteLine($"OnClose JumpGateLink");
        }
        public override void UpdateBeforeSimulation100()
        {
            base.UpdateBeforeSimulation100();
        }
        /*
        private void FindClientsJumpDrivesForDraw() //Dictionary<string, int> density
        {
        List<Vector3D> HyperDriveList = new List<Vector3D>();
        Vector3D JumpGateLinkPos1 = Vector3D.Zero;
        Logging.Instance.WriteLine($" tru to find!");
           
            var tmp = new BoundingSphereD(Entity.GetPosition(), 5000);
            var allships = MyAPIGateway.Entities.GetEntitiesInSphere(ref tmp);


            foreach (IMyEntity Entity2 in allships)
            {
                
                if (!(Entity2 is IMyCubeGrid))
                continue;

                IMyCubeGrid grid = (IMyCubeGrid)Entity2;
                if (grid.GridSizeEnum == MyCubeSize.Small)
                continue;

                List<IMySlimBlock> blocks = new List<IMySlimBlock>();
                grid.GetBlocks(blocks);
                MyStringHash hypsting = MyStringHash.GetOrCompute("HyperDrive");
                MyStringHash hypsting2 = MyStringHash.GetOrCompute("JumpGateLink");

                foreach (var block in blocks)
                {
                    if (block.BlockDefinition.Id.SubtypeId == hypsting)
                    {
                        var tmp2 = block.CubeGrid.GridIntegerToWorld(block.Position);
                        Logging.Instance.WriteLine($"found HyperDrive !" + tmp2 + "StoredPower " + (block.GetObjectBuilder() as MyObjectBuilder_JumpDrive).StoredPower);
                     
                        HyperDriveList.Add(block.CubeGrid.GridIntegerToWorld(block.Position));
                    }
                    if (block.BlockDefinition.Id.SubtypeId == hypsting2)
                    {
                        var tmp3 = block.CubeGrid.GridIntegerToWorld(block.Position);
                        Logging.Instance.WriteLine($"found JumpGateLink !" + tmp3);
                        JumpGateLinkPos1 = block.CubeGrid.GridIntegerToWorld(block.Position);

                        var msg = new Communication.JumpInfo
                        {
                            position = JumpGateLinkPos1,
                            steamId = block.CubeGrid.EntityId,
                            IP = (Entity as IMyTerminalBlock).CustomData 
                        };

                        byte[] data = Encoding.UTF8.GetBytes(MyAPIGateway.Utilities.SerializeToXML(msg));

                        Communication.SendToServer(Communication.MessageType.ClientRequestJump, data);
                    }
                }
            }

           // StaticLinkModCoreblock.HyperDriveList = HyperDriveList;
           // StaticLinkModCoreblock.JumpGateLinkPos = JumpGateLinkPos1;
        }*/
    }



    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation, 5)]
    public class LinkModCore : MySessionComponentBase
    {
        private const string HELP_TEXT = "Use !join to find a server to join, then '!join #' to join that server. !hub will take you back to the hub. !countown will hide the countdown timer.";
        private const string MODERATOR_HELP = HELP_TEXT + " '!spectate #' will take you to a match server without your ship, only available to moderators.";
        private const string ADMIN_HELP = MODERATOR_HELP + " !endjoin ends the join timer. !endmatch ends the match timer.";
        private static bool _init;
        private static bool _playerInit;
        public static bool Debug;
        public static LinkModCore Instance;

        private readonly HashSet<IMyEntity> _entityCache = new HashSet<IMyEntity>();
        private readonly Random _random = new Random();
     
        private bool _countdown = true;

        private int _updateCount;
        
        private Vector4 Color2 = ((new Color(240, 0, 0)*0.2f).ToVector4()); //желтый
        private Vector4 Color1 = ((new Color(0,255,255)*0.2f).ToVector4()); //белый

        public override void Draw()
        {
            if (StaticLinkModCoreblock.stJumpGateLink == null || StaticLinkModCoreblock.HyperDriveList.Count <=0) return;
           
            foreach (IMyEntity line in StaticLinkModCoreblock.HyperDriveList)
            {
                int tmp = ((int)((line as IMyJumpDrive).CurrentStoredPower * 100) / (int)(line as IMyJumpDrive).MaxStoredPower);
              //  Vector4 xColor1 = Color1;
               //  xColor1.Z = xColor1.Z + tmp;
                // Vector4 xColor2 = Color2;
               //  xColor2.X = xColor2.X -( tmp *2);
               /// xColor2.Y = xColor2.Y + (tmp * 2) + 15;
                MySimpleObjectDraw.DrawLine(StaticLinkModCoreblock.stJumpGateLink.GetPosition(), line.GetPosition(), MyStringId.GetOrCompute("WeaponLaser"), ref Color1, 1f);
                MySimpleObjectDraw.DrawLine(StaticLinkModCoreblock.stJumpGateLink.GetPosition(), line.GetPosition(), MyStringId.GetOrCompute("WeaponLaser"), ref Color2,3f);
            }
        }


        public override void UpdateBeforeSimulation()
        {
              if (MyAPIGateway.Multiplayer.IsServer)
               return;
            
                if (MyAPIGateway.Session == null)
                    return;


                _updateCount++;

                if (!_init)
                    Initialize();

                if (!_playerInit && MyAPIGateway.Session?.Player?.Character != null)
                {
                    _playerInit = true;
                    if (MyAPIGateway.Utilities.FileExistsInLocalStorage("Ship.bin", typeof(LinkModCore)))
                    {
                        BinaryReader reader = MyAPIGateway.Utilities.ReadBinaryFileInLocalStorage("Ship.bin", typeof(LinkModCore));
                        int count = reader.ReadInt32();
                        byte[] bytes = reader.ReadBytes(count);

                        Logging.Instance.WriteLine($"Sending grid parts: {count} bytes.");

                        Communication.SegmentAndSend(Communication.MessageType.ServerGridPart, bytes, MyAPIGateway.Session.Player.SteamUserId);
                      
                    }
                    if (!MyAPIGateway.Utilities.FileExistsInLocalStorage("Greeting.cfm", typeof(LinkModCore)))
                    {
                        MyAPIGateway.Utilities.ShowMissionScreen("ServerLink",
                                                                 "",
                                                                 null,
                                                                 "Welcome to the server link demo! Important rules and explanations:\r\n" +
                                                                 "Pistons and rotors are prohibited.\r\n" +
                                                                 "Grids are limited to 5k blocks.\r\n" +
                                                                 "Grids in the hub will always be static, and all weapons are disabled.\r\n" +
                                                                 "Grids in the hub MUST be owned! Unowned grids and grids beloning to offline players will be deleted every 10 minutes.\r\n\r\n" +
                                                                 "Hub server provided by Neimoh of Galaxy Strike Force\r\n" +
                                                                 "Match server #1 provided by Franky500 of Frankys Space\r\n" +
                                                                 "Be sure to visit their regular servers!\r\n" +
                                                                 "All other servers provided by X_Wing_Ian\r\n\r\n" +
                                                                 "Use !join to get a list of servers you can join. Use !help for a full list of commands you can use.\r\n\r\n\r\n" +
                                                                 "Enjoy!\r\n" +
                                                                 "-rexxar",
                                                                 null,
                                                                 "Close");
                        var w = MyAPIGateway.Utilities.WriteFileInLocalStorage("Greeting.cfm", typeof(LinkModCore));
                        w.Write("true");
                        w.Flush();
                        w.Close();
                    }
                    //else if (!Settings.Instance.Hub && !Exempt.Contains(MyAPIGateway.Session.Player.SteamUserId))
                    //{
                    //    MyAPIGateway.Utilities.ShowMessage("System", "You shouldn't be here!");
                    //    Communication.RedirectClient(MyAPIGateway.Session.Player.SteamUserId, Utilities.ZERO_IP);
                    //}
                }

              /*  if (MyAPIGateway.Session.Player != null)
                {
                    if (LobbyTime.HasValue && LobbyTime > DateTime.Now)
                    {
                        IMyHudObjectiveLine line = MyAPIGateway.Utilities.GetObjectiveLine();
                        line.Title = "Match starting in:";
                        line.Objectives.Clear();
                        line.Objectives.Add((DateTime.Now - LobbyTime.Value).ToString(@"mm\:ss"));
                        if (_countdown && !line.Visible)
                            line.Show();
                    }
                    else
                    {
                        if (MatchTime.HasValue && MatchTime >= DateTime.Now)
                        {
                            IMyHudObjectiveLine line = MyAPIGateway.Utilities.GetObjectiveLine();
                            line.Title = "Match ending in:";
                            line.Objectives.Clear();
                            line.Objectives.Add((DateTime.Now - MatchTime.Value).ToString(@"mm\:ss"));
                            if (_countdown && !line.Visible)
                                line.Show();
                        }
                    }
                }*/



           
        }


    
        protected override void UnloadData()
        {
            if (MyAPIGateway.Multiplayer.IsServer)
                return;
            MyAPIGateway.Utilities.MessageEntered -= MessageEntered;
            Communication.UnregisterHandlers();
            Logging.Instance.WriteLine($"UnloadData");
            Logging.Instance.Close();
        }

        private void Initialize()
        {
            Instance = this;
            _init = true;
            Logging.Instance.WriteLine("Client LinkMod initialized.");
            MyAPIGateway.Utilities.MessageEntered += MessageEntered;
            Communication.RegisterHandlers();
        }

        
        private void MessageEntered(string messageText, ref bool sendToOthers)
        {
            if (messageText.Equals("!particle", StringComparison.CurrentCultureIgnoreCase))
            {
                sendToOthers = false;
                //   StartCharjing();
                return;
            }

            if (messageText.StartsWith("!"))
            {
                sendToOthers = false;
                Communication.SendClientChat(messageText);
            }
        }

      
    }
}
