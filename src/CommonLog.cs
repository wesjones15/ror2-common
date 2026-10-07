using BepInEx.Logging;

namespace Ror2Common
{
    // ror2-common is compiled into each plugin (no shared DLL to version), so this is each
    // plugin's own logger: set it first thing in the plugin's Awake.
    public static class CommonLog
    {
        public static ManualLogSource Log;

        internal static void Info(string m) => Log?.LogInfo(m);
        internal static void Warn(string m) => Log?.LogWarning(m);
        internal static void Error(string m) => Log?.LogError(m);
    }
}
