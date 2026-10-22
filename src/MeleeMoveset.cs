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

    /// The fighter's own move list, read from its sim (`ssbm_sim_moves`: a move exists when the fighter's
    /// disc action table has it). The pause sheet uses it only to word a few rows per fighter (does this
    /// one have a second or third jab, a rapid jab, a dash grab); it never lists the moves one by one.
    public static class MeleeMoveset
    {
        // Native `Move` ids (libssbm sim.rs).
        public const uint Jab = 1, SideTilt = 2, UpTilt = 3, DownTilt = 4, DashAttack = 5,
            NeutralAir = 6, ForwardAir = 7, UpAir = 8, DownAir = 9, SideSmash = 10, UpSmash = 11, DownSmash = 12,
            Jab2 = 13, BackAir = 14, Jab3 = 15, RapidJab = 16,
            SpecialN = 20, SpecialS = 21, SpecialS2 = 22, SpecialHi = 23, SpecialLw = 24,
            Grab = 25, DashGrab = 26, AirGrab = 27, Pummel = 28,
            ThrowForward = 29, ThrowBack = 30, ThrowUp = 31, ThrowDown = 32;

        /// Whether the list has the move.
        public static bool Has(IList<MeleeMoveInfo> moves, uint id)
        {
            foreach (var move in moves) if (move.Id == id) return true;
            return false;
        }

        /// The pause sheet's jab row from the moves the fighter has: "Jab", "Jab, jab", "Jab, jab, rapid jab"...
        /// "Rapid jab" only appears for a fighter whose sim has one; with no move list it is just "Jab combo".
        public static string JabText(IList<MeleeMoveInfo> moves)
        {
            if (moves == null) return "Jab combo";
            var text = new StringBuilder("Jab");
            if (Has(moves, Jab2)) text.Append(", jab");
            if (Has(moves, Jab3)) text.Append(", jab");
            if (Has(moves, RapidJab)) text.Append(", rapid jab");
            return text.ToString();
        }

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
