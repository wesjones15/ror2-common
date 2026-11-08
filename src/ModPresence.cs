using BepInEx.Bootstrap;
namespace Ror2Common
{
    public static class ModPresence
    {
        public const string MarioGuid = "com.libsms.mariotest", OlimarGuid = "com.libpikmin.olimartest", MeleeGuid = "com.libssbm.melee";
        public static bool Mario => Chainloader.PluginInfos.ContainsKey(MarioGuid);
        public static bool Melee => Chainloader.PluginInfos.ContainsKey(MeleeGuid);
    }
}
