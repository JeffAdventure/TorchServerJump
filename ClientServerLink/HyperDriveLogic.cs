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
    /// <summary>
    /// This class for "HyperDrive" Block, contains all grid transfer logic
    /// </summary>
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_JumpDrive), true, "HyperDrive")]
    public class HyperJumpLogic : MyGameLogicComponent
    {
        private IMyFunctionalBlock JumpDrv { get; set; }
        private MyEntity3DSoundEmitter m_soundEmitter;
        private MyEntity3DSoundEmitter LOOP_soundEmitter;
        private bool imInit = false;
        private bool ShoudPlayLoop = false;
        private MyEntity3DSoundEmitter LoopSoundEmitter { get { return LOOP_soundEmitter; } }
        private MyEntity3DSoundEmitter SoundEmitter { get { return m_soundEmitter; } }

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)//bugged shit
        { 
            base.Init(objectBuilder);
            Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
        }

        private void MyInit()
        {
            JumpDrv = Container.Entity as IMyFunctionalBlock;
            if (!StaticLinkModCoreblock.HyperDriveList.Contains(Entity)) StaticLinkModCoreblock.HyperDriveList.Add(Entity);
            JumpDrv.AppendingCustomInfo += Tool_AppendingCustomInfo;
            JumpDrv.OnClose += OnClose;
            m_soundEmitter = new MyEntity3DSoundEmitter(Container.Entity as VRage.Game.Entity.MyEntity);
            LOOP_soundEmitter = new MyEntity3DSoundEmitter(Container.Entity as VRage.Game.Entity.MyEntity);
            imInit = true;
        }

        public void OnClose(IMyEntity obj)
        {
            if (StaticLinkModCoreblock.HyperDriveList.Contains(Entity)) StaticLinkModCoreblock.HyperDriveList.Remove(Entity);
            JumpDrv.AppendingCustomInfo -= Tool_AppendingCustomInfo;
            JumpDrv.OnClose -= OnClose;

        }

        public override void UpdateBeforeSimulation100()
        {
            LinkModCore.WriteToLogDbg("HyperDrive:" + "UpdateBeforeSimulation100");
            if (!imInit) MyInit();
            try
            {
                if (StaticLinkModCoreblock.InitedJumpDriveControls)
                {
                    JumpDrv.RefreshCustomInfo();
                }
            }
            catch { LinkModCore.WriteToLogDbg("HyperDrive:" + "RefreshCustomInfo catch"); }
            try
            {
                if (!StaticLinkModCoreblock.InitedJumpDriveControls)
                {
                    InitJumpDriveControls();
                }
                if (imInit)
                {
                    if (StaticLinkModCoreblock.stJumpGateLink != null && (Vector3D.DistanceSquared(StaticLinkModCoreblock.stJumpGateLink.GetPosition(), Entity.GetPosition())) < 5000 * 5000)
                    {
                        if ((JumpDrv as IMyJumpDrive).CurrentStoredPower < (JumpDrv as IMyJumpDrive).MaxStoredPower)
                        {
                            if (!ShoudPlayLoop)
                            {
                                ShoudPlayLoop = true;
                                if (!LoopSoundEmitter.IsPlaying) { PlayLoopSound("Foogs.JumpDriveChargeLoop"); }
                            }
                                (JumpDrv as IMyJumpDrive).CurrentStoredPower = (JumpDrv as IMyJumpDrive).CurrentStoredPower + ((JumpDrv as IMyJumpDrive).MaxStoredPower / 150);
                        }
                        else
                        {
                            (JumpDrv as IMyJumpDrive).CurrentStoredPower = (JumpDrv as IMyJumpDrive).MaxStoredPower;
                            if (LoopSoundEmitter.IsPlaying) { PlayLoopSound(""); }
                        }
                    }
                }
            }
            catch { LinkModCore.WriteToLogDbg("HyperDrive:" + "catch !! UpdateBeforeSimulation100"); }

        }

        private void PlayLoopSound(string soundname, bool stopPrevious = false, float maxdistance = 1000, float CustomVolume = 1f, bool CanPlayLoopSounds = true)
        {
            LinkModCore.WriteToLogDbg("PlayLoopSound");
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
        private void PlaySound(string soundname, bool stopPrevious = false, float maxdistance = 100, float CustomVolume = 1f, bool CanPlayLoopSounds = false)
        {
            LinkModCore.WriteToLogDbg("PlaySound");
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
            LinkModCore.WriteToLogDbg("HyperDrive:"+ "Tool_AppendingCustomInfo");
            Info.AppendLine($">CROSS-SERVER JUMP STATUS:<");
            if ((trash.GetObjectBuilderCubeBlock() as MyObjectBuilder_JumpDrive).StoredPower >= (trash as MyJumpDrive).BlockDefinition.PowerNeededForJump)
            { Info.AppendLine($"Charged and Ready"); }
            else
            {
                Info.AppendLine($"Not Charged and not ready");
            }
            // Info.AppendLine($"Current Input: {Math.Round(JumpDrv.ResourceSink.RequiredInputByType(Electricity), 2)} MW");
        }


        private static bool CanJump(IMyTerminalBlock Block)
        {
            try
            {
                LinkModCore.WriteToLogDbg("HyperDrive:" + "CanJump?");

                if (((Block.GetObjectBuilderCubeBlock() as MyObjectBuilder_JumpDrive).StoredPower >= (Block as MyJumpDrive).BlockDefinition.PowerNeededForJump) 
                    && StaticLinkModCoreblock.stJumpGateLink != null 
                    && (Vector3D.DistanceSquared(StaticLinkModCoreblock.stJumpGateLink.GetPosition(), Block.GetPosition()) < 5000 * 5000))
                return true;
                return false;
            }
            catch
            {
                LinkModCore.WriteToLogDbg("HyperDrive:" + "CanJump catch false");

                return false;
            }
        }

        /// <summary>
        /// Launch transfer from button click
        /// </summary>
        private void TryJump()
        {
            if (LoopSoundEmitter.IsPlaying) { PlayLoopSound(""); }
            JumpDrv.GameLogic.GetAs<HyperJumpLogic>().PlaySound("Foogs.JumpDriveStart", true);

            LinkModCore.WriteToLogDbg("HyperDrive : TryJump Jump Started (client)");

            var msg = new Communication.JumpInfo
            {
                steamId = MyAPIGateway.Session.Player.SteamUserId
            };
            byte[] data = Encoding.UTF8.GetBytes(MyAPIGateway.Utilities.SerializeToXML(msg));
            Communication.SendToServer(Communication.MessageType.ClientRequestJump, data);//send ship to server
        }

        private void InitJumpDriveControls()
        {   //TODO delete unused controls from block.how? see CoreBlockMod

            LinkModCore.WriteToLogDbg("HyperDrive:" + "InitJumpDriveControls");
            if (StaticLinkModCoreblock.InitedJumpDriveControls) return;
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
