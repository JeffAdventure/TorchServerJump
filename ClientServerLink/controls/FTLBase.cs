/**
 * Script is Copyright © 2014, Phoenix
 * I know, this code is a mess :-(
 **/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Sandbox.Common;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Engine;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using Sandbox.Game.GameSystems.Electricity;
using VRage;
using VRage.Game.Components;
using VRage.ObjectBuilders;
using VRage.Game;
using VRage.ModAPI;
using VRageMath;
using Sandbox.Game.Entities;
using VRage.Game.ModAPI;
using SpaceEngineers.Game.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using Sandbox.Game.EntityComponents;

namespace Phoenix.FTL
{
    /// <summary>
    /// Base class for core FTL function.
    /// This may or may not be removed after gyro is gone
    /// </summary>
    public abstract class FTLBase : MyGameLogicComponent
    {
        #region Common Data
        protected MyObjectBuilder_EntityBase m_objectBuilder = null;
        protected int m_counter = 0;
        protected IMyFunctionalBlock m_ftl = null;
        protected MyEntity3DSoundEmitter m_soundEmitter;
        protected bool m_playedSound = false;
        public MyEntity3DSoundEmitter SoundEmitter { get { return m_soundEmitter; } }
        #endregion

        #region FTL Data
        // This is temporarily public while I move everything back into this class
        public FTLData Data { get { return m_ftld; } }
        public FTLData m_ftld;
        #endregion

        #region Entity Events
        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return (copy && m_objectBuilder != null ? m_objectBuilder.Clone() as MyObjectBuilder_EntityBase : m_objectBuilder);
        }

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            m_objectBuilder = objectBuilder;
            m_ftl = Container.Entity as IMyFunctionalBlock;
            base.Init(objectBuilder);
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
            m_ftld = InitFTLData();
            IMyCubeBlock ftl = m_ftl as IMyCubeBlock;

            // Set up upgrades
            if (!ftl.UpgradeValues.ContainsKey(ModifierType.Range.ToString()))
            {
                ftl.UpgradeValues.Add(ModifierType.Range.ToString(), m_ftld.rangeFactor);
                ftl.UpgradeValues.Add(ModifierType.Spool.ToString(), m_ftld.spoolFactor);
                ftl.UpgradeValues.Add(ModifierType.Accuracy.ToString(), m_ftld.accuracyFactor);
                ftl.UpgradeValues.Add(ModifierType.PowerEfficiency.ToString(), m_ftld.powerFactor);
            }
            ftl.OnUpgradeValuesChanged += UpgradeValuesChanged;
            m_ftl.AppendingCustomInfo += AppendingCustomInfo;
            m_ftl.PropertiesChanged += FTLBase_PropertiesChanged;

