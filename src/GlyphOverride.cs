#if ROR2COMMON_HOOKS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using MonoMod.RuntimeDetour;
using Rewired;
using RoR2;
using RoR2.UI;
using UnityEngine;

namespace Ror2Common
{
    /// A physical button on an Xbox-style pad (Rewired's gamepad template; the glyph shown is
    /// whatever the player's pad draws for it).
    public enum PadButton { A, B, X, Y, LB, RB, LT, RT }

    // The glyphs on RoR2's HUD skill icons say which button the player bound to the slot's RoR2
    // action. A survivor whose native sim reads the pad by position (Mario's LT for the ground
    // pound, Olimar's A to throw) uses other buttons than those binds, so the icons would name
    // the wrong ones. This swaps in the right glyph, for a gamepad only (a keyboard goes through
    // RoR2's binds), while the player is playing a registered body. Glyphs.GetGlyphString is
    // hooked, so the HUD, and any other place that asks for a skill action's glyph, agree.
    //
    // A slot maps to a button, to several (drawn joined with "+": Olimar's LB + B), or to none
    // (a passive or automatic move: no glyph). The glyph is found as RoR2 would, from the
    // pad's name and the physical element the button reads (`glyphMap`).
    public static class GlyphOverride
    {
        sealed class Entry
        {
            public Func<bool> active;
            public readonly Dictionary<string, PadButton[]> slots = new Dictionary<string, PadButton[]>();
        }

        static readonly Dictionary<string, Entry> bodies = new Dictionary<string, Entry>();
        static readonly List<Hook> hooks = new List<Hook>();
        static readonly StringBuilder text = new StringBuilder();
        static readonly HashSet<string> seen = new HashSet<string>();
        static FieldInfo glyphMapField;
        static Type keyType;
        static bool installed, warned;

        /// RoR2's action name for a skill slot.
        public static string Action(SkillSlot slot)
        {
            switch (slot)
            {
                case SkillSlot.Primary: return "PrimarySkill";
                case SkillSlot.Secondary: return "SecondarySkill";
                case SkillSlot.Utility: return "UtilitySkill";
                case SkillSlot.Special: return "SpecialSkill";
                default: return null;
            }
        }

        /// Registers `bodyName` (its prefab's, as BodyCatalog names it). `slots` gives the pad
        /// buttons per slot; a slot left out keeps RoR2's glyph, an empty array shows none.
        /// `active` (null: always) can turn it off, say for a control scheme which uses RoR2's
        /// binds.
        public static void Set(string bodyName, Func<bool> active, IDictionary<SkillSlot, PadButton[]> slots)
        {
            var e = new Entry { active = active };
            foreach (var kv in slots) e.slots[Action(kv.Key)] = kv.Value;
            bodies[bodyName] = e;
        }

