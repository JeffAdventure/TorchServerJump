using System;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.ModAPI;
using Sandbox.Common.ObjectBuilders;
using VRage.ObjectBuilders;
using Sandbox.Definitions;
using VRage.Game;
using System.Collections.Generic;
using Ingame = Sandbox.ModAPI.Ingame;
using VRageMath;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using Sandbox.ModAPI.Interfaces;
using Sandbox.Game.Entities;
using System.Linq;
using Sandbox.Game.EntityComponents;
using VRage.Game.ObjectBuilders.Definitions;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using ProtoBuf;

namespace Cheetah.Radars
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class SessionCore : MySessionComponentBase
    {
        public static bool Debug { get; } = false;
        public static bool VerboseDebug { get; } = false;
        /// <summary>
        /// How many ticks to skip between Work() calls. Working speed is compensated.
        /// </summary>
        public static int WorkSkipTicks { get; } = 30;
        public const float TickLengthMs = 1000 / 60;
        public const string ModName = "LaserWelders.";
        public const uint ModID = 927381544;
        public static readonly Guid StorageGuid = new Guid("22125116-4EE3-4F87-B6D6-AE1232014EA5");

        static bool Inited = false;
        protected static readonly HashSet<Action> SaveActions = new HashSet<Action>();
        public static void SaveRegister(Action Proc) => SaveActions.Add(Proc);
        public static void SaveUnregister(Action Proc) => SaveActions.Remove(Proc);

        public override void UpdateBeforeSimulation()
        {
            if (!Inited) Init();
        }

        void Init()
        {
            if (Inited || MyAPIGateway.Session == null) return;
            try
            {
                Networking.Networker.Init(ModID);
            }
            catch (Exception Scrap)
            {
                LogError("Init", Scrap);
            }
            Inited = true;
        }

        public override void SaveData()
        {
            foreach (var Proc in SaveActions)
                try
                {
                    Proc.Invoke();
                }
                catch { }
        }

        protected override void UnloadData()
        {
            try
            {
                SaveActions.Clear();
                Networking.Networker.Close();
            }
            catch { }
        }

        void PurgeGPSMarkers()
        {
            try
            {
                List<IMyIdentity> Identities = new List<IMyIdentity>();
                MyAPIGateway.Players.GetAllIdentites(Identities);
                foreach (var Player in Identities)
                {
                    long ID = Player.IdentityId;
                    foreach (var Marker in MyAPIGateway.Session.GPS.GetGpsList(ID))
                    {
                        if (Marker.Description.Contains("RadarEntity"))
                            MyAPIGateway.Session.GPS.RemoveGps(ID, Marker);
                    }
                }
            }
            catch { }
        }

        public static bool HasBlockLogic(IMyTerminalBlock Block)
        {
            try
            {
                return Block.HasComponent<BlockLogic>();
            }
            catch (Exception Scrap)
            {
                LogError("IsOurBlock", Scrap);
                return false;
            }
        }

        public static void BlockAction(IMyTerminalBlock Block, Action<BlockLogic> Action)
        {
            try
            {
                BlockLogic Logic;
                if (!Block.TryGetComponent(out Logic)) return;
                Action(Logic);
            }
            catch (Exception Scrap)
            {
                LogError("BlockAction", Scrap);
                return;
            }
        }

        public static T BlockReturn<T>(IMyTerminalBlock Block, Func<BlockLogic, T> Getter, T Default = default(T))
        {
            try
            {
                BlockLogic Logic;
                if (!Block.TryGetComponent(out Logic)) return Default;
                return Getter(Logic);
            }
            catch (Exception Scrap)
            {
                LogError("BlockReturn", Scrap);
                return Default;
            }
        }

        public static bool InitedWelderControls { get; protected set; } = false;
        public static void InitWelderControls()
        {
            if (InitedWelderControls) return;

            var WelderBeam = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipWelder>("BeamLength");
            var WelderBeam2 = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipWelder>("BeamLength");
            WelderBeam.Title = MyStringId.GetOrCompute("Beam Length");
            WelderBeam.Tooltip = MyStringId.GetOrCompute("Sets the laser beam's length.");
            WelderBeam.SupportsMultipleBlocks = true;
            WelderBeam.Enabled = HasBlockLogic;
            WelderBeam.Visible = HasBlockLogic;
            WelderBeam.SetLimits(Block => BlockReturn(Block, x => x.MinBeamLengthBlocks), Block => BlockReturn(Block, x => x.MaxBeamLengthBlocks));
            WelderBeam.Getter = (Block) => BlockReturn(Block, x => x.BeamLength);
            WelderBeam.Setter = (Block, NewLength) => BlockAction(Block, x => x.BeamLength = (int)NewLength);
            WelderBeam.Writer = (Block, Info) => Info.Append($"{BlockReturn(Block, x => x.BeamLength)} blocks");
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(WelderBeam);

            var DistanceMode = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("DistanceMode");
            DistanceMode.Title = MyStringId.GetOrCompute("Weld Furthest First");
            DistanceMode.Tooltip = MyStringId.GetOrCompute("If enabled, Laser Welder will try to complete furthest block first before proceeding on new one.");
            DistanceMode.SupportsMultipleBlocks = true;
            DistanceMode.Enabled = HasBlockLogic;
            DistanceMode.Visible = HasBlockLogic;
            DistanceMode.Getter = (Block) => BlockReturn(Block, x => x.DistanceMode);
            DistanceMode.Setter = (Block, NewMode) => BlockAction(Block, x => x.DistanceMode = NewMode);
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(DistanceMode);

            DebugWrite("InitControls", "Welder Controls inited.");
            InitedWelderControls = true;
        }

        public static bool InitedGrinderControls { get; protected set; } = false;
        public static void InitGrinderControls()
        {
            if (InitedGrinderControls) return;

            var GrinderBeam = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipGrinder>("BeamLength");
            GrinderBeam.Title = MyStringId.GetOrCompute("Beam Length");
            GrinderBeam.Tooltip = MyStringId.GetOrCompute("Sets the laser beam's length.");
            GrinderBeam.SupportsMultipleBlocks = true;
            GrinderBeam.Enabled = HasBlockLogic;
            GrinderBeam.Visible = HasBlockLogic;
            GrinderBeam.SetLimits(Block => BlockReturn(Block, x => x.MinBeamLengthBlocks), Block => BlockReturn(Block, x => x.MaxBeamLengthBlocks));
            GrinderBeam.Getter = (Block) => BlockReturn(Block, x => x.BeamLength);
            GrinderBeam.Setter = (Block, NewLength) => BlockAction(Block, x => x.BeamLength = (int)NewLength);
            GrinderBeam.Writer = (Block, Info) => Info.Append($"{BlockReturn(Block, x => x.BeamLength)} blocks");
            MyAPIGateway.TerminalControls.AddControl<IMyShipGrinder>(GrinderBeam);

            var DistanceMode = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipGrinder>("DistanceMode");
            DistanceMode.Title = MyStringId.GetOrCompute("Grind Closest First");
            DistanceMode.Tooltip = MyStringId.GetOrCompute("If enabled, Laser Grinder will dismantle closest block first before proceeding on new one.");
            DistanceMode.SupportsMultipleBlocks = true;
            DistanceMode.Enabled = HasBlockLogic;
            DistanceMode.Visible = HasBlockLogic;
            DistanceMode.Getter = (Block) => BlockReturn(Block, x => x.DistanceMode);
            DistanceMode.Setter = (Block, NewMode) => BlockAction(Block, x => x.DistanceMode = NewMode);
            MyAPIGateway.TerminalControls.AddControl<IMyShipGrinder>(DistanceMode);


            DebugWrite("InitControls", "Grinder Controls inited.", DebugPrefix: "LaserWelders.");
            InitedGrinderControls = true;
        }

        public static void DebugWrite(string Source, string Message, bool IsExcessive = false, string DebugPrefix = null)
        {
            try
            {
                if (DebugPrefix == null) DebugPrefix = $"{ModName}.";
                if (Debug && (!IsExcessive || VerboseDebug))
                {
                    MyAPIGateway.Utilities.ShowMessage(DebugPrefix + Source, $"Debug message: {Message}");
                    MyLog.Default.WriteLine(DebugPrefix + Source + $": Debug message: {Message}");
                    MyLog.Default.Flush();
                }
            }
            catch { }
        }

        public static void LogError(string Source, Exception Scrap, bool IsExcessive = false, string DebugPrefix = null)
        {
            try
            {
                if (DebugPrefix == null) DebugPrefix = $"{ModName}.";
                if (Debug && (!IsExcessive || VerboseDebug))
                {
                    MyAPIGateway.Utilities.ShowMessage(DebugPrefix + Source, $"CRASH: '{Scrap.Message}'");
                    MyLog.Default.WriteLine(Scrap);
                    MyLog.Default.Flush();
                }
            }
            catch { }
        }
    }
}
