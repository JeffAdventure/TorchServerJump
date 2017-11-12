using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Phoenix.FTL
{
    public class Globals
    {
        public static bool Debug = false;
        public static readonly string ModName = "FTL";
        public const double RAD_RPM_FACTOR = 9.549296586D;
        public Dictionary<ModifierType, float> RuntimeConfig;
        private static Globals _instance = null;
        public const int MIN_COUNTDOWN_TIME_S = 10;

        public static Globals Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new Globals();
                return _instance;
            }
        }

        private Globals()
        {
            RuntimeConfig = new Dictionary<ModifierType, float>();
        }

        static Globals()
        {
            _instance = new Globals();
            Reload();
        }

        /// <summary>
        /// Clears Global settings and reloads defaults
        /// </summary>
        public static void Reload()
        {
            Instance.RuntimeConfig.Clear();
            Instance.RuntimeConfig.Add(ModifierType.LargeMass, 3000000);
            Instance.RuntimeConfig.Add(ModifierType.SmallMass, 105000);
            Instance.RuntimeConfig.Add(ModifierType.Accuracy, 0.6f);
            Instance.RuntimeConfig.Add(ModifierType.Range, 0.6f);
            Instance.RuntimeConfig.Add(ModifierType.Spool, 1.0f);
            Instance.RuntimeConfig.Add(ModifierType.PowerEfficiency, 1.0f);
            Instance.RuntimeConfig.Add(ModifierType.LargeRange, 100000);
            Instance.RuntimeConfig.Add(ModifierType.SmallRange, 50000);
            Instance.RuntimeConfig.Add(ModifierType.FTLMedFactor, (float)2 / (float)3);
            Instance.RuntimeConfig.Add(ModifierType.FTLSmlFactor, (float)1 / (float)3);

            Instance.RuntimeConfig.Add(ModifierType.MaxSpool, 120);
            Instance.RuntimeConfig.Add(ModifierType.MaxCooldown, 360);
            Instance.RuntimeConfig.Add(ModifierType.CooldownMultiplier, 3.0f);

            // FTL Inhibitor
            Instance.RuntimeConfig.Add(ModifierType.InhibitorRange, 50.0f);
            Instance.RuntimeConfig.Add(ModifierType.InhibitorPowerEfficiency, 1.0f);

            // Load custom values back
            foreach (var value in FTLAdmin.Configuration.BaseValues)
            {
                Instance.RuntimeConfig[value.Item1] = value.Item2;
            }
        }

        /// <summary>
        /// This unloads all the data to free statics
        /// </summary>
        public static void Unload()
        {
            Instance.RuntimeConfig = null;
            _instance = null;
        }
    }

    // These need to be initialized at start, so they are cached
    internal static class Regexes
    {
        public static Regex Coordinates = new Regex(@"(\s*-?\d+\s*\.?\s*)");  // finds numbers in the format: 0.0.0
    }
}
