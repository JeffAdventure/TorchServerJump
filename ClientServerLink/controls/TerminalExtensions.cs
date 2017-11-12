using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;

namespace Phoenix.FTL
{
    public struct MyTuple<T1, T2>
    {
        public T1 Item1;
        public T2 Item2;

        public MyTuple(T1 item1, T2 item2)
        {
            Item1 = item1;
            Item2 = item2;
        }
    }

    public static class TerminalExtensions
    {
        public static void ShowMessageToUsersInRange(this IMyFunctionalBlock ftl, string message, int time = 2000, bool bIsError = false)
        {
            bool isMe = false;

            if (MyAPIGateway.Players == null || MyAPIGateway.Entities == null || MyAPIGateway.Session == null || MyAPIGateway.Utilities == null)
                return;

            if (MyAPIGateway.Multiplayer.IsServer || MyAPIGateway.Session.Player == null)
            {
                // DS, look for players
                VRageMath.BoundingBoxD box = ftl.GetTopMostParent().PositionComp.WorldAABB;

                List<IMyEntity> entities = MyAPIGateway.Entities.GetEntitiesInAABB(ref box);
                HashSet<IMyPlayer> players = new HashSet<IMyPlayer>();

                foreach (var entity in entities)
                {
                    if (entity == null)
                        continue;

                    var player = MyAPIGateway.Players.GetPlayerControllingEntity(entity);

                    if (player != null && entity.PositionComp.WorldAABB.Intersects(box))
                        players.Add(player);
                }

                foreach (var player in players)
                {
                    SendTextMessage(player, message, time, bIsError);
                }
            }
            else
            {
                if (MyAPIGateway.Session.Player == null || MyAPIGateway.Session.Player.Controller == null
                    || MyAPIGateway.Session.Player.Controller.ControlledEntity == null)
                    return;

                VRageMath.BoundingBoxD box = ftl.GetTopMostParent().PositionComp.WorldAABB;

                List<IMyEntity> entities = MyAPIGateway.Entities.GetEntitiesInAABB(ref box);

                foreach (var entity in entities)
                {
                    if (entity == null)
                        continue;

                    if (entity.EntityId == MyAPIGateway.Session.Player.Controller.ControlledEntity.Entity.GetTopMostParent().EntityId &&
                        entity.PositionComp.WorldAABB.Intersects(box))
                    {
                        isMe = true;
                        break;
                    }
                }

                if ((MyAPIGateway.Players.GetPlayerControllingEntity(ftl.GetTopMostParent()) != null
                    && MyAPIGateway.Session.Player != null
                    && MyAPIGateway.Session.Player.PlayerID == MyAPIGateway.Players.GetPlayerControllingEntity(ftl.GetTopMostParent()).PlayerID)
                    || isMe)
                    MyAPIGateway.Utilities.ShowNotification(message, time);
            }
        }


        public static void SendTextMessage(IMyPlayer player, string message, int displayTime, bool bIsError)
        {
            MessageUtils.SendMessageToPlayer(player.SteamUserId, new MessageText() { Message = message, Timeout = displayTime, Error = bIsError });
        }

        public static HashSet<IMyTerminalBlock> GetGroupedBlocks(this IMyFunctionalBlock reference, MyObjectBuilderType objtype, string subtype = null)
        {
            var blocks = new HashSet<IMyTerminalBlock>();

            if (MyAPIGateway.TerminalActionsHelper == null)
                return blocks;

            var terminal = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(reference.CubeGrid as IMyCubeGrid);

            if (terminal == null)   // This will get hit when leaving the world
                return blocks;
            var groups = new List<IMyBlockGroup>();
            var blocksInGroup = new List<IMyTerminalBlock>();
            terminal.GetBlockGroups(groups);

            // Scan each group, looking for the FTL
            foreach (var group in groups)
            {
                group.GetBlocks(blocksInGroup);
                if (blocksInGroup.Contains(reference))
                {
                    // We found one, grab the blocks we want
                    foreach (var block in blocksInGroup)
                    {
                        // Make sure the blocks match the type we're looking for
                        if (block.BlockDefinition.TypeId == objtype
                            && (string.IsNullOrEmpty(subtype)
                                || block.BlockDefinition.SubtypeId.ToUpperInvariant().Contains(subtype)))
                            blocks.Add(block as IMyTerminalBlock);
                    }
                }
            }
            return blocks;
        }

