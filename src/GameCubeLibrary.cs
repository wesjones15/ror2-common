using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Ror2Common
{
    // The optional shared folder all three plugins read: the user's disc images and one generated
    // cache per mod.
    //
    //   <library>/                 disc images (any names; told apart by their game ID)
    //   <library>/isos/            also scanned, with one level of subfolders
    //   <library>/assets/Olimar/   the Olimar plugin's cache (Pikmin, with p2/ inside)
    //   <library>/assets/Mario/    the Mario plugin's cache (Sunshine)
    //   <library>/assets/Melee/    the Melee plugin's cache
    //
    // It never moves an existing layout: a plugin's configured AssetsDir, its assets_dir.txt, its
    // environment variable, or an assets/ folder already beside it all still win. The library's
    // cache is only where a fresh install generates. The library's discs are searched by everyone.
    public static class GameCubeLibrary
    {
        public const string PointerFile = "gamecube_library.txt";
        public const string EnvVar = "GAMECUBE_LIBRARY";
        public const string Olimar = "Olimar", Mario = "Mario", Melee = "Melee";

        static readonly string[] DiscExtensions = { ".iso", ".gcm", ".ciso" };

        /// The library folder, or null when none is set. `configured` is the plugin's
        /// Paths.Library; empty: a gamecube_library.txt in the plugin folder or up to three
        /// folders above it (the plugins folder, so one file serves all three), then $GAMECUBE_LIBRARY.
        public static string Root(string configured, string here)
        {
            string chosen = configured?.Trim();
            if (string.IsNullOrEmpty(chosen)) chosen = FromPointer(here);
            if (string.IsNullOrEmpty(chosen)) chosen = Environment.GetEnvironmentVariable(EnvVar)?.Trim();
            if (string.IsNullOrEmpty(chosen)) return null;
            try
            {
                string full = Path.GetFullPath(chosen);
                if (Directory.Exists(full)) return full;
                CommonLog.Log?.LogWarning($"GameCube library '{full}' does not exist; ignoring it.");
            }
            catch (Exception e) { CommonLog.Log?.LogWarning($"GameCube library '{chosen}': {e.Message}"); }
            return null;
        }

        static string FromPointer(string here)
        {
            if (string.IsNullOrEmpty(here)) return null;
            var at = new DirectoryInfo(here);
            for (int i = 0; i < 4 && at != null; i++, at = at.Parent)
            {
                string file = Path.Combine(at.FullName, PointerFile);
                if (!File.Exists(file)) continue;
                string target = File.ReadAllText(file).Trim();
                if (string.IsNullOrEmpty(target)) continue;
                // A relative pointer is relative to the file.
                return Path.IsPathRooted(target) ? target : Path.Combine(at.FullName, target);
            }
            return null;
        }

        /// `<root>/assets/<mod>`, or null without a library.
        public static string AssetsFor(string root, string mod) =>
            root == null ? null : Path.Combine(root, "assets", mod);

        /// A plugin's cache folder. Explicit choices win in the order they always did:
        /// `configured` (Paths.AssetsDir), assets_dir.txt beside the plugin, then `envVar` if
        /// given. With none of them, an assets/ folder beside the plugin that already holds
        /// anything stays in use; otherwise the library's folder for `mod`; otherwise assets/
        /// beside the plugin (the old default). `source` says which, for the log.
        public static string ResolveAssets(string configured, string here, string envVar, string root, string mod, out string source)
        {
            if (!string.IsNullOrWhiteSpace(configured)) { source = "config"; return configured.Trim(); }
            string pointer = Path.Combine(here, "assets_dir.txt");
            if (File.Exists(pointer))
            {
                string target = File.ReadAllText(pointer).Trim();
                if (!string.IsNullOrEmpty(target)) { source = "assets_dir.txt"; return target; }
            }
            if (envVar != null)
            {
                string env = Environment.GetEnvironmentVariable(envVar);
                if (!string.IsNullOrWhiteSpace(env)) { source = "$" + envVar; return env.Trim(); }
            }
            string beside = Path.Combine(here, "assets");
            if (HasAnything(beside)) { source = "beside the plugin"; return beside; }
            string shared = AssetsFor(root, mod);
            if (shared != null) { source = "GameCube library"; return shared; }
            source = "beside the plugin";
            return beside;
        }

        static bool HasAnything(string dir)
        {
            try { return Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any(); }
            catch { return false; }
        }

        /// The library's folders that may hold disc images: the root, isos/ and isos/'s
        /// subfolders. Empty without a library.
        public static string[] DiscDirs(string root)
        {
            var dirs = new List<string>();
            if (root == null) return dirs.ToArray();
            dirs.Add(root);
            string isos = Path.Combine(root, "isos");
            if (Directory.Exists(isos))
            {
                dirs.Add(isos);
                try { dirs.AddRange(Directory.GetDirectories(isos).OrderBy(d => d, StringComparer.OrdinalIgnoreCase)); }
                catch { }
            }
            return dirs.ToArray();
        }

        /// `dirs` joined for the native disc finders' `;`-separated lists, with the library's
        /// disc folders appended. Blank entries are dropped.
        public static string SearchList(string root, params string[] dirs) =>
            string.Join(";", dirs.Concat(DiscDirs(root)).Where(d => !string.IsNullOrWhiteSpace(d)));

        /// The six-character game ID of a GameCube image (.iso/.gcm, or .ciso whose first data
        /// block starts at 0x8000), or null if it can't be read. `revision` is the header's
        /// version byte (0x07).
        public static string GameId(string path, out int revision)
        {
            revision = -1;
            try
            {
                using (var f = File.OpenRead(path))
                {
                    var head = new byte[8];
                    if (f.Read(head, 0, 4) != 4) return null;
                    if (Encoding.ASCII.GetString(head, 0, 4) == "CISO") f.Position = 0x8000;
                    else f.Position = 0;
                    if (f.Read(head, 0, 8) != 8) return null;
                    for (int i = 0; i < 6; i++) if (head[i] < 0x20 || head[i] > 0x7E) return null;
                    revision = head[7];
                    return Encoding.ASCII.GetString(head, 0, 6);
                }
            }
            catch { return null; }
        }

        /// The first image in the library whose game ID starts with `idPrefix` (and, unless
        /// `revision` is negative, whose version byte is `revision`), or null.
        public static string FindDisc(string root, string idPrefix, int revision = -1)
        {
            foreach (string dir in DiscDirs(root))
            {
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(dir).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray(); }
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
    }
}