        /// Hooks Glyphs.GetGlyphString. Call once at start-up.
        public static void Install()
        {
            if (installed) return;
            installed = true;
            glyphMapField = typeof(Glyphs).GetField("glyphMap", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
            keyType = typeof(Glyphs).GetNestedType("GlyphKey", BindingFlags.NonPublic | BindingFlags.Public);
            if (glyphMapField == null || keyType == null)
            {
                CommonLog.Warn("GlyphOverride: RoR2's glyph table isn't as expected; skill icons keep RoR2's glyphs");
                return;
            }
            try
            {
                var five = typeof(Glyphs).GetMethod("GetGlyphString",
                    new[] { typeof(MPEventSystem), typeof(string), typeof(AxisRange), typeof(MPEventSystem.InputSource), typeof(bool) });
                var three = typeof(Glyphs).GetMethod("GetGlyphString",
                    new[] { typeof(MPEventSystem), typeof(string), typeof(AxisRange) });
                if (five != null)
                    hooks.Add(new Hook(five, (Func<Func<MPEventSystem, string, AxisRange, MPEventSystem.InputSource, bool, string>,
                        MPEventSystem, string, AxisRange, MPEventSystem.InputSource, bool, string>)OnGlyph5));
                if (three != null)
                    hooks.Add(new Hook(three, (Func<Func<MPEventSystem, string, AxisRange, string>,
                        MPEventSystem, string, AxisRange, string>)OnGlyph3));
            }
            catch (Exception e)
            {
                CommonLog.Warn($"GlyphOverride: couldn't hook Glyphs.GetGlyphString ({e.Message}); skill icons keep RoR2's glyphs");
                return;
            }
            var go = new GameObject("Ror2Common glyph refresh") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Refresher>();
        }

        static string OnGlyph5(Func<MPEventSystem, string, AxisRange, MPEventSystem.InputSource, bool, string> orig,
            MPEventSystem es, string action, AxisRange range, MPEventSystem.InputSource source, bool allowFallback)
        {
            return Override(es, action, source) ?? orig(es, action, range, source, allowFallback);
        }

        static string OnGlyph3(Func<MPEventSystem, string, AxisRange, string> orig,
            MPEventSystem es, string action, AxisRange range)
        {
            return es ? Override(es, action, es.currentInputSource) ?? orig(es, action, range) : orig(es, action, range);
        }

        // The body the event system's player is playing, by its BodyCatalog name.
        static string BodyName(MPEventSystem es)
        {
            var body = es && es.localUser != null ? es.localUser.cachedBody : null;
            return body ? BodyCatalog.GetBodyName(body.bodyIndex) : null;
        }

        static string Override(MPEventSystem es, string action, MPEventSystem.InputSource source)
        {
            if (source != MPEventSystem.InputSource.Gamepad || string.IsNullOrEmpty(action) || bodies.Count == 0) return null;
            try
            {
                var name = BodyName(es);
                if (name == null || !bodies.TryGetValue(name, out var entry)) return null;
                if (entry.active != null && !entry.active()) return null;
                if (!entry.slots.TryGetValue(action, out var buttons))
                {
                    // Not a skill slot (or RoR2 names it otherwise): say so once, so a log shows it.
                    if (seen.Add(name + "/" + action)) CommonLog.Info($"GlyphOverride: {name} keeps RoR2's glyph for action '{action}'");
                    return null;
                }
                if (buttons.Length == 0) return "";
                var joystick = Pad(es.player);
                var gamepad = joystick != null && joystick.ImplementsTemplate<IGamepadTemplate>()
                    ? joystick.GetTemplate<IGamepadTemplate>() : null;
                if (gamepad == null) return null;
                text.Length = 0;
                foreach (var button in buttons)
                {
                    var glyph = Glyph(joystick, gamepad, button);
                    if (glyph == null) return null;
                    if (text.Length > 0) text.Append('+');
                    text.Append(glyph);
                }
                return text.ToString();
            }
            catch (Exception e)
            {
                if (!warned) CommonLog.Warn($"GlyphOverride: {e.Message}; RoR2's glyphs are used");
                warned = true;
                return null;
            }
        }

        static Joystick Pad(Player player)
        {
            if (player == null) return null;
            var last = player.controllers.GetLastActiveController(ControllerType.Joystick) as Joystick;
            if (last != null) return last;
            foreach (var j in player.controllers.Joysticks) return j;
            return null;
        }

        /// The same device-specific glyph markup RoR2 puts on its skill bar, for a
        /// physical pad button used outside a skill slot (such as Olimar's Onion menu).
        public static string PhysicalGlyph(Player player, PadButton button)
        {
            if (glyphMapField == null || keyType == null) return null;
            try
            {
                var joystick = Pad(player);
                var gamepad = joystick != null && joystick.ImplementsTemplate<IGamepadTemplate>()
                    ? joystick.GetTemplate<IGamepadTemplate>() : null;
                return gamepad == null ? null : Glyph(joystick, gamepad, button);
            }
            catch (Exception e)
            {
                if (!warned) CommonLog.Warn($"GlyphOverride: physical button glyph unavailable ({e.Message})");
                warned = true;
                return null;
            }
        }

        static IControllerTemplateButton Element(IGamepadTemplate g, PadButton b)
        {
            switch (b)
            {
                case PadButton.A: return g.a;
                case PadButton.B: return g.b;
                case PadButton.X: return g.x;
                case PadButton.Y: return g.y;
                case PadButton.LB: return g.leftBumper;
                case PadButton.RB: return g.rightBumper;
                case PadButton.LT: return g.leftTrigger.AsButton;
                default: return g.rightTrigger.AsButton;
            }
        }

        // The glyph RoR2 draws for the physical element `button` reads on this pad, or null.
        static string Glyph(Joystick joystick, IGamepadTemplate gamepad, PadButton button)
        {
            var element = Element(gamepad, button);
            if (element == null) return null;
            var target = element.source.target;
            if (!target.hasTarget) return null;
            var map = (IDictionary)glyphMapField.GetValue(null);
            if (map == null) return null;
            // Keyed by the pad's name and element id, as Glyphs.GetGlyphString does it; its
            // fallback name is the Xbox One pad.
            foreach (var pad in new[] { joystick.name, "Xbox One Controller" })
            {
                var key = Activator.CreateInstance(keyType, pad, target.elementIdentifierId);
                if (map.Contains(key)) return (string)map[key];
            }
            return null;
        }

        // Icons draw their glyph once, then again only when the input source changes, so
        // when the player's body changes they're told to draw it afresh.
        sealed class Refresher : MonoBehaviour
        {
            string last;
            float next;

            void Update()
            {
                if (Time.unscaledTime < next) return;
                next = Time.unscaledTime + 0.5f;
                var user = LocalUserManager.GetFirstLocalUser();
                var body = user != null ? user.cachedBody : null;
                var name = body ? BodyCatalog.GetBodyName(body.bodyIndex) : null;
                if (name == last) return;
                last = name;
                foreach (var c in UnityEngine.Object.FindObjectsOfType<InputBindingDisplayController>())
                    if (c) c.SendMessage("Refresh", SendMessageOptions.DontRequireReceiver);
            }
        }
    }
}
#endif