        #region Ship Controller
        /// <summary>
        /// Find the best reference orientation to use for jump drive coordinates.
        /// It will try one of several things:
        /// 1) Find a cockpit or RC block grouped to the FTL. If it find any, it will use the 'Main Cockpit', if set, otherwise random.
        /// 2) Search for a non-grouped cockpit or remote control block. If it finds only one, it uses that
        /// 3) If there are multiple, and one has 'FTL' in the name, it uses the first one found.
        /// 4) If there are multiple, and not named, it finds the first one with 'Main Cockpit' set
        /// 4) If there are multiple, and not named, it finds the first one with 'Control Thrusters' set
        /// 5) If there are no cockpits, it uses the original entity reference.
        /// </summary>
        /// <returns>Worldmatrix of entity to use for reference</returns>
        public static VRageMath.MatrixD GetShipReference(this IMyFunctionalBlock ftl)
        {
            VRageMath.MatrixD reference = ftl.GetTopMostParent().PositionComp.WorldMatrix;
            var controller = GetShipController(ftl);
            if (controller != null)
                reference = controller.WorldMatrix;

            return reference;
        }

        public static IMyTerminalBlock GetShipController(this IMyFunctionalBlock ftl, string tag = "FTL")
        {
            IMyTerminalBlock reference = null;
            var cockpitObjectBuilder = new MyObjectBuilderType(typeof(MyObjectBuilder_Cockpit));
            var rcObjectBuilder = new MyObjectBuilderType(typeof(MyObjectBuilder_RemoteControl));

            var cockpitlist = ftl.GetGroupedBlocks(cockpitObjectBuilder);
            var rclist = ftl.GetGroupedBlocks(rcObjectBuilder);
            cockpitlist.UnionWith(rclist);

            if (cockpitlist.Count != 0)
            {
                // If it's grouped, just find the first non-passenger seat one
                foreach (var cockpit in cockpitlist)
                {
                    var definition = MyDefinitionManager.Static.GetCubeBlockDefinition(cockpit.BlockDefinition);

                    if (definition != null && definition is MyShipControllerDefinition && (definition as MyShipControllerDefinition).EnableShipControl)
                    {
                        //if (Globals.Debug)
                        //    MyAPIGateway.Utilities.ShowNotification("Found grouped cockpit: " + cockpit.CustomName, 5000);

                        reference = cockpit;

                        // If this is a main cockpit, stop searching for any others and use this one
                        if (((cockpit as IMyCubeBlock).GetObjectBuilderCubeBlock() as MyObjectBuilder_ShipController).IsMainCockpit)
                            break;
                    }
                }
                return reference;       // If there were any grouped cockpits, never continue looking
            }

            var blocks = new List<IMySlimBlock>();
            var filterBlocks = new HashSet<IMySlimBlock>();

            // Get a list of cockpit/remote control blocks
            // Might not work with modded cockpits
            (ftl.CubeGrid as IMyCubeGrid).GetBlocks(blocks,
                (x) => x.FatBlock != null
                && (x.FatBlock.BlockDefinition.TypeId == cockpitObjectBuilder
                    || x.FatBlock.BlockDefinition.TypeId == rcObjectBuilder
                   )
                );

            // Loop through and filter by non-passenger
            foreach (var block in blocks)
            {
                var definition = MyDefinitionManager.Static.GetCubeBlockDefinition(block.FatBlock.BlockDefinition);

                if (definition != null && definition is MyShipControllerDefinition && (definition as MyShipControllerDefinition).EnableShipControl)
                    filterBlocks.Add(block);
            }

            //if (Globals.Debug)
            //    MyAPIGateway.Utilities.ShowNotification("Cockpit count: " + filterBlocks.Count);

            if (filterBlocks.Count == 1)
            {
                //if (Globals.Debug)
                //    MyAPIGateway.Utilities.ShowNotification("Found one cockpit: " + filterBlocks[0].FatBlock.DisplayNameText);
                // If we only found one cockpit, use that
                reference = filterBlocks.FirstElement().FatBlock as IMyTerminalBlock;
            }
            else if (filterBlocks.Count > 1)
            {
                foreach (var block in filterBlocks)
                {
                    if (block.FatBlock.DisplayNameText.Contains(tag))
                    {
                        if (Globals.Debug)
                            MyAPIGateway.Utilities.ShowNotification("Found named cockpit: " + block.FatBlock.DisplayNameText);
                        reference = block.FatBlock as IMyTerminalBlock;
                        break;
                    }
                    else if (((block.FatBlock as IMyCubeBlock).GetObjectBuilderCubeBlock() as MyObjectBuilder_ShipController).IsMainCockpit)
                    {
                        if (Globals.Debug)
                            MyAPIGateway.Utilities.ShowNotification("Found main control cockpit: " + block.FatBlock.DisplayNameText);
                        reference = block.FatBlock as IMyTerminalBlock;
                        break;
                    }
                    else if (((block.FatBlock as IMyCubeBlock).GetObjectBuilderCubeBlock() as MyObjectBuilder_ShipController).ControlThrusters)
                    {
                        if (Globals.Debug)
                            MyAPIGateway.Utilities.ShowNotification("Found thruster control cockpit: " + block.FatBlock.DisplayNameText);
                        reference = block.FatBlock as IMyTerminalBlock;
                    }
                }
            }

            return reference;
        }
        #endregion
        
