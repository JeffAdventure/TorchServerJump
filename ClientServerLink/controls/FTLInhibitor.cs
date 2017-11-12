using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;

namespace Phoenix.FTL
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Gyro), new string[] { "FTLInhibitor" })]
    public class FTLInhibitor : MyGameLogicComponent
    {
        VRage.ObjectBuilders.MyObjectBuilder_EntityBase m_objectBuilder = null;
        IMyTerminalBlock m_inhibitor;
        private bool m_bAutoOff = false;
        public Double MaxRange { get; private set; }
        public Double Range
        {
            get
            {
                return MaxRange * ((m_inhibitor as IMyCubeBlock).GetObjectBuilderCubeBlock() as MyObjectBuilder_Gyro).GyroPower;
            }
        }
        float m_upgradeFactor = 1.0f;

        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return (copy && m_objectBuilder != null ? m_objectBuilder.Clone() as MyObjectBuilder_EntityBase : m_objectBuilder);
        }

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            m_objectBuilder = objectBuilder;
            m_inhibitor = Entity as IMyTerminalBlock;
            MaxRange = Globals.Instance.RuntimeConfig[ModifierType.InhibitorRange];

            var parent = Entity as IMyCubeBlock;
            parent.OnUpgradeValuesChanged += UpgradeValuesChanged;
            parent.NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;

            if (!parent.UpgradeValues.ContainsKey(ModifierType.InhibitorRange.ToString()))
            {
                parent.UpgradeValues.Add(ModifierType.InhibitorRange.ToString(), m_upgradeFactor);
            }
            (Entity as IMyTerminalBlock).AppendingCustomInfo += AppendingCustomInfo;
            (Entity as IMyTerminalBlock).PropertiesChanged += PropertiesChanged;
            (Entity as IMyFunctionalBlock).EnabledChanged += EnabledChanged;
            (Entity as IMyCubeBlock).IsWorkingChanged += IsWorkingChanged;
            //(Entity as IMyPowerConsumer).PowerReceiver.SuppliedRatioChanged += PowerReceiver_SuppliedRatioChanged;
        }

        public override void UpdateOnceBeforeFrame()
        {
            ReloadValues();
        }
        void IsWorkingChanged(IMyCubeBlock obj)
        {
            (obj as IMyTerminalBlock).RefreshCustomInfo();
        }

        void EnabledChanged(IMyTerminalBlock obj)
        {
            (obj as IMyTerminalBlock).RefreshCustomInfo();
        }

        //void PowerReceiver_SuppliedRatioChanged()
        //{
        //    MyAPIGateway.Utilities.ShowNotification("ratio changed: " + (Entity as IMyPowerConsumer).PowerReceiver.SuppliedRatio);
        //}

        void PropertiesChanged(IMyTerminalBlock obj)
        {
            (obj as IMyTerminalBlock).RefreshCustomInfo();
        }

        void AppendingCustomInfo(IMyTerminalBlock arg1, StringBuilder arg2)
        {
            arg2.AppendFormat("Max Range: {0:N0} m\r\n", MaxRange);
            arg2.AppendFormat("Current Range: {0:N0} m\r\n", Range);

            try
            {
                var power = GetAvailableGridPower();
                var def = MyDefinitionManager.Static.GetDefinition(m_inhibitor.BlockDefinition) as MyGyroDefinition;
                var ftlpower = def.RequiredPowerInput * m_inhibitor.GetValueFloat("Power") * m_upgradeFactor;

                if (power - ftlpower <= 0 && m_inhibitor.IsWorking)
                {
                    m_inhibitor.GetActionWithName("OnOff_Off").Apply(m_inhibitor);
                    m_bAutoOff = true;
                }
                if (m_bAutoOff && (power - ftlpower) > 0)
                {
                    m_inhibitor.GetActionWithName("OnOff_On").Apply(m_inhibitor);
                    m_bAutoOff = false;
                }

                arg2.AppendFormat("Power: {0} MW\r\n", power);
                arg2.AppendFormat("Inhibitor: {0} MW\r\n", ftlpower);

                if (power - ftlpower <= 0)
                    arg2.Append("Insufficient power\r\n");
            }
            catch { }
        }

        private void UpgradeValuesChanged()
        {
            try
            {
                m_upgradeFactor = (m_inhibitor as IMyCubeBlock).UpgradeValues[ModifierType.InhibitorRange.ToString()];
                //TODO: (m_inhibitor as IMyFunctionalBlock).PowerConsumptionMultiplier = m_upgradeFactor * Globals.Instance.RuntimeConfig[ModifierType.InhibitorPowerEfficiency];

                MaxRange = Globals.Instance.RuntimeConfig[ModifierType.InhibitorRange] * m_upgradeFactor;

                //var upgrades = new StringBuilder(50);

                //upgrades.Append(string.Format("Upgrade Accuracy: {0:P0}\r\n", ftld.accuracyFactor));
                //upgrades.Append(string.Format("Upgrade Range: {0}\r\n", ftld.rangeFactor));
                //upgrades.Append(string.Format("Upgrade Spool: {0:P0}\r\n", ftld.spoolFactor));
                //upgrades.Append(string.Format("Upgrade Power: {0:P0}\r\n", ftld.powerFactor));
                //upgrades.Append(string.Format("Power Multiplier: {0:F2}", (block as IMyFunctionalBlock).PowerConsumptionMultiplier));
                //Logger.Instance.LogMessage(upgrades.ToString());

                (m_inhibitor as IMyTerminalBlock).RefreshCustomInfo();
                //(m_ftl as IMyTerminalBlock).SetCustomDetailedInfo(upgrades);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogException(ex);
            }
            finally
            {
                Logger.Instance.IndentLevel--;
            }
        }

        public double GetAvailableGridPower()
        {
            float power = 0;
            List<IMySlimBlock> reactors = new List<IMySlimBlock>();
            float maxpower = 0;
            float currentpower = 0;

            (m_inhibitor.CubeGrid as IMyCubeGrid).GetBlocks(reactors, (x) => x.FatBlock != null && x.FatBlock.Components.Has<Sandbox.Game.EntityComponents.MyResourceSourceComponent>());
            foreach (var block in reactors)
            {
                if ((block.FatBlock as IMyTerminalBlock).IsWorking)        // We only care about working ones
                {
                    var resource = block.FatBlock.Components.Get<Sandbox.Game.EntityComponents.MyResourceSourceComponent>();
                    try
                    {
                        Logger.Instance.LogDebug(string.Format("Found power source, Max: {0}, Current: {1}", resource.MaxOutputByType(FTLExtensions._powerDefinitionId), resource.CurrentOutputByType(FTLExtensions._powerDefinitionId)));
                        maxpower += resource.MaxOutputByType(FTLExtensions._powerDefinitionId);
                        currentpower += resource.CurrentOutputByType(FTLExtensions._powerDefinitionId);
                    }
                    catch { continue; } // Oxygen generators will have source, but no power, and crash :-(
                }
            }

            power = maxpower - currentpower;

            if (m_inhibitor.IsWorking)
            {
                var def = MyDefinitionManager.Static.GetDefinition(m_inhibitor.BlockDefinition) as MyGyroDefinition;
                var ftlpower = def.RequiredPowerInput * m_inhibitor.GetValueFloat("Power") * m_upgradeFactor * Globals.Instance.RuntimeConfig[ModifierType.InhibitorPowerEfficiency];
                power += Math.Min(ftlpower, maxpower);
            }

            if (power < 0)
                power = 0;

            Logger.Instance.LogDebug(string.Format("Available power: {0}", power));
            return power;
        }

        public void ReloadValues()
        {
            UpgradeValuesChanged();
        }

        public static void ReloadAll()
        {
            var grids = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(grids, x => x is IMyCubeGrid);

            foreach (var grid in grids)
            {
                var ftls = new List<IMySlimBlock>();
                (grid as IMyCubeGrid).GetBlocks(ftls, x => x.FatBlock != null && x.FatBlock is IMyFunctionalBlock && (x.FatBlock as IMyFunctionalBlock).BlockDefinition.SubtypeId == "FTLInhibitor");

                foreach (var ftl in ftls)
                {
                    ftl.FatBlock.GameLogic.GetAs<FTLInhibitor>().ReloadValues();
                }
            }
        }
    }

}
