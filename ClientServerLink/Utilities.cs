using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Timers;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace ServerLinkMod
{
    public class Utilities
    {
        public enum VerifyResult
        {
            Error,
            Ok,
            Timeout,
            ContentModified
        }

        public const string ZERO_IP = "0.0.0.0:27016";

        private static readonly List<IMyPlayer> _playerCache = new List<IMyPlayer>();

        private static readonly Random Random = new Random();

        /// <summary>
        ///     IMyMultiplayer.JoinServer has to be called this way to prevent crashes.
        /// </summary>
        /// <param name="ip"></param>
        public static void JoinServer(string ip)
        {
           // Logging.Instance.WriteLine($"JoinServer ");
          //  MyAPIGateway.Multiplayer.JoinServer(ip);
            Logging.Instance.WriteLine($"JoinServer InvokeOnGameThread");
            MyAPIGateway.Utilities.InvokeOnGameThread(() => MyAPIGateway.Multiplayer.JoinServer(ip));
           
        }
        

        public static IMyPlayer GetPlayerBySteamId(ulong steamId)
        {
            _playerCache.Clear();
            MyAPIGateway.Players.GetPlayers(_playerCache);
            return _playerCache.FirstOrDefault(p => p.SteamUserId == steamId);
        }

        public static IMyPlayer GetPlayerById(long identityId)
        {
            _playerCache.Clear();
            MyAPIGateway.Players.GetPlayers(_playerCache);
            return _playerCache.FirstOrDefault(p => p.IdentityId == identityId);
        }

        /// <summary>
        ///     Randomizes a vector by the given amount
        /// </summary>
        /// <param name="start"></param>
        /// <param name="distance"></param>
        /// <returns></returns>
        public static Vector3D RandomPositionFromPoint(Vector3D start, double distance)
        {
            double z = Random.NextDouble() * 2 - 1;
            double piVal = Random.NextDouble() * 2 * Math.PI;
            double zSqrt = Math.Sqrt(1 - z * z);
            var direction = new Vector3D(zSqrt * Math.Cos(piVal), zSqrt * Math.Sin(piVal), z);

            //var mat = MatrixD.CreateFromYawPitchRoll(RandomRadian, RandomRadian, RandomRadian);
            //Vector3D direction = Vector3D.Transform(start, mat);
            direction.Normalize();
            start += direction * -2;
            return start + direction * distance;
        }

        //Provided by Phoera
        public static void Explode(Vector3D position, float damage, double radius, IMyEntity owner, MyExplosionTypeEnum type, bool affectVoxels = true)
        {
            var exp = new MyExplosionInfo(damage, damage, new BoundingSphereD(position, radius), type, true)
                      {
                          Direction = Vector3D.Up,
                          ExplosionFlags = MyExplosionFlags.APPLY_FORCE_AND_DAMAGE | MyExplosionFlags.CREATE_PARTICLE_EFFECT | MyExplosionFlags.APPLY_DEFORMATION,
                          OwnerEntity = owner as MyEntity,
                          VoxelExplosionCenter = position
                      };
            if (affectVoxels)
                exp.AffectVoxels = true;
            MyExplosions.AddExplosion(ref exp);
        }
    }

    public static class Extensions
    {
        public static void AddOrUpdate<T>(this Dictionary<T, int> dic, T key, int value)
        {
            if (dic.ContainsKey(key))
                dic[key] += value;
            else
                dic[key] = value;
        }

        public static Vector3D GetPosition(this IMySlimBlock block)
        {
            return block.CubeGrid.GridIntegerToWorld(block.Position);
        }
    }
}
