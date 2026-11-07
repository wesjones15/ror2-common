// Headless check of the unfinished-content switch and the SharedFiles extractor-version marker.
//
// Runtime part (ror2-common's own Experimental.cs and SharedFiles.cs, compiled against the stubs below):
//   - off by default (no config file, no WESLEY_DEV); ShowUnfinished=true or WESLEY_DEV=1 turns it on;
//   - Allows() keeps finished items and drops marked ones only while off;
//   - ShareWithLobby() adds one network-mod entry per process, however many plugins call it;
//   - extracted.txt: Current/MarkCurrent round-trip per set, other sets untouched, lock re-entrant.
// Source part (sibling checkouts; SKIP when they are absent): the release roster's markers (Melee ships Samus
// and Yoshi; Mario's Shadow/Bowser Jr. hidden), and every place the Melee plugin registers or extracts a fighter
// goes through its Enabled() filter, so a hidden fighter is never registered and never extracted.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Ror2Common;

static class Program
{
    static int failures;
    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + what);
        if (!ok) failures++;
    }

    static void ResetExperimental() =>
        typeof(Experimental).GetField("enabled", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, null);

    static int Main()
    {
        // Under the build output, not %TEMP% (read-only for some shells; see docs/Development.md).
        string temp = Path.Combine(AppContext.BaseDirectory, "scratch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            Runtime(temp);
            Sources();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Experimental.EnvVar, null);
            try { Directory.Delete(temp, true); } catch { }
        }
        Console.WriteLine(failures == 0 ? "PASS" : $"FAILED ({failures})");
        return failures == 0 ? 0 : 1;
    }

    static void Runtime(string temp)
    {
        string config = Path.Combine(temp, "config"), plugins = Path.Combine(temp, "plugins");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(plugins);
        BepInEx.Paths.ConfigPath = config;
        BepInEx.Paths.PluginPath = plugins;

        // Default: hidden.
        Environment.SetEnvironmentVariable(Experimental.EnvVar, null);
        ResetExperimental();
        Check(!Experimental.Enabled, "default install hides unfinished content");
        Check(Experimental.Allows(false), "finished items are allowed by default");
        Check(!Experimental.Allows(true), "marked items are not allowed by default");
        string cfg = Path.Combine(config, Experimental.ConfigFileName);
        Check(File.Exists(cfg) && File.ReadAllText(cfg).Contains("ShowUnfinished = false"),
            "the shared config file is created with ShowUnfinished = false");

        // The lobby entry: once per process, carrying the setting.
        var before = RoR2.NetworkModCompatibilityHelper.networkModList.ToList();
        Experimental.ShareWithLobby();
        Experimental.ShareWithLobby(); // a second plugin's copy
        var added = RoR2.NetworkModCompatibilityHelper.networkModList.Except(before).ToList();
        Check(added.Count == 1 && added[0] == "wesleyasaurus.unfinished;hidden",
            "ShareWithLobby adds one entry (" + string.Join(",", added) + ")");

        // Config on.
        File.WriteAllText(cfg, File.ReadAllText(cfg).Replace("ShowUnfinished = false", "ShowUnfinished = true"));
        ResetExperimental();
        Check(Experimental.Enabled && Experimental.Allows(true), "ShowUnfinished = true shows marked items");

        // Environment on, config off.
        File.WriteAllText(cfg, File.ReadAllText(cfg).Replace("ShowUnfinished = true", "ShowUnfinished = false"));
        Environment.SetEnvironmentVariable(Experimental.EnvVar, "1");
        ResetExperimental();
        Check(Experimental.Enabled, "WESLEY_DEV=1 shows marked items");
        Environment.SetEnvironmentVariable(Experimental.EnvVar, null);
        ResetExperimental();
        Check(!Experimental.Enabled, "back to hidden without either");

        // Extractor versions.
        string game = SharedFiles.Melee;
        Check(SharedFiles.Root == Path.Combine(plugins, SharedFiles.PackageFolder), "SharedFiles created under plugins");
        Check(!SharedFiles.Current(game, "fighter0", 1), "a set with no record is not current (extracts once)");
        SharedFiles.MarkCurrent(game, "fighter0", 1);
        Check(SharedFiles.Current(game, "fighter0", 1), "a marked set is current");
        Check(!SharedFiles.Current(game, "fighter0", 2), "a bumped version is stale");
        Check(!SharedFiles.Current(game, "fighter6", 1), "other sets are unaffected");
        using (SharedFiles.Lock(game)) SharedFiles.MarkCurrent(game, "fighter6", 1); // re-entrant under a held lock
        Check(SharedFiles.Current(game, "fighter6", 1) && SharedFiles.Current(game, "fighter0", 1),
            "marking a second set keeps the first");
        SharedFiles.MarkCurrent(game, "fighter0", 2);
        Check(SharedFiles.Current(game, "fighter0", 2) && !SharedFiles.Current(game, "fighter0", 1), "re-marking replaces the version");
        Check(!SharedFiles.Current(SharedFiles.Pikmin, "fighter0", 2), "games keep separate records");
        Check(!File.Exists(Path.Combine(SharedFiles.Assets(game), SharedFiles.VersionFile + ".tmp")), "no temp file left");
    }

    static string FindWorkspace()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "ror2-common", "src"))) return d.FullName;
        return null;
    }

    static void Sources()
    {
        string ws = FindWorkspace();
        string samus = ws == null ? null : Path.Combine(ws, "ror2-samus", "Samus");
        if (samus == null || !Directory.Exists(samus)) Console.WriteLine("SKIP  Melee roster (no ror2-samus checkout beside ror2-common)");
        else MeleeRoster(samus);
        string mario = ws == null ? null : Path.Combine(ws, "ror2-mario", "MarioTest");
        if (mario == null || !Directory.Exists(mario)) Console.WriteLine("SKIP  Mario skins (no ror2-mario checkout beside ror2-common)");
        else MarioSkins(mario);
    }

    // The release roster: the fighters a default install has. Change this when a fighter is finished.
    static readonly HashSet<string> MeleeReleased = new HashSet<string> { "Samus", "Yoshi" };
    static readonly string[] MeleeAll = { "Samus", "Fox", "Luigi", "Pikachu", "Captain", "Link", "Yoshi", "Donkey" };

    static void MeleeRoster(string samus)
    {
        var marked = new Dictionary<string, bool>();
        foreach (string file in Directory.GetFiles(samus, "*Def.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            var m = Regex.Match(text, @"fighterKind\s*=\s*MeleeNative\.Fighter(\w+)");
            if (!m.Success) continue;
            marked[m.Groups[1].Value] = Regex.IsMatch(text, @"\bexperimental\s*=\s*true");
        }
        foreach (string name in MeleeAll)
            Check(marked.ContainsKey(name), $"Melee {name} has a fighter def");
        // Run the real switch over the markers, as Plugin.Enabled() does, with the default (hidden) setting.
        ResetExperimental();
        var shown = marked.Where(kv => Experimental.Allows(kv.Value)).Select(kv => kv.Key).ToList();
        Check(shown.Count == MeleeReleased.Count && shown.All(MeleeReleased.Contains),
            "default Melee roster is " + string.Join(", ", MeleeReleased) + " (got " + string.Join(", ", shown) + ")");

        // Every per-fighter registration and extraction goes through Enabled(); Roster is walked only to define
        // Enabled(), to read a kind's readiness and to clear it on failure.
        string plugin = File.ReadAllText(Path.Combine(samus, "Plugin.cs"));
        var rosterLoops = Regex.Matches(plugin, @"foreach\s*\(\s*var\s+f\s+in\s+Roster\s*\)(?<body>[^\r\n]*)");
        foreach (Match loop in rosterLoops)
        {
            string body = loop.Groups["body"].Value;
            bool allowed = body.Contains("Experimental.Allows") || body.Contains("f.Kind==kind") || body.Contains("f.Ready=false");
            Check(allowed, "Roster loop only filters/reads: " + body.Trim());
        }
        Check(Regex.IsMatch(plugin, @"foreach\s*\(\s*var\s+f\s+in\s+Roster\s*\)\s*if\s*\(\s*Experimental\.Allows\(f\.Def\(\)\.experimental\)\)"),
            "Enabled() filters by each def's marker");
        foreach (Match call in Regex.Matches(plugin, @"ssbm_extract\([^;]*f\.Kind\)"))
        {
            // The enclosing loop of every per-fighter extract is `foreach(var f in Enabled())`.
            string before = plugin.Substring(0, call.Index);
            int loopAt = before.LastIndexOf("foreach(var f in ", StringComparison.Ordinal);
            Check(loopAt >= 0 && before.Substring(loopAt).StartsWith("foreach(var f in Enabled())", StringComparison.Ordinal),
                "fighter extraction loops over Enabled()");
        }
        Check(Regex.IsMatch(plugin, @"foreach\s*\(\s*var\s+f\s+in\s+Enabled\(\)\s*\)\s*\{[^}]*AddBody|static void AddContent[\s\S]{0,200}foreach\(var f in Enabled\(\)\)"),
            "content registration loops over Enabled()");
    }

    static void MarioSkins(string mario)
    {
        string skin = File.ReadAllText(Path.Combine(mario, "MarioSkin.cs"));
        var shadow = Regex.Match(skin, @"\[Shadow\]\s*=\s*new\s+SkinSpec[\s\S]*?\}");
        if (!shadow.Success) shadow = Regex.Match(skin, @"Shadow[\s\S]{0,600}?OwnSurvivor\s*=\s*true[^\r\n]*");
        Check(shadow.Success && shadow.Value.Contains("Experimental = true"), "Mario's Shadow/Bowser Jr. spec is marked unfinished");
        Check(Regex.IsMatch(skin, @"Offered\(SkinSpec spec\)[^;]*Experimental\.Allows\(spec\.Experimental\)"), "Mario skins are offered through Experimental.Allows");
        string plugin = File.ReadAllText(Path.Combine(mario, "Plugin.cs"));
        Check(Regex.IsMatch(plugin, @"if \(KoopaJrEnabled\) KoopaJrBody\.Register\("), "Bowser Jr.'s survivor is registered only when allowed");
        Check(Regex.IsMatch(plugin, @"KoopaJrEnabled => Experimental\.Allows\(MarioSkin\.Specs\[MarioSkin\.Shadow\]\.Experimental\)"), "Bowser Jr. follows the Shadow marker");
    }
}
