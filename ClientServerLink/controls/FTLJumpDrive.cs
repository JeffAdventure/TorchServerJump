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
using Ingame = Sandbox.ModAPI.Ingame;
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
using SpaceEngineers.Game.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using Sandbox.Game.EntityComponents;

namespace Phoenix.FTL
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_JumpDrive), new string[] {
        "Phoenix_FTL_LargeShipLargeFTL", "Phoenix_FTL_LargeShipMediumFTL", "Phoenix_FTL_LargeShipSmallFTL",
        "Phoenix_FTL_SmallShipLargeFTL", "Phoenix_FTL_SmallShipMediumFTL", "Phoenix_FTL_SmallShipSmallFTL" })]
    public class FTLJumpDrive : FTLBase
    {
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            m_ftl.PropertiesChanged += FTLJumpDrive_PropertiesChanged;

            if (!m_ftl.IsFTL())
                return;
        }

        public override void AppendingCustomInfo(IMyTerminalBlock arg1, StringBuilder arg2)
        {
            arg2.Append("vv ------- Use Values Below ------- vv\r\n");
            base.AppendingCustomInfo(arg1, arg2);
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();

            CreateTerminalControls<IMyJumpDrive>();

            if (MyAPIGateway.Multiplayer.IsServer)
                m_ftl.NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;

            ParseNameArguments();                                       // Make sure parameters are parsed
        }

        protected override void CreateTerminalControls<T>()
        {
            if (m_ControlsInited.Contains(typeof(T)))
                return;                         // This must be first!

            base.CreateTerminalControls<T>();

            // Change the Jump button to do my own stuff
            List<IMyTerminalControl> controls;
            List<IMyTerminalAction> actions;
            MyAPIGateway.TerminalControls.GetControls<T>(out controls);
            MyAPIGateway.TerminalControls.GetActions<T>(out actions);

            var jumpControl = controls.FirstOrDefault((x) => x.Id == "Jump") as IMyTerminalControlButton;
            if (jumpControl != null)
            {
                jumpControl.Visible = (b) => b.IsFTL();
                var oldAction = jumpControl.Action;
                jumpControl.Action = (b) =>
                {
                    if (b.IsFTL())
                        b.GameLogic.GetAs<FTLBase>().RequestJump();
                    else
                        oldAction(b);
                };
            }

            var jumpAction = actions.FirstOrDefault((x) => x.Id == "Jump") as IMyTerminalAction;

            if (jumpAction != null)
            {
                var oldAction = jumpAction.Action;
                var oldToolbars = jumpAction.InvalidToolbarTypes;
                jumpAction.InvalidToolbarTypes = null;
                jumpAction.Action = (b) =>
                    {
                        if (b.IsFTL())
                            b.GameLogic.GetAs<FTLBase>().RequestJump();
                        else
                            oldAction(b);
                    };
            }

            // Keep track of slider control, to change limits
            var distanceControl = controls.FirstOrDefault((x) => x.Id == "JumpDistance") as IMyTerminalControlSlider;
            if (distanceControl != null)
            {
                var oldWriter = distanceControl.Writer;
                distanceControl.Writer = (b,t) =>
                {
                    if (b.IsFTL())
                    {
                        t.AppendFormat("{0:P0} (", distanceControl.Getter(b) / 100f);
                        MyValueFormatter.AppendDistanceInBestUnit(b.GameLogic.GetAs<FTLJumpDrive>().ComputeMaxDistance(), t);
                        t.Append(")");
                    }
                    else
                        oldWriter(b, t);
                };
            }
        }

        protected override float ComputeMaxDistance()
        {
            var jumpDistance = (float)m_ftld.baseRange * m_ftld.rangeFactor * ((m_ftl.GetObjectBuilderCubeBlock() as MyObjectBuilder_JumpDrive).StoredPower / 100.0f);
            return jumpDistance;
        }

        protected override double ComputePowerMultiplier(double power)
        {
            //TODO
            return 1.0;
        }

        static void FTLJumpDrive_PropertiesChanged(IMyTerminalBlock obj)
        {
            var hash = (obj.GetObjectBuilderCubeBlock() as MyObjectBuilder_JumpDrive).JumpTarget;
            var ftld = obj.GameLogic.GetAs<FTLBase>().Data;

            if( ftld.flags.HasFlag(JumpFlags.GPSWaypoint))  // One-time GPS coordinate
                return;

            if( hash != null )
            {
                ftld.jumpTargetGPS = FTLExtensions.GetGPSFromHash(hash.Value);
                return;
            }
            else
            {
                ftld.jumpTargetGPS = null;
                ftld.jumpDistance = obj.GameLogic.GetAs<FTLJumpDrive>().ComputeMaxDistance();
            }
        }

        protected override void SendDistanceChange(float value)
        {
            m_ftl.SetValueFloat("JumpDistance", value / ComputeMaxDistance());
        }

    }
}