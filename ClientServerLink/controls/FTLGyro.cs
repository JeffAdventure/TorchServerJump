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
using Sandbox.ModAPI.Interfaces.Terminal;
using SpaceEngineers.Game.ModAPI;
using VRage.Utils;

namespace Phoenix.FTL
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate, 1)]
    class MissionComponent : MySessionComponentBase
    {
        private bool m_init = false;

        public override void BeforeStart()
        {
            Init();                 // First run code
        }
        public override void LoadData()
        {
            Logger.Instance.Init("FTL");
            Logger.Instance.Enabled = false;
        }

        private void Init()
        {
            if (m_init)
                return;

            m_init = true;

            Logger.Instance.Enabled = true;
            Logger.Instance.Active = true;
        }

        protected override void UnloadData()
        {
            try
            {
                Logger.Instance.Close();
            }
            catch { }
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Gyro), new string[] { "SmallFTL", "LargeFTL", "SmallFTLMed", "LargeFTLMed", "SmallFTLSml", "LargeFTLSml" })]
    public class FTLGyro : FTLBase
    {
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            if (!m_ftl.IsFTL())
                return;

            m_ftl.PropertiesChanged += FTLGyro_PropertiesChanged;
        }

        public override void AppendingCustomInfo(IMyTerminalBlock arg1, StringBuilder arg2)
        {
            arg2.Append("LEGACY - REBUILD FTL\r\n");
            base.AppendingCustomInfo(arg1, arg2);
        }

        public override FTLData InitFTLData()
        {
            var ftld = base.InitFTLData();
            ftld.objectBuilderGyro = ((m_ftl as IMyCubeBlock).GetObjectBuilderCubeBlock() as MyObjectBuilder_Gyro);
            return ftld;
        }

        void FTLGyro_PropertiesChanged(IMyTerminalBlock obj)
        {
            m_ftld.objectBuilderGyro = ((m_ftl as IMyCubeBlock).GetObjectBuilderCubeBlock() as MyObjectBuilder_Gyro);
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();
            CreateTerminalControls<IMyGyro>();

            if (MyAPIGateway.Multiplayer.IsServer)
                m_ftl.NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;

            ParseNameArguments();                                       // Make sure parameters are parsed
        }

        #region Terminal Controls
        private IMyGps m_selectedGps = null;

        protected override void CreateTerminalControls<T>()
        {
            if (m_ControlsInited.Contains(typeof(T)))
                return;                         // This must be first!

            base.CreateTerminalControls<T>();      // This must be second!

            // Do rest of init
            // Create controls first, so they can be referenced below
            var distance = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, T>("JumpDistance");    // Use for compatibility with stock drive
            var selectedTarget = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, T>("Phoenix.FTL.SelectedTarget");
            var remButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, T>("Phoenix.FTL.RemoveBtn");
            var addButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, T>("Phoenix.FTL.SelectBtn");
            var gpsList = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, T>("Phoenix.FTL.GpsList");
            var jumpButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, T>("Phoenix.FTL.Jump");
            IMyTerminalAction jumpAction = MyAPIGateway.TerminalControls.CreateAction<IMyTerminalBlock>("Jump");        // Use for compatibility with stock drive

            jumpButton.Title = MyStringId.GetOrCompute("BlockActionTitle_Jump");
            jumpButton.Visible = (b) => b.IsFTL();
            jumpButton.Enabled = (b) => b.IsWorking;
            jumpButton.Action = (b) => b.GameLogic.GetAs<FTLBase>().RequestJump();
            MyAPIGateway.TerminalControls.AddControl<T>(jumpButton);

            Action updateDelegate = () =>
            {
                distance.UpdateVisual();
                addButton.UpdateVisual();
                remButton.UpdateVisual();
                selectedTarget.UpdateVisual();
            };

            StringBuilder actionname = new StringBuilder();
            actionname.Append("Jump");
            jumpAction.Name = actionname;
            jumpAction.Icon = @"Textures\GUI\Icons\Actions\Toggle.dds";
            jumpAction.ValidForGroups = false;
            jumpAction.Enabled = (b) => b.IsWorking;
            jumpAction.Action = (b) => jumpButton.Action(b);
            MyAPIGateway.TerminalControls.AddAction<T>(jumpAction);

            distance.Title = MyStringId.GetOrCompute("BlockPropertyTitle_JumpDistance");
            distance.SetLimits(0f, 100f);
            distance.Getter = (b) => (b.GameLogic.GetAs<FTLBase>().Data.jumpDistance / b.GameLogic.GetAs<FTLGyro>().ComputeMaxDistance()) * 100f;
            distance.Setter = (b, v) => b.GameLogic.GetAs<FTLGyro>().SendDistanceChange((v / 100f) * b.GameLogic.GetAs<FTLGyro>().ComputeMaxDistance());
            distance.Writer = (b, v) =>
            {
                v.AppendFormat("{0:P0} (", distance.Getter(b) / 100f);
                MyValueFormatter.AppendDistanceInBestUnit(b.GameLogic.GetAs<FTLBase>().Data.jumpDistance, v);
                v.Append(")");
            };
            distance.Visible = (b) => b.IsFTL();
            distance.Enabled = (b) => b.GameLogic.GetAs<FTLBase>().Data.jumpTargetGPS == null;
            MyAPIGateway.TerminalControls.AddControl<IMyGyro>(distance);

            selectedTarget.Title = MyStringId.GetOrCompute("BlockPropertyTitle_DestinationGPS");
            selectedTarget.ListContent = (b, list1, list2) => b.GameLogic.GetAs<FTLGyro>().FillSelectedTarget(b, list1, list2);
            selectedTarget.Visible = (b) => b.IsFTL();
            selectedTarget.VisibleRowsCount = 1;
            MyAPIGateway.TerminalControls.AddControl<IMyGyro>(selectedTarget);

            remButton.Title = MyStringId.GetOrCompute("RemoveProjectionButton");
            remButton.Visible = (b) => b.IsFTL();
            remButton.Enabled = (b) => b.GameLogic.GetAs<FTLGyro>().CanRemove();
            remButton.Action = (b) =>
                {
                    b.GameLogic.GetAs<FTLGyro>().RemoveSelected();
                    updateDelegate();
                };
            MyAPIGateway.TerminalControls.AddControl<IMyGyro>(remButton);

            addButton.Title = MyStringId.GetOrCompute("SelectBlueprint");
            addButton.Visible = (b) => b.IsFTL();
            addButton.Enabled = (b) => b.GameLogic.GetAs<FTLGyro>().CanSelect();
            addButton.Action = (b) =>
                {
                    b.GameLogic.GetAs<FTLGyro>().SelectTarget();
                    updateDelegate();
                };
            MyAPIGateway.TerminalControls.AddControl<IMyGyro>(addButton);

            gpsList.Title = MyStringId.GetOrCompute("BlockPropertyTitle_GpsLocations");
            gpsList.ListContent = (b, list1, list2) => b.GameLogic.GetAs<FTLGyro>().FillGpsList(b, list1, list2);
            gpsList.ItemSelected = (b, v) =>
                {
                    b.GameLogic.GetAs<FTLGyro>().SelectGps(v);
                    updateDelegate();
                };
            gpsList.Visible = (b) => b.IsFTL();
            gpsList.VisibleRowsCount = 8;
            MyAPIGateway.TerminalControls.AddControl<IMyGyro>(gpsList);
        }

        public override void SaveTerminalValues()
        {
            base.SaveTerminalValues();

            StringBuilder customName = new StringBuilder(20);

            var name = m_ftl.CustomName;

            if (string.IsNullOrWhiteSpace(name))
                customName.Append(name);
            else
                customName.Append(m_ftl.DisplayNameText);

            int cmdStartIdx = name.IndexOf('[');
            int cmdEndIdx = name.LastIndexOf(']');

            if (cmdStartIdx != -1 && cmdEndIdx != -1)
                customName.Remove(cmdStartIdx, cmdEndIdx - cmdStartIdx + 1);

            customName.Append("[");

            // Begin saving values
            if( m_ftld.jumpTargetGPS != null )
            {
                customName.AppendFormat("GPS:{0}:{1}:{2}:{3}:;", m_ftld.jumpTargetGPS.Name, m_ftld.jumpTargetGPS.Coords.X, m_ftld.jumpTargetGPS.Coords.Y, m_ftld.jumpTargetGPS.Coords.Z);
            }
            else if (m_ftld.jumpDistance != 0f)
            {
                customName.AppendFormat("D:{0};", m_ftld.jumpDistance);
            }

            customName.Append("]");

            m_ftl.SetCustomName(customName.ToString());
        }
        private void SelectGps(List<MyTerminalControlListBoxItem> selection)
        {
            if (selection.Count > 0)
            {
                m_selectedGps = (IMyGps)selection[0].UserData;
            }
        }

        private bool CanSelect()
        {
            return m_selectedGps != null;
        }

        private void SelectTarget()
        {
            if (CanSelect())
            {
                MessageUtils.SendMessageToAll(new MessageSelectGPS() { GPSHash = m_selectedGps.Hash, FTLId = m_ftl.EntityId });
            }
        }

        private bool CanRemove()
        {
            return m_ftld.jumpTargetGPS != null;
        }

        private void RemoveSelected()
        {
            if (CanRemove())
            {
                MessageUtils.SendMessageToAll(new MessageSelectGPS() { GPSHash = 0, FTLId = m_ftl.EntityId });
            }
        }

        private void FillSelectedTarget(IMyTerminalBlock block, ICollection<MyTerminalControlListBoxItem> selectedTargetList, ICollection<MyTerminalControlListBoxItem> emptyList)
        {
            var ftld = block.GameLogic.GetAs<FTLBase>().Data;

            if (ftld.jumpTargetGPS != null)
            {
                selectedTargetList.Add(new MyTerminalControlListBoxItem(MyStringId.GetOrCompute(ftld.jumpTargetGPS.Name), MyStringId.NullOrEmpty, ftld.jumpTargetGPS));
            }
            else
            {
                selectedTargetList.Add(new MyTerminalControlListBoxItem(MyStringId.GetOrCompute("Blind Jump"), MyStringId.NullOrEmpty, null));
            }
        }

        private void FillGpsList(IMyTerminalBlock block, ICollection<MyTerminalControlListBoxItem> gpsItemList, ICollection<MyTerminalControlListBoxItem> selectedGpsItemList)
        {
            List<IMyGps> gpsList = new List<IMyGps>();
            gpsList = MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.PlayerID);

            foreach (var gps in gpsList)
            {
                var item = new MyTerminalControlListBoxItem(MyStringId.GetOrCompute(gps.Name), MyStringId.NullOrEmpty, gps);
                gpsItemList.Add(item);

                if (m_selectedGps == gps)
                {
                    selectedGpsItemList.Add(item);
                }
            }
        }
        #endregion

        #region Syncing

        protected override void SendDistanceChange(float value)
        {
            m_ftld.jumpDistance = value;
            MessageUtils.SendMessageToAll(new MessageSetJumpDistance() { FTLId = m_ftl.EntityId, Distance = value });
        }

        #endregion

        protected override float ComputeMaxDistance()
        {
            return m_ftld.baseRange * m_ftld.rangeFactor;
        }

        protected override double ComputePowerMultiplier(double power)
        {
            var def = MyDefinitionManager.Static.GetDefinition(m_ftl.BlockDefinition);
            power /= ((def as MyGyroDefinition).RequiredPowerInput * 1000); // Get percentage difference
            (m_ftl as IMyGyro).PowerConsumptionMultiplier = (float)power;
            return power;
        }
    }
}
