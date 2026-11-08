using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Ror2Common
{
    // The one folder every plugin (Olimar, Mario, Melee) reads and writes: the `wesleyasaurus-SharedFiles`
    // package, a separate install with two folders and no code.
    //
    //   <plugins>/wesleyasaurus-SharedFiles/put_games_here/   the player's disc images (any names, one level of
    //                                                         subfolders too; told apart by their game ID)
    //   <plugins>/wesleyasaurus-SharedFiles/assets/<game>/    what any plugin extracts from that game's disc
    //
    // Assets are per game, not per mod: whichever plugin needs a file extracts it into that game's folder, and
    // every plugin reads it from there, so nothing is extracted twice. There is no path setting and no pointer
    // file. r2modman names the folder `<team>-<package>`; a manual install unzips the same folder name. A renamed
    // folder is still found by the marker file in its put_games_here/.
    public static class SharedFiles
    {
        public const string PackageFolder = "wesleyasaurus-SharedFiles";
        public const string DiscFolder = "put_games_here";
        public const string AssetsFolder = "assets";
        /// Where a player is told to put discs, relative to the game's BepInEx/plugins.
        public const string DiscHint = "BepInEx/plugins/" + PackageFolder + "/" + DiscFolder + "/";
        /// Shipped inside put_games_here/ so the folder survives zipping and can be found under any name.
        public const string Marker = "README.txt";

        /// Asset folders, one per game. Pikmin 2's files sit in `pikmin/p2/` (libpikmin reads them there).
        public const string Pikmin = "pikmin", Sunshine = "sunshine", Melee = "melee";

        static readonly string[] DiscExtensions = { ".iso", ".gcm", ".ciso" };
        static string root;
        static readonly object rootLock = new object();

        /// `<plugins>/wesleyasaurus-SharedFiles`, created if missing (a manual install that skipped the package).
        public static string Root
        {
            get
            {
                lock (rootLock)
                {
                    if (root == null) root = Find(BepInEx.Paths.PluginPath);
                    return root;
                }
            }
        }

        /// The SharedFiles folder under `plugins`: the package's own name first, then any first-level folder whose
        /// put_games_here/ holds the marker, else the package's name (created, with a note in put_games_here/).
        public static string Find(string plugins)
        {
            string named = Path.Combine(plugins, PackageFolder);
            if (Directory.Exists(named)) return named;
            try
            {
                foreach (string dir in Directory.GetDirectories(plugins).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                    if (File.Exists(Path.Combine(dir, DiscFolder, Marker))) return dir;
            }
            catch { }
            // Absence is normal. Extraction creates its output only when an optional
            // disc is supplied; basic play must not need a writable SharedFiles folder.
            return named;
        }

        /// Where the player puts their discs.
        public static string DiscDir => Path.Combine(Root, DiscFolder);

        /// `assets/<game>`, the cache for one game's extracted files.
        public static string Assets(string game) => Path.Combine(Root, AssetsFolder, game);

        /// put_games_here/ and its direct subfolders.
        public static string[] DiscDirs()
        {
            if (OptionalAssets.BuiltinOnly) return Array.Empty<string>();
            var dirs = new List<string> { DiscDir };
            try { dirs.AddRange(Directory.GetDirectories(DiscDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase)); }
            catch { }
            return dirs.ToArray();
        }

        /// The disc folders joined for the native finders' `;`-separated search lists.
        public static string SearchList() => string.Join(";", DiscDirs());

        /// The six-character game ID of a GameCube image (.iso/.gcm, or .ciso whose first data block starts at
        /// 0x8000), or null if it can't be read. `revision` is the header's version byte (0x07).
        public static string GameId(string path, out int revision)
        {
            revision = -1;
            try
            {
                using (var f = File.OpenRead(path))
                {
                    var head = new byte[8];
                    if (f.Read(head, 0, 4) != 4) return null;
                    f.Position = System.Text.Encoding.ASCII.GetString(head, 0, 4) == "CISO" ? 0x8000 : 0;
                    if (f.Read(head, 0, 8) != 8) return null;
                    for (int i = 0; i < 6; i++) if (head[i] < 0x20 || head[i] > 0x7E) return null;
                    revision = head[7];
                    return System.Text.Encoding.ASCII.GetString(head, 0, 6);
                }
            }
            catch { return null; }
        }

        /// The first image in put_games_here/ whose game ID starts with `idPrefix` (and, unless `revision` is
        /// negative, whose version byte is `revision`), or null.
        public static string FindDisc(string idPrefix, int revision = -1)
        {
            foreach (string dir in DiscDirs())
            {
                string[] files;
                try { files = Directory.GetFiles(dir).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray(); }
                catch { continue; }
                foreach (string file in files)
                {
                    if (!DiscExtensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
                    string id = GameId(file, out int rev);
                    if (id != null && id.StartsWith(idPrefix, StringComparison.Ordinal) && (revision < 0 || rev == revision)) return file;
                }
            }
            return null;
        }

        /// Holds one game's asset folder against the other plugins while extracting into it. A named mutex, so it
        /// works across the separately compiled plugins in the one game process. Take it on the thread that
        /// extracts, check completeness again inside it (another plugin may have just finished), then extract.
        public static IDisposable Lock(string game) => new GameLock(game);

        sealed class GameLock : IDisposable
        {
            readonly Mutex mutex;
            bool held;
            public GameLock(string game)
            {
                mutex = new Mutex(false, @"Local\" + PackageFolder + "-extract-" + game);
                try { held = mutex.WaitOne(TimeSpan.FromMinutes(10)); }
                catch (AbandonedMutexException) { held = true; }
                if (!held) CommonLog.Log?.LogWarning($"Waited 10 minutes for another mod to finish extracting {game}; going ahead.");
            }
            public void Dispose()
            {
                if (held) { held = false; try { mutex.ReleaseMutex(); } catch { } }
                mutex.Dispose();
            }
        }

        // Extractor versions. The native completeness checks only see whether each file is there and parses, so a
        // file an older extractor wrote differently would be kept forever. Each extraction set (one native extract
        // call: "main", "p2", "luigi", a fighter...) records the version that wrote it in assets/<game>/extracted.txt,
        // one `set=version` line each; a set whose recorded version differs from the plugin's is extracted again.
        // Bump a set's version (the constant next to its extract call) whenever that extractor's output changes for
        // files that already exist. An install without the file (or the line) extracts once.
        public const string VersionFile = "extracted.txt";

        static Dictionary<string, string> ReadVersions(string game)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                string path = Path.Combine(Assets(game), VersionFile);
                if (!File.Exists(path)) return map;
                foreach (string line in File.ReadAllLines(path))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch { }
            return map;
        }

        /// Whether `set` in `game`'s folder was written by extractor `version`.
        public static bool Current(string game, string set, int version) =>
            ReadVersions(game).TryGetValue(set, out string v) && v == version.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// Record that `set` was just extracted by `version` (call after a successful extract and completeness check).
        public static void MarkCurrent(string game, string set, int version)
        {
            using (Lock(game)) // re-entrant: the extracting thread usually holds it already
            {
                try
                {
                    var map = ReadVersions(game);
                    map[set] = version.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    string dir = Assets(game);
                    Directory.CreateDirectory(dir);
                    string path = Path.Combine(dir, VersionFile), temp = path + ".tmp";
                    var lines = new List<string> { "# Written by the wesleyasaurus mods: which extractor version wrote each set. Delete to re-extract everything." };
                    lines.AddRange(map.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value));
                    File.WriteAllLines(temp, lines);
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(temp, path);
                }
                catch (Exception e) { CommonLog.Log?.LogWarning($"Couldn't record the {game} extractor version: {e.Message}"); }
            }
        }

        /// The one line a plugin logs when a disc it needs isn't there.
        public static string MissingDisc(string game) =>
            $"Put your own {game} disc image (.iso, .gcm or .ciso; any name) in {DiscDir} and restart the game.";
    }
}
