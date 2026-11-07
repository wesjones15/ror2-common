// The few BepInEx and RoR2 members Experimental.cs, SharedFiles.cs and CommonLog.cs use, so they compile and run
// headless. ConfigFile reads and writes the one `Key = value` line the real one would.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BepInEx
{
    public static class Paths
    {
        public static string ConfigPath;
        public static string PluginPath;
    }
}

namespace BepInEx.Logging
{
    public class ManualLogSource
    {
        public void LogInfo(object m) => Console.WriteLine("      [info] " + m);
        public void LogWarning(object m) => Console.WriteLine("      [warn] " + m);
        public void LogError(object m) => Console.WriteLine("      [error] " + m);
    }
}

namespace BepInEx.Configuration
{
    public class ConfigEntry<T> { public T Value; }

    public class ConfigFile
    {
        readonly string path;
        public ConfigFile(string path, bool saveOnInit) { this.path = path; }

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description)
        {
            var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
            string prefix = key + " = ";
            string line = lines.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));
            if (line == null)
            {
                lines.Add("[" + section + "]");
                lines.Add("## " + description);
                lines.Add(prefix + defaultValue.ToString().ToLowerInvariant());
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(path, lines);
                return new ConfigEntry<T> { Value = defaultValue };
            }
            return new ConfigEntry<T> { Value = (T)Convert.ChangeType(line.Substring(prefix.Length).Trim(), typeof(T)) };
        }
    }
}

namespace RoR2
{
    public static class NetworkModCompatibilityHelper
    {
        public static IEnumerable<string> networkModList = new[] { "other.mod;1.0" };
    }
}
