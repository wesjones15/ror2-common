using System;
using System.Collections.Generic;
using RoR2;

namespace Ror2Common
{
    /// Keeps overview text in step with optional assets and the selected costume.
    public static class AssetOverview
    {
        sealed class Entry
        {
            public string body, token, text, last;
            public Func<string> note;
            public Language language;
        }
        static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
#if ROR2COMMON_HOOKS
        static bool installed;
#endif
        public static string Append(string description, string note) => string.IsNullOrEmpty(note)
            ? description : description + "\n\n<style=cSub>" + note + "</style>";

        public static string Missing(params string[] games) => games.Length == 0 ? null
            : OptionalAssets.BuiltinOnly
                ? "Using built-in stand-ins. Optional assets are disabled in the config."
                : "Using built-in stand-ins. Optional " + string.Join(" and ", games)
                    + " assets are missing. Put the disc image (.iso, .gcm or .ciso; any name) in "
                    + SharedFiles.DiscHint + " and restart the game.";

        public static void Set(Language language, string body, string token, string text, Func<string> note)
        {
            var entry = new Entry { body = body, token = token, text = text, note = note, language = language };
            entries[token] = entry;
            Update(entry, false);
#if ROR2COMMON_HOOKS
            if (!installed) { installed = true; RoR2Application.onFixedUpdate += Refresh; }
#endif
        }
        static void Update(Entry entry, bool refresh)
        {
            string text = Append(entry.text, entry.note());
            if (text == entry.last) return;
            entry.last = text;
            entry.language.SetStringByToken(entry.token, text);
#if ROR2COMMON_HOOKS
            var index = BodyCatalog.FindBodyIndex(entry.body);
            if (refresh && index != BodyIndex.None) SkinPresentation.RefreshMenus(BodyCatalog.GetBodyPrefab(index));
#endif
        }
        public static void Refresh()
        {
            foreach (var entry in entries.Values)
                if (entry.language == Language.currentLanguage) Update(entry, true);
        }
    }
}
