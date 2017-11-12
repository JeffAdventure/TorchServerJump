using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;

namespace Phoenix.FTL
{
    /// <summary>
    /// Contains all the runtime information for a specific FTL block
    /// </summary>
    public class FTLData
    {
        //public VRage.FastResourceLock ftlLock = new FastResourceLock();
        public JumpFlags flags = JumpFlags.None;
        public MyObjectBuilder_EntityBase objectBuilder;
        public MyObjectBuilder_Gyro objectBuilderGyro;
        public float baseRange;            // Starting range drive can jump to
        public double power = 0;
        public double powerConsumptionMultiplier = 1;
        public IMyCubeBlock ftl = null;

        /// <summary>
        /// Data related to upgrades
        /// </summary>
        #region Upgrades
        public float spoolBase = Globals.Instance.RuntimeConfig[ModifierType.Spool];
        public float rangeBase = Globals.Instance.RuntimeConfig[ModifierType.Range];
        public float accuracyBase = Globals.Instance.RuntimeConfig[ModifierType.Accuracy];
        public float powerBase = Globals.Instance.RuntimeConfig[ModifierType.PowerEfficiency];

        public float spoolFactor = Globals.Instance.RuntimeConfig[ModifierType.Spool];
        public float rangeFactor = Globals.Instance.RuntimeConfig[ModifierType.Range];
        public float accuracyFactor = Globals.Instance.RuntimeConfig[ModifierType.Accuracy];
        public float powerFactor = Globals.Instance.RuntimeConfig[ModifierType.PowerEfficiency];
        #endregion

        /// <summary>
        /// Data related to actual jumping
        /// </summary>
        #region Jumping
        public float jumpDistance = 0;
        public DateTime jumpTime;
        public JumpState jumpState = JumpState.Idle;
        public DateTime resetTime;
        public VRageMath.Vector3D jumpDest;         // Actual destination position
        public VRageMath.Vector3D jumpDirection;    // Direction relative to FTL entity
        public IMyGps jumpTargetGPS;
        public double rangeMultiplier = 1.0;
        public double totalPowerAvailable;
        public double totalPowerWanted;
        public double totalPowerActive;
        public double totalMass;
        public float cooldownPower = 0;         // Keep track of power required, to force cooldown to maintain it
        #endregion

        /// <summary>
        /// Data related to slave control
        /// </summary>
        #region Slave
        public bool SlaveActive { get; set; }
        #endregion

        /// <summary>
        /// Data related to messages
        /// </summary>
        #region Messages
        private DateTime m_messageTimer;

        public bool ShowMessage
        {
            get
            {
                if (m_messageTimer < DateTime.Now)
                    return true;
                else
                    return false;
            }
            set
            {
                // If messages are hidden, they are so for up to 1 second
                if (!value)
                    m_messageTimer = DateTime.Now.AddSeconds(1);
                else
                    m_messageTimer = DateTime.Now;
            }
        }
        #endregion

        public FTLData()
        {
            //ReloadValues();
        }

        public void ReloadValues()
        {
            HashSet<ModifierType> bFoundValues = new HashSet<ModifierType>();

            // Load custom overrides
            foreach (var value in FTLAdmin.Configuration.BaseValues)
            {
                bFoundValues.Add(value.Item1);
                ParseConfigValue(value);
            }

            if (ftl != null)
            {
                if (!bFoundValues.Contains(ModifierType.Accuracy))
                    ParseConfigValue(new MyTuple<ModifierType, float>() { Item1 = ModifierType.Accuracy, Item2 = Globals.Instance.RuntimeConfig[ModifierType.Accuracy] });
                if (!bFoundValues.Contains(ModifierType.Range))
                    ParseConfigValue(new MyTuple<ModifierType, float>() { Item1 = ModifierType.Range, Item2 = Globals.Instance.RuntimeConfig[ModifierType.Range] });
                if (!bFoundValues.Contains(ModifierType.Spool))
                    ParseConfigValue(new MyTuple<ModifierType, float>() { Item1 = ModifierType.Spool, Item2 = Globals.Instance.RuntimeConfig[ModifierType.Spool] });
                if (!bFoundValues.Contains(ModifierType.PowerEfficiency))
                    ParseConfigValue(new MyTuple<ModifierType, float>() { Item1 = ModifierType.PowerEfficiency, Item2 = Globals.Instance.RuntimeConfig[ModifierType.PowerEfficiency] });
                if (!bFoundValues.Contains(ModifierType.LargeRange))
                    ParseConfigValue(new MyTuple<ModifierType, float>() { Item1 = ModifierType.LargeRange, Item2 = Globals.Instance.RuntimeConfig[ModifierType.LargeRange] });
                if (!bFoundValues.Contains(ModifierType.SmallRange))
                    ParseConfigValue(new MyTuple<ModifierType, float>() { Item1 = ModifierType.SmallRange, Item2 = Globals.Instance.RuntimeConfig[ModifierType.SmallRange] });
                if (!bFoundValues.Contains(ModifierType.LargeMass))
                    ParseConfigValue(new MyTuple<ModifierType, float>() { Item1 = ModifierType.LargeMass, Item2 = Globals.Instance.RuntimeConfig[ModifierType.LargeMass] });
                if (!bFoundValues.Contains(ModifierType.SmallMass))
                    ParseConfigValue(new MyTuple<ModifierType, float>() { Item1 = ModifierType.SmallMass, Item2 = Globals.Instance.RuntimeConfig[ModifierType.SmallMass] });
            }
        }

