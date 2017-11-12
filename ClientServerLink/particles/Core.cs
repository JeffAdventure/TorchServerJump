using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;
using VRage.Game.Components;
using WeatherSystem;
using VRage.Utils;
using Sandbox.Game.GameSystems;
using VRage.ModAPI;
using Sandbox.Game.Entities;

namespace WeatherSystem
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation, 5)]
    class Core : MySessionComponentBase
    {
        private bool m_initialize = false;

        private void Initialize()
        {
            m_initialize = true;
            Logging.Instance.Log("Starting");
            MyAPIGateway.Utilities.MessageEntered += MessageHandler;
        }

        private void Message(string msg)
        {
            MyAPIGateway.Utilities.ShowMessage("Weather", msg);
            MyAPIGateway.Utilities.ShowNotification(msg);
        }

        private void MessageHandler(string msg, ref bool others)
        {
            Logging.Instance.Log("Recv message {0}", msg);
            if (msg.Equals("/test"))
            {
                others = false;
                Dictionary<string, int> weatherSpec = new Dictionary<string, int>();
                weatherSpec["Snow"] = 100;
                weatherSpec["Fog"] = 10;
                SetWeather(weatherSpec);
            }
        }

        #region WeatherParticles
        private List<MyParticleEffect> nearEffects = new List<MyParticleEffect>();
        private List<MyParticleEffect> farEffects = new List<MyParticleEffect>();

        private void SetWeather(Dictionary<string, int> density)
        {
            DisposeEffects();
            Random r = new Random();
            foreach (string key in density.Keys)
            {
                for (int n = 0; n < density[key]; n++)
                {
                    MyParticleEffect near, far;
                    if (MyParticlesManager.TryCreateParticleEffect("Equinox_Weather_" + key + "_Near", out near))
                    {
                        near.UserRadiusMultiplier = (float)(1 + (r.NextDouble() * 2 - 1) * 0.1);
                        near.UserBirthMultiplier = (float)(1 + (r.NextDouble() * 2 - 1) * 0.1);
                        nearEffects.Add(near);
                    }
                    else
                        Logging.Instance.Log("Failed to create near effect for {0}", key);
                    if (MyParticlesManager.TryCreateParticleEffect("Equinox_Weather_" + key + "_Far", out far))
                    {
                        far.UserRadiusMultiplier = (float)(1 + (r.NextDouble() * 2 - 1) * 0.1);
                      
                        far.UserBirthMultiplier = (float)(1 + (r.NextDouble() * 2 - 1) * 0.1);
                        farEffects.Add(far);
                    }
                    else
                        Logging.Instance.Log("Failed to create far effect for {0}", key);
                }
            }
        }

        #endregion

        private Vector3D lastCamPos;
        private DateTime lastCamTime;
        public override void UpdateBeforeSimulation()
        {
            if (MyAPIGateway.Session == null)
                return;
            if (MyAPIGateway.Utilities.IsDedicated)
                return;

            if (!m_initialize)
            {
                Initialize();
            }

            if (nearEffects.Count + farEffects.Count > 0)
            {
                IMyCamera cam = MyAPIGateway.Session.Camera;
                Vector3D pos = cam.Position;
                Vector3D vel = (pos - lastCamPos) / (DateTime.Now - lastCamTime).TotalSeconds;
                Vector3D camForward = cam.WorldMatrix.Forward;
                Vector3D camLeft = cam.WorldMatrix.Left;
                Vector3D camUp = cam.WorldMatrix.Up;

                lastCamTime = DateTime.Now;
                lastCamPos = pos;

                pos += vel;
                Vector3D grav = new Vector3D(0);
                {
                    var planets = new List<IMyVoxelBase>();
                    MyAPIGateway.Session.VoxelMaps.GetInstances(planets, v => v is MyPlanet);
                    foreach (IMyVoxelBase planet in planets)
                    {
                        MyGravityProviderComponent gravC = planet.Components.Get<MyGravityProviderComponent>();
                        if (gravC != null)
                            grav += gravC.GetWorldGravity(pos);
                    }
                }
                Vector3D up = -grav;
                up.Normalize();
                Vector3D forward = Vector3D.Cross(Vector3D.Cross(up, camForward), up);
                forward.Normalize();
                Random r = new Random();
                double tanFOV = Math.Tan(Math.PI * cam.FieldOfViewAngle / 360.0);
                foreach (MyParticleEffect effect in nearEffects)
                {
                    double dist = (2 * r.NextDouble() - 1) * 10;
                    double theta = r.NextDouble() * Math.PI * 2;
                    double lrField = Math.Abs(tanFOV * dist) + 5;
                    Vector3D refPos = pos + (camForward * Math.Cos(theta) * dist) + (camLeft * dist * Math.Sin(theta)) + (lrField * camUp * (r.NextDouble() * 2 - 1));
                    effect.WorldMatrix = MatrixD.CreateWorld(refPos, forward, up);
                }
                foreach (MyParticleEffect effect in farEffects)
                {
                    double dist = 10 + r.NextDouble() * 50;
                    double lrMod = (r.NextDouble() * 2) - 1;
                    double lrField = Math.Abs(tanFOV * dist);
                    Vector3D refPos = pos + (camForward * dist) + (lrMod * lrField * camLeft) + (lrField * camUp * (r.NextDouble() * 2 - 1));
                    effect.WorldMatrix = MatrixD.CreateWorld(refPos, forward, up);
                }
            }

            //Logging.Instance.Log("Did a thing");
        }

        private void DisposeEffects()
        {
            nearEffects.ForEach((a) => MyParticlesManager.RemoveParticleEffect(a));
            nearEffects.Clear();
            farEffects.ForEach((a) => MyParticlesManager.RemoveParticleEffect(a));
            farEffects.Clear();
        }

        protected override void UnloadData()
        {
            base.UnloadData();
            DisposeEffects();
            Logging.Instance.Log("Ending");
            m_initialize = false;
            MyAPIGateway.Utilities.MessageEntered -= MessageHandler;
            Logging.Instance.Close();
        }
    }
}
