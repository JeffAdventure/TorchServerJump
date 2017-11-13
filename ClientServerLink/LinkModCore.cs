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
   
    /// <summary>
    ///this is staticclass for connect gate and jumpdrive
    /// </summary>
    public static class StaticLinkModCoreblock
    {
        public static  List<IMyEntity> HyperDriveList = new List<IMyEntity>();
        public static IMyEntity stJumpGateLink = null;
        public static bool InitedJumpDriveControls  = false;
    }

    /// <summary>
    /// This is class for gate ("beacon " ingame) block,only for client
    /// </summary>
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
            StaticLinkModCoreblock.stJumpGateLink = Entity; //this class for it. Hook gate entity.

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



   /// <summary>
   /// This is main big class for jump logic,shoud be identical client/server, works without block.
   /// </summary>
   [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation, 5)]
    public class LinkModCore : MySessionComponentBase
    {
        public static bool Debug = true;
        private static bool _init;
        private static bool _playerInit;
        public static LinkModCore Instance;

        private readonly HashSet<IMyEntity> _entityCache = new HashSet<IMyEntity>();
        private int _updateCount;

        private Vector4 Color2 = ((new Color(240, 0, 0) * 0.2f).ToVector4()); //yellow
        private Vector4 Color1 = ((new Color(0,255,255) * 0.2f).ToVector4()); //white



        public static void WriteToLogDbg(string msg)
        {
            MyAPIGateway.Utilities.ShowMessage("dbg", msg);
            Logging.Instance.WriteLine(msg);
        }
        public override void Draw() //Draw charge pulse
        {
            if (StaticLinkModCoreblock.stJumpGateLink == null || StaticLinkModCoreblock.HyperDriveList.Count <=0) return;
           
            foreach (IMyEntity line in StaticLinkModCoreblock.HyperDriveList)
            {
                int tmp = ((int)((line as IMyJumpDrive).CurrentStoredPower * 100) / (int)(line as IMyJumpDrive).MaxStoredPower);
              //TODO Fix Colors
              //TODO Add Pulse/Particles
                MySimpleObjectDraw.DrawLine(StaticLinkModCoreblock.stJumpGateLink.GetPosition(), line.GetPosition(), MyStringId.GetOrCompute("WeaponLaser"), ref Color1, 1f);
                MySimpleObjectDraw.DrawLine(StaticLinkModCoreblock.stJumpGateLink.GetPosition(), line.GetPosition(), MyStringId.GetOrCompute("WeaponLaser"), ref Color2,3f);
            }
        }


        public override void UpdateBeforeSimulation()
        {
            if (MyAPIGateway.Multiplayer.IsServer)//only client pls
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
                   
                }            
        }


    
        protected override void UnloadData()
        {
            if (MyAPIGateway.Multiplayer.IsServer) //only client pls
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
            if (messageText.Equals("!testparticle", StringComparison.CurrentCultureIgnoreCase))
            {
                sendToOthers = false;
                // StartCharjing();
                return; //dont send to server
            }

            if (messageText.StartsWith("!"))
            {
                sendToOthers = false;
                Communication.SendClientChat(messageText); //send msg to server
            }
        }

      
    }
}