        private void ParseConfigValue(MyTuple<ModifierType, float> value)
        {
            if (ftl == null || ftl.UpgradeValues == null)
                return;

            if (value.Item1 == ModifierType.Spool)
            {
                spoolFactor = value.Item2;
                if (ftl.UpgradeValues.ContainsKey(ModifierType.Spool.ToString()))
                    spoolFactor = ftl.UpgradeValues[ModifierType.Spool.ToString()] = (ftl.UpgradeValues[ModifierType.Spool.ToString()] / spoolBase) * value.Item2;
                spoolBase = value.Item2;
            }
            else if (value.Item1 == ModifierType.Accuracy)
            {
                accuracyFactor = value.Item2;
                if (ftl.UpgradeValues.ContainsKey(ModifierType.Accuracy.ToString()))
                    accuracyFactor = ftl.UpgradeValues[ModifierType.Accuracy.ToString()] = (ftl.UpgradeValues[ModifierType.Accuracy.ToString()] - accuracyBase) + value.Item2;
                accuracyBase = value.Item2;
            }
            else if (value.Item1 == ModifierType.Range)
            {
                rangeFactor = value.Item2;
                if (ftl.UpgradeValues.ContainsKey(ModifierType.Range.ToString()))
                    rangeFactor = ftl.UpgradeValues[ModifierType.Range.ToString()] = (ftl.UpgradeValues[ModifierType.Range.ToString()] / rangeBase) * rangeFactor;
                rangeBase = rangeFactor;
            }
            else if (value.Item1 == ModifierType.SmallRange)
            {
                // Depending on the grid size, set the range factor accordingly
                if (ftl.CubeGrid.GridSizeEnum == MyCubeSize.Small)
                {
                    float range = value.Item2;

                    if (ftl.BlockDefinition.SubtypeId.Contains("FTLMed"))
                        range *= Globals.Instance.RuntimeConfig[ModifierType.FTLMedFactor];
                    else if (ftl.BlockDefinition.SubtypeId.Contains("FTLSml"))
                        range *= Globals.Instance.RuntimeConfig[ModifierType.FTLSmlFactor];
                    baseRange = range;
                }
            }
            else if (value.Item1 == ModifierType.LargeRange)
            {
                // Depending on the grid size, set the range factor accordingly
                if (ftl.CubeGrid.GridSizeEnum == MyCubeSize.Large)
                {
                    float range = value.Item2;

                    if (ftl.BlockDefinition.SubtypeId.Contains("FTLMed"))
                        range *= Globals.Instance.RuntimeConfig[ModifierType.FTLMedFactor];
                    else if (ftl.BlockDefinition.SubtypeId.Contains("FTLSml"))
                        range *= Globals.Instance.RuntimeConfig[ModifierType.FTLSmlFactor];
                    baseRange = range;
                }
            }
            else if (value.Item1 == ModifierType.PowerEfficiency)
            {
                powerFactor = value.Item2;
                if (ftl.UpgradeValues.ContainsKey(ModifierType.PowerEfficiency.ToString()))
                    powerFactor = ftl.UpgradeValues[ModifierType.PowerEfficiency.ToString()] = (ftl.UpgradeValues[ModifierType.PowerEfficiency.ToString()] / powerBase) * value.Item2;
                powerBase = value.Item2;
            }
        }

        public static void ReloadAll()
        {
            var grids = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(grids, x => x is IMyCubeGrid);

            foreach (var grid in grids)
            {
                var ftls = new List<IMySlimBlock>();
                (grid as IMyCubeGrid).GetBlocks(ftls, x => x.FatBlock != null && x.FatBlock is IMyFunctionalBlock && (x.FatBlock as IMyFunctionalBlock).IsFTL());

                foreach (var ftl in ftls)
                {
                    var parent = ftl.FatBlock as IMyCubeBlock;
                    var ftld = (parent as IMyFunctionalBlock).GetFTLData();

                    ftld.ReloadValues();

                    //if (parent.UpgradeValues.ContainsKey(ModifierType.Range.ToString()))
                    //    parent.UpgradeValues.Remove(ModifierType.Range.ToString());
                    //if (parent.UpgradeValues.ContainsKey(ModifierType.Spool.ToString()))
                    //    parent.UpgradeValues.Remove(ModifierType.Spool.ToString());
                    //if (parent.UpgradeValues.ContainsKey(ModifierType.Accuracy.ToString()))
                    //    parent.UpgradeValues.Remove(ModifierType.Accuracy.ToString());
                    //if (parent.UpgradeValues.ContainsKey(ModifierType.PowerEfficiency.ToString()))
                    //    parent.UpgradeValues.Remove(ModifierType.PowerEfficiency.ToString());

                    //parent.UpgradeValues.Add(ModifierType.Range.ToString(), ftld.rangeFactor);
                    //parent.UpgradeValues.Add(ModifierType.Spool.ToString(), ftld.spoolFactor);
                    //parent.UpgradeValues.Add(ModifierType.Accuracy.ToString(), ftld.accuracyFactor);
                    //parent.UpgradeValues.Add(ModifierType.PowerEfficiency.ToString(), ftld.powerFactor);

                    parent.GameLogic.GetAs<FTLBase>().UpgradeValuesChanged();
                }
            }
        }
    }
}