        /// <summary>
        /// This will get a hashset of all entities connected to reference block or entity.
        /// </summary>
        /// <param name="block"></param>
        /// <returns></returns>
        public static HashSet<IMyEntity> GetConnectedGrids(this IMyEntity block, HashSet<IMyEntity> existing = null)
        {
            var list = existing ?? new HashSet<IMyEntity>();
            var parent = block.GetTopMostParent() as IMyCubeGrid;

            if (parent == null)
                return list;            // This was not a cubegrid

            //Logger.Instance.LogDebug("Total list items: " + list.Count);

            var rotorbases = GetPistonOrRotorBase(parent);
            var rotortops = GetPistonOrRotorTops(parent);

            //Logger.Instance.LogDebug(string.Format("Bases: {0}, Tops: {1}", rotorbases.Count, rotortops.Count));

            if (list.Contains(parent))
                return list;

            list.Add(parent);

            foreach (var id in rotorbases)
            {
                IMyEntity entity = null;

                if (MyAPIGateway.Entities.TryGetEntityById(id.Value, out entity))
                    list.UnionWith(GetConnectedGrids(entity, list));
            }

            foreach (var id in rotorbases)
            {
                IMyEntity entity = null;

                if (MyAPIGateway.Entities.TryGetEntityById(id.Value, out entity))
                    list.UnionWith(GetConnectedGrids(entity, list));
            }

            //Logger.Instance.LogDebug(string.Format("End: Bases: {0}, Tops: {1}", rotorbases.Count, rotortops.Count));
            return list;
        }
        // This is probably really inefficient
        // Until I can find something better, this will have to do
        private static IMyEntity FindConnectingPiece(IMyEntity entity)
        {
            Logger.Instance.LogDebug(string.Format("FindConnectingPiece({0})", entity.DisplayName));

            var blocks = new List<IMyEntity>();
            var type = (entity as IMyCubeBlock).BlockDefinition.TypeId;
            MyObjectBuilderType searchtype = null;

            //Logger.Instance.LogDebug("Type: " + type.ToString());

            if (type == new MyObjectBuilderType(typeof(MyObjectBuilder_MotorAdvancedRotor)))
                searchtype = new MyObjectBuilderType(typeof(MyObjectBuilder_MotorAdvancedStator));
            else if (type == new MyObjectBuilderType(typeof(MyObjectBuilder_MotorRotor)))
                searchtype = new MyObjectBuilderType(typeof(MyObjectBuilder_MotorStator));
            else if (type == new MyObjectBuilderType(typeof(MyObjectBuilder_Wheel)))
                searchtype = new MyObjectBuilderType(typeof(MyObjectBuilder_MotorSuspension));

            //Logger.Instance.LogDebug("Search Type: " + searchtype.ToString());
            var aabb = entity.WorldAABB;

            blocks = MyAPIGateway.Entities.GetEntitiesInAABB(ref aabb);

            foreach (var block in blocks)
            {
                //Logger.Instance.LogDebug("Block type: " + block.GetType());
                if (block is IMyMotorStator &&
                    ((block as IMyCubeBlock).GetObjectBuilderCubeBlock() as MyObjectBuilder_MotorBase).RotorEntityId == entity.EntityId)
                {
                    Logger.Instance.LogDebug(string.Format("Found base: {0}, {1}", (block as IMyCubeBlock).DisplayNameText, block.EntityId));
                    return block;
                }
            }
            return null;
        }

