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
using Sandbox.Game;
using ProtoBuf;
using Cheetah.Networking;
using SpaceEngineers.Game.ModAPI;

namespace Cheetah.Radars
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_ShipWelder), false, "LargeShipLaserWelder", "SmallShipLaserWelder")]
    public class WelderLogic : MyGameLogicComponent
    {
        IMyShipToolBase Tool { get; set; }
        bool IsWelder => Tool is IMyShipWelder;
        bool IsGrinder => Tool is IMyShipGrinder;
        float WorkCoefficient => MyShipGrinderConstants.GRINDER_COOLDOWN_IN_MILISECONDS * 0.001f;
        float GrinderSpeed => MyAPIGateway.Session.GrinderSpeedMultiplier * MyShipGrinderConstants.GRINDER_AMOUNT_PER_SECOND * WorkCoefficient / 4;
        float WelderSpeed => MyAPIGateway.Session.WelderSpeedMultiplier * 2 * WorkCoefficient / 4; // 2 is WELDER_AMOUNT_PER_SECOND from MyShipWelder.cs
        float WelderBoneRepairSpeed => 0.6f * WorkCoefficient; // 0.6f is WELDER_MAX_REPAIR_BONE_MOVEMENT_SPEED from MyShipWelder.cs
        IMyInventory ToolCargo { get; set; }
        HashSet<IMyCubeBlock> OnboardInventoryOwners = new HashSet<IMyCubeBlock>();
        IMyCubeGrid Grid;
        IMyGridTerminalSystem Term;
        MyResourceSinkComponent MyPowerSink;
        float SuppliedPowerRatio => Tool.ResourceSink.SuppliedRatioByType(Electricity);
        public static MyDefinitionId Electricity { get; } = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Electricity");
        /// <summary>
        /// Grid block size, in meters.
        /// </summary>
        float GridBlockSize => Grid.GridSize;
        Vector3I BlockDimensions => (Tool.SlimBlock.BlockDefinition as MyCubeBlockDefinition).Size;
        Vector3D BlockPosition => Tool.GetPosition();
        AutoSet<bool> SyncDistanceMode;
        public bool DistanceMode
        {
            get { return SyncDistanceMode.Get(); }
            set { SyncDistanceMode.Set(value); }
        }
        AutoSet<float> SyncBeamLength;
        public int BeamLength
        {
            get { return (int)SyncBeamLength.Get(); }
            set { SyncBeamLength.Set(value); }
        }
        public float MinBeamLengthM => MinBeamLengthBlocks * GridBlockSize;
        public float MaxBeamLengthM => MaxBeamLengthBlocks * GridBlockSize;
        public int MinBeamLengthBlocks => 1;
        public int MaxBeamLengthBlocks => Grid.GridSizeEnum == MyCubeSize.Small ? 30 : 8;
        Vector3D BlockForwardEnd => Tool.WorldMatrix.Forward * GridBlockSize * (BlockDimensions.Z) / 2;
        Vector3 LaserEmitterPosition
        {
            get
            {
                var EmitterDummy = Tool.Model.GetDummy("Laser_Emitter");
                return EmitterDummy != null ? EmitterDummy.Matrix.Translation : (Vector3)BlockForwardEnd;
            }
        }
        Vector3D BeamStart => BlockPosition + LaserEmitterPosition;
        Vector3D BeamEnd => BeamStart + Tool.WorldMatrix.Forward * BeamLength * GridBlockSize * SuppliedPowerRatio;
        Color InternalBeamColor { get; } = Color.WhiteSmoke;
        Color ExternalWeldBeamColor { get; } = Color.DeepSkyBlue;
        Color ExternalGrindBeamColor { get; } = Color.IndianRed;
        IMyHudNotification DebugNote;
        System.Diagnostics.Stopwatch Watch = new System.Diagnostics.Stopwatch();
        IMyCubeGrid ClosestGrid;
        Dictionary<string, int> MissingComponents = new Dictionary<string, int>();
        /// <summary>
        /// In milliseconds
        /// </summary>
        Queue<float> LastRunTimes = new Queue<float>();
        const float RunTimeCacheSize = 120;
        bool RunTimesAvailable => LastRunTimes.Count > 0;
        float AvgRunTime => LastRunTimes.Average();
        float MaxRunTime => LastRunTimes.Max();
        ushort Ticks = 0;


        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
            Tool = Entity as IMyShipToolBase;
            Grid = Tool.CubeGrid;
            SessionCore.SaveRegister(Save);
            Tool.ResourceSink.SetMaxRequiredInputByType(Electricity, (float)Math.Pow(1.2, MaxBeamLengthM));
            //Tool.ResourceSink.SetRequiredInputFuncByType(Electricity, PowerConsumptionFunc);
            if (!Tool.HasComponent<MyModStorageComponent>())
            {
                Tool.Storage = new MyModStorageComponent();
                Tool.Components.Add(Tool.Storage);
                SessionCore.DebugWrite($"{Tool.CustomName}.Init()", "Block doesn't have a Storage component!", IsExcessive: false);
            }
        }

        #region Loading stuff
        [ProtoContract]
        public struct Persistent
        {
            [ProtoMember(1)]
            public float BeamLength;
            [ProtoMember(2)]
            public bool DistanceBased;
        }

        public void Load()
        {
            try
            {
                string Storage = "";
                //if (Tool.Storage.ContainsKey(SessionCore.StorageGuid))
                if (MyAPIGateway.Utilities.GetVariable($"settings_{Tool.EntityId}", out Storage))
                {
                    byte[] Raw = Convert.FromBase64String(Storage);
                    try
                    {
                        Persistent persistent = MyAPIGateway.Utilities.SerializeFromBinary<Persistent>(Raw);
                        SyncBeamLength.Set(persistent.BeamLength);
                        SyncDistanceMode.Set(persistent.DistanceBased);
                        SessionCore.DebugWrite($"{Tool.CustomName}.Load()", $"Loaded from storage. beamlength={persistent.BeamLength}");
                    }
                    catch (Exception Scrap)
                    {
                        SessionCore.LogError($"{Tool.CustomName}.Load()", Scrap);
                    }
                }
                else
                {
                    SessionCore.DebugWrite($"{Tool.CustomName}.Load()", "Storage access failed.");
                }
            }
            catch (Exception Scrap)
            {
                SessionCore.LogError($"{Tool.CustomName}.Load().AccessStorage", Scrap);
            }
        }

        public void Save()
        {
            try
            {
                Persistent persistent;
                persistent.BeamLength = SyncBeamLength.Get();
                persistent.DistanceBased = SyncDistanceMode.Get();
                string Raw = Convert.ToBase64String(MyAPIGateway.Utilities.SerializeToBinary(persistent));
                MyAPIGateway.Utilities.SetVariable($"settings_{Tool.EntityId}", Raw);
                SessionCore.DebugWrite($"{Tool.CustomName}.Load()", "Set settings to storage.");
            }
            catch (Exception Scrap)
            {
                SessionCore.LogError($"{Tool.CustomName}.Save()", Scrap);
            }
        }

        public override void Close()
        {
            try
            {
                if (SessionCore.Debug)
                {
                    DebugNote.Hide();
                    DebugNote.AliveTime = 0;
                    DebugNote = null;
                }
            }
            catch (Exception Scrap)
            {
                SessionCore.LogError($"{Tool.CustomName}.Close().DebugClose", Scrap);
            }
            try
            {
                SessionCore.SaveUnregister(Save);
                SyncBeamLength.Close();
                SyncDistanceMode.Close();
            }
            catch (Exception Scrap)
            {
                SessionCore.LogError($"{Tool.CustomName}.Close()", Scrap);
            }
        }
        #endregion

        public override void UpdateOnceBeforeFrame()
        {
            try
            {
                if (Tool.CubeGrid.Physics == null)
                {
                    NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                    return;
                }
                Term = Grid.GetTerminalSystem();
                ToolCargo = Tool.GetInventory();
                CheckInitControls();
                SyncBeamLength = new AutoSet<float>(Tool, "BeamLength", Checker: val => val >= MinBeamLengthBlocks && val <= MaxBeamLengthBlocks);
                SyncDistanceMode = new AutoSet<bool>(Tool, "DistanceBasedMode");
                SyncBeamLength.Ask();
                SyncDistanceMode.Ask();
                BeamLength = 1;
                Tool.AppendingCustomInfo += Tool_AppendingCustomInfo;
                Load();
                NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_100TH_FRAME;
                DebugNote = MyAPIGateway.Utilities.CreateNotification($"{Tool.CustomName}", int.MaxValue, (IsWelder ? "Blue" : "Red"));
                if (SessionCore.Debug) DebugNote.Show();
            }
            catch { }
        }

        private void Tool_AppendingCustomInfo(IMyTerminalBlock trash, StringBuilder Info)
        {
            Info.Clear();
            Info.AppendLine($"Current Input: {Math.Round(Tool.ResourceSink.RequiredInputByType(Electricity),2)} MW");
            Info.AppendLine($"Max Required Input: {Math.Round(Math.Pow(1.2, BeamLength * GridBlockSize), 2)} MW");
            Info.AppendLine($"Performance impact: {(RunTimesAvailable ? Math.Round(AvgRunTime, 4).ToString() : "--")}/{(RunTimesAvailable ? Math.Round(MaxRunTime, 4).ToString() : "--")} ms (avg/max)");
            if (IsWelder && MissingComponents.Count > 0)
            {
                Info.AppendLine($"Total Missing Components on Welded Grid:\n");
                foreach(var ItemPair in MissingComponents)
                {
                    Info.AppendLine($"{ItemPair.Key}: {ItemPair.Value}");
                }
            }
        }

        void CheckInitControls()
        {
            if (IsWelder)
            {
                if (!SessionCore.InitedWelderControls) SessionCore.InitWelderControls();
            }
            else if (IsGrinder)
            {
                if (!SessionCore.InitedGrinderControls) SessionCore.InitGrinderControls();
            }
        }

        void Main(int ticks)
        {
            try
            {
                Tool.ResourceSink.SetRequiredInputByType(Electricity, PowerConsumptionFunc());
                if (Tool.IsToolWorking())
                {
                    Work(ticks);
                    DrawBeam();
                }
                else
                {
                    DebugNote.Text = $"{Tool.CustomName}: idle";
                    ClosestGrid = null;
                    MissingComponents.Clear();
                }
                Tool.RefreshCustomInfo();
                if (SessionCore.Debug) DebugNote.Text = $"{Tool.CustomName} perf. impact: {(RunTimesAvailable ? Math.Round(AvgRunTime, 5).ToString() : "--")}/{(RunTimesAvailable ? Math.Round(MaxRunTime, 5).ToString() : "--")} ms (avg/max)";
            }
            catch (Exception Scrap)
            {
                SessionCore.LogError(Tool.CustomName, Scrap);
            }
        }

        void Aux(int ticks)
        {
            if (Tool.IsToolWorking()) DrawBeam();
        }

        public override void UpdateBeforeSimulation()
        {
            Watch.Start();
            Aux(ticks: 1);

            Ticks += 1;
            if (Ticks >= SessionCore.WorkSkipTicks)
            {
                Ticks = 0;
                Main(ticks: SessionCore.WorkSkipTicks);
            }
            
            Watch.Stop();
            if (LastRunTimes.Count >= RunTimeCacheSize) LastRunTimes.Dequeue();
            LastRunTimes.Enqueue(1000 * (Watch.ElapsedTicks / System.Diagnostics.Stopwatch.Frequency));
            Watch.Reset();
        }

        public override void UpdateAfterSimulation100()
        {
            if (Tool.UseConveyorSystem) BuildInventoryCache();
            //if (IsWelder && ClosestGrid != null) MissingComponents = ClosestGrid.CalculateMissingComponents();
        }

        /// <summary>
        /// Builds cache of accessible inventories on this ship.
        /// </summary>
        /// <param name="Force">Forces to build cache even if UseConveyorSystem == false.</param>
        void BuildInventoryCache(bool Force = false)
        {
            if (!Tool.UseConveyorSystem && !Force) return;
            List<IMyTerminalBlock> InventoryOwners = new List<IMyTerminalBlock>();
            Term.GetBlocksOfType(InventoryOwners, x => x.HasPlayerAccess(Tool.OwnerId) && x.HasInventory);
            OnboardInventoryOwners.Clear();
            OnboardInventoryOwners.Add(Tool);
            foreach (var InventoryOwner in InventoryOwners)
            {
                if (InventoryOwner == Tool) continue;
                foreach (var Inventory in InventoryOwner.GetInventories())
                {
                    if (Inventory == ToolCargo) continue;
                    if (Inventory.IsConnectedTo(ToolCargo)) OnboardInventoryOwners.Add(InventoryOwner);
                }
            }
        }



        void Weld(List<IMySlimBlock> Blocks, int ticks = 1)
        {
            var Welder = Tool as IMyShipWelder;
            if (Welder == null) return;
            Blocks = Blocks.Where(x => x.IsWeldable() || x.IsProjectable()).ToList();
            if (Blocks.Count == 0) return;
            if (DistanceMode) Blocks = Blocks.OrderByDescending(x => Vector3D.DistanceSquared(x.GetPosition(), Tool.GetPosition())).ToList();
            float SpeedRatio = WelderSpeed / (DistanceMode ? 1 : Blocks.Count) * ticks;
            float BoneFixSpeed = WelderBoneRepairSpeed * ticks;
            var Missing = Blocks.ReadMissingComponents();
            bool Pull = Tool.UseConveyorSystem ? ToolCargo.PullAny(OnboardInventoryOwners, Missing) : false;
            Missing.Clear();
            HashSet<IMySlimBlock> UnbuiltBlocks = new HashSet<IMySlimBlock>();
            foreach (IMySlimBlock Block in Blocks)
            {
                if (Block.CubeGrid.Physics?.Enabled == true)
                {
                    if (Block.CanContinueBuild(ToolCargo) || Block.HasDeformation)
                    {
                        Block.MoveItemsToConstructionStockpile(ToolCargo);
                        Block.IncreaseMountLevel(SpeedRatio, Welder.OwnerId, ToolCargo, BoneFixSpeed, false);
                    }
                    else
                    {
                        UnbuiltBlocks.Add(Block);
                    }
                }
                else
                {
                    try
                    {
                        var FirstItem = ((MyCubeBlockDefinition)Block.BlockDefinition).Components[0].Definition.Id;
                        if (ToolCargo.PullAny(OnboardInventoryOwners, FirstItem.SubtypeName, 1))
                        {
                            try
                            {
                                var Projector = ((Block.CubeGrid as MyCubeGrid).Projector as IMyProjector);
                                Projector.Build(Block, 0, Tool.EntityId, false);
                            }
                            catch (Exception Scrap)
                            {
                                SessionCore.LogError(Tool.CustomName + ".WeldProjectorBlock.Build", Scrap);
                            }
                            ToolCargo.RemoveItemsOfType(1, FirstItem);
                        }
                    }
                    catch (Exception Scrap)
                    {
                        SessionCore.LogError(Tool.CustomName + ".WeldProjectorBlockStart", Scrap);
                    }
                }
                if (DistanceMode) break;
            }
            PrintMissing(UnbuiltBlocks);
        }

        void PrintMissing(HashSet<IMySlimBlock> UnbuiltBlocks)
        {
            //if (UnbuiltBlocks == null || UnbuiltBlocks.Count == 0) return;
            var Player = MyAPIGateway.Session.Player;
            if (Player == null) return;
            if (Player.IdentityId != Tool.OwnerId) return;
            if (Player.GetPosition().DistanceTo(Tool.GetPosition()) > 1000) return;

            StringBuilder Text = new StringBuilder();
            Text.AppendLine($"Performance impact: {(RunTimesAvailable ? Math.Round(AvgRunTime, 4).ToString() : "--")}/{(RunTimesAvailable ? Math.Round(MaxRunTime, 4).ToString() : "--")} ms (avg/max)");
            if (UnbuiltBlocks.Count == 1)
            {
                IMySlimBlock Block = UnbuiltBlocks.First();
                Text.AppendLine($"{Tool.CustomName}: can't proceed to build {Block.BlockDefinition.DisplayNameText}, missing:\n");
                var Missing = Block.ReadMissingComponents();
                foreach (var ItemPair in Missing)
                {
                    Text.AppendLine($"{ItemPair.Key}: {ItemPair.Value}");
                }
            }
            else if (UnbuiltBlocks.Count > 1)
            {
                Text.AppendLine($"{Tool.CustomName}: can't proceed to build {UnbuiltBlocks.Count} blocks:\n");
                foreach (IMySlimBlock Block in UnbuiltBlocks)
                {
                    var Missing = Block.ReadMissingComponents();
                    Text.AppendLine($"{Block.BlockDefinition.DisplayNameText}: missing:");
                    foreach (var ItemPair in Missing)
                    {
                        Text.AppendLine($"{ItemPair.Key}: {ItemPair.Value}");
                    }
                    Text.AppendLine();
                }
            }
            Text.RemoveTrailingNewlines();
            IMyHudNotification hud = MyAPIGateway.Utilities.CreateNotification(Text.ToString(), (int)(SessionCore.TickLengthMs * (SessionCore.WorkSkipTicks+1)), "Red"); // Adding 1 excess tick is needed, otherwise notification can flicker
            hud.Show();
        }

        void Grind(List<IMySlimBlock> Blocks, int ticks = 1)
        {
            var Grinder = Tool as IMyShipGrinder;
            if (Grinder == null) return;
            Blocks = Blocks.Where(x => x.IsGrindable() && x != Grinder.SlimBlock).ToList();
            if (Blocks.Count == 0) return;
            if (DistanceMode) Blocks = Blocks.OrderBy(x => Vector3D.DistanceSquared(x.GetPosition(), Tool.GetPosition())).ToList();
            float SpeedRatio = GrinderSpeed / (DistanceMode ? 1 : Blocks.Count) * ticks;
            foreach (IMySlimBlock Block in Blocks)
            {
                Block.MoveItemsFromConstructionStockpile(ToolCargo);
                Block.DecreaseMountLevel(SpeedRatio, ToolCargo, useDefaultDeconstructEfficiency: true);
                if (Block.FatBlock?.IsFunctional == false && Block.FatBlock?.HasInventory == true)
                {
                    foreach (var Inventory in Block.FatBlock.GetInventories())
                    {
                        if (Inventory.CurrentVolume == VRage.MyFixedPoint.Zero) continue;
                        foreach (var Item in Inventory.GetItems())
                        {
                            var Amount = Inventory.ComputeAmountThatFits(Item);
                            ToolCargo.TransferItemFrom(Inventory, (int)Item.ItemId, null, null, Amount, false);
                        }
                    }
                }
                if (Block.IsFullyDismounted) Block.CubeGrid.RazeBlock(Block.Position);
                if (DistanceMode) break;
            }
        }

        void Work(int ticks = 1)
        {
            if (!MyAPIGateway.Multiplayer.IsServer) return;
            LineD WeldRay = new LineD(BeamStart, BeamEnd);
            List<IHitInfo> Hits = new List<IHitInfo>();
            List<MyLineSegmentOverlapResult<MyEntity>> Overlaps = new List<MyLineSegmentOverlapResult<MyEntity>>();
            MyGamePruningStructure.GetTopmostEntitiesOverlappingRay(ref WeldRay, Overlaps);

            List<IMyCubeGrid> Grids = new List<IMyCubeGrid>();
            List<IMyCharacter> Characters = new List<IMyCharacter>();
            List<IMyFloatingObject> Flobjes = new List<IMyFloatingObject>();
            Overlaps.Select(x => x.Element as IMyEntity).SortByType(Grids, Characters, Flobjes);

            ClosestGrid = Grids.OrderBy(Grid => Vector3D.DistanceSquared(Tool.GetPosition(), Grid.GetPosition())).FirstOrDefault();
            foreach (IMyCharacter Char in Characters)
            {
                Char.DoDamage(GrinderSpeed, MyDamageType.Grind, true, null, Tool.EntityId);
            }

            foreach (IMyCubeGrid Grid in Grids)
            {
                if (Grid == Tool.CubeGrid) continue;
                try
                {
                    List<IMySlimBlock> Blocks = Grid.GetBlocksOnRay(WeldRay.From, WeldRay.To);
                    //if (!SessionCore.Debug && Blocks.Count == 0) return;

                    if (IsWelder) Weld(Blocks, ticks);
                    if (IsGrinder) Grind(Blocks, ticks);
                }
                catch (Exception Scrap)
                {
                    SessionCore.LogError(Grid.DisplayName, Scrap);
                }
            }

            foreach (IMyFloatingObject Flobj in Flobjes)
            {
                ToolCargo.PickupItem(Flobj);
            }
        }

        void DrawBeam()
        {
            if (MyAPIGateway.Session.Player == null) return;
            var Internal = InternalBeamColor.ToVector4();
            var External = Vector4.Zero;
            if (IsWelder) External = ExternalWeldBeamColor.ToVector4();
            if (IsGrinder) External = ExternalGrindBeamColor.ToVector4();
            var BeamStart = this.BeamStart;
            var BeamEnd = this.BeamEnd;
            MySimpleObjectDraw.DrawLine(BeamStart, BeamEnd, MyStringId.GetOrCompute("WeaponLaser"), ref Internal, 0.1f);
            MySimpleObjectDraw.DrawLine(BeamStart, BeamEnd, MyStringId.GetOrCompute("WeaponLaser"), ref External, 0.2f);
        }

        public override void UpdatingStopped()
        {
            
        }

        float PowerConsumptionFunc()
        {
            try
            {
                return Tool.IsToolWorking() ? (float)Math.Pow(1.2, BeamLength * GridBlockSize) : 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}