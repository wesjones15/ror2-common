namespace Ror2Common
{
    /// Whether a Melee move the sim has started may deal damage, spawn its articles and catch grabs.
    /// The pad (`MeleeSkillSet.Allowed`) already refused the press if the slot had no charge, so by
    /// the time the sim starts the move it is allowed. A charge is only bookkeeping (RoR2's
    /// cooldown, Backup Magazine and the like): a chained start (a jab's second hit, a rapid
    /// jab) finds the slot's stock already spent and must still hit. Pure, so the headless checks
    /// can pin it (tests/MeleeHostCheck).
    public static class MeleePermit
    {
        /// `holdsOwn`: the slot holds this fighter's own skill, not another skill overriding it (Heresy).
        /// `authority`: this machine runs the body's sim (`MeleeNet.Acts`).
        /// `spent`: whether a charge was there to spend; it is deliberately not part of the answer.
        public static bool Allowed(bool holdsOwn, bool authority, bool spent) => holdsOwn && authority;
    }
}
