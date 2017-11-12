using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Interfaces.Terminal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Phoenix.FTL
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_JumpDrive))]
    public class StockJumpInhibitor : MyGameLogicComponent
    {
        MyObjectBuilder_EntityBase m_objectBuilder = null;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            m_objectBuilder = objectBuilder;

            Entity.NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return (copy && m_objectBuilder != null ? m_objectBuilder.Clone() as MyObjectBuilder_EntityBase : m_objectBuilder);
        }

        public override void UpdateOnceBeforeFrame()
        {
            if (MyAPIGateway.Multiplayer == null)
            {
                Entity.NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                return;
            }

            if (MyAPIGateway.Multiplayer.IsServer)
                Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;
        }

        public override void UpdateBeforeSimulation10()
        {
            if (FTLAdmin.Configuration.BlockStockJump && (Entity as IMyJumpDrive).Enabled)
            {
                try
                {
                    float distance = (Entity as IMyJumpDrive).GetValueFloat("JumpDistance");
                    var reference = (Entity as IMyFunctionalBlock).GetShipReference();
                    VRageMath.Vector3D pos = reference.Translation;

                    var smaxdist = (Entity as IMyJumpDrive).DetailedInfo.Split(' ')[(Entity as IMyJumpDrive).DetailedInfo.Split(' ').Length - 2];
                    float maxdist = 0;
                    if (float.TryParse(smaxdist, out maxdist))
                    {
                        // Found formula in workshop script: 479442492
                        // per = (Dist - 5) * (100/(Max-5))
                        // dist = (per / (100/(max-5))) + 5
                        distance = (distance / (100 / (maxdist - 5))) + 5;
                    }

                    distance *= 1000;           // Distance from jump drive is in km, convert to m
                    pos += (reference.Forward * distance);

                    if (FTLExtensions.IsInhibited(Entity.GetPosition()) || FTLExtensions.IsInhibited(pos))
                    {
                        (Entity as IMyJumpDrive).RequestEnable(false);
                        (Entity as IMyFunctionalBlock).ShowMessageToUsersInRange("Jump drive disabled, interference detected", 5000, true);
                    }
                }
                catch (Exception)
                {
                    // Ignore exceptions for now, they occur during load
                    //Logger.Instance.LogException(ex);
                }
            }
        }
    }
}