        /// <summary>
        /// This function gets a list of motor/piston 'tops'
        /// </summary>
        /// <param name="obj">Grid to search</param>
        /// <returns></returns>
        public static Dictionary<long, long> GetPistonOrRotorTops(IMyEntity obj)
        {
            List<IMySlimBlock> list = new List<IMySlimBlock>();
            Dictionary<long, long> topParts = new Dictionary<long, long>();

            if (obj.GetTopMostParent() is IMyCubeGrid)
            {
                (obj.GetTopMostParent() as IMyCubeGrid).GetBlocks(list, (x) => x.FatBlock != null && (x.FatBlock is IMyMotorRotor || x.FatBlock is IMyPistonTop));
            }

            foreach (var entity in list)
            {
                //if (entity.FatBlock.BlockDefinition.TypeId == new MyObjectBuilderType(typeof(MyObjectBuilder_PistonTop)) ||
                Logger.Instance.LogDebug(string.Format("Found top: {0}, {1}", (entity.FatBlock as IMyCubeBlock).DisplayNameText, entity.FatBlock.EntityId));
                long? baseId = 0;

                if (entity.FatBlock is IMyPistonTop)
                    baseId = (entity.FatBlock as IMyPistonTop).Piston?.EntityId;
                else if (entity.FatBlock is IMyMotorRotor)
                    baseId = (entity.FatBlock as IMyMotorRotor).Stator?.EntityId;

                topParts.Add(entity.FatBlock.EntityId, baseId ?? 0);
            }
            return topParts;
        }

        /// <summary>
        /// This retrieves all the piston/rotor/wheel bases on a ship
        /// </summary>
        /// <param name="obj">Parent cubegrid</param>
        /// <returns></returns>
        public static Dictionary<long, long> GetPistonOrRotorBase(IMyEntity obj)
        {
            List<IMySlimBlock> list = new List<IMySlimBlock>();
            Dictionary<long, long> baseParts = new Dictionary<long, long>();

            if (obj.GetTopMostParent() is IMyCubeGrid)
            {
                (obj.GetTopMostParent() as IMyCubeGrid).GetBlocks(list, (x) => x.FatBlock is IMyPistonBase || x.FatBlock is IMyMotorBase || x.FatBlock is IMyShipConnector);
            }

            foreach (var entity in list)
            {
                //Logger.Instance.LogDebug("Found item: " + entity.FatBlock.BlockDefinition.TypeId.ToString());
                long? topId = null;
                Logger.Instance.LogDebug("Found base: " + entity.FatBlock.GetType().ToString());

                if (entity.FatBlock is IMyPistonBase)
                    topId = (entity.FatBlock as IMyPistonBase).Top?.EntityId;
                else if (entity.FatBlock is IMyMotorBase)
                    topId = (entity.FatBlock as IMyMotorBase).Rotor?.EntityId;
                else if (entity.FatBlock is IMyShipConnector)
                    topId = (entity.FatBlock as IMyShipConnector).OtherConnector?.EntityId;

                Logger.Instance.LogDebug("Connected to: " + topId);

                baseParts.Add(entity.FatBlock.EntityId, topId ?? 0);
            }

            return baseParts;
        }

        public static bool HasPistonOrRotorTop(IMyEntity obj)
        {
            // This could be optimized, since we only care if there are ANY, 
            // we only need to know if at least one exists.
            Dictionary<long, long> list = GetPistonOrRotorTops(obj);

            return (list.Count > 0);
        }
    }
}
