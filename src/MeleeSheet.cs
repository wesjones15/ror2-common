using System;
using RoR2;

namespace Ror2Common
{
    /// The pause-menu controls window for a Melee survivor: one page in the style of Olimar's
    /// (Specials, Attacks, Grab and Moving), listing the fighter's own moves and the controls the
    /// native sim reads. The four stock skills and the fifth show the names the loadout and HUD
    /// use, including a chosen alternate, and the left column is RoR2's own binding: the physical
    /// pad button's glyph or the player's key, never an action name such as "Primary".
    public static class MeleeSheet
    {
        /// <param name="subtitle">Shown under the fighter's name.</param>
        /// <param name="skills">The fighter's skill layout (names and the fifth slot's name).</param>
        /// <param name="footer">Optional note at the foot of the window (one or two short lines).</param>
        /// <param name="extras">Optional: appends fighter-specific sections or rows.</param>
        /// <param name="grab">Optional: fills the gamepad and keyboard Grab sections instead of the generic rows.</param>
        /// <param name="body">The player's body, to show the selected primary and B-skill variants.</param>
        public static ControlSheet Build(string subtitle, in MeleeSkillLayout skills, string footer = null,
            Action<ControlSheet> extras = null, Action<ControlSection, ControlSection> grab = null, CharacterBody body = null)
        {
            var sheet = new ControlSheet { Subtitle = subtitle, Footer = footer };
            var driver = body ? body.GetComponent<MeleeDriver>() : null;
            var moves = driver ? driver.MoveList : null;
            string jab = MeleeMoveset.JabText(moves);
            bool dashGrab = moves == null || MeleeMoveset.Has(moves, MeleeMoveset.DashGrab);

            var user = LocalUserManager.GetFirstLocalUser();
            var player = user != null ? user.inputPlayer : null;
            var eventSystem = user != null ? user.eventSystem : null;

            // Pad: the physical buttons MeleePad reads, drawn as RoR2 draws them.
            string rt = ControlGlyphs.Pad(player, PadButton.RT, "RT");
            string lt = ControlGlyphs.Pad(player, PadButton.LT, "LT");
            string lb = ControlGlyphs.Pad(player, PadButton.LB, "LB");
            string rb = ControlGlyphs.Pad(player, PadButton.RB, "RB");
            string b = ControlGlyphs.Pad(player, PadButton.B, "B");
            string x = ControlGlyphs.Pad(player, PadButton.X, "X");
            string a = ControlGlyphs.Pad(player, PadButton.A, "A");
            string click = ControlGlyphs.Pad(player, PadButton.LS, "LS click");

            // Keyboard: RoR2's own binds. C is MeleePad's fixed fifth-skill key.
            string primary = ControlGlyphs.Key(eventSystem, "PrimarySkill", "Primary");
            string secondary = ControlGlyphs.Key(eventSystem, "SecondarySkill", "Secondary");
            string utility = ControlGlyphs.Key(eventSystem, "UtilitySkill", "Utility");
            string special = ControlGlyphs.Key(eventSystem, "SpecialSkill", "Special");
            string interact = ControlGlyphs.Key(eventSystem, "Interact", "Interact");
            string jump = ControlGlyphs.Key(eventSystem, "Jump", "Jump");
            string sprint = ControlGlyphs.Key(eventSystem, "Sprint", "Sprint");

            // The loadout can swap the primary (Luigi's Super Scope) and the B skill (the shield),
            // so read the names from the body's current skills rather than the layout alone.
            string primaryName = Name(body, SkillSlot.Primary, skills.primary.displayName);
            string utilityName = Name(body, SkillSlot.Utility, skills.utility.displayName);
            string specialName = Name(body, SkillSlot.Special, skills.special.displayName);
            string extraName = ExtraName(body, skills);

            // Specials, by button: the fighter's own skill names.
            sheet.Pad("Specials")
                .Add(rt, primaryName)
                .Add(rb, utilityName)
                .Add(lb, specialName)
                .Add(b, extraName);
            sheet.Keys("Specials")
                .Add(primary, primaryName)
                .Add(utility, utilityName)
                .Add(special, specialName)
                .Add("C", extraName);

            // The A attack, in its directions: aim up or down with the camera.
            sheet.Pad("Attacks")
                .Add(lt, jab)
                .Add("LS + " + lt, "Tilts (aim up/down)")
                .Add("Flick + " + lt, "Smashes (hold to charge)")
                .Add(click + " + " + lt, "Dash attack")
                .Add(lt + " in air", "Aerials");
            sheet.Keys("Attacks")
                .Add(secondary, jab)
                .Add("Move + " + secondary, "Tilts (aim up/down)")
                .Add("Flick + " + secondary, "Smashes (hold to charge)")
                .Add(sprint + " + " + secondary, "Dash attack")
                .Add(secondary + " in air", "Aerials");

            var padGrab = sheet.Pad("Grab");
            var keysGrab = sheet.Keys("Grab");
            if (grab != null) grab(padGrab, keysGrab);
            else
            {
                padGrab.Add(x, dashGrab ? "Grab (Sprint + X: dash grab)" : "Grab")
                    .Add("While held", "LT pummel, LS + LT throw");
                keysGrab.Add(interact, dashGrab ? "Grab (Sprint: dash grab)" : "Grab")
                    .Add("While held", "Secondary pummel, Move throw");
            }

            sheet.Pad("Moving")
                .Add("Left stick", "Walk, run")
                .Add(a, "Jump; again in the air")
                .Add(click, "Sprint");
            sheet.Keys("Moving")
                .Add("Move", "Walk (Sprint: run)")
                .Add(jump, "Jump; again in the air");

            extras?.Invoke(sheet);
            return sheet;
        }

        static string Name(CharacterBody body, SkillSlot slot, string fallback)
        {
            var skill = body && body.skillLocator ? body.skillLocator.GetSkill(slot) : null;
            return SkillName(skill, fallback);
        }

        static string ExtraName(CharacterBody body, in MeleeSkillLayout skills)
        {
            var locator = body ? body.skillLocator : null;
            var skill = locator && !string.IsNullOrEmpty(skills.extraName) ? locator.FindSkill(skills.extraName) : null;
            return SkillName(skill, skills.extra.displayName);
        }

        static string SkillName(GenericSkill skill, string fallback)
        {
            var def = skill ? skill.skillDef : null;
            if (!def || string.IsNullOrEmpty(def.skillNameToken)) return fallback;
            var name = Language.GetString(def.skillNameToken);
            return string.IsNullOrEmpty(name) || name == def.skillNameToken ? fallback : name;
        }
    }
}