            m_soundEmitter = new MyEntity3DSoundEmitter(Container.Entity as VRage.Game.Entity.MyEntity);
        }

        void CustomNameChanged(IMyTerminalBlock ftl)
        {
            ParseNameArguments();
        }

        void OnClose(IMyEntity obj)
        {
            m_ftl.CustomNameChanged -= CustomNameChanged;
            m_ftl.AppendingCustomInfo -= AppendingCustomInfo;
            m_ftl.OnClose -= OnClose;

            var parent = m_ftl as IMyCubeBlock;
            parent.OnUpgradeValuesChanged -= UpgradeValuesChanged;
            parent.UpgradeValues.Clear();

            (m_ftl.CubeGrid as IMyCubeGrid).OnBlockAdded -= BlockAdded;
            (m_ftl.CubeGrid as IMyCubeGrid).OnBlockRemoved -= BlockRemoved;
        }

        #endregion

        #region Terminal Controls
        protected static List<Type> m_ControlsInited = new List<Type>();
        static IMyTerminalControlSeparator sep;

        protected virtual void CreateTerminalControls<T>()
        {
            if (m_ControlsInited.Contains(typeof(T)))
                return;

            m_ControlsInited.Add(typeof(T));

            MyAPIGateway.TerminalControls.CustomControlGetter -= TerminalControls_CustomControlGetter;
            MyAPIGateway.TerminalControls.CustomActionGetter -= TerminalControls_CustomActionGetter;

            MyAPIGateway.TerminalControls.CustomControlGetter += TerminalControls_CustomControlGetter;
            MyAPIGateway.TerminalControls.CustomActionGetter += TerminalControls_CustomActionGetter;

            // Separator
            if (sep == null)
            {
                sep = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyTerminalBlock>(string.Empty);
                sep.Visible = (b) => b.BlockDefinition.SubtypeId.Contains("FTL");
            }
            MyAPIGateway.TerminalControls.AddControl<T>(sep);

            var gpsAction = MyAPIGateway.TerminalControls.CreateProperty<Vector3D?, T>("Phoenix.FTL.OneTimeDestination");
            if (gpsAction != null)
            {
                StringBuilder actionname = new StringBuilder();
                gpsAction.Enabled = (b) => b.IsFTL() && b.IsWorking;
                gpsAction.Setter = (b, v) => MessageUtils.SendMessageToServer(new MessageGPS() { FTLId = b.EntityId, Destination = (v.HasValue ? new SerializableVector3D?(v.Value) : null) });
                gpsAction.Getter = (b) => b.GameLogic.GetAs<FTLBase>().Data.jumpTargetGPS != null ? new Vector3D?(b.GameLogic.GetAs<FTLBase>().Data.jumpTargetGPS.Coords) : null;
                MyAPIGateway.TerminalControls.AddControl<T>(gpsAction);
            }

            var jumpStateProperty = MyAPIGateway.TerminalControls.CreateProperty<string, T>("Phoenix.FTL.State");
            if (jumpStateProperty != null)
            {
                StringBuilder actionname = new StringBuilder();
                jumpStateProperty.Enabled = (b) => b.IsFTL() && b.IsWorking;
                jumpStateProperty.Setter = (b, v) => { };               // No setter, but define one, otherwise crash
                jumpStateProperty.Getter = (b) => b.GameLogic.GetAs<FTLBase>().Data.jumpState.ToString();
                MyAPIGateway.TerminalControls.AddControl<T>(jumpStateProperty);
            }

            var timeProperty = MyAPIGateway.TerminalControls.CreateProperty<long, T>("Phoenix.FTL.TimeRemaining");
            if (timeProperty != null)
            {
                StringBuilder actionname = new StringBuilder();
                timeProperty.Enabled = (b) => b.IsFTL() && b.IsWorking;
                timeProperty.Setter = (b, v) => { };                    // No setter, but define one, otherwise crash
                timeProperty.Getter = (b) =>
                    {
                        if (b.GameLogic.GetAs<FTLBase>().Data.jumpState == JumpState.Spooling)
                            return (long)Math.Max((long)0, Math.Round((b.GameLogic.GetAs<FTLBase>().Data.jumpTime - DateTime.Now).TotalSeconds));
                        else if(b.GameLogic.GetAs<FTLBase>().Data.jumpState == JumpState.Cooldown)
                            return (long)Math.Max((long)0, Math.Round((b.GameLogic.GetAs<FTLBase>().Data.resetTime - DateTime.Now).TotalSeconds));
                        else
                            return 0;
                    };
                MyAPIGateway.TerminalControls.AddControl<T>(timeProperty);
            }

            var jumpDistanceProperty = MyAPIGateway.TerminalControls.CreateProperty<float, T>("Phoenix.FTL.JumpDistanceInMeters");
            if (jumpDistanceProperty != null)
            {
                StringBuilder actionname = new StringBuilder();
                jumpDistanceProperty.Enabled = (b) => b.IsFTL() && b.IsWorking;
                jumpDistanceProperty.Getter = (b) => b.GameLogic.GetAs<FTLBase>().Data.jumpDistance;
                jumpDistanceProperty.Setter = (b, v) => b.GameLogic.GetAs<FTLBase>().SendDistanceChange(v);
                MyAPIGateway.TerminalControls.AddControl<T>(jumpDistanceProperty);
            }
        }

        static void TerminalControls_CustomActionGetter(IMyTerminalBlock block, List<IMyTerminalAction> actions)
        {
            if (block is IMyTerminalBlock)
            {
                string subtype = block.BlockDefinition.SubtypeId;
                var itemsToRemove = new List<IMyTerminalAction>();

                foreach (var action in actions)
                {
                    //Logger.Instance.LogMessage("Action: " + action.Id);
                    switch (subtype)
                    {
                        case "SmallFTL":
                        case "LargeFTL":
                        case "SmallFTLMed":
                        case "LargeFTLMed":
                        case "SmallFTLSml":
                        case "LargeFTLSml":
                        case "Phoenix_FTL_LargeShipLargeFTL":
                            if (
                                action.Id.StartsWith("OnOff") ||
                                action.Id.StartsWith("Jump") ||
                                action.Id.StartsWith("IncreaseJumpDistance") ||
                                action.Id.StartsWith("DecreaseJumpDistance") ||
                                action.Id.StartsWith("Phoenix.FTL")
                                )
                                break;
                            else
                                itemsToRemove.Add(action);
                            break;
                    }
                }

                foreach (var action in itemsToRemove)
                {
                    actions.Remove(action);
                }
            }
        }

        static void TerminalControls_CustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if (block is IMyTerminalBlock)
            {
                string subtype = block.BlockDefinition.SubtypeId;
                var itemsToRemove = new List<IMyTerminalControl>();
                int separatorsToKeep = 2;

                foreach (var control in controls)
                {
                    //Logger.Instance.LogMessage("Control: " + control.Id);
                    switch (subtype)
                    {
                        case "SmallFTL":
                        case "LargeFTL":
                        case "SmallFTLMed":
                        case "LargeFTLMed":
                        case "SmallFTLSml":
                        case "LargeFTLSml":
                        case "Phoenix_FTL_LargeShipLargeFTL":
                            switch (control.Id)
                            {
                                case "OnOff":
                                case "ShowInTerminal":
                                case "ShowInToolbarConfig":
                                case "Name":
                                case "ShowOnHUD":
                                case "Jump":
                                case "JumpDistance":
                                case "SelectedTarget":
                                case "RemoveBtn":
                                case "SelectBtn":
                                case "GpsList":
                                    break;
                                default:
                                    if (control.Id.StartsWith("Phoenix.FTL"))
                                        break;
                                    if (control is IMyTerminalControlSeparator && separatorsToKeep-- > 0)
                                        break;
                                    itemsToRemove.Add(control);
                                    break;
                            }
                            // Reorder new buttons on stock jump drive, so they are consistent
                            if( subtype.StartsWith("Phoenix_FTL_"))
                            {

                            }
                            break;
                    }
                }

                foreach (var action in itemsToRemove)
                {
                    controls.Remove(action);
                }
            }
        }

        public void RequestJump()
        {
            if (m_ftld.jumpState == JumpState.Idle && IsFTLValid())
                BeginSpooling();
            else if( m_ftld.jumpState == JumpState.Spooling)
                AbortJump();
        }
        #endregion

        #region Update Methods
        public override void UpdateOnceBeforeFrame()
        {
            //LoadTerminalValues();
            SetupPowerSink();
        }

        public override void UpdateBeforeSimulation10()
        {
            FTLData ftld = m_ftl.GetFTLData();

            if (!m_ftl.Enabled)
            {
                // Allow LCD updates when disabled, as all it'll do is put 'FTL Status: Disabled' in the LCD.
                if ((++m_counter % 30) == 0)  // Only allow this to run every 5 seconds
                {
                    if (MyAPIGateway.Multiplayer.IsServer)
                        RefreshLCDs();
                }
                return;
            }

            if (((ftld.flags & JumpFlags.SlaveMode) == JumpFlags.SlaveMode))
            {
                // The slave is controlled by the master FTL drive
                if (m_ftl.IsFunctional)
                {
                    if (ftld.SlaveActive && !m_ftl.Enabled)
                        SetFTLPower("OnOff_On");
                    if (!ftld.SlaveActive && m_ftl.Enabled)
                        SetFTLPower("OnOff_Off");
                }
            }
            else
            {
                UpdateParticleEffects();
                if ((++m_counter % 6) == 0)  // Only allow this to run every second
                {
                    if (MyAPIGateway.Multiplayer.IsServer)
                    {
                        UpdateFTLStats();
                        RefreshLCDs();
                        SetPower();
                    }
                    m_ftl.RefreshCustomInfo();

                    if (m_ftl.Enabled && ftld.jumpState == JumpState.Idle)
                    {
                        // TODO
                        if (MyAPIGateway.Multiplayer.IsServer)
                            ftld.jumpDest = CalculateJumpPosition(out ftld.jumpDirection);
                    }
                }
                //    if (obj.IsFunctional)
                //    {
                //        if (m_jumpState == JumpState.Idle && obj.IsWorking)
                //            SetFTLPower("OnOff_Off");
                //    }
            }
        }

        public override void UpdateAfterSimulation10()
        {
            FTLData ftld = m_ftl.GetFTLData();

            if( MyAPIGateway.Multiplayer.IsServer )
            {
                if (!m_ftl.IsWorking || !m_ftl.IsFunctional)
                {
                    if (ftld.jumpState == JumpState.Spooling)
                        AbortJump();

                    if (ftld.jumpState != JumpState.Idle && ftld.resetTime < DateTime.Now)
                        ResetFTL();
                }

                switch (ftld.jumpState)
                {
                    case JumpState.Idle:
                        break;
                    case JumpState.Spooling:
                        if (!IsFTLValid(false))
                        {
                            IsFTLValid();   // Actually show the error message this time
                            AbortJump();
                            return;
                        }
                        if (ftld.jumpTime < DateTime.Now)
                        {
                            Jump();
                            return;
                        }

                        long time = (long)(Math.Round((ftld.jumpTime - DateTime.Now).TotalSeconds));
                        if (time % 5 == 0 || time < 5)
                        {
                            DoCollisionChecks(time);
                            if (ftld.ShowMessage)
                            {
                                m_ftl.ShowMessageToUsersInRange("FTL: Jumping in " + time, (time > 5 ? 4900 : 900));
                                if (time == 10)
                                {
                                    SendPlaySound("Phoenix.FTL.JumpDriveCharging", true);
                                    MessageUtils.SendMessageToAll(new MessageWarpEffect() { FTLId = m_ftl.EntityId, Remove = false });
                                }

                                if (time == 3)
                                {
                                    m_playedSound = PlaySoundBlocks();

                                    if (!m_playedSound)
                                        SendPlaySound("Phoenix.FTL.JumpDriveJumpOut");
                                }

                                if (time == 1)
                                {
                                    ToggleLights();
                                }

                                if (time == 0)
                                {
                                    if (!m_playedSound)
                                        SendPlaySound("Phoenix.FTL.JumpDriveJumpIn");
                                }

                                ftld.ShowMessage = false;
                            }
                        }
                        break;
                    case JumpState.Jumped:
                        ftld.jumpState = JumpState.Cooldown;
                        SendPlaySound("Phoenix.FTL.JumpDriveRecharge");
                        ToggleCapacitors(false);
                        RunProgrammableBlocks();
                        RunTimers();
                        break;
                    case JumpState.Cooldown:
                        // Wait about a second before turning off the lights
                        if ((DateTime.Now - ftld.jumpTime).TotalSeconds > 1)
                            ToggleLights(true);
                        break;
                }

                if ((ftld.jumpState == JumpState.Jumped || ftld.jumpState == JumpState.Cooldown) && 
                    ftld.jumpTime <= DateTime.Now && ftld.resetTime <= DateTime.Now)
                {
                    ResetFTL();
                }
            }
        }
        #endregion

        #region Particle Effects
        private bool m_effectPlayed = false;
        private MyParticleEffect m_effect = null;
        private MyParticleEffect m_effectDest = null;

        public void PlayParticleEffects()
        {
            if (m_effectPlayed)
                return;

            m_effectPlayed = true;
            PlayParticleEffect(ref m_effect, false);
            PlayParticleEffect(ref m_effectDest, true);
        }

        public void PlayParticleEffect(ref MyParticleEffect effect, bool atDest = false)
        {
            if (MyParticlesManager.TryCreateParticleEffect("Warp", out effect))
            {
                effect.UserScale = (float)m_ftl.CubeGrid.PositionComp.WorldAABB.HalfExtents.AbsMax() / 15f;
                //effect.UserScale = (float)m_ftl.CubeGrid.PositionComp.WorldAABB.HalfExtents.AbsMax() / 25f;
                UpdateParticleEffect(effect, atDest);
            }
        }

        public void UpdateParticleEffects()
        {
            if (m_effectPlayed)
            {
                UpdateParticleEffect(m_effect, false);
                UpdateParticleEffect(m_effectDest, true);
            }
        }

        private void UpdateParticleEffect(MyParticleEffect effect, bool atDest = false)
        {
            if (effect == null)
                return;

            if (m_ftld.jumpState == JumpState.Spooling)
            {
                //Vector3D dir = Vector3D.Normalize(m_ftld.jumpDirection);  // Relative to jump direction
                Vector3D dir = Vector3D.Normalize(m_ftl.GetShipReference().Forward);  // Relative to grid
                MatrixD matrix = MatrixD.CreateFromDir(-dir);
                var offset = m_ftl.CubeGrid.PositionComp.GetPosition() - m_ftl.CubeGrid.PositionComp.WorldAABB.Center;
                var position = (atDest ? m_ftld.jumpDest - offset : m_ftl.CubeGrid.PositionComp.WorldAABB.Center);
                matrix.Translation = position + dir * m_ftl.CubeGrid.PositionComp.WorldAABB.HalfExtents.AbsMax() * 2f;
                effect.WorldMatrix = matrix;
            }
        }

        public void StopParticleEffects()
        {
            StopParticleEffect(m_effect);
            StopParticleEffect(m_effectDest);
        }

        public void StopParticleEffect(MyParticleEffect effect)
        {
            if (effect != null)
                effect.Stop();
        }
        #endregion

        #region Upgrades
        public virtual void UpgradeValuesChanged()
        {
            IMyCubeBlock ftl = m_ftl as IMyCubeBlock;
            try
            {
                //Logger.Instance.LogMessage(string.Format("UpgradeValuesChanged({0})",
                //                            ftl.GetPlayerRelationToOwner() == MyRelationsBetweenPlayerAndBlock.Owner ?
                //                            (ftl as IMyFunctionalBlock).CustomName : string.Empty));
                Logger.Instance.IndentLevel++;

                var ftld = (ftl as IMyFunctionalBlock).GetFTLData();
                ftld.rangeFactor = ftl.UpgradeValues[ModifierType.Range.ToString()];
                ftld.accuracyFactor = Math.Min(1.0f, ftl.UpgradeValues[ModifierType.Accuracy.ToString()]);        // Clamp to 100% max
                ftld.spoolFactor = ftl.UpgradeValues[ModifierType.Spool.ToString()];
                ftld.powerFactor = ftl.UpgradeValues[ModifierType.PowerEfficiency.ToString()];

                // Calculate Power
                // Only current mass of ship will be use for calculations.
                // Additional mass (such as hanger ships), will not be included
                // massless formula is P=distance ^ 1/1.6 * 7
                double power;
                double cubefactor = 1.0f;               // Cubefactor ensures same power for mass size, regardless of small or large cube
                var def = MyDefinitionManager.Static.GetDefinition(ftl.BlockDefinition);
                var lrgreactor = MyDefinitionManager.Static.GetDefinition(new MyDefinitionId(typeof(MyObjectBuilder_Reactor), "LargeBlockLargeGenerator"));
                var smlreactor = MyDefinitionManager.Static.GetDefinition(new MyDefinitionId(typeof(MyObjectBuilder_Reactor), "SmallBlockLargeGenerator"));

                if (ftl.CubeGrid.GridSizeEnum == MyCubeSize.Large)
                    cubefactor = (lrgreactor as MyReactorDefinition).MaxPowerOutput / (smlreactor as MyReactorDefinition).MaxPowerOutput;

                power = ftld.baseRange;
                power *= ftld.rangeFactor;                                      // Max power is based on current max range
                power = (Math.Pow(power, 1 / 1.6) * 7 * cubefactor);            // Calculate main power based on formula

                if (ftl.CubeGrid.Physics != null)                               // Add mass in (only of immedate ship)
                    power *= Math.Sqrt(ftl.CubeGrid.Physics.Mass /
                        (ftl.CubeGrid.GridSizeEnum == MyCubeSize.Large ? Globals.Instance.RuntimeConfig[ModifierType.LargeMass] : Globals.Instance.RuntimeConfig[ModifierType.SmallMass]));

                power *= ftld.spoolFactor;                                      // Increase power requirements when spool time is reduced

                ftld.power = (float)power / 1000;

                // TODO: Redo power to fix
                ftld.powerConsumptionMultiplier = ComputePowerMultiplier(power);

                //power /= ((def as MyGyroDefinition).RequiredPowerInput * 1000); // Get percentage difference
                //(ftl as IMyFunctionalBlock).PowerConsumptionMultiplier = (float)power;

                //var upgrades = new StringBuilder(50);

                //upgrades.Append(string.Format("Upgrade Accuracy: {0:P0}\r\n", ftld.accuracyFactor));
                //upgrades.Append(string.Format("Upgrade Range: {0}\r\n", ftld.rangeFactor));
                //upgrades.Append(string.Format("Upgrade Spool: {0:P0}\r\n", ftld.spoolFactor));
                //upgrades.Append(string.Format("Upgrade Power: {0:P0}\r\n", ftld.powerFactor));
                //upgrades.Append(string.Format("Power Multiplier: {0:F2}", (block as IMyFunctionalBlock).PowerConsumptionMultiplier));
                //Logger.Instance.LogMessage(upgrades.ToString());

                (ftl as IMyTerminalBlock).RefreshCustomInfo();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(string.Format("UpgradeValuesChanged({0})",
                                            ftl.GetPlayerRelationToOwner() == MyRelationsBetweenPlayerAndBlock.Owner ?
                                            (ftl as IMyFunctionalBlock).CustomName : string.Empty));
                Logger.Instance.LogException(ex);
            }
            finally
            {
                Logger.Instance.IndentLevel--;
            }
        }

        void BlockAdded(IMySlimBlock obj)
        {
            UpgradeValuesChanged();
        }

        void BlockRemoved(IMySlimBlock obj)
        {
            UpgradeValuesChanged();
        }
        #endregion

        #region Controlling other blocks
        public void ToggleCapacitors(bool off = false)
        {
            if (!MyAPIGateway.Multiplayer.IsServer)
                return;                                     // Only trigger on the server, actions will sync themselves

            var capblocks = GetCapacitors();

            try
            {
                // Toggle all found
                foreach (var entity in capblocks)
                {
                    var capblock = entity as IMyBatteryBlock;

                    //Logger.Instance.LogDebug("Found capacitor: " + capblock.DisplayNameText);

                    // Activate capacitor
                    if (capblock.IsWorking && m_ftl.IsBlockAccessible(capblock as IMyTerminalBlock))
                    {
                        // NOTE, this will NOT toggle the block On or Off, only adjust the Recharge setting.
                        // This allows the player to disable a capacitor so it won't be used.
                        //capblock.GetActionWithName((off ? "OnOff_Off" : "OnOff_On")).Apply(capblock);
                        var resource = capblock.Components.Get<Sandbox.Game.EntityComponents.MyResourceSourceComponent>();
                        if ((!off && resource.MaxOutputByType(FTLExtensions._powerDefinitionId) == 0) || (off && (resource.MaxOutputByType(FTLExtensions._powerDefinitionId) != 0 || !capblock.HasCapacityRemaining)))
                        {
                            capblock.GetActionWithName("Recharge").Apply(capblock);
                            Logger.Instance.LogDebug("Toggling capacitor: " + capblock.DisplayNameText);
                        }
                    }
                    else if (m_ftl.IsBlockAccessible(capblock as IMyTerminalBlock))
                    {
                        // We'll hit this case if the batteries were depleted
                        if (off && !capblock.HasCapacityRemaining)
                        {
                            capblock.GetActionWithName("Recharge").Apply(capblock);
                            Logger.Instance.LogDebug("Toggling capacitor: " + capblock.DisplayNameText);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogException(ex);
            }
        }

        public HashSet<IMyBatteryBlock> GetCapacitors()
        {
            var capob = new MyObjectBuilderType(typeof(MyObjectBuilder_BatteryBlock));
            var battblocks = m_ftl.GetGroupedBlocks(capob);
            var capblocks = new HashSet<IMyBatteryBlock>();

            if (battblocks.Count == 0)
            {
                List<IMySlimBlock> list = new List<IMySlimBlock>();

                // Get all the lightblocks on the entity
                if (m_ftl.GetTopMostParent() is IMyCubeGrid)
                {
                    (m_ftl.GetTopMostParent() as IMyCubeGrid).GetBlocks(list,
                        (x) => x.FatBlock != null && x.FatBlock.BlockDefinition.TypeId == capob);
                }

                // Search all of them finding the best one
                foreach (var entity in list)
                {
                    IMyFunctionalBlock capblock = entity.FatBlock as IMyFunctionalBlock;

                    if (capblock == null)
                        continue;

                    if (!string.IsNullOrEmpty(capblock.DisplayNameText) && capblock.DisplayNameText.Contains("FTL"))
                    {
                        // If a lcdblock contains the text FTL, always use it
                        battblocks.Add(capblock as IMyTerminalBlock);
                    }
                }
            }

            // Filter only ones that are FTL Capacitors
            foreach (var entity in battblocks)
            {
                var capblock = entity as IMyBatteryBlock;

                if (capblock == null)
                    continue;

                if (capblock.BlockDefinition.SubtypeId.Contains("FTLCap"))
                {
                    capblocks.Add(capblock);
                }
            }

            return capblocks;
        }

        public void ToggleLights(bool off = false)
        {
#if false
            // HACK: Work around game bug in ReorderClusters()
            // If the user jumps further than a cluster size (20k) in a limited world,
            // The game loses track of the entity during a cluster split.
            // World sizes less than 50km nearly always have connected clusters.
            if (MyAPIGateway.Session.SessionSettings.WorldSizeKm > 50)
            {
                if (!off)
                {
                    if (m_ftl.GetFTLData().jumpSafety == JumpSafety.Course)
                        m_ftl.GetFTLData().jumpCourse = m_ftl.PlotJumpCourse();
                    else if (m_ftl.GetFTLData().jumpSafety == JumpSafety.Trail)
                        m_ftl.GetFTLData().jumpHack = m_ftl.SpawnTrail();
                }
            }
            if (!off || (off && m_ftl.GetFTLData().jumpHack.Count > 0))
                MessageUtils.SendMessageToAll(new MessageWarpEffect() { FTLId = m_ftl.EntityId, Remove = off });
#endif

            if (!MyAPIGateway.Multiplayer.IsServer)
                return;                                     // Only trigger on the server, actions will sync themselves

            var reflightob = new MyObjectBuilderType(typeof(MyObjectBuilder_ReflectorLight));
            var lightob = new MyObjectBuilderType(typeof(MyObjectBuilder_LightingBlock));

            var reflightblocks = m_ftl.GetGroupedBlocks(reflightob);
            var lightblocks = m_ftl.GetGroupedBlocks(lightob);

            lightblocks.UnionWith(reflightblocks);

            if (lightblocks.Count == 0)
            {
                List<IMySlimBlock> list = new List<IMySlimBlock>();

                // Get all the lightblocks on the entity
                if (m_ftl.GetTopMostParent() is IMyCubeGrid)
                {
                    (m_ftl.GetTopMostParent() as IMyCubeGrid).GetBlocks(list,
                        (x) => x.FatBlock != null &&
                            (x.FatBlock.BlockDefinition.TypeId == reflightob || x.FatBlock.BlockDefinition.TypeId == lightob));
                }

                // Search all of them finding the best one
                foreach (var entity in list)
                {
                    Sandbox.ModAPI.IMyFunctionalBlock lightblock = entity.FatBlock as Sandbox.ModAPI.IMyFunctionalBlock;

                    if (lightblock == null)
                        continue;

                    if (!string.IsNullOrEmpty(lightblock.DisplayNameText) && lightblock.DisplayNameText.Contains("FTL"))
                    {
                        // If a lcdblock contains the text FTL, always use it
                        lightblocks.Add(lightblock as IMyTerminalBlock);
                    }
                }
            }

            // Toggle all found
            foreach (var entity in lightblocks)
            {
                IMyLightingBlock lightblock = entity as IMyLightingBlock;

                if (Globals.Debug)
                    Sandbox.ModAPI.MyAPIGateway.Utilities.ShowNotification("Found lightblock: " + lightblock.DisplayNameText, 3500);

                // Activate light
                if (m_ftl.IsBlockAccessible(lightblock as IMyTerminalBlock))
                    lightblock.GetActionWithName((off ? "OnOff_Off" : "OnOff_On")).Apply(lightblock);
            }
        }

        public bool PlaySoundBlocks(bool stop = false)
        {
            if (!MyAPIGateway.Multiplayer.IsServer)
                return false;                                     // Only trigger on the server, actions will sync themselves

            var objBuilderType = new MyObjectBuilderType(typeof(MyObjectBuilder_SoundBlock));
            var soundblocks = m_ftl.GetGroupedBlocks(objBuilderType);

            if (soundblocks.Count == 0)
            {
                List<IMySlimBlock> list = new List<IMySlimBlock>();

                // Get all the lcdblocks on the entity
                if (m_ftl.GetTopMostParent() is IMyCubeGrid)
                {
                    (m_ftl.GetTopMostParent() as IMyCubeGrid).GetBlocks(list,
                        (x) => x.FatBlock != null && x.FatBlock.BlockDefinition.TypeId == objBuilderType);
                }

                // Search all of them finding the best one
                foreach (var entity in list)
                {
                    IMySoundBlock soundblock = entity.FatBlock as IMySoundBlock;

                    if (soundblock == null || !soundblock.IsSoundSelected)
                        continue;

                    if (!string.IsNullOrEmpty(soundblock.DisplayNameText) && soundblock.DisplayNameText.Contains("FTL"))
                    {
                        // If a lcdblock contains the text FTL, always use it
                        soundblocks.Add(soundblock as IMyTerminalBlock);
                        break;
                    }
                }
            }

            // Play on all found
            foreach (var entity in soundblocks)
            {
                IMySoundBlock soundblock = entity as IMySoundBlock;

                if (Globals.Debug)
                    Sandbox.ModAPI.MyAPIGateway.Utilities.ShowNotification("Found soundblock: " + soundblock.DisplayNameText, 3500);

                // Play sound (player must have it configured for the correct sound)
                if (m_ftl.IsBlockAccessible(soundblock as IMyTerminalBlock))
                    soundblock.GetActionWithName((stop ? "StopSound" : "PlaySound")).Apply(soundblock);
            }

            if (soundblocks.Count == 0)
            {
                return false;
            }
            return true;
        }

        public void RunProgrammableBlocks(bool error = false)
        {
            HashSet<IMyEntity> hash = new HashSet<IMyEntity>();

            try
            {
                var progblocks = m_ftl.GetGroupedBlocks(new MyObjectBuilderType(typeof(MyObjectBuilder_MyProgrammableBlock)));

                if (progblocks.Count == 0)
                {
                    List<IMySlimBlock> blockList = new List<IMySlimBlock>();
                    IMyCubeGrid grid = m_ftl.GetTopMostParent() as IMyCubeGrid;
                    List<IMySlimBlock> blocks = new List<IMySlimBlock>();

                    grid.GetBlocks(blocks, (x) => x.FatBlock != null &&
                        x.FatBlock is IMyProgrammableBlock &&
                        (!string.IsNullOrEmpty((x.FatBlock as IMyTerminalBlock).CustomName)));

                    foreach (var block in blocks)
                    {
                        // Skip disabled or destroyed gates
                        if (block.IsDestroyed || block.FatBlock == null || !block.FatBlock.IsFunctional)
                            continue;

                        if (!string.IsNullOrEmpty((block.FatBlock as IMyTerminalBlock).CustomName))
                        {
                            string name = (block.FatBlock as IMyTerminalBlock).CustomName.ToUpperInvariant();

                            if (name.Contains("FTL"))
                                progblocks.Add(block.FatBlock as IMyTerminalBlock);
                        }
                    }
                }

                var ftld = m_ftl.GetFTLData();
                foreach (var block in progblocks)
                {
                    if (!(block as IMyTerminalBlock).HasPlayerAccess(m_ftl.OwnerId))
                        continue;

                    Logger.Instance.LogDebug(string.Format("Running programmable block {0} with arguments: {1}", block.DisplayNameText, ftld.jumpState.ToString()));
                    (block as IMyProgrammableBlock).TryRun((error ? "Error" : ftld.jumpState.ToString()));
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogException(ex);
            }
        }

        public void RunTimers(bool error = false)
        {
            HashSet<IMyEntity> hash = new HashSet<IMyEntity>();

            try
            {
                var progblocks = m_ftl.GetGroupedBlocks(new MyObjectBuilderType(typeof(MyObjectBuilder_TimerBlock)));

                if (progblocks.Count == 0)
                {
                    List<IMySlimBlock> blockList = new List<IMySlimBlock>();
                    IMyCubeGrid grid = m_ftl.GetTopMostParent() as IMyCubeGrid;
                    List<IMySlimBlock> blocks = new List<IMySlimBlock>();

                    grid.GetBlocks(blocks, (x) => x.FatBlock != null &&
                        x.FatBlock is IMyTimerBlock &&
                        (!string.IsNullOrEmpty((x.FatBlock as IMyTerminalBlock).CustomName)));

                    foreach (var block in blocks)
                    {
                        // Skip disabled or destroyed gates
                        if (block.IsDestroyed || block.FatBlock == null || !block.FatBlock.IsFunctional)
                            continue;

                        if (!string.IsNullOrEmpty((block.FatBlock as IMyTerminalBlock).CustomName))
                        {
                            string name = (block.FatBlock as IMyTerminalBlock).CustomName.ToUpperInvariant();

                            if (name.Contains("FTL"))
                                progblocks.Add(block.FatBlock as IMyTerminalBlock);
                        }
                    }
                }

                var ftld = m_ftl.GetFTLData();
                foreach (var block in progblocks)
                {
                    if (!(block as IMyTerminalBlock).HasPlayerAccess(m_ftl.OwnerId))
                        continue;

                    Logger.Instance.LogDebug("Found timer block: " + block.DisplayNameText + " on " + block.GetTopMostParent().DisplayName);

                    // Only use a block either if there's no tag, or the tag matches the current FTL state
                    if (block.CheckExecute(ftld.jumpState, error))
                        block.GetActionWithName("Start").Apply(block);
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogException(ex);
            }
        }

        public void RefreshLCDs()
        {
            var objBuilderType = new MyObjectBuilderType(typeof(MyObjectBuilder_TextPanel));
            var lcdblocks = m_ftl.GetGroupedBlocks(objBuilderType);
            var ftld = m_ftl.GetFTLData();

            if (lcdblocks.Count == 0)
            {
                var list = new List<IMySlimBlock>();

                // Get all the lcdblocks on the entity
                if (m_ftl.GetTopMostParent() is IMyCubeGrid)
                {
                    (m_ftl.GetTopMostParent() as IMyCubeGrid).GetBlocks(list,
                        (x) => x.FatBlock != null && x.FatBlock.BlockDefinition.TypeId == objBuilderType);
                }

                // Search all of them finding the best one
                foreach (var entity in list)
                {
                    var lcdblock = entity.FatBlock as IMyTextPanel;

                    if (lcdblock == null || !lcdblock.IsFunctional)
                        continue;

                    //Logger.Instance.LogDebug("Found LCD: " + lcdblock.DisplayNameText);

                    if (!string.IsNullOrEmpty(lcdblock.DisplayNameText) &&
                        (lcdblock.DisplayNameText.ToUpperInvariant().Contains("FTL") ||
                            lcdblock.GetPublicTitle().ToUpperInvariant().Contains("FTL") ||
                            lcdblock.GetPrivateTitle().ToUpperInvariant().Contains("FTL")))
                    {
                        // If a lcdblock contains the text FTL, always use it
                        lcdblocks.Add(lcdblock as IMyTerminalBlock);
                        break;
                    }
                }
            }

            if (lcdblocks.Count == 0)
                return;

            // TODO: have different LCDs display different information
            StringBuilder LCDText = new StringBuilder(60);
            StringBuilder LCDTextUpgrade = new StringBuilder(80);

            // Calculate distance
            double distance = (ftld.jumpDest - m_ftl.GetTopMostParent().PositionComp.WorldMatrix.Translation).Length();
            string distunits = "m";
            double range = ftld.baseRange * ftld.rangeFactor;
            string rangeunits = "m";
            string status = ftld.jumpState.ToString();

            if (!m_ftl.IsWorking)
                status = "Offline";
            if (!m_ftl.Enabled)
                status = "Disabled";

            FTLExtensions.CalculateUnits(ref distance, ref distunits);
            FTLExtensions.CalculateUnits(ref range, ref rangeunits);

            LCDText.Append(string.Format("FTL Status: {0}\n", status));
            LCDTextUpgrade.Append(string.Format("Upgrade Modules:\n"));

            //Logger.Instance.Active = false;
            if (m_ftl.IsWorking && !((ftld.flags & JumpFlags.Disabled) == JumpFlags.Disabled))
            {
                LCDText.Append(string.Format("Power: {0,16:F2} MW\n", GetPowerWanted()));
                //LCDText.Append(string.Format("Power: {0,21:P0}\n", (MyAPIGateway.Session.CreativeMode ? 1.0 : GetTotalFTLPower(m_ftl.GetFTLDrives()) / m_ftl.GetPowerPercentage())));
                //LCDText.Append(string.Format("Power Allocated: {0:F0}/{1:P0}\n", GetTotalFTLPower(m_ftl.GetFTLDrives()) * 100, m_ftl.GetPowerPercentage()));
                LCDText.Append(string.Format("Distance: {0,13:N0} {1,-2}\n", distance, distunits));
                LCDText.Append(string.Format("Mass: {0,15:N0} kg\n", ftld.totalMass));

                bool doesCollide = false;

                // Do we collide?
                if ((ftld.flags & JumpFlags.ShowCollision) == JumpFlags.ShowCollision)
                {
                    VRageMath.BoundingSphereD jumpSphere;
                    jumpSphere = new VRageMath.BoundingSphereD(ftld.jumpDest, m_ftl.GetTopMostParent().PositionComp.LocalVolume.Radius);
                    List<IMyEntity> entities = MyAPIGateway.Entities.GetEntitiesInSphere(ref jumpSphere);
                    VRageMath.Vector3 lastPos = new VRageMath.Vector3();

                    if (MyAPIGateway.Entities.IsInsideVoxel(
                        new VRageMath.Vector3(jumpSphere.Center.X, jumpSphere.Center.Y, jumpSphere.Center.Z),
                        VRageMath.Vector3D.Zero,
                        out lastPos))
                    {
                        doesCollide = true;
                    }

                    if (!doesCollide)
                    {
                        foreach (var entity in entities)
                        {
                            if (entity is IMyCubeGrid && entity.GetTopMostParent().EntityId != m_ftl.GetTopMostParent().EntityId)
                            {
                                doesCollide = true;
                                break;
                            }
                        }
                    }
                }

                if (ftld.jumpState == JumpState.Spooling)
                    LCDText.Append(string.Format("Jumping in {0:F0}s\n", (ftld.jumpTime - DateTime.Now).TotalSeconds));
                else if (ftld.jumpState == JumpState.Cooldown)
                    LCDText.Append(string.Format("Cooldown for {0:F0}s\n", (ftld.resetTime - DateTime.Now).TotalSeconds));
                else
                    LCDText.Append("\n");

                if (FTLExtensions.IsInhibited(ftld.jumpDest) || FTLExtensions.IsInhibited(m_ftl.GetPosition()))
                    LCDText.Append("\nDestination:\nX = ?\nY = ?\nZ = ?\n");
                else
                    LCDText.Append(string.Format("\nDestination{3}:\nX = {0:N0}\nY = {1:N0}\nZ = {2:N0}\n",
                        ftld.jumpDest.X, ftld.jumpDest.Y, ftld.jumpDest.Z, doesCollide ? " (Warning!)" : string.Empty));

                var capblocks = GetCapacitors();
                int totalcaps = 0;

                foreach (var cap in capblocks)
                {
                    if (cap.HasCapacityRemaining && cap.IsWorking && cap.IsFunctional)
                        totalcaps++;
                }

                LCDTextUpgrade.Append(string.Format("Max Range: {0,10:N0} {1,-2}\n", range, rangeunits));
                LCDTextUpgrade.Append(string.Format("Max Accuracy:{0,12:P0}\n", ftld.accuracyFactor));
                LCDTextUpgrade.Append(string.Format("Spool Efficiency:{0,8:P0}\n", ftld.spoolFactor));
                LCDTextUpgrade.Append(string.Format("Power Efficiency:{0,8:P0}\n", ftld.powerFactor));
                LCDTextUpgrade.Append(string.Format("Working Capacitors: {0}\r\n", totalcaps));
            }
            else
            {
                LCDTextUpgrade.Append(string.Format("FTL Offline\n"));
            }
            Logger.Instance.Active = true;

            foreach (var entity in lcdblocks)
            {
                IMyTextPanel lcdblock = entity as IMyTextPanel;

                //if (Globals.Debug)
                //    Sandbox.ModAPI.MyAPIGateway.Utilities.ShowNotification("Found LCD: " + lcdblock.DisplayNameText, 3500);
                var existingText = lcdblock.GetPublicText();
                var clearText = "                    \n                    \n                    \n                    \n                    \n                    \n                    \n                    ";

                // For network optimization, don't do anything if the text is the same
                if (existingText == LCDText.ToString())
                    continue;

                if (!m_ftl.IsBlockAccessible(entity))
                {
                    var accessDeniedText = "FTL Status:         \nAccess Denied";

                    if (lcdblock.CustomName.ToUpperInvariant().Contains("UPGRADES"))
                        accessDeniedText = "Upgrade Modules:    \nAccess Denied";

                    if (existingText != accessDeniedText)
                    {
                        // The spaces prevent the ghosting that happens when updating text
                        lcdblock.WritePublicText(string.Empty);
                        lcdblock.WritePublicText(clearText);
                        lcdblock.WritePublicText(accessDeniedText);
                    }
                    continue;
                }

                // Update LCD text
                if (lcdblock.CustomName.ToUpperInvariant().Contains("UPGRADE") ||
                    lcdblock.GetPublicTitle().ToUpperInvariant().Contains("UPGRADE") ||
                    lcdblock.GetPrivateTitle().ToUpperInvariant().Contains("UPGRADE"))
                {
                    if (existingText != LCDTextUpgrade.ToString())
                    {
                        lcdblock.WritePublicText(string.Empty);
                        lcdblock.WritePublicText(clearText);
                        lcdblock.WritePublicText(LCDTextUpgrade.ToString());
                    }
                    else
                    {
                        continue;
                    }
                }
                else
                {
                    if (existingText != LCDText.ToString())
                    {
                        lcdblock.WritePublicText(string.Empty);
                        lcdblock.WritePublicText(clearText);
                        lcdblock.WritePublicText(LCDText.ToString());
                    }
                    else
                    {
                        continue;
                    }
                }

                if (FTLAdmin.Configuration.FixedFontSize)
                {
                    float fontsize = 2.0f;          // Default to a more conservative 2.0, in case there are modded blocks

                    // Set font size depending on the block (only handles stock blocks)
                    switch (lcdblock.BlockDefinition.SubtypeId)
                    {
                        case "LargeLCDPanelWide":
                        case "SmallLCDPanelWide":
                            fontsize = 3.5f;
                            break;
                        case "LargeLCDPanel":
                        case "SmallLCDPanel":
                            fontsize = 1.7f;
                            break;
                        case "LargeTextPanel":
                        case "SmallTextPanel":
                            fontsize = 1.7f;
                            break;
                    }

                    lcdblock.SetValueFloat("FontSize", fontsize);
                }
                lcdblock.ShowPublicTextOnScreen();
            }
        }
        #endregion

        #region Movement/Jumping
        private static VRageMath.Vector3D GetFuzzyPosition(VRageMath.Vector3D center, double accuracy, double maxRadius, out VRageMath.BoundingSphereD bSphere)
        {
            VRageMath.Vector3D retVec = center;
            double radius = 0;
            Random rand = new Random((int)DateTime.Now.Ticks);
            VRageMath.BoundingSphereD collisionSphere;

            // Use the formula radius = max - (max * (percentage of power usage) / 1)
            // This means Power usage of 100% means radius is 0.
            // Power usage of 0% means use max radius.
            radius = (maxRadius - (maxRadius * (accuracy / 1)));

            //if (Globals.Debug)
            //    Sandbox.ModAPI.MyAPIGateway.Utilities.ShowNotification(string.Format("acc: {0:F1}, rad: {1:F1}, maxRad: {2:F1}", accuracy, radius, maxRadius), 2000);

            // A negative radius means more power was allocated than was needed
            if (radius < 0)
                radius = 0;

            // Now we choose 3 random values, radius, azimuth, elevation
            double randRadius = rand.NextDouble();
            double randAzim = rand.NextDouble();
            double randElev = rand.NextDouble();

            // However they are between 0.0 and 1.0, so multiply it like a percentage to get real values
            randRadius *= radius;
            randAzim *= (2 * Math.PI);
            randElev *= Math.PI;
            randElev -= (Math.PI / 2);

            //if (Globals.Debug)
            //    Sandbox.ModAPI.MyAPIGateway.Utilities.ShowNotification(string.Format("az/el/ra: {0:F1}, {1:F1}, {2:F1}", randAzim, randElev, randRadius), 2000);

            // Create vector from spherical coordinates
            VRageMath.Vector3D vec = new VRageMath.Vector3();
            VRageMath.Vector3D.CreateFromAzimuthAndElevation(randAzim, randElev, out vec);
            retVec = vec;
            retVec *= randRadius;

            // Add that to the original destination vector to get the 'true' destination
            retVec = center + retVec;

            collisionSphere = new VRageMath.BoundingSphereD(center, randRadius);
            bSphere = collisionSphere;

            return retVec;
        }

        public VRageMath.Vector3D CalculateJumpPosition(out VRageMath.Vector3D direction)
        {
            VRageMath.BoundingSphereD jumpSphere;

            return CalculateJumpPosition(out jumpSphere, out direction);
        }

        public VRageMath.Vector3D CalculateJumpPosition(out VRageMath.BoundingSphereD jumpSphere, out VRageMath.Vector3D direction)
        {
            var ftld = m_ftl.GetFTLData();
            VRageMath.MatrixD destMat;

            //// why does this happen?
            //if (ftld.totalPowerAvailable == 0.0)
            //ftld.totalPowerAvailable = GetTotalFTLPower(ftl.GetFTLDrives());

            IMyEntity parent = m_ftl.GetTopMostParent();
            VRageMath.MatrixD refMatrix = m_ftl.GetShipReference();
            VRageMath.Vector3D destVec = m_ftl.GetTopMostParent().PositionComp.GetPosition();

            if (ftld.jumpTargetGPS != null )
            {
                Logger.Instance.LogDebug("Explicit coords");
                destVec = ftld.jumpTargetGPS.Coords;
            }
            else if( ftld.flags.HasFlag(JumpFlags.GPSWaypoint) )
            {
                destVec = ftld.jumpDest;
            }
            else
            {
                destVec = destVec + refMatrix.Forward * ftld.jumpDistance;
            }

            Logger.Instance.LogDebug(string.Format("Using destination: {0}, {1}, {2}", destVec.X, destVec.Y, destVec.Z));

            // Calculate 'perfect' destination
            destMat = parent.PositionComp.WorldMatrix;

            destMat.Translation = new VRageMath.Vector3D(destVec.X, destVec.Y, destVec.Z);

            // Calculate fuzzy endpoint, based on power level of FTL drive
            destVec = GetFuzzyPosition(destVec, ftld.accuracyFactor, (parent.PositionComp.GetPosition() - destMat.Translation).Length() / 2, out jumpSphere);
            jumpSphere = new VRageMath.BoundingSphereD(destMat.Translation, jumpSphere.Radius);

            // Set true position
            destMat = parent.PositionComp.WorldMatrix;

            destMat.Translation = new VRageMath.Vector3D(destVec.X, destVec.Y, destVec.Z);

            jumpSphere = new VRageMath.BoundingSphereD(destMat.Translation, parent.PositionComp.WorldVolume.Radius);
            direction = destMat.Translation - parent.PositionComp.GetPosition();
            Logger.Instance.LogDebug(string.Format("{6}: Destination: {0:F0}, {1:F0}, {2:F0}; Direction: {3:F0}, {4:F0}, {5:F0}",
                    destMat.Translation.X, destMat.Translation.Y, destMat.Translation.Z, direction.X, direction.Y, direction.Z, m_ftl.CustomName));
            return destMat.Translation;
        }

        public void MoveObjects(VRageMath.MatrixD sourceReference, VRageMath.Vector3D directionVector)
        {
            Logger.Instance.LogMessage("MoveObjects()");
            Logger.Instance.IndentLevel++;

            var objects = m_ftl.GetAllValidObjects();

            Logger.Instance.LogDebug("Entities to move: " + objects.Count);
            Logger.Instance.LogDebug(string.Format("Direction: {0}, {1}, {2}", directionVector.X, directionVector.Y, directionVector.Z));

            var updates = new Dictionary<long, Dictionary<ulong, VRageMath.Vector3D>>();

            foreach (var objToMove in objects)
            {
                var mat = sourceReference;
                var savedPlayerMatrix = objToMove.PositionComp.WorldMatrix;

                // Apply orientation and set position
                if (!((objToMove is IMyCubeGrid) && (objToMove as IMyCubeGrid).IsStatic))     // Exclude stations
                {
                    var player = MyAPIGateway.Players.GetPlayerControllingEntity(objToMove);
                    var destination = objToMove.WorldMatrix.Translation + directionVector;

                    if (player == null)
                    {
                        Logger.Instance.LogMessage(string.Format("local update {0} to: {1:F0}, {2:F0}, {3:F0}", objToMove.DisplayName, destination.X, destination.Y, destination.Z));
                        updates.Add(objToMove.EntityId, new Dictionary<ulong, VRageMath.Vector3D>() { { 0, destination } });
                    }
                    else
                    {
                        Logger.Instance.LogMessage(string.Format("remote update {0} to: {1:F0}, {2:F0}, {3:F0}", objToMove.DisplayName, destination.X, destination.Y, destination.Z));
                        updates.Add(objToMove.EntityId, new Dictionary<ulong, VRageMath.Vector3D>() { { player.SteamUserId, destination } });
                    }
                }
            }
            SendPositionUpdates(updates);                   // Send queued updates
            Logger.Instance.IndentLevel--;
        }

        public void Jump()
        {
            int indent = Logger.Instance.IndentLevel;
            var ftld = m_ftl.GetFTLData();
            try
            {
                Logger.Instance.LogMessage("Jump()");
                Logger.Instance.IndentLevel++;

                var dir = ftld.jumpDirection;
                if( m_ftl is IMyGyro)
                    ftld.cooldownPower = m_ftl.GetValueFloat("Power");

#if false
                // Copy the translation from the original vector calculated at spool start
                if (ftld.jumpCourse.Count > 0)
                {
                    // Delay each jump 1s, so the game can catch up
                    if ((DateTime.Now - ftld.lastJumpCourseTime).TotalMilliseconds < 1000)
                        return;

                    var pos = ftld.jumpCourse.FirstElement();
                    dir = pos - m_ftl.GetTopMostParent().PositionComp.WorldMatrix.Translation;
                    ftld.jumpCourse.Remove(pos);
                    ftld.lastJumpCourseTime = DateTime.Now;
                }
#endif
                // Log current values for debugging later
                Logger.Instance.LogDebug("Mass: " + ftld.totalMass);
                Logger.Instance.LogDebug("Power: " + ftld.power);
                Logger.Instance.LogDebug("Power Wanted: " + ftld.totalPowerWanted);
                Logger.Instance.LogDebug("Power Available: " + ftld.totalPowerAvailable);

                // To avoid network lag, make server authoritive on updates.
                // Clients will just 'go through the motions'
                // The server needs to handle all movement, otherwise a situation happens
                // where the client controlling a ship will move before the server parses nearby entities
                // causing things to get left behind unexpectedly.
                // Sometimes the reverse happens and floating players are left behind.
                if (MyAPIGateway.Multiplayer.IsServer)
                    MoveObjects(m_ftl.GetShipReference(), dir);
                //MoveAndOrientObject(ftl.GetShipReference(), destMat, ftl.GetTopMostParent());
            }
            catch (Exception ex)
            {
                Logger.Instance.LogException(ex);
            }
            finally
            {
                ftld.jumpState = JumpState.Jumped;
                m_ftl.GameLogic.GetAs<FTLBase>().RunProgrammableBlocks();
                m_ftl.GameLogic.GetAs<FTLBase>().RunTimers();

                Logger.Instance.IndentLevel = indent;
            }
        }
        #endregion

        #region Messages
        protected abstract void SendDistanceChange(float value);
        protected abstract float ComputeMaxDistance();

        protected void SendJumpMessage()
        {
            MessageUtils.SendMessageToAllPlayers(new MessageSpooling() { FTLId = m_ftl.EntityId, Destination = m_ftld.jumpDest });
        }

        protected void SendGPS(IMyGps gps)
        {
            if (MyAPIGateway.Session.Player == null)
                return;

            MessageUtils.SendMessageToServer(new MessageGPS() { FTLId = m_ftl.EntityId, Name = gps.Name, Destination = gps.Coords });
        }

        protected static void SendPositionUpdates(Dictionary<long, Dictionary<ulong, VRageMath.Vector3D>> updates)
        {
            var entitiesByPlayer = new Dictionary<ulong, Dictionary<long, VRageMath.Vector3D>>();

            foreach (var entity in updates)
            {
                var player = entity.Value.Keys.First();

                if (!entitiesByPlayer.ContainsKey(player))
                    entitiesByPlayer.Add(player, new Dictionary<long, VRageMath.Vector3D>());

                entitiesByPlayer[entity.Value.Keys.First()].Add(entity.Key, entity.Value[player]);
            }

            foreach (var player in entitiesByPlayer)
            {
                if (player.Key == 0)
                    MessageUtils.SendMessageToServer(new MessageMove() { Entities = player.Value.Keys.ToList(), Positions = player.Value.Values.ToList() });
                else
                {
                    // To debug jump issues, try sending updates to server anyway
                    MessageUtils.SendMessageToPlayer(player.Key, new MessageMove() { Entities = player.Value.Keys.ToList(), Positions = player.Value.Values.ToList() });
                    MessageUtils.SendMessageToServer(new MessageMove() { Entities = player.Value.Keys.ToList(), Positions = player.Value.Values.ToList() });
                }
            }
        }

        #endregion

        #region Block Properties
        static public void FTLBase_PropertiesChanged(IMyTerminalBlock obj)
        {
            (obj as IMyTerminalBlock).RefreshCustomInfo();
            try
            {
                obj.GameLogic.GetAs<FTLBase>().UpdateFTLStats();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogException(ex);
            }
        }

        public virtual void AppendingCustomInfo(IMyTerminalBlock arg1, StringBuilder arg2)
        {
            var ftld = (arg1 as IMyFunctionalBlock).GetFTLData();
            var upgrades = new StringBuilder(100);

            if ((ftld.flags & JumpFlags.Disabled) == JumpFlags.Disabled)
            {
                upgrades.Append("Disabled");
            }
            else
            {
                //var def = MyDefinitionManager.Static.GetDefinition(arg1.BlockDefinition) as MyGyroDefinition;
                var capblocks = arg1.GameLogic.GetAs<FTLBase>().GetCapacitors();
                int totalcaps = 0;

                foreach (var cap in capblocks)
                {
                    if (cap.HasCapacityRemaining && cap.IsWorking && cap.IsFunctional)
                        totalcaps++;
                }

                //TODO: upgrades.Append(string.Format("Max Power Input: {0:F2} MW\r\n", def.RequiredPowerInput * (arg1 as IMyFunctionalBlock).PowerConsumptionMultiplier));
                upgrades.Append(string.Format("Max Range: {0:N0} m\r\n", ComputeMaxDistance()));
                upgrades.Append(string.Format("Jump Accuracy: {0:P0}\r\n", ftld.accuracyFactor));
                upgrades.Append(string.Format("Spool Efficiency: {0:P0}\r\n", ftld.spoolFactor));
                upgrades.Append(string.Format("Power Efficiency: {0:P0}\r\n", ftld.powerFactor));
                upgrades.Append(string.Format("Working Capacitors: {0}", totalcaps));
                //upgrades.Append(string.Format("Power Multiplier: {0:F2}", ftld.powerConsumptionMultiplier));
            }
            arg2.Append(upgrades);
            //Logger.Instance.LogMessage(arg2.ToString());
        }

        public virtual void ParseNameArguments(string name = null)
        {
            if (name == null)
                name = m_ftl.CustomName;

            FTLData ftld = m_ftl.GetFTLData();

            if (string.IsNullOrWhiteSpace(name))
                return;

            int cmdStartIdx = name.IndexOf('[');
            int cmdEndIdx = name.LastIndexOf(']');
            // Make sure options are set back to defaults (keep track if we had a GPS set)
            ftld.rangeMultiplier = 1.0;
            ftld.flags = JumpFlags.ShowCollision;
            //ftld.flags = (ftld.flags & JumpFlags.GPSWaypoint) | JumpFlags.ShowCollision;

            try
            {
                // Check if we have custom commands in the name
                if (cmdStartIdx != -1 && cmdEndIdx != -1)
                {
                    Logger.Instance.LogMessage(string.Format("ParseNameArguments(\"{0}\")", name));
                    Logger.Instance.IndentLevel++;

                    string sCmd = name.Remove(cmdEndIdx).Remove(0, cmdStartIdx + 1);

                    // Split the commands for parsing
                    string[] cmds = sCmd.Split(new Char[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (var cmd in cmds)
                    {
                        string tempCmd = cmd.Trim().ToUpperInvariant();
                        double range;
                        float distance;

                        if (string.CompareOrdinal(tempCmd, 0, "X", 0, 1) == 0)
                        {
                            if (double.TryParse(tempCmd.Remove(0, 1), out range))
                                ftld.rangeMultiplier = range;
                            else
                                Logger.Instance.LogDebug("Error setting range multiplier.");
                        }

                        if (tempCmd.Length > 0 && string.CompareOrdinal(tempCmd, tempCmd.Length - 1, "X", 0, 1) == 0)
                        {
                            if (double.TryParse(tempCmd.Remove(tempCmd.Length - 1, 1), out range))
                                ftld.rangeMultiplier = range;
                            else
                                Logger.Instance.LogDebug("Error setting range multiplier.");
                        }

                        if (tempCmd.StartsWith("ABS"))
                            ftld.flags |= JumpFlags.AbsolutePosition;

                        if (tempCmd.StartsWith("NOWARN"))
                            ftld.flags &= ~(JumpFlags.ShowCollision);

                        if (tempCmd.StartsWith("DISABLE"))
                        {
                            ftld.flags |= JumpFlags.Disabled;
                            m_ftl.RefreshCustomInfo();                // Update terminal to show disabled state
                            RefreshLCDs();
                            Logger.Instance.LogMessage("FTL Disabled: " + m_ftl.CustomName);
                        }

                        if (string.Compare(tempCmd, "SLAVE", true) == 0)
                        {
                            ftld.flags |= JumpFlags.SlaveMode;
                            Logger.Instance.LogDebug("Slave mode active");
                        }

                        // Format is: "GPS:Phoenix #1"
                        if (tempCmd.StartsWith("GPS"))
                        {
                            string[] gpswp = tempCmd.Split(new Char[] { ':' });

                            if (gpswp.Length == 2 || (gpswp.Length == 3 && gpswp[2].Trim().Length == 0))
                            {
                                var waypoints = FTLExtensions.GetGPSWaypoints();
                                Logger.Instance.LogDebug("GPS count: " + waypoints.Count);
                                foreach (var waypoint in waypoints)
                                {
                                    if (string.Compare(waypoint.Name, gpswp[1], true) == 0 && m_ftl.GetPlayerRelationToOwner() != MyRelationsBetweenPlayerAndBlock.Enemies)
                                    {
                                        //TODO: Migrate to new format
                                        ftld.jumpTargetGPS = waypoint;
                                        ftld.flags |= JumpFlags.ExplicitCoords | JumpFlags.AbsolutePosition;

                                        if (!MyAPIGateway.Multiplayer.IsServer)
                                            SendGPS(waypoint);

                                        Logger.Instance.LogDebug("GPS waypoint " + gpswp[1] + " found.");
                                        break;
                                    }
                                    else
                                    {
                                        Logger.Instance.LogDebug(string.Format("GPS: {0}, owner relation: {1}", waypoint.Name, m_ftl.GetPlayerRelationToOwner()));
                                    }
                                }
                                if ((ftld.flags & JumpFlags.ExplicitCoords) == 0)
                                {
                                    //if (MyAPIGateway.Multiplayer.IsServer)
                                    //{
                                    //    // This event can trigger on the server after the GPS was already sent over
                                    //    // If the GPS flag is set, use the saved coordinates
                                    //    if ((ftld.flags & JumpFlags.GPSWaypoint) != 0)
                                    //    {
                                    //        ftld.flags |= JumpFlags.ExplicitCoords | JumpFlags.AbsolutePosition;
                                    //        continue;
                                    //    }
                                    //}
                                    Logger.Instance.LogDebug("GPS waypoint " + gpswp[1] + " not found.");
                                }
                            }
                            else if (gpswp.Length == 6)
                            {
                                double x, y, z;

                                if (Double.TryParse(gpswp[2], out x) &&
                                    Double.TryParse(gpswp[3], out y) &&
                                    Double.TryParse(gpswp[4], out z))
                                {
                                    Logger.Instance.LogDebug("Found GPS waypoint: " + tempCmd);

                                    //TODO: Migrate to new format
                                    ftld.jumpTargetGPS = MyAPIGateway.Session.GPS.Create("Legacy FTL", null, new VRageMath.Vector3D(x, y, z), false, true);
                                    ftld.flags |= JumpFlags.ExplicitCoords | JumpFlags.AbsolutePosition;
                                }
                                else
                                {
                                    Logger.Instance.LogDebug("Error parsing GPS waypoint: " + tempCmd);
                                }
                            }
                            else
                            {
                                Logger.Instance.LogDebug("Error detecting GPS waypoint");
                            }
                        }
                        // Format is: "D:float"
                        if (tempCmd.StartsWith("D:"))
                        {
                            if (float.TryParse(tempCmd.Remove(0, 2), out distance))
                                ftld.jumpDistance = distance;
                            else
                                Logger.Instance.LogDebug("Error setting distance.");
                        }
                        // Parse coordinates (no multiplier needed)
                        MatchCollection matches = Regexes.Coordinates.Matches(tempCmd);

                        Logger.Instance.LogDebug("regex matches: " + matches.Count);

                        if (matches.Count > 1)
                        {
                            if (matches.Count == 3)
                            {
                                // TODO: Migrate to new format
                                ftld.jumpTargetGPS = MyAPIGateway.Session.GPS.Create("Legacy FTL", null, new VRageMath.Vector3D(double.Parse(matches[0].Value), double.Parse(matches[1].Value), double.Parse(matches[2].Value)), false, true);
                                ftld.flags |= JumpFlags.ExplicitCoords | JumpFlags.AbsolutePosition;
                                Logger.Instance.LogDebug(string.Format("Found explicit coords: ({0}, {1}, {2})", ftld.jumpTargetGPS.Coords.X, ftld.jumpTargetGPS.Coords.Y, ftld.jumpTargetGPS.Coords.Z));
                            }
                            else
                            {
                                Logger.Instance.LogDebug("Invalid coordinates detected");
                            }
                        }
                    }
                    Logger.Instance.LogDebug("range multiplier: " + ftld.rangeMultiplier);
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogException(ex);
            }
            finally
            {
                m_ftl.RefreshCustomInfo();
                Logger.Instance.IndentLevel--;
            }
        }

        public virtual void SaveTerminalValues()
        { }

        /// <summary>
        /// Updates the FTLData structure with current values.
        /// </summary>
        /// <param name="m_ftl"></param>
        public void UpdateFTLStats()
        {
            var ftld = m_ftl.GetFTLData();

            if ((ftld.flags & JumpFlags.Disabled) == JumpFlags.Disabled)
                return;
            //Logger.Instance.LogDebug("UpdateFTLStats");
            // Order is important!
            ftld.totalMass = m_ftl.GetTotalMass();

            if (ftld.jumpState != JumpState.Cooldown)
                ftld.totalPowerWanted = GetPowerWanted();
            //if (!ftld.flags.HasFlag(JumpFlags.Disabled))
            //    Logger.Instance.LogDebug(ftl.EntityId + " power: " + ftld.totalPowerWanted);
            //ftld.totalPowerWanted = m_ftl.GetPowerPercentage();
            ftld.totalPowerAvailable = GetTotalFTLPower(m_ftl.GetFTLDrives());

            if (ftld.jumpState == JumpState.Idle)
            {
                ftld.jumpDest = CalculateJumpPosition(out ftld.jumpDirection);
                //Logger.Instance.LogMessage(m_ftl.CustomName + "; jumpDest: " + ftld.jumpDest);
            }
        }

        public virtual FTLData InitFTLData()
        {
            //Logger.Instance.LogMessage("AddNewFTL()");

            // New object, do stuff
            var ftld = new FTLData();
            ftld.ftl = m_ftl as IMyCubeBlock;

            //if (ftld.objectBuilderGyro == null)
            //    return ftld;

            ((IMyTerminalBlock)m_ftl).CustomNameChanged += CustomNameChanged;
            m_ftl.OnClose += OnClose;                                           // This will handle cleanup when the entity is removed

            if (m_ftl.CubeGrid.GridSizeEnum == MyCubeSize.Large)
                ftld.baseRange = Globals.Instance.RuntimeConfig[ModifierType.LargeRange];
            else
                ftld.baseRange = Globals.Instance.RuntimeConfig[ModifierType.SmallRange];

            // Correct for different FTL sizes
            if (m_ftl.BlockDefinition.SubtypeId.Contains("FTLMed"))
                ftld.baseRange *= Globals.Instance.RuntimeConfig[ModifierType.FTLMedFactor];
            if (m_ftl.BlockDefinition.SubtypeId.Contains("FTLSml"))
                ftld.baseRange *= Globals.Instance.RuntimeConfig[ModifierType.FTLSmlFactor];

            // Load values from configuration if set
            ftld.ReloadValues();

            // Handle block events (to recalculate mass)
            (m_ftl.CubeGrid as IMyCubeGrid).OnBlockAdded += BlockAdded;
            (m_ftl.CubeGrid as IMyCubeGrid).OnBlockRemoved += BlockRemoved;
            return ftld;
        }

        #endregion

        #region Power
        protected MyDefinitionId m_powerDefinitionId = new VRage.Game.MyDefinitionId(typeof(VRage.Game.ObjectBuilders.Definitions.MyObjectBuilder_GasProperties), "Electricity");

        public double GetGridPower()
        {
            double power = 0;
            List<IMySlimBlock> reactors = new List<IMySlimBlock>();
            double maxpower = 0;
            double currentpower = 0;

            (m_ftl.CubeGrid as IMyCubeGrid).GetBlocks(reactors, (x) => x.FatBlock != null && x.FatBlock.Components.Has<Sandbox.Game.EntityComponents.MyResourceSourceComponent>());
            foreach (var block in reactors)
            {
                if ((block.FatBlock as IMyFunctionalBlock).IsWorking)        // We only care about working ones
                {
                    // If the block is an FTL cap, treat it differently
                    if (block.FatBlock is IMyBatteryBlock && block.FatBlock.BlockDefinition.SubtypeId.Contains("FTLCap"))
                    {
                        var capacitor = block.FatBlock as IMyBatteryBlock;
                        var resource = block.FatBlock.Components.Get<Sandbox.Game.EntityComponents.MyResourceSourceComponent>();
                        Logger.Instance.LogDebug(string.Format("Found power source: {2}, Max: {0}, Current: {1}", resource.DefinedOutputByType(FTLExtensions._powerDefinitionId), capacitor.CurrentStoredPower, block.FatBlock.BlockDefinition.SubtypeId));
                        maxpower += capacitor.HasCapacityRemaining ? resource.DefinedOutputByType(FTLExtensions._powerDefinitionId) : resource.MaxOutputByType(FTLExtensions._powerDefinitionId);
                        currentpower += resource.CurrentOutputByType(FTLExtensions._powerDefinitionId);
                    }
                    else
                    {
                        var reactor = block.FatBlock as IMyFunctionalBlock;
                        var resource = block.FatBlock.Components.Get<Sandbox.Game.EntityComponents.MyResourceSourceComponent>();

                        if (!resource.ResourceTypes.Contains(FTLExtensions._powerDefinitionId))
                            continue;                       // Oxygen generators contain a gas source, but not power source

                        Logger.Instance.LogDebug(string.Format("Found power source: {2}, Max: {0}, Current: {1}", resource.MaxOutputByType(FTLExtensions._powerDefinitionId), resource.CurrentOutputByType(FTLExtensions._powerDefinitionId), block.FatBlock.BlockDefinition.SubtypeId));
                        maxpower += resource.MaxOutputByType(FTLExtensions._powerDefinitionId);
                        currentpower += resource.CurrentOutputByType(FTLExtensions._powerDefinitionId);
                    }
                }
            }

            power = maxpower - currentpower;

            // If the FTL is turned on, adjust for the amount it's drawing
            if (m_ftl.IsWorking)
            {
                var ftld = m_ftl.GetFTLData();
                var ftlpower = ftld.power * (m_ftl is IMyGyro ? m_ftl.GetValueFloat("Power") : 1);
                //Logger.Instance.LogMessage("FTL power: " + ftlpower);
                power += Math.Min(ftlpower, maxpower);
            }
            Logger.Instance.LogDebug("Remaining power: " + power);

            return power;
        }

        public static double GetTotalFTLPower(List<IMySlimBlock> ftlDrives, bool activeOnly = false)
        {
            double power = 0;

            foreach (var ftl in ftlDrives)
            {
                if (ftl.FatBlock.IsFunctional)
                {
                    if ((!activeOnly || (activeOnly && ftl.FatBlock.IsWorking)) && ftl.FatBlock is IMyGyro)
                        power += ((MyObjectBuilder_Gyro)((IMyCubeBlock)ftl.FatBlock).GetObjectBuilderCubeBlock()).GyroPower;
                }
            }
            return power;
        }

        public void SetFTLPower(string action)
        {
            SetFTLPower(action, m_ftl);
        }

        public static void SetFTLPower(string action, IMyEntity ftl)
        {
            var actionList = new List<ITerminalAction>();
            Sandbox.ModAPI.MyAPIGateway.TerminalActionsHelper.GetActions(ftl.GetType(), actionList, (x) => x.Id.Contains(action));
            actionList[0].Apply(ftl as IMyCubeBlock);
        }

        public void SetPower()
        {
            if (MyAPIGateway.Session.CreativeMode)
                return;

            var ftld = m_ftl.GetFTLData();
            float power = 0;

            if (ftld.jumpState == JumpState.Cooldown)      // Keep current power until cooldown is done
            {
                power = ftld.cooldownPower;
            }
            else
            {
                power = GetPowerPercentage();

                if (power > 1.0f)
                    power = 1.0f;

            }
            Logger.Instance.LogMessage("Power: " + power);
            if (m_ftl is IMyGyro)
            {
                if (m_ftl.GetValueFloat("Power") != power)
                    m_ftl.SetValueFloat("Power", (float)power);
            }
        }

        public float GetPowerWanted()
        {
            var ftld = m_ftl.GetFTLData();

            double distance = (ftld.jumpDest - m_ftl.GetTopMostParent().PositionComp.WorldMatrix.Translation).Length();
            double power;
            var def = MyDefinitionManager.Static.GetDefinition(m_ftl.BlockDefinition);
            var lrgreactor = MyDefinitionManager.Static.GetDefinition(new MyDefinitionId(typeof(MyObjectBuilder_Reactor), "LargeBlockLargeGenerator"));
            var smlreactor = MyDefinitionManager.Static.GetDefinition(new MyDefinitionId(typeof(MyObjectBuilder_Reactor), "SmallBlockLargeGenerator"));

            // Base formula is: power = (distance ^ (1/1.6)) * 7 * sqrt(mass/baseline)
            power = Math.Pow(distance, (1.0 / 1.6));

            power *= 7;

            // Power returned is for a small ship
            // For a large ship, multiply by the factor of the small to large ship large capacitor output
            if (m_ftl.CubeGrid.GridSizeEnum == MyCubeSize.Large)
                power *= ((lrgreactor as MyReactorDefinition).MaxPowerOutput / (smlreactor as MyReactorDefinition).MaxPowerOutput);

            //Logger.Instance.Active = false;
            //Logger.Instance.Active = true;
            //if (!ftld.flags.HasFlag(JumpFlags.Disabled))
            //{
            //    Logger.Instance.LogDebug(ftl.EntityId + " power1: " + power);
            //    Logger.Instance.LogDebug(ftl.EntityId + " mass: " + ftld.totalMass);
            //    Logger.Instance.LogDebug(ftl.EntityId + " math1: " + ftld.totalMass / (ftl.CubeGrid.GridSizeEnum == MyCubeSize.Small ? Globals.Instance.RuntimeConfig[ModifierType.SmallMass] : Globals.Instance.RuntimeConfig[ModifierType.LargeMass]));
            //    Logger.Instance.LogDebug(ftl.EntityId + " math2: " + Math.Sqrt(ftld.totalMass / (ftl.CubeGrid.GridSizeEnum == MyCubeSize.Small ? Globals.Instance.RuntimeConfig[ModifierType.SmallMass] : Globals.Instance.RuntimeConfig[ModifierType.LargeMass])));
            //}

            power *= Math.Sqrt(ftld.totalMass / (m_ftl.CubeGrid.GridSizeEnum == MyCubeSize.Small ? Globals.Instance.RuntimeConfig[ModifierType.SmallMass] : Globals.Instance.RuntimeConfig[ModifierType.LargeMass]));

            //if (!ftld.flags.HasFlag(JumpFlags.Disabled))
            //    Logger.Instance.LogDebug(ftl.EntityId + " power2: " + power);

            power *= ftld.spoolFactor;                          // Increase power requirements when spool time is reduced
            power *= ftld.accuracyFactor;                       // Reduce power for inaccuracy
            power /= ftld.powerFactor;                          // Decrease power requirement for upgrades

            power /= ftld.powerConsumptionMultiplier;

            //if (!MyAPIGateway.Session.SurvivalMode)
            //    return 0;

            if (m_ftld.jumpState == JumpState.Idle || MyAPIGateway.Session.CreativeMode)
                return 0.2f;
           Logger.Instance.LogMessage(m_ftl.CustomName + "; power: " + power);

            return (float)(power / 1000);
        }

        public float GetPowerPercentage()
        {
            var ftld = m_ftl.GetFTLData();
            
            if (ftld.jumpState == JumpState.Idle)
                return 0;

            float powerPercent;
            powerPercent = (float)(ftld.totalPowerWanted / ftld.power);
            Logger.Instance.LogMessage(string.Format("Power: FTL={0:F0}, ftld.power={1:F0}", ftld.totalPowerWanted, ftld.power));

            return powerPercent;
        }

        protected void SetupPowerSink()
        {
            //var def = MyDefinitionManager.Static.GetCubeBlockDefinition(m_ftl.BlockDefinition) as MyCubeBlockDefinition;
            MyResourceSinkComponent sink;
            m_ftl.Components.TryGet<MyResourceSinkComponent>(out sink);
            if( sink == null )
            {
                var sinkComp = new MyResourceSinkComponent();
                sinkComp.Init(
                    MyStringHash.GetOrCompute("Defense"),
                    (float)m_ftld.totalPowerWanted,
                    ComputeRequiredPower);
                m_ftl.Components.Add<MyResourceSinkComponent>(sinkComp);
            }
            else
            {
                sink.SetRequiredInputFuncByType(m_powerDefinitionId, ComputeRequiredPower);
            }
        }

        protected float ComputeRequiredPower()
        {
            var power = (float)m_ftld.totalPowerWanted;
            //Logger.Instance.LogDebug("ComputeRequiredPower: " + power);
            return power;
        }

        protected abstract double ComputePowerMultiplier(double power);

        #endregion

        #region FTL
        void BeginSpooling()
        {
            m_ftld.jumpState = JumpState.Spooling;
            SendPlaySound("Phoenix.FTL.JumpDriveRecharge");
            UpdateFTLStats();
            ToggleCapacitors();
            UpdateFTLStats();
            RunProgrammableBlocks();
            RunTimers();
            //ftld.totalPowerWanted = m_ftl.GetPowerPercentage();

            // spool time is square root of the length it would take to traverse without block
            //ftld.jumpDest = m_ftl.CalculateJumpPosition(out ftld.jumpDirection);

            var time = (m_ftld.jumpDest - m_ftl.GetTopMostParent().PositionComp.GetPosition()).Length();
            if (m_ftl.CubeGrid.GridSizeEnum == MyCubeSize.Large)
                time /= MyDefinitionManager.Static.EnvironmentDefinition.LargeShipMaxSpeed;
            else
                time /= MyDefinitionManager.Static.EnvironmentDefinition.SmallShipMaxSpeed;

            time = Math.Sqrt(time);

            m_ftld.jumpTime = DateTime.Now.AddSeconds(
                Math.Min(
                    Globals.Instance.RuntimeConfig[ModifierType.MaxSpool],
                    Globals.MIN_COUNTDOWN_TIME_S + (MyAPIGateway.Session.CreativeMode ? 0 : (time / m_ftld.spoolFactor))));

            m_ftld.resetTime = m_ftld.jumpTime.AddSeconds(
                Math.Min(
                Globals.Instance.RuntimeConfig[ModifierType.MaxCooldown],
                Globals.MIN_COUNTDOWN_TIME_S + (MyAPIGateway.Session.CreativeMode ? 0 : (time / m_ftld.spoolFactor) * Globals.Instance.RuntimeConfig[ModifierType.CooldownMultiplier])));       // Started as workaround for multiplayer to wait for syncing, now full cooldown

            if (MyAPIGateway.Multiplayer.IsServer)
                SendJumpMessage();

            if (Globals.Debug)
                MyAPIGateway.Utilities.ShowNotification(string.Format("FTL: JumpTime: {0:F0}s, ResetTime: {1:F0}s", (m_ftld.jumpTime - DateTime.Now).TotalSeconds, (m_ftld.resetTime - DateTime.Now).TotalSeconds), 5000);

            m_ftl.ShowMessageToUsersInRange("FTL: Spooling...", 5000);

            //m_ftl.SetSlaveFTLPower(true);
        }

        public void AbortJump()
        {
            if (MyAPIGateway.Multiplayer.IsServer)
            {
                m_ftl.ShowMessageToUsersInRange("FTL: Jump aborted", 5000, true);
                ResetFTL();
                //SetFTLPower("OnOff_Off");
            }
        }

        void DoCollisionChecks(long time)
        {
            if ((m_ftld.flags & JumpFlags.ShowCollision) == JumpFlags.ShowCollision)
            {
                if (m_ftld.ShowMessage)
                {
                    var msgTime = (time > 5 ? 4900 : 900);

                    VRageMath.BoundingSphereD jumpSphere;
                    jumpSphere = new VRageMath.BoundingSphereD(m_ftld.jumpDest, m_ftl.GetTopMostParent().PositionComp.LocalVolume.Radius);
                    List<IMyEntity> entities = MyAPIGateway.Entities.GetEntitiesInSphere(ref jumpSphere);

                    foreach (var entity in entities)
                    {
                        if (entity is IMyCubeGrid &&
                                entity.GetTopMostParent().EntityId != m_ftl.GetTopMostParent().EntityId &&
                                (entity as IMyCubeGrid).DisplayName != "Phoenix_FTL_WarpEffect")
                            m_ftl.ShowMessageToUsersInRange("FTL: Warning! Possible Collision with " + (entity as IMyCubeGrid).DisplayName, msgTime, true);
                    }
                    VRageMath.Vector3 lastPos = new VRageMath.Vector3();

                    if (MyAPIGateway.Entities.IsInsideVoxel(
                        new VRageMath.Vector3(jumpSphere.Center.X, jumpSphere.Center.Y, jumpSphere.Center.Z),
                        new VRageMath.Vector3(m_ftl.PositionComp.GetPosition().X, m_ftl.PositionComp.GetPosition().Y, m_ftl.PositionComp.GetPosition().Z),
                        out lastPos))
                        m_ftl.ShowMessageToUsersInRange("FTL: Warning! Possible Collision with asteroid", msgTime, true);

                    // Check if in gravity well
                    var planets = new HashSet<IMyEntity>();
                    MyAPIGateway.Entities.GetEntities(planets, (x) => x is MyPlanet);
                    foreach (var planet in planets)
                    {
                        if ((planet as MyPlanet).DoOverlapSphereTest((float)jumpSphere.Radius, jumpSphere.Center))
                        {
                            m_ftl.ShowMessageToUsersInRange("FTL: Warning! Possible Collision with planet", msgTime, true);
                        }
                        else if (planet.Components.Get<MyGravityProviderComponent>().IsPositionInRange(jumpSphere.Center))
                        {
                            m_ftl.ShowMessageToUsersInRange("FTL: Warning! Destination within gravity well", msgTime, true);
                            break;
                        }
                    }
                }
            }
        }
        public void SendPlaySound(string soundname, bool stopPrevious = false)
        {
            if (MyAPIGateway.Multiplayer.IsServer)
            {
                MessageUtils.SendMessageToAll(new MessageSoundEffect() { FTLId = m_ftl.EntityId, SoundName = soundname, StopPrevious = stopPrevious });
            }
        }

        public void PlaySound(string soundname, bool stopPrevious = false)
        {
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
                    emitter.CustomMaxDistance = (float)m_ftl.GetTopMostParent().PositionComp.WorldVolume.Radius * 2.0f;
                    Logger.Instance.LogDebug("Distance: " + emitter.CustomMaxDistance);
                    emitter.CustomVolume = 0.75f;
                    emitter.PlaySound(sound, stopPrevious);
                }
            }
        }

        public void ResetFTL()
        {
            var ftld = m_ftl.GetFTLData();

            Logger.Instance.LogDebug("FTL Reset");

            PlaySoundBlocks(true);   // Stop playing, in case one was in progress (cancelled in last several seconds)
            SendPlaySound(null);
            ToggleLights(true);
            ToggleCapacitors(true);

            ftld.jumpState = JumpState.Idle;
            ftld.flags &= ~JumpFlags.GPSWaypoint;
            ftld.resetTime = DateTime.Now;
            ftld.jumpTime = DateTime.Now;
            ftld.jumpDirection = VRageMath.Vector3D.Zero;
            ftld.jumpDest = VRageMath.Vector3D.Zero;
            //ftld.totalMass = 0.0;
            //m_ftl.SetSlaveFTLPower(false);
            ftld.ShowMessage = true;
            RunProgrammableBlocks();
            RunTimers();
            m_effectPlayed = false;
            MessageUtils.SendMessageToAll(new MessageWarpEffect() { FTLId = m_ftl.EntityId, Remove = true });
        }

        public bool IsFTLValid(bool showError = true)
        {
            if (m_ftl == null)
                return false;

            var ftld = m_ftl.GetFTLData();

            bool valid = true;
            const string sBaseMsg = "FTL: Cannot jump - {0}";
            string sError = string.Empty;
            VRageMath.Vector3D jumpVec = ftld.jumpDest;
            double powerPercent;
            double powerAvailable;

            // Check CD
            if ((ftld.jumpState == JumpState.Jumped || ftld.jumpState == JumpState.Cooldown) && ftld.resetTime < DateTime.Now)
            {
                sError = string.Format("FTL: Drive is in cooldown, cannot jump for {0:F0}s", (ftld.resetTime - DateTime.Now).TotalSeconds);
                valid = false;
            }

            // TODO: Check if necessary
            //if (!((ftld.flags & JumpFlags.AbsolutePosition) == JumpFlags.AbsolutePosition) && ftld.objectBuilderGyro.TargetAngularVelocity.IsZero)
            if (ftld.jumpTargetGPS == null && ftld.jumpDest == m_ftl.CubeGrid.PositionComp.GetPosition() && !ftld.flags.HasFlag(JumpFlags.GPSWaypoint))
            {
                sError = "No Coordinates set";
                valid = false;
            }

            if (ftld.rangeMultiplier < 1.0)
            {
                sError = "Range multiplier must be at least 1x";
                valid = false;
            }

            if ((ftld.jumpDest - m_ftl.GetTopMostParent().PositionComp.WorldMatrix.Translation).Length() > ftld.baseRange * ftld.rangeFactor)
            {
                sError = "Distance too far, add range upgrade";
                valid = false;
            }

            float power = (float)ftld.totalPowerWanted;
            if (power > GetGridPower())
            {
                sError = string.Format("Insufficient power, need {0:F2} MW, have {1:F2} MW", power, GetGridPower());
                valid = false;
            }

            if (!MyAPIGateway.Entities.IsInsideWorld(jumpVec))
            {
                sError = "Destination beyond known world";
                valid = false;
            }

            if (Double.IsNaN(jumpVec.X) || Double.IsNaN(jumpVec.Y) || Double.IsNaN(jumpVec.Z))
            {
                sError = "Destination is NaN, please screenshot (F4) and report";
                valid = false;
            }

            // Check if inhibitor nearby
            if (FTLExtensions.IsInhibited(jumpVec) || FTLExtensions.IsInhibited(m_ftl.GetPosition()))
            {
                sError = "Unable to plot course, interference detected";
                valid = false;
            }

            // Check if in gravity well
            var planets = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(planets, (x) => x is MyPlanet);
            foreach (var planet in planets)
            {
                if (!FTLAdmin.Configuration.AllowGravityWellJump)
                {
                    if (planet.Components.Get<MyGravityProviderComponent>().IsPositionInRange(jumpVec))
                    {
                        sError = "Destination within gravity well";
                        valid = false;
                        break;
                    }
                    if (planet.Components.Get<MyGravityProviderComponent>().IsPositionInRange(m_ftl.GetTopMostParent().PositionComp.GetPosition()))
                    {
                        sError = "Within gravity well";
                        valid = false;
                        break;
                    }
                }
            }

            //if( ftl.GetShipReference() is IMyShipConnector && (ftl.GetShipReference() as IMyCockpit).Physics.

            powerPercent = GetPowerPercentage();
            powerAvailable = ftld.totalPowerAvailable;

            if (!valid)
            {
                m_ftl.GameLogic.GetAs<FTLBase>().RunTimers(true);
                //ftl.RunProgrammableBlocks();
                m_ftl.GameLogic.GetAs<FTLBase>().ResetFTL();
                if (showError)
                {
                    sError = string.Format(sBaseMsg, sError);
                    m_ftl.ShowMessageToUsersInRange(sError, 5000, true);
                }
            }

            return valid;
        }
        #endregion

    }
}
