using System;
using System.Collections.Generic;
using System.Text;

namespace Ror2Common
{
    /// One move of a Melee fighter as the native sim reports it: the shared move id and the name of the
    /// source action (the decomp's `Attack11`, `AttackAirF`, `ThrowHi`) it plays.
    public struct MeleeMoveInfo
    {
        public uint Id;
        public string Source;
    }

    /// The pause sheet's Moveset page, generated rather than written per fighter. The list of moves comes
    /// from the fighter's own sim (`ssbm_sim_moves`: a move exists when the fighter's disc action table
    /// has it), the names of the shared moves from one table here keyed by the generic move id, and the
    /// specials' names from the fighter's skill layout, so Fox's Blaster, Falcon Punch and Giant Punch can
    /// never drift from what the loadout and HUD call them. Inputs are the Melee control scheme this mod
    /// reads (MeleePad, MeleeControls): LT is the A attack, the stick and camera aim choose its direction.
    public static partial class MeleeMoveset
    {
        // Native `Move` ids (libssbm sim.rs).
        public const uint Jab = 1, SideTilt = 2, UpTilt = 3, DownTilt = 4, DashAttack = 5,
            NeutralAir = 6, ForwardAir = 7, UpAir = 8, DownAir = 9, SideSmash = 10, UpSmash = 11, DownSmash = 12,
            Jab2 = 13, BackAir = 14, Jab3 = 15, RapidJab = 16,
            SpecialN = 20, SpecialS = 21, SpecialS2 = 22, SpecialHi = 23, SpecialLw = 24,
            Grab = 25, DashGrab = 26, AirGrab = 27, Pummel = 28,
            ThrowForward = 29, ThrowBack = 30, ThrowUp = 31, ThrowDown = 32;

        // Move id -> (name, pad input, keyboard input). The specials have no row here: their names are the
        // fighter's skill names and their buttons follow the skill slots.
        struct Entry { public string Name, Pad, Keys; }
        static readonly Dictionary<uint, Entry> table = new Dictionary<uint, Entry>
        {
            [Jab] = new Entry { Name = "Jab", Pad = "LT", Keys = "Secondary" },
            [Jab2] = new Entry { Name = "Jab, second hit", Pad = "LT x2", Keys = "Secondary x2" },
            [Jab3] = new Entry { Name = "Jab, third hit", Pad = "LT x3", Keys = "Secondary x3" },
            [RapidJab] = new Entry { Name = "Rapid jab", Pad = "Mash LT", Keys = "Mash Secondary" },
            [SideTilt] = new Entry { Name = "Forward tilt", Pad = "LS + LT", Keys = "Move + Secondary" },
            [UpTilt] = new Entry { Name = "Up tilt", Pad = "LS + LT, aim up", Keys = "Move + Secondary, aim up" },
            [DownTilt] = new Entry { Name = "Down tilt", Pad = "LS + LT, aim down", Keys = "Move + Secondary, aim down" },
            [DashAttack] = new Entry { Name = "Dash attack", Pad = "Sprint + LS + LT", Keys = "Sprint + Move + Secondary" },
            [SideSmash] = new Entry { Name = "Forward smash", Pad = "Flick + LT", Keys = "Flick + Secondary" },
            [UpSmash] = new Entry { Name = "Up smash", Pad = "Flick + LT, aim up", Keys = "Flick + Secondary, aim up" },
            [DownSmash] = new Entry { Name = "Down smash", Pad = "Flick + LT, aim down", Keys = "Flick + Secondary, aim down" },
            [NeutralAir] = new Entry { Name = "Neutral air", Pad = "LT", Keys = "Secondary" },
            [ForwardAir] = new Entry { Name = "Forward air", Pad = "LS ahead + LT", Keys = "Move ahead + Secondary" },
            [BackAir] = new Entry { Name = "Back air", Pad = "LS back + LT", Keys = "Move back + Secondary" },
            [UpAir] = new Entry { Name = "Up air", Pad = "LS + LT, aim up", Keys = "Move + Secondary, aim up" },
            [DownAir] = new Entry { Name = "Down air", Pad = "LS + LT, aim down", Keys = "Move + Secondary, aim down" },
            [Grab] = new Entry { Name = "Grab", Pad = "X", Keys = "Interact" },
            [DashGrab] = new Entry { Name = "Dash grab", Pad = "Sprint + X", Keys = "Sprint + Interact" },
            [AirGrab] = new Entry { Name = "Air grab", Pad = "X in the air", Keys = "Interact in the air" },
            [Pummel] = new Entry { Name = "Pummel", Pad = "Held: LT / RT", Keys = "Held: Secondary / Primary" },
            [ThrowForward] = new Entry { Name = "Forward throw", Pad = "Held: LS right + LT", Keys = "Held: D + Secondary" },
            [ThrowBack] = new Entry { Name = "Back throw", Pad = "Held: LS left + LT", Keys = "Held: A + Secondary" },
            [ThrowUp] = new Entry { Name = "Up throw", Pad = "Held: LS up + LT", Keys = "Held: W + Secondary" },
            [ThrowDown] = new Entry { Name = "Down throw", Pad = "Held: LS down + LT", Keys = "Held: S + Secondary" },
        };

        /// The shared human name of a generic move id ("Forward tilt"), or null for the specials and unknown ids.
        public static string Name(uint id) => table.TryGetValue(id, out var entry) ? entry.Name : null;

        /// Reads a native sim's move list; null when the library has no move-list export or reports none.
        public static MeleeMoveInfo[] Read(IntPtr sim)
        {
            if (!MeleeNative.HasMoves || sim == IntPtr.Zero) return null;
            try
            {
                uint count = MeleeNative.ssbm_sim_moves(sim, null, 0);
                if (count == 0 || count > 256) return null;
                var words = new uint[count * 8];
                count = Math.Min(count, MeleeNative.ssbm_sim_moves(sim, words, count));
                var list = new MeleeMoveInfo[count];
                var name = new byte[28];
                for (int i = 0; i < count; i++)
                {
                    for (int w = 0; w < 7; w++) BitConverter.GetBytes(words[i * 8 + 1 + w]).CopyTo(name, w * 4);
                    int length = Array.IndexOf(name, (byte)0);
                    list[i] = new MeleeMoveInfo { Id = words[i * 8], Source = Encoding.ASCII.GetString(name, 0, length < 0 ? name.Length : length) };
                }
                return list;
            }
            catch (EntryPointNotFoundException) { return null; }
        }
    }
}
