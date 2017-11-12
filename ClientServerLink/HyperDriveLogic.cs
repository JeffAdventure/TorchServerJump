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
using VRage.Game.Entity;
using Sandbox.Game.Gui;
using Ingame = Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces.Terminal;
using ProtoBuf;
using SpaceEngineers.Game.ModAPI;


namespace ServerLinkMod
{

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_JumpDrive), true, "HyperDrive")]
    public class HyperJumpLogic : MyGameLogicComponent
    {
        IMyFunctionalBlock JumpDrv { get; set; }
        IMyCubeGrid Grid;
        IMyGridTerminalSystem Term;
        MyResourceSinkComponent MyPowerSink;
        private MyEntity3DSoundEmitter m_soundEmitter;
        private MyEntity3DSoundEmitter LOOP_soundEmitter;
        private bool m_playedSound = false;
        public bool magicblack = false;
        public bool ShoudPlayLoop = false;
        public MyEntity3DSoundEmitter LoopSoundEmitter { get { return LOOP_soundEmitter; } }
        public MyEntity3DSoundEmitter SoundEmitter { get { return m_soundEmitter; } }

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            JumpDrv = Container.Entity as IMyFunctionalBlock;
            base.Init(objectBuilder);
            Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME | MyEntityUpdateEnum.EACH_FRAME;

            if (!StaticLinkModCoreblock.HyperDriveList.Contains(Entity)) StaticLinkModCoreblock.HyperDriveList.Add(Entity);
            JumpDrv.AppendingCustomInfo += Tool_AppendingCustomInfo;
            JumpDrv.OnClose += OnClose;
            m_soundEmitter = new MyEntity3DSoundEmitter(Container.Entity as VRage.Game.Entity.MyEntity);
            LOOP_soundEmitter = new MyEntity3DSoundEmitter(Container.Entity as VRage.Game.Entity.MyEntity);
            magicblack = true;
        }
        void OnClose(IMyEntity obj)
        {
            if (StaticLinkModCoreblock.HyperDriveList.Contains(Entity)) StaticLinkModCoreblock.HyperDriveList.Remove(Entity);
            JumpDrv.AppendingCustomInfo -= Tool_AppendingCustomInfo;
            JumpDrv.OnClose -= OnClose;

        }
        public override void UpdateBeforeSimulation()
        {

            try
            {
                if (StaticLinkModCoreblock.InitedJumpDriveControls)
                {
                    JumpDrv.RefreshCustomInfo();
                    //    MyAPIGateway.Utilities.ShowMessage("HyperDrive:", "RefreshCustomInfo");
                }
            }
            catch { MyAPIGateway.Utilities.ShowMessage("HyperDrive:", "RefreshCustomInfo catch"); }

        }
        public override void UpdateBeforeSimulation100()
        {
            Logging.Instance.WriteLine("UpdateBeforeSimulation100 ");
            MyAPIGateway.Utilities.ShowMessage("HyperDrive:", "UpdateBeforeSimulation100");

            try
            {
                if (!StaticLinkModCoreblock.InitedJumpDriveControls)
                {
                    InitJumpDriveControls();
                    Logging.Instance.WriteLine("InitJumpDriveControls inside");
                    MyAPIGateway.Utilities.ShowMessage("HyperDrive:", "InitJumpDriveControls");
                }
                if (magicblack)
                {
                    if (StaticLinkModCoreblock.stJumpGateLink != null && (Vector3D.DistanceSquared(StaticLinkModCoreblock.stJumpGateLink.GetPosition(), Entity.GetPosition())) < 5000 * 5000)
                    {

                        //  Logging.Instance.WriteLine("Before boost: CurrentStoredPower" + ((JumpDrv as IMyJumpDrive).CurrentStoredPower));
                        //Logging.Instance.WriteLine("Before Entity as MyJumpDrive" + (Entity as MyJumpDrive).CurrentStoredPower);
                        if ((JumpDrv as IMyJumpDrive).CurrentStoredPower < (JumpDrv as IMyJumpDrive).MaxStoredPower)
                        {
                            if (!ShoudPlayLoop)
                            {
                                ShoudPlayLoop = true;
                                if (!LoopSoundEmitter.IsPlaying) { PlayLoopSound("Foogs.JumpDriveChargeLoop"); }
                                //   
                            }
                                (JumpDrv as IMyJumpDrive).CurrentStoredPower = (JumpDrv as IMyJumpDrive).CurrentStoredPower + ((JumpDrv as IMyJumpDrive).MaxStoredPower / 150);
                                //Logging.Instance.WriteLine("After boost: CurrentStoredPower" + ((JumpDrv as IMyJumpDrive).CurrentStoredPower));
                                //Logging.Instance.WriteLine("After Entity as MyJumpDrive" + (Entity as MyJumpDrive).CurrentStoredPower);
                            }
                            else
                            {
                              //  PlaySound(""); //stop sound
                                (JumpDrv as IMyJumpDrive).CurrentStoredPower = (JumpDrv as IMyJumpDrive).MaxStoredPower;
                            if (LoopSoundEmitter.IsPlaying) { PlayLoopSound(""); }
                        }
                        }
                    }
                }
            catch { MyAPIGateway.Utilities.ShowMessage("HyperDrive:", "catch !! UpdateBeforeSimulation100"); }

        }
        public void PlayLoopSound(string soundname, bool stopPrevious = false, float maxdistance = 1000, float CustomVolume = 1f, bool CanPlayLoopSounds = true)
        {
            Logging.Instance.WriteLine($"PlayLoopSound");
            MyEntity3DSoundEmitter emitter = null;
            emitter = LoopSoundEmitter;

            if (emitter != null)
            {
                if (string.IsNullOrEmpty(soundname))
                {
                    emitter.StopSound((emitter.Loop ? true : false), true);
                }
                else
                {
                    MySoundPair sound = new MySoundPair(soundname);
                    emitter.CustomMaxDistance = maxdistance;
                    // Logger.Instance.LogDebug("Distance: " + emitter.CustomMaxDistance);
                    emitter.CustomVolume = CustomVolume;
                    emitter.SourceChannels = 2;
                    emitter.Force2D = true;
                    emitter.CanPlayLoopSounds = CanPlayLoopSounds;
                    emitter.PlaySound(sound, stopPrevious);
                }
            }
        }
        public void PlaySound(string soundname, bool stopPrevious = false, float maxdistance = 100, float CustomVolume = 1f, bool CanPlayLoopSounds = false)
        {
            Logging.Instance.WriteLine($"PlaySound");
            MyEntity3DSoundEmitter emitter = null;
            emitter = SoundEmitter;

            if (emitter != null)
            {
                if (string.IsNullOrEmpty(soundname))
                {
                    emitter.StopSound((emitter.Loop ? true : false), true);
                }
                else
                {
                    MySoundPair sound = new MySoundPair(soundname);
                    emitter.CustomMaxDistance = maxdistance;
                    // Logger.Instance.LogDebug("Distance: " + emitter.CustomMaxDistance);
                    emitter.CustomVolume = CustomVolume;
                    emitter.SourceChannels = 2;
                    emitter.Force2D = true;
                    emitter.CanPlayLoopSounds = CanPlayLoopSounds;
                    emitter.PlaySound(sound, stopPrevious);
                }
            }
        }

        private void Tool_AppendingCustomInfo(IMyTerminalBlock trash, StringBuilder Info)
        {
            Info.Clear();
            //MyAPIGateway.Utilities.ShowMessage("HyperDrive:", "Tool_AppendingCustomInfo");
            Info.AppendLine($">CROSS-SERVER JUMP STATUS:<");
            if ((trash.GetObjectBuilderCubeBlock() as MyObjectBuilder_JumpDrive).StoredPower >= (trash as MyJumpDrive).BlockDefinition.PowerNeededForJump)
            { Info.AppendLine($"Charged and Ready"); }
            else
            {
                Info.AppendLine($"Not Charged and not ready");
            }
            // Info.AppendLine($"Current Input: {Math.Round(JumpDrv.ResourceSink.RequiredInputByType(Electricity), 2)} MW");
        }


        public static bool CanJump(IMyTerminalBlock Block)
        {
            try
            {
                MyAPIGateway.Utilities.ShowMessage("HyperDrive:", "CanJump???");
                if (((Block.GetObjectBuilderCubeBlock() as MyObjectBuilder_JumpDrive).StoredPower >= (Block as MyJumpDrive).BlockDefinition.PowerNeededForJump) && StaticLinkModCoreblock.stJumpGateLink != null && (Vector3D.DistanceSquared(StaticLinkModCoreblock.stJumpGateLink.GetPosition(), Block.GetPosition()) < 5000 * 5000))
                    return true;
                return false;
            }
            catch
            {
                MyAPIGateway.Utilities.ShowMessage("HyperDrive:", "CanJump catch false");

                return false;
            }
        }

        public void TryJump()
        {
            if (LoopSoundEmitter.IsPlaying) { PlayLoopSound(""); }
            JumpDrv.GameLogic.GetAs<HyperJumpLogic>().PlaySound("Foogs.JumpDriveStart", true);
            Logging.Instance.WriteLine($"TryJump Jump Started (client)");
            MyAPIGateway.Utilities.ShowMessage("HyperDrive:", "Jump Started");
            var msg = new Communication.JumpInfo
            {
                steamId = MyAPIGateway.Session.Player.SteamUserId
            };
            byte[] data = Encoding.UTF8.GetBytes(MyAPIGateway.Utilities.SerializeToXML(msg));
            Communication.SendToServer(Communication.MessageType.ClientRequestJump, data);
        }

        public void InitJumpDriveControls()
        {

            if (StaticLinkModCoreblock.InitedJumpDriveControls) return;
            Logging.Instance.WriteLine($"InitJumpDriveControls");
            MyAPIGateway.Utilities.ShowMessage("HyperDrive:", "InitJumpDriveControls");
            var JumpButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyJumpDrive>("JumpButton");
            JumpButton.Title = MyStringId.GetOrCompute("Hyper Jump");
            JumpButton.Tooltip = MyStringId.GetOrCompute("Init Hyper Jump to remote universe");
            JumpButton.Action = (b) => b.GameLogic.GetAs<HyperJumpLogic>().TryJump();
            JumpButton.Enabled = CanJump;
            MyAPIGateway.TerminalControls.AddControl<IMyJumpDrive>(JumpButton);
            StaticLinkModCoreblock.InitedJumpDriveControls = true;
        }
    }
}
