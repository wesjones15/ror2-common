using System;
using BepInEx;
using BepInEx.Configuration;
using Path = System.IO.Path;

namespace Ror2Common
{
    // One switch, shared by every plugin, for content that isn't finished: a survivor or skin marked
    // `experimental` is not registered, not offered and never extracted unless this is on. Each unfinished item
    // carries its own marker (MeleeFighterDef.experimental, a skin spec's Experimental); delete the marker when the
    // item is finished. Off by default, so a fresh install shows only finished content.
    //
    // On: `ShowUnfinished = true` in BepInEx/config/Ror2Common.Experimental.cfg (one file read by all three
    // plugins; releases never ship BepInEx/config, so a developer's setting survives updates), or the environment
    // variable WESLEY_DEV=1.
    public static class Experimental
    {
        public const string ConfigFileName = "Ror2Common.Experimental.cfg";
        public const string EnvVar = "WESLEY_DEV";
        static bool? enabled;

        public static bool Enabled
        {
            get
            {
                if (enabled == null)
                {
                    bool on = Environment.GetEnvironmentVariable(EnvVar)?.Trim() == "1";
                    try
                    {
                        var config = new ConfigFile(Path.Combine(Paths.ConfigPath, ConfigFileName), true);
                        on |= config.Bind("General", "ShowUnfinished", false,
                            "Show unfinished survivors and skins in every wesleyasaurus mod (Mario, Olimar, Melee). " +
                            "They may be broken. Everyone in a multiplayer game must use the same setting. Restart the game after changing it.").Value;
                    }
                    catch (Exception e) { CommonLog.Log?.LogWarning($"{ConfigFileName}: {e.Message}"); }
                    enabled = on;
                    CommonLog.Log?.LogInfo(on ? "Unfinished content is shown (ShowUnfinished or WESLEY_DEV=1)." : "Unfinished content is hidden.");
                }
                return enabled.Value;
            }
        }

        /// Whether an item with this marker is in the game.
        public static bool Allows(bool experimental) => !experimental || Enabled;

        const string NetworkKey = "wesleyasaurus.unfinished.registered";

        /// Put the setting into RoR2's network mod list, which the lobby compares before letting a player join.
        /// Hidden survivors are never registered, so two players with different settings would have different
        /// body catalogs (shifted indexes, prefabs one side cannot spawn); this makes RoR2 refuse the join instead
        /// of desyncing. Every plugin calls it at startup; ror2-common is compiled into each plugin, so the guard
        /// lives in the AppDomain and the entry is added once, whichever plugins are installed.
        public static void ShareWithLobby()
        {
            var domain = AppDomain.CurrentDomain;
            lock (typeof(object))
            {
                if (domain.GetData(NetworkKey) != null) return;
                domain.SetData(NetworkKey, true);
            }
            try
            {
                RoR2.NetworkModCompatibilityHelper.networkModList = System.Linq.Enumerable.Concat(
                    RoR2.NetworkModCompatibilityHelper.networkModList,
                    new[] { "wesleyasaurus.unfinished;" + (Enabled ? "shown" : "hidden") });
            }
            catch (Exception e) { CommonLog.Log?.LogWarning($"Unfinished-content lobby check not added: {e.Message}"); }
        }
    }
}
