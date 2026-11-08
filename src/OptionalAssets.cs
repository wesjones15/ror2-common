using System.IO;
using BepInEx;
using BepInEx.Configuration;

namespace Ror2Common
{
    public static class OptionalAssets
    {
        static ConfigEntry<bool> builtin;
        public static bool BuiltinOnly
        {
            get
            {
                if (builtin == null)
                    builtin = new ConfigFile(Path.Combine(Paths.ConfigPath, "Ror2Common.Assets.cfg"), true)
                        .Bind("Assets", "BuiltinOnly", false,
                            "Use built-in gameplay and generic shapes without reading or extracting optional game assets. Restart to apply.");
                return builtin.Value;
            }
        }
        // Native construction decides whether gameplay data is usable independently of presentation.
        public static string SimulationPath(string assets, bool ready) => BuiltinOnly ? "" : assets;
    }
}
