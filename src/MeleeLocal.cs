using RoR2;

namespace Ror2Common
{
    /// The local player's Melee fighter: which one (its native `FighterKind`) in which run. Every
    /// `MeleeDriver` records it the same way when its sim starts on a player-controlled body this
    /// machine owns, so run-wide presentations (the end-of-run GAME! banner) need no per-fighter
    /// state. The run is kept so a record from an earlier run never matches a later one.
    public static class MeleeLocal
    {
        static Run run;
        static uint kind;

        public static void Set(uint fighterKind, Run current)
        {
            run = current;
            kind = fighterKind;
        }

        /// The fighter the local player played in `which`; false if the local player had no Melee fighter there.
        public static bool TryGet(Run which, out uint fighterKind)
        {
            fighterKind = kind;
            return which && run && which == run;
        }
    }
}
