using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace Phoenix.FTL
{

    public static class FTLExtensions
    {
        public static readonly MyDefinitionId _powerDefinitionId = new MyDefinitionId(typeof(VRage.Game.ObjectBuilders.Definitions.MyObjectBuilder_GasProperties), "Electricity");

        public static IMyGps GetGPSFromHash(int hash, long playerid = 0)
        {
            if (playerid == 0)
            {
                List<IMyPlayer> players = new List<IMyPlayer>();
                MyAPIGateway.Players.GetPlayers(players);
                
                foreach(var player in players)
                {
                    var gps = GetGPSFromHash(hash, player.IdentityId);
                    if (gps != null)
                        return gps;
                }
            }
            else
            {
                var gpss = MyAPIGateway.Session.GPS.GetGpsList(playerid);
                
                foreach (var gps in gpss)
                {
                    if (gps.Hash == hash)
                    {
                        Logger.Instance.LogMessage(string.Format("Found GPS: {0}; {1}", gps.Name, gps.Coords.ToString()));
                        return gps;
                    }
                }
            }
            return null;
        }

        public static bool IsFTL(this IMyTerminalBlock obj)
        {
            if ((obj is IMyGyro || obj is IMyJumpDrive) &&
                obj.GameLogic.GetAs<FTLBase>() != null &&
                !string.IsNullOrEmpty(obj.BlockDefinition.SubtypeId)
                && obj.BlockDefinition.SubtypeId.Contains("FTL")
                && obj.BlockDefinition.SubtypeId != "FTLInhibitor")
                return true;
            return false;
        }

        /// <summary>
        /// This gets the FTL strData for a specific entity.
        /// If it doesn't exist, one is added and returned.
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static FTLData GetFTLData(this IMyFunctionalBlock ftl)
        {
            var gamelogic = ftl.GameLogic.GetAs<FTLBase>();
            if (gamelogic.m_ftld == null)
                gamelogic.InitFTLData();
            return gamelogic.m_ftld;                      // Grab the strData that corresponds to the DHD
        }

        public static List<IMySlimBlock> GetFTLDrives(this IMyFunctionalBlock ftl)
        {
            List<IMySlimBlock> ftlDrives = new List<IMySlimBlock>();
            (ftl.CubeGrid as IMyCubeGrid).GetBlocks(ftlDrives,
                (x) => x.FatBlock != null
                    && x.FatBlock is IMyFunctionalBlock
                    && (x.FatBlock as IMyFunctionalBlock).IsFTL());

            return ftlDrives;
        }

        /// <summary>
        /// Gets all valid connected grids, and floating objects or players.
        /// </summary>
        /// <param name="ftl"></param>
        /// <returns></returns>
        public static HashSet<IMyEntity> GetAllValidObjects(this IMyFunctionalBlock ftl)
        {
            Logger.Instance.LogDebug("GetAllValidObjects()");

            var objects = ftl.GetConnectedGrids();
            var floatingobjs = new HashSet<IMyEntity>();

            foreach (var entity in objects)
            {
                var collisions = new List<IMyEntity>();
                var uniqueEntities = new HashSet<IMyEntity>();
                VRageMath.BoundingBoxD shipBB = entity.PositionComp.WorldAABB;

                // Get a list of entities nearby
                collisions = MyAPIGateway.Entities.GetEntitiesInAABB(ref shipBB);
                // The collision list will contain every block on a entity
                // So we need to reduce it down to just the entities themselves
                foreach (var col in collisions)
                {
                    if (col == null)
                        continue;

                    if (col.GetTopMostParent() is IMyCubeGrid)
                    {
                        if (!uniqueEntities.Contains(col.GetTopMostParent()))
                            uniqueEntities.Add(col.GetTopMostParent());
                    }
                    else
                    {
                        uniqueEntities.Add(col);
                    }
                }
                Logger.Instance.LogDebug("uniqueEntities: " + uniqueEntities.Count);

                foreach (var unique in uniqueEntities)
                {
                    if (!IsEntityValid(unique))
                        continue;

                    // Check if the two entities actually intersect, otherwise we don't want to jump it
                    if (!floatingobjs.Contains(unique.GetTopMostParent()) && shipBB.Intersects(unique.GetTopMostParent().PositionComp.WorldAABB))
                        floatingobjs.Add(unique.GetTopMostParent());
                }
            }

            objects.UnionWith(floatingobjs);
            return objects;
        }

        private static bool IsEntityValid(IMyEntity entity)
        {
            if (entity == null)
                return false;

            var ent = entity.GetTopMostParent();
            IMyCubeGrid grid = entity as IMyCubeGrid;

            // Exclude invalid ones
            if (entity.Physics != null && entity.Physics.IsStatic
                || grid != null && grid.IsStatic
                || ((ent is IMyCubeGrid)
                    && (ent as IMyCubeGrid).IsStatic)     // Exclude stations
                || !(entity is IMyFloatingObject || entity is IMyCharacter || entity is IMyCubeGrid)
                || ((ent is IMyCubeGrid)
                    && (ent as IMyCubeGrid).DisplayName.Contains("Phoenix_FTL_WarpEffect"))     // Exclude warp effect
                )
                return false;
            return true;
        }

        //public static HashSet<IMyCubeGrid> GetConnectedGrids()
        //{
        //    var entities = new HashSet<IMyCubeGrid>();

        //}

        public static List<IMyGps> GetGPSWaypoints(long player = 0)
        {
            var gps = new List<IMyGps>();

            if (player == 0 && MyAPIGateway.Session.Player != null)
                player = MyAPIGateway.Session.Player.PlayerID;

            gps = MyAPIGateway.Session.GPS.GetGpsList(player);

            return gps;
        }

#if false
        // This it to spawn small prefabs between the source and destination
        // This works around a game bug causing ships to be deleted if jumping to
        // a map cluster not connected to the source.
        public static HashSet<IMyEntity> SpawnTrail(this IMyFunctionalBlock ftl)
        {
            string prefabName = "NewStationPrefab";
            var ftld = ftl.GetFTLData();

            //looks for the definition of the ship
            var prefab = MyDefinitionManager.Static.GetPrefabDefinition(prefabName);
            if (prefab != null && prefab.CubeGrids == null)
            {
                // If cubegrids is null, reload definitions and try again
                MyDefinitionManager.Static.ReloadPrefabsFromFile(prefab.PrefabPath);
                prefab = MyDefinitionManager.Static.GetPrefabDefinition(prefabName);
            }

            if (prefab == null || prefab.CubeGrids == null || prefab.CubeGrids.Length == 0)
            {
                MyAPIGateway.Utilities.ShowNotification("Error loading prefab: " + prefabName, 7500);
                return null;
            }

            //get the entity containing the ship
            var grid = prefab.CubeGrids[0];

            if (grid == null)
            {
                MyAPIGateway.Utilities.ShowNotification("Error loading prefab entity: " + prefabName, 7500);
                return null;
            }
            //entity.CreatePhysics = false;

            var tempList = new HashSet<IMyEntity>();
            var obList = new HashSet<MyObjectBuilder_EntityBase>();
            var destunit = ftld.jumpDest - ftl.PositionComp.GetPosition();
            double remainingDistance = destunit.Length();
            destunit.Normalize();
            destunit = VRageMath.Vector3D.Negate(destunit);

            var clusterSize = 10000;    // VRageMath.Spatial.MyClusterTree.IdealClusterSize.Length() - 10;
            var collisionCheckAmount = 100;

            do
            {
                var offset = new VRageMath.Vector3D(destunit * remainingDistance);
                var pos = new MyPositionAndOrientation(ftld.jumpDest + offset, ftl.WorldMatrix.Forward, ftl.WorldMatrix.Up);

                Logger.Instance.LogDebug(string.Format("Jump point: {0:F0}, {1:F0}, {2:F0}", pos.Position.X, pos.Position.Y, pos.Position.Z));

                // Check make sure the position won't collide with anything
                var sphere = new VRageMath.BoundingSphereD(pos.Position, collisionCheckAmount);
                while (MyAPIGateway.Entities.GetEntitiesInSphere(ref sphere).Count > 0)
                {
                    pos.Position += (destunit * collisionCheckAmount);
                    sphere = new VRageMath.BoundingSphereD(pos.Position, collisionCheckAmount);
                    remainingDistance += collisionCheckAmount;
                }
                                    
                grid.PositionAndOrientation = pos;

                //important ! every entity has a unique ID. If you try to add your prefab twice, they would have the same ID so that wont work
                //so you use RemapObjectBuilder to generate new IDs for the ship and other entities onboard.
                MyAPIGateway.Entities.RemapObjectBuilder(grid);

                //Create the object and add it to the world.
                var entity = MyAPIGateway.Entities.CreateFromObjectBuilderAndAdd(grid);
                entity.Visible = false;
                tempList.Add(entity);
                obList.Add(grid);
                remainingDistance -= clusterSize;
            } while (remainingDistance > clusterSize / 2);
            return tempList;
        }

        // Plot a course for jumping
        // This works around a game bug causing ships to be deleted if jumping to
        // a map cluster not connected to the source.
        public static HashSet<VRageMath.Vector3D> PlotJumpCourse(this IMyFunctionalBlock ftl)
        {
            var ftld = ftl.GetFTLData();
            var tempList = new HashSet<VRageMath.Vector3D>();
            var destunit = ftld.jumpDest - ftl.PositionComp.GetPosition();
            double remainingDistance = destunit.Length();
            destunit.Normalize();
            destunit = VRageMath.Vector3D.Negate(destunit);
            Logger.Instance.LogDebug(string.Format("Jump destination: {0:F0}, {1:F0}, {2:F0}", ftld.jumpDest.X, ftld.jumpDest.Y, ftld.jumpDest.Z));
            
            var clusterSize = 10000;    // VRageMath.Spatial.MyClusterTree.IdealClusterSize.Length() - 1000;
            var collisionOffsetAmount = 100;

            do
            {
                var offset = new VRageMath.Vector3D(destunit * remainingDistance);
                var pos = ftld.jumpDest + offset;
                Logger.Instance.LogDebug(string.Format("Jump point: {0:F0}, {1:F0}, {2:F0}", pos.X, pos.Y, pos.Z));
                // Check make sure the position won't collide with anything
                var sphere = new VRageMath.BoundingSphereD(pos, ftl.GetTopMostParent().PositionComp.WorldVolume.Radius);
                while (MyAPIGateway.Entities.GetEntitiesInSphere(ref sphere).Count > 0)
                {
                    pos += (destunit * collisionOffsetAmount);
                    sphere = new VRageMath.BoundingSphereD(pos, ftl.GetTopMostParent().PositionComp.WorldVolume.Radius);
                    remainingDistance += collisionOffsetAmount;
                }
                tempList.Add(pos);
                remainingDistance -= clusterSize;
            } while (remainingDistance > clusterSize / 2);
            tempList.Add(ftld.jumpDest);
            
            //MyAPIGateway.Multiplayer.SendEntitiesCreated(obList.ToList());
            //return new HashSet<VRageMath.Vector3D>(tempList.Reverse());
            return tempList;
        }
#endif

        public static bool CheckExecute(this IMyTerminalBlock block, JumpState ftlState, bool error)
        {
            bool executeBlock = false;
            var name = block.DisplayName ?? block.DisplayNameText;

            if (!string.IsNullOrWhiteSpace(name))
            {
                int cmdStartIdx = name.IndexOf('[');
                int cmdEndIdx = name.LastIndexOf(']');

                try
                {
                    // Check if we have custom commands in the name
                    if (cmdStartIdx != -1 && cmdEndIdx != -1)
                    {
                        string sTags = name.Remove(cmdEndIdx).Remove(0, cmdStartIdx + 1);

                        // Split the commands for parsing
                        string[] tags = sTags.Split(new Char[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);

                        foreach (var tag in tags)
                        {
                            Logger.Instance.LogDebug(string.Format("Timer state: {0}, FTL State: {1}", tag, ftlState));

                            if (ftlState == JumpState.Spooling && tag.StartsWith("spool", StringComparison.InvariantCultureIgnoreCase))
                                executeBlock = true;
                            else if (ftlState == JumpState.Jumped && tag.StartsWith("jump", StringComparison.InvariantCultureIgnoreCase))
                                executeBlock = true;
                            else if (ftlState == JumpState.Cooldown && tag.StartsWith("cooldown", StringComparison.InvariantCultureIgnoreCase))
                                executeBlock = true;
                            else if (ftlState == JumpState.Idle && tag.StartsWith("idle", StringComparison.InvariantCultureIgnoreCase))
                                executeBlock = true;
                            else if (error && tag.StartsWith("error", StringComparison.InvariantCultureIgnoreCase))
                                executeBlock = true;
                        }
                    }
                    else
                    {
                        // Execute if block is untagged and we jumped
                        if (ftlState == JumpState.Jumped)
                            executeBlock = true;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogException(ex);
                }
            }
            return executeBlock;
        }

        public static bool IsInhibited(VRageMath.Vector3D pos)
        {
            VRageMath.BoundingSphereD searchRange = new VRageMath.BoundingSphereD(pos, 1000000);
            List<IMyEntity> entities = MyAPIGateway.Entities.GetEntitiesInSphere(ref searchRange);

            foreach (var entity in entities)
            {
                if (entity is IMyFunctionalBlock && (entity as IMyFunctionalBlock).BlockDefinition.SubtypeId == "FTLInhibitor")
                {
                    var ent = entity as IMyFunctionalBlock;

                    // We found an inhibitor, check if has enough range
                    if (ent != null)
                    {
                        if (!ent.IsWorking || !ent.IsFunctional)
                            continue;

                        if ((ent.CubeGrid.GridIntegerToWorld(ent.Position) - pos).Length() <= (ent.GameLogic.GetAs<FTLInhibitor>().Range))
                        {
                            if (FTLAdmin.Configuration.Debug)
                                Logger.Instance.LogDebug(string.Format("Found inhibitor: {0}, at {1}, {2}, {3}", ent.DisplayNameText, ent.PositionComp.GetPosition().X, ent.PositionComp.GetPosition().Y, ent.PositionComp.GetPosition().Z));
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        public static void CalculateUnits(ref double distance, ref string distunits)
        {
            if (distance > 999999)
            {
                distance /= 1000;
                distunits = "km";
            }
            if (distance > 999999)
            {
                distance /= 1000;
                distunits = "Mm";
            }
            if (distance > 999999)
            {
                distance /= 1000;
                distunits = "Gm";
            }
            if (distance > 999999)
            {
                distance /= 1000;
                distunits = "Tm";
            }
        }

        public static bool IsBlockAccessible(this IMyFunctionalBlock ftl, IMyTerminalBlock obj)
        {
            return ftl.HasPlayerAccess(obj.OwnerId);
        }

        public static double GetTotalMass(this IMyFunctionalBlock ftl)
        {
            var ftld = ftl.GetFTLData();

            var oldActive = Logger.Instance.Active;
            Logger.Instance.Active = false;

            Logger.Instance.LogMessage("GetTotalMass()");
            Logger.Instance.IndentLevel++;

            double totalMass = 0;
            try
            {
                var entities = ftl.GetAllValidObjects();

                foreach (var entity in entities)
                {
                    if (entity != null && entity.Physics != null)
                        totalMass += entity.Physics.Mass;
                }
                Logger.Instance.LogDebug(string.Format("Total planets: {0}, mass: {1}", entities.Count, totalMass));
                //m_ftl.ShowMessageToUsersInRange(string.Format("Total mass: {0:F0}", totalMass), 5000);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogException(ex);
            }
            finally
            {
                Logger.Instance.Active = oldActive;
                Logger.Instance.IndentLevel--;
            }

            return totalMass;
        }
#if false
        public static void SetSlaveFTLPower(this IMyFunctionalBlock ftl, bool active)
        {
            var ftld = ftl.GetFTLData();
            List<IMySlimBlock> ftlDrives = ftl.GetFTLDrives();

            if (active)
            {
                // We only want to power on the additional drives
                // we need to make our jump, not all of them
                // This code isn't smart though, just just powers on
                // all blocks in the order found, until there's enough power.
                double totalPower = ftld.objectBuilderGyro.GyroPower;
                if (totalPower < ftld.totalPowerWanted)
                {
                    foreach (var obj in ftlDrives)
                    {
                        if (obj.FatBlock.EntityId == ftl.EntityId)
                            continue;                                   // skip ourself

                        if (obj.FatBlock.IsFunctional)
                        {
                            var ftlsd = (obj.FatBlock as IMyFunctionalBlock).GetFTLData();
                            ftlsd.SlaveActive = true;
                            totalPower += ((MyObjectBuilder_Gyro)(((IMyCubeBlock)(obj.FatBlock)).GetObjectBuilderCubeBlock())).GyroPower;
                        }
                        if (totalPower >= ftld.totalPowerWanted)
                            break;
                    }
                }

                ftld.totalPowerActive = totalPower;
            }
            else
            {
                foreach (var obj in ftlDrives)
                {
                    var ftlsd = (obj.FatBlock as IMyFunctionalBlock).GetFTLData();
                    ftlsd.SlaveActive = false;
                }
            }
        }
#endif
    }
}
