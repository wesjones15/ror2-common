using System;
using System.Collections.Generic;
using RoR2;

namespace Ror2Common
{
    /// The pause-menu controls window for any Melee survivor: one page in the style of Olimar's and
    /// Mario's sheets (a few short sections, rows of a few words) so it can be read at a glance with
    /// the game running. Specials (the fighter's own skill names, from `MeleeSkillLayout`, so they can
    /// never drift from the loadout and HUD), Attacks (the A attack in its directions, worded for the
    /// moves this fighter's sim actually has), Grab and Moving. The text never needs the window's
    /// auto-size: keep every action under about 34 characters and the sheet under Olimar's row count.
    /// A fighter supplies a subtitle and, if it has inputs of its own (Samus's grapple), replacement
    /// grab rows or a callback that appends them.
    ///
    /// Buttons are read by position (MeleePad), so the rows name physical
    /// controls: RT neutral special, LT A attack, RB side special, LB up special,
    /// B down special (or the shield alternate). A fighter whose specials sit on
    /// other buttons (Pikachu) lists them correctly because its layout is already
    /// by slot.
    public static class MeleeSheet
    {
        /// <param name="subtitle">Shown under the fighter's name.</param>
        /// <param name="skills">The fighter's skill layout (names).</param>
        /// <param name="footer">Optional note at the foot of the window (one or two short lines).</param>
        /// <param name="extras">Optional: appends fighter-specific sections or rows.</param>
        /// <param name="grab">Optional: fills the gamepad and keyboard Grab sections instead of the generic rows.</param>
        /// <param name="body">The player's body, to word the Attacks and Grab rows by the fighter's own move list.</param>
        public static ControlSheet Build(string subtitle, in MeleeSkillLayout skills, string footer = null,
            Action<ControlSheet> extras = null, Action<ControlSection, ControlSection> grab = null, CharacterBody body = null)
        {
            var sheet = new ControlSheet { Subtitle = subtitle, Footer = footer };
            var driver = body ? body.GetComponent<MeleeDriver>() : null;
            var moves = driver ? driver.MoveList : null;
            string jab = MeleeMoveset.JabText(moves);
            bool dashGrab = moves == null || MeleeMoveset.Has(moves, MeleeMoveset.DashGrab);

            // Specials, by button: the fighter's own skill names.
            sheet.Pad("Specials")
                .Add("RT", skills.primary.displayName)
                .Add("RB", skills.utility.displayName)
                .Add("LB", skills.special.displayName)
                .Add("B", skills.extra.displayName);
            sheet.Keys("Specials")
                .Add("Primary", skills.primary.displayName)
                .Add("Utility", skills.utility.displayName)
                .Add("Special", skills.special.displayName)
                .Add("C", skills.extra.displayName);

            // The A attack, in its directions: aim up or down with the camera.
            sheet.Pad("Attacks")
                .Add("LT", jab)
                .Add("LS + LT", "Tilts (aim up/down)")
                .Add("Flick + LT", "Smashes (hold to charge)")
                .Add("Sprint + LT", "Dash attack")
                .Add("LT in air", "Aerials");
            sheet.Keys("Attacks")
                .Add("Secondary", jab)
                .Add("Move + Secondary", "Tilts (aim up/down)")
                .Add("Flick + Secondary", "Smashes (hold to charge)")
                .Add("Sprint + Secondary", "Dash attack")
                .Add("Secondary in air", "Aerials");

            var padGrab = sheet.Pad("Grab");
            var keysGrab = sheet.Keys("Grab");
            if (grab != null) grab(padGrab, keysGrab);
            else
            {
                padGrab.Add("X", dashGrab ? "Grab (Sprint + X: dash grab)" : "Grab")
                    .Add("While held", "LT pummel, LS + LT throw");
                keysGrab.Add("Interact", dashGrab ? "Grab (Sprint: dash grab)" : "Grab")
                    .Add("While held", "Secondary pummel, Move throw");
            }

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
    }
}
