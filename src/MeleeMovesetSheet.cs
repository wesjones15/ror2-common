using System;
using System.Collections.Generic;

namespace Ror2Common
{
    // The sheet half of MeleeMoveset (needs the control-sheet and skill-layout types; the move table and the
    // native reader are in MeleeMoveset.cs, which the headless native check compiles on its own).
    public static partial class MeleeMoveset
    {
        // Sheet sections: heading (pad, keyboard) and the move ids in them, in order.
        static readonly (string Pad, string Keys, uint[] Ids)[] groups =
        {
            ("Ground attacks", "Ground attacks", new[] { Jab, Jab2, Jab3, RapidJab, SideTilt, UpTilt, DownTilt, DashAttack }),
            ("Smash attacks (flick the stick)", "Smash attacks (flick Move)", new[] { SideSmash, UpSmash, DownSmash }),
            ("Aerials", "Aerials", new[] { NeutralAir, ForwardAir, BackAir, UpAir, DownAir }),
            ("Grabs and throws", "Grabs and throws", new[] { Grab, DashGrab, AirGrab, Pummel, ThrowForward, ThrowBack, ThrowUp, ThrowDown }),
        };

        // The pad button and keyboard action of each skill slot (0..4), as in MeleePad.
        static readonly string[] padButtons = { "RT", "LT", "RB", "LB", "B" };
        static readonly string[] keyButtons = { "Primary", "Secondary", "Utility", "Special", "C" };
        static readonly uint[] specialIds = { SpecialN, SpecialS, SpecialHi, SpecialLw };

        // The moves of a fighter the sim could not be asked about (older native library, no sim yet): every
        // shared move and the four specials. The page still lists real inputs; only the roster is generic.
        static readonly MeleeMoveInfo[] fallback = BuildFallback();
        static MeleeMoveInfo[] BuildFallback()
        {
            var list = new List<MeleeMoveInfo>();
            foreach (uint id in new[] { Jab, SideTilt, UpTilt, DownTilt, DashAttack, SideSmash, UpSmash, DownSmash, NeutralAir, ForwardAir,
                BackAir, UpAir, DownAir, Grab, DashGrab, Pummel, ThrowForward, ThrowBack, ThrowUp, ThrowDown, SpecialN, SpecialS, SpecialHi, SpecialLw })
                list.Add(new MeleeMoveInfo { Id = id, Source = "" });
            return list.ToArray();
        }

        /// Adds the Moveset page to `sheet`, for the pad and the keyboard: the shared moves the fighter has
        /// (`moves`, null for the generic roster), then its specials by skill name on the button of their slot.
        /// `grapple` is for Samus, whose grab is a grapple beam rather than a grab.
        public static void Add(ControlSheet sheet, in MeleeSkillLayout skills, IList<MeleeMoveInfo> moves = null, bool grapple = false)
        {
            moves = moves ?? fallback;
            var has = new HashSet<uint>();
            foreach (var move in moves) has.Add(move.Id);
            var infos = new[] { skills.primary, skills.secondary, skills.utility, skills.special, skills.extra };
            var slotFor = skills.slotForMove ?? (Func<uint, int>)(id => MeleeSkillSet.DefaultSlot(id));

            string saved = sheet.CurrentPage;
            sheet.CurrentPage = "Moveset";
            foreach (bool pad in new[] { true, false })
            {
                foreach (var group in groups)
                {
                    var section = pad ? sheet.Pad(group.Pad) : sheet.Keys(group.Keys);
                    section.InputColumn = pad ? 240 : 300;
                    foreach (uint id in group.Ids)
                    {
                        if (!has.Contains(id)) continue;
                        var entry = table[id];
                        string name = entry.Name;
                        if (grapple && (id == DashGrab || id == AirGrab)) continue;
                        if (grapple && id == Grab) name = "Grapple beam";
                        section.Add(pad ? entry.Pad : entry.Keys, name);
                    }
                    if (section.Rows.Count == 0) (pad ? sheet.Gamepad : sheet.Keyboard).Remove(section);
                }
                var specials = pad ? sheet.Pad("Special moves") : sheet.Keys("Special moves");
                specials.InputColumn = pad ? 240 : 300;
                foreach (uint id in specialIds)
                {
                    if (!has.Contains(id)) continue;
                    int slot = Math.Max(0, Math.Min(4, slotFor(id)));
                    string name = infos[slot].displayName;
                    if (string.IsNullOrEmpty(name)) continue;
                    specials.Add(pad ? padButtons[slot] : keyButtons[slot], name);
                }
            }
            sheet.CurrentPage = saved;
        }
    }
}
