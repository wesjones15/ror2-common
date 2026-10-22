using RoR2;
using UnityEngine;

namespace Ror2Common
{
    /// Diagnostic log lines for the Melee damage path, behind the plugin's `Debug/Diagnostics` config
    /// entry (`MeleeDiag.On`). Each line starts with "[MeleeDiag]". Cheap when off (one bool test);
    /// when on, move starts and hitbox batches log once per event or move serial, and the per-hit
    /// `Deal` line is capped at `DealsPerSecond` a second (a count of the rest follows).
    public static class MeleeDiag
    {
        public static bool On;
        const int DealsPerSecond = 40;
        static float window; static int dealsInWindow, suppressed;

        /// The sim started a move. `stock` is the slot's charges before the spend (-1: no slot).
        internal static void MoveStart(string fighter, uint move, int slot, bool holdsOwn, int stock, bool spent, bool acts, bool permit)
        {
            if (!On) return;
            CommonLog.Info($"[MeleeDiag] move fighter={fighter} move={move} slot={slot} holdsOwn={holdsOwn} stock={stock} spent={spent} acts={acts} permitDamage={permit}");
        }

        /// A batch of hitboxes came out of the sim this tick; logged once per hitbox serial (a new move or hit group).
        internal static void Batch(string fighter, uint stateMove, uint startedMove, MeleeNative.HitboxSample[] samples, int count, bool permit, ref ulong lastSerial)
        {
            if (!On || count <= 0) return;
            ulong serial = samples[0].serial;
            if (serial == lastSerial) return;
            lastSerial = serial;
            var first = samples[0];
            CommonLog.Info($"[MeleeDiag] hitboxes fighter={fighter} stateMove={stateMove} startedMove={startedMove} serial={serial} count={count} first.damage={first.damage:0.##} first.element={first.element} permitDamage={permit}");
        }

        /// `NativeHit.Deal` ran. `outcome`: server, sent-to-server, client-unsent or victim-dead.
        internal static void Deal(CharacterBody attacker, HealthComponent victim, in NativeHitSpec spec, string outcome, bool result, float damage, float healthBefore, float healthAfter, string stagger)
        {
            if (!On) return;
            float now = Time.unscaledTime;
            if (now - window >= 1f)
            {
                if (suppressed > 0) CommonLog.Info($"[MeleeDiag] deal: {suppressed} more hits in the last second not logged");
                window = now; dealsInWindow = 0; suppressed = 0;
            }
            if (++dealsInWindow > DealsPerSecond) { suppressed++; return; }
            string attackerName = attacker ? attacker.name : "none";
            string victimName = victim && victim.body ? victim.body.name : "none";
            CommonLog.Info($"[MeleeDiag] deal attacker={attackerName} victim={victimName} outcome={outcome} result={result} coefficient={spec.coefficient:0.##} damage={damage:0.#} health={healthBefore:0.#}->{healthAfter:0.#} stagger={spec.staggerDuration:0.##}({stagger})");
        }
    }
}
