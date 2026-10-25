using System;
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
    /// One-off on purpose: the general home for character-agnostic moves is the Melee plugin's
    /// own `IEggLayer`; this only bridges the assembly boundary.
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

        /// The Melee plugin is loaded and has published the egg.
        public static bool Ready => Catch != null && Lay != null;

        public static bool TryShow(CharacterBody owner, Vector3 target)
        {
            if (Show == null || !owner) return false;
            Show(owner, target);
            return true;
        }

        public static bool TryCatch(CharacterBody owner, CharacterBody victim)
            => Catch != null && owner && victim && Catch(owner, victim);

        public static bool TryLay(CharacterBody owner, CharacterBody victim, Vector3? at = null)
            => Lay != null && owner && victim && Lay(owner, victim, at);

        /// Is this character in the middle of an egg lay (catching or laying)?
        public static bool IsLaying(CharacterBody owner)
            => Laying != null && owner && Laying(owner);
    }
}
