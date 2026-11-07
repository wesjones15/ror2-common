using System;
using System.Reflection;
using RoR2;
using UnityEngine;

namespace Ror2Common
{
    /// The Melee **Egg Lay** (Yoshi's own, borrowed by Pikachu), published as a service so a
    /// character outside the Melee plugin can use the exact same egg. The Melee plugin
    /// registers its `YoshiFull`/`YoshiEgg` here at startup; a caller (Sunshine Luigi's secret
    /// dpad move) then catches and lays without linking the Melee assembly, and the egg, the
    /// trap, the fan behind the owner, its knockback and the break-out are the Melee ones.
    ///
    /// ror2-common is compiled into every plugin (no shared DLL), so the Melee plugin's
    /// static registrations do not reach a caller's copy of this type; the `Try*` methods and
    /// `Ready` therefore fall back to reflection into the loaded Melee assembly.
    public static class EggLay
    {
        /// Show the egg-lay tongue toward `target` (world space); it lashes, catches what it
        /// touches (the shared `YoshiFull` hold) and lays the egg on the catch's own sequence.
        public static Action<CharacterBody, Vector3> Show;

        /// Queue a caught victim on `owner` (the shared `YoshiFull` hold); the eggs are laid
        /// by the owner's own lay sequence, as Yoshi's and Pikachu's are.
        public static Func<CharacterBody, CharacterBody, bool> Catch;

        /// Lay an egg on `victim` at once, at `at` (or the owner's front when null).
        public static Func<CharacterBody, CharacterBody, Vector3?, bool> Lay;

        /// The owner is already busy catching or laying (its move should not start again).
        public static Func<CharacterBody, bool> Laying;

        static Action<CharacterBody, Vector3> resolvedShow;
        static Func<CharacterBody, CharacterBody, bool> resolvedCatch;
        static Func<CharacterBody, CharacterBody, Vector3?, bool> resolvedLay;
        static Func<CharacterBody, bool> resolvedLaying;
        static bool readyResolved, readyValue;

        static MethodInfo Find(string typeName, string method)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("Yoshi." + typeName);
                if (t == null) continue;
                return t.GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            }
            return null;
        }

        /// Yoshi's readiness in the loaded Melee plugin (`Samus.Plugin.YoshiReady`: his files are extracted, and he
        /// is enabled), read through reflection; null when that plugin isn't loaded or has no such property.
        static Func<bool> yoshiFiles;
        static bool yoshiFilesResolved;

        /// The Melee plugin is loaded, has the egg (published, or reachable by reflection) and Yoshi's files are
        /// extracted (the egg and the trap are drawn and voiced from them). Checked again each call, since the
        /// Melee plugin extracts after startup.
        public static bool Ready
        {
            get
            {
                if (!readyResolved)
                {
                    readyResolved = true;
                    readyValue = (Catch != null && Lay != null) || (Find("YoshiFull", "Begin") != null && Find("YoshiEgg", "Lay") != null);
                }
                if (!readyValue) return false;
                if (!yoshiFilesResolved)
                {
                    yoshiFilesResolved = true;
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        var p = asm.GetType("Samus.Plugin")?.GetProperty("YoshiReady", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        if (p == null || p.PropertyType != typeof(bool)) continue;
                        yoshiFiles = () => (bool)p.GetValue(null);
                        break;
                    }
                }
                // Without the property (an older Melee build) keep the old answer: the types are there.
                return yoshiFiles == null || yoshiFiles();
            }
        }

        public static bool TryShow(CharacterBody owner, Vector3 target)
        {
            if (!owner) return false;
            if (Show != null) { Show(owner, target); return true; }
            if (resolvedShow == null)
            {
                var m = Find("YoshiTongue", "Show");
                if (m == null) return false;
                resolvedShow = (o, t) => m.Invoke(null, new object[] { o, t });
            }
            resolvedShow(owner, target);
            return true;
        }

        public static bool TryCatch(CharacterBody owner, CharacterBody victim)
        {
            if (!owner || !victim) return false;
            if (Catch != null) return Catch(owner, victim);
            if (resolvedCatch == null)
            {
                var m = Find("YoshiFull", "Begin");
                if (m == null) return false;
                resolvedCatch = (o, v) => (bool)m.Invoke(null, new object[] { o, v });
            }
            return resolvedCatch(owner, victim);
        }

        public static bool TryLay(CharacterBody owner, CharacterBody victim, Vector3? at = null)
        {
            if (!owner || !victim) return false;
            if (Lay != null) return Lay(owner, victim, at);
            if (resolvedLay == null)
            {
                var m = Find("YoshiEgg", "Lay");
                if (m == null) return false;
                resolvedLay = (o, v, a) => (bool)m.Invoke(null, new object[] { o, v, a });
            }
            return resolvedLay(owner, victim, at);
        }

        /// Is this character in the middle of an egg lay (catching or laying)?
        public static bool IsLaying(CharacterBody owner)
        {
            if (!owner) return false;
            if (Laying != null) return Laying(owner);
            if (resolvedLaying == null)
            {
                var m = Find("YoshiFull", "IsLaying");
                if (m == null) return false;
                resolvedLaying = o => (bool)m.Invoke(null, new object[] { o });
            }
            return resolvedLaying(owner);
        }
    }
}
