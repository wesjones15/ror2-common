using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Ror2Common
{
    /// The pause-menu controls window for any Melee survivor, generated from the
    /// skills the fighter already defines (`MeleeSkillLayout`) plus the generic
    /// Melee control scheme every fighter shares. A fighter supplies only a
    /// subtitle and, if it has extra inputs of its own (Samus's grapple), a
    /// callback that appends them; its special-move rows can never drift from
    /// the skill names and descriptions shown in the loadout and HUD.
    ///
    /// Buttons are read by position (MeleePad), so the rows name physical
    /// controls: RT neutral special, LT A attack, RB side special, LB up special,
    /// B down special (or the shield alternate). A fighter whose specials sit on
    /// other buttons (Pikachu) lists them correctly because its layout is already
    /// by slot.
    public static class MeleeSheet
    {
        /// <param name="subtitle">Shown under the fighter's name.</param>
        /// <param name="skills">The fighter's skill layout (names and descriptions).</param>
        /// <param name="footer">Optional note at the foot of the window.</param>
        /// <param name="extras">Optional: appends fighter-specific sections or rows.</param>
        /// <param name="grabPad">Pad text for X when the fighter's grab differs from the generic one.</param>
        /// <param name="grabKeys">Keyboard text for Interact likewise.</param>
        public static ControlSheet Build(string subtitle, in MeleeSkillLayout skills, string footer = null,
            Action<ControlSheet> extras = null, string grabPad = null, string grabKeys = null)
        {
            var sheet = new ControlSheet { Subtitle = subtitle, Footer = footer };

            // Specials, by button: the fighter's own skill names and descriptions.
            var pad = sheet.Pad("Special moves");
            pad.Add("RT", Move(skills.primary));
            pad.Add("RB", Move(skills.utility));
            pad.Add("LB", Move(skills.special));
            pad.Add("B", Move(skills.extra));
            pad.Add("B (loadout)", "Shield instead, and an evade once in the air. Pick it in the loadout's B Skill row.");
            var keys = sheet.Keys("Special moves");
            keys.Add("Primary", Move(skills.primary));
            keys.Add("Utility", Move(skills.utility));
            keys.Add("Special", Move(skills.special));
            keys.Add("C", Move(skills.extra));
            keys.Add("C (loadout)", "Shield instead, and an evade once in the air. Pick it in the loadout's B Skill row.");

            // The A attack, in all its directions.
            sheet.Pad("A attacks")
                .Add("LT", "Jab. Press again to combo. Keep tapping for the rapid jab (Fox, Captain Falcon, Link); holding it is one jab.")
                .Add("LS + LT", "Tilt toward the stick.")
                .Add("Sprint + LS + LT", "Dash attack.")
                .Add("LS flick + LT", "Smash attack. Hold LT to charge it.")
                .Add("Camera up / down", "With LS held, aiming 20 deg above or 50 deg below level makes any of these an up or down attack.")
                .Add("In the air", "LT is the neutral air; LS toward or away from the camera is the forward or back air.");
            sheet.Keys("A attacks")
                .Add("Secondary", "Jab. Press again to combo. Keep tapping for the rapid jab (Fox, Captain Falcon, Link); holding it is one jab.")
                .Add("Move + Secondary", "Tilt toward your movement.")
                .Add("Sprint + Move + Secondary", "Dash attack.")
                .Add("Move flick + Secondary", "Smash attack. Hold Secondary to charge it.")
                .Add("Camera up / down", "While moving, aiming 20 deg above or 50 deg below level makes any of these an up or down attack.")
                .Add("In the air", "Secondary is the neutral air; moving toward or away from the camera is the forward or back air.");

            sheet.Pad("Grab")
                .Add("X", grabPad ?? "Grab (Sprint + X while running). Then LT to pummel, LS + LT to throw. Not while an interact prompt is up.");
            sheet.Keys("Grab")
                .Add("Interact", grabKeys ?? "Grab (Sprint + Interact while running). Then Secondary to pummel, Move + Secondary to throw.");

            sheet.Pad("Moving")
                .Add("Left stick", "Walk, run")
                .Add("A", "Jump; again in the air")
                .Add("Left stick click", "Sprint");
            sheet.Keys("Moving")
                .Add("Move", "Walk (Sprint: run)")
                .Add("Jump", "Jump; again in the air");

            extras?.Invoke(sheet);
            return sheet;
        }

        /// "Name: what it does", from the skill's own strings.
        static string Move(in MeleeSkillInfo skill) => skill.displayName + ": " + Describe(skill.description);

        static readonly Regex ButtonPrefix = new Regex(@"^\s*(RT|LT|RB|LB|B|X|A)\s*:\s*", RegexOptions.Compiled);

        /// The description without its leading button label, kept to whole
        /// sentences that fit the window (about 110 characters).
        internal static string Describe(string description)
        {
            if (string.IsNullOrEmpty(description)) return "";
            var text = ButtonPrefix.Replace(description, "");
            var sentences = text.Split(new[] { ". " }, StringSplitOptions.RemoveEmptyEntries);
            var result = new StringBuilder();
            foreach (var sentence in sentences)
            {
                var piece = sentence.Trim().TrimEnd('.');
                if (piece.Length == 0) continue;
                if (result.Length > 0 && result.Length + piece.Length + 2 > 110) break;
                if (result.Length > 0) result.Append(' ');
                result.Append(piece).Append('.');
            }
            return result.ToString();
        }
    }
}
