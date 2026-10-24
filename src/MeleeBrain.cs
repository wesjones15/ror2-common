using RoR2;
using UnityEngine;

namespace Ror2Common
{
    // The decisions of a Melee fighter no player controls: Goobo Jr.'s gummy clone, an
    // Artifact of Vengeance umbra, and the like. The AI master only walks (SurvivorAi.Drivers:
    // stop and face the enemy, else run at it, else follow the leader); this one aggressive
    // policy works every fighter's controls from what the AI sees:
    //  - face and aim at the AI's current enemy, sprint after it and hop over the last stretch;
    //  - in A reach, alternate the spammable B attack with a ready skill, so all four slots
    //    are used as often as their charges allow;
    //  - farther out, work the slots by distance, holding a charge move through its wind-up
    //    and releasing it (Samus's held charge gets its second press to fire);
    //  - when a hit lands, cancel into an evade: shield if the loadout has one, otherwise a
    //    sideways hop away from the enemy.
    // No per-fighter branches: the slot mapping, charge state and readiness come from the
    // shared MeleeSkillSet and the sim's own state.
    public class MeleeBrain
    {
        public struct Commands
        {
            public uint buttons;
            public Vector3 move, aim;
        }

        readonly bool primaryRanged, utilityRanged;

        /// `primaryRanged`/`utilityRanged`: the fighter's own native move in that slot is a
        /// projectile or charge shot (the driver reads them from IsRangedMove). A melee
        /// primary such as Falcon Punch is then only used in reach; a ranged one from afar.
        public MeleeBrain(bool primaryRanged = true, bool utilityRanged = true)
        {
            this.primaryRanged = primaryRanged;
            this.utilityRanged = utilityRanged;
        }

        // Native Phase::ChargeShot (libssbm sim.rs): the stored charge waits for a second press.
        const uint ChargeShotPhase = 4;

        // Beyond this only chase. A attack reach, and where the brain stops closing.
        const float EngageRange = 35f, MeleeRange = 2.8f, StandOff = 1.35f;
        // Longest a skill button is held (s): a charge move gets a wind-up, a tap move is
        // simply held through its first clip. Aggressive: shorter than a player's full charge.
        const float HoldFar = 0.95f, HoldNear = 0.2f;
        // Gaps between presses (s). Short, so a clone keeps the pressure on.
        const float AttackGap = 0.12f, SkillGap = 0.26f, RefireGap = 0.2f;
        // A hop after a running enemy, and a hit's evade.
        const float HopGap = 2.2f, EvadeTime = 0.45f;

        int heldSlot = -1;
        float holdLeft, attackGap, skillGap, refireGap, hopGap, evadeLeft, lastHealth;
        int nextSlot, closeActions, evadeSide = 1;
        CharacterBody seen;

        /// The buttons, movement and aim for the next fixed step. `dt` is the step length.
        public virtual Commands Think(CharacterBody body, MeleeSkillSet set, in MeleeControlLayout layout,
            in MeleeNative.State state, Vector3 move, float dt)
        {
            var c = new Commands { move = move };
            attackGap -= dt; skillGap -= dt; refireGap -= dt; hopGap -= dt; evadeLeft -= dt;
            if (heldSlot >= 0) holdLeft -= dt;
            var enemy = SurvivorAi.Enemy(body);
            if (!enemy || set == null)
            {
                Reset();
                return c;
            }

            var from = body.corePosition;
            var at = enemy.corePosition;
            var to = at - from;
            var flat = new Vector3(to.x, 0f, to.z);
            float d = flat.magnitude;
            if (to.sqrMagnitude > 0.0001f) c.aim = to.normalized;
            if (enemy != seen)
            {
                seen = enemy;
                CommonLog.Info($"Goobo {body.name} fights {enemy.name} at {d:0.0} m");
            }
            // A wall between them: keep running after the enemy, but do not throw moves into it.
            if (d > EngageRange || Physics.Linecast(from, at, WorldProbe.Mask, QueryTriggerInteraction.Ignore))
            {
                heldSlot = -1;
                return c;
            }

            // A hit landed: get out of it at once. A shield loadout holds the shield; the
            // others hop sideways and away, so the next attack starts from a new angle.
            float health = body.healthComponent ? body.healthComponent.combinedHealth : 0f;
            if (lastHealth > 0f && lastHealth - health > Mathf.Max(0.5f,
                (body.healthComponent ? body.healthComponent.fullCombinedHealth : 100f) * 0.005f))
            {
                evadeLeft = EvadeTime;
                evadeSide = -evadeSide;
                hopGap = 0f;
            }
            lastHealth = health;
            if (evadeLeft > 0f && state.grounded != 0 && d > 1.2f)
            {
                if (set.ShieldOnExtra(body) || set.ShieldOnSecondary(body))
                {
                    c.move = Vector3.zero;
                    c.buttons = set.ShieldOnExtra(body) ? layout.Extra : layout.Secondary;
                }
                else
                {
                    c.move = (Vector3.Cross(Vector3.up, flat.normalized) * evadeSide - flat.normalized * 0.3f).normalized * 0.95f;
                    c.buttons = MeleeControls.Jump;
                    hopGap = HopGap;
                }
                return c;
            }

            // A skill or attack is already running: hold a charge, and fire Samus's stored shot.
            if (state.move_id != 0)
            {
                int slot = set.Slot(body, state.move_id);
                if (heldSlot == slot && holdLeft > 0f)
                {
                    c.buttons = Button(in layout, slot);
                    return c;
                }
                heldSlot = -1;
                if (state.phase == ChargeShotPhase && refireGap <= 0f)
                {
                    c.buttons = Button(in layout, slot);
                    refireGap = RefireGap;
                }
                return c;
            }

            // The AI master stops at its hold range; run the last step in so A attacks reach.
            heldSlot = -1;
            if (d > StandOff && d < 10f && move.sqrMagnitude < 0.12f)
                c.move = flat.normalized * 0.95f;
            if (d < 1.2f) c.move = Vector3.zero;
            if (d > 3.5f && c.move.sqrMagnitude > 0.03f) c.buttons |= MeleeControls.Sprint;
            // Hop after a running enemy: closes gaps and makes the clone harder to hit.
            if (state.grounded != 0 && d > 3f && d < 14f && c.move.sqrMagnitude > 0.03f && hopGap <= 0f)
            {
                c.buttons |= MeleeControls.Jump;
                hopGap = HopGap;
            }
            if ((c.buttons & MeleeControls.Jump) != 0) return c;

            if (attackGap > 0f || skillGap > 0f) return c;

            uint allowed = set.Allowed(body);
            bool attack = set.AttackSelected(body) && (allowed & (1u << 4)) != 0;
            // In reach: B, then a skill, then B... so charges are spent as fast as they come back.
            if (d <= MeleeRange && attack && (++closeActions & 1) != 0)
            {
                c.buttons = layout.Extra;
                attackGap = AttackGap;
                return c;
            }
            int pick = Pick(set, body, allowed, d, state.grounded != 0);
            if (pick < 0)
            {
                if (d <= MeleeRange && attack)
                {
                    c.buttons = layout.Extra;
                    attackGap = AttackGap;
                }
                return c;
            }
            nextSlot = (pick + 1) & 3;
            heldSlot = pick;
            holdLeft = HoldTime(d);
            skillGap = SkillGap;
            c.buttons = Button(in layout, pick);
            return c;
        }

        // The next ready skill that suits this distance, starting after the last one used.
        int Pick(MeleeSkillSet set, CharacterBody body, uint allowed, float d, bool grounded)
        {
            for (int i = 0; i < 4; i++)
            {
                int slot = (nextSlot + i) & 3;
                if ((allowed & (1u << slot)) == 0) continue;
                // The shield alternate is the hurt evade above, not a random combat skill.
                if (slot == 1 && set.ShieldOnSecondary(body)) continue;
                if (!Fits(slot, d, grounded)) continue;
                return slot;
            }
            return -1;
        }

        /// How long a skill's button is held: a charge move far out, a quick tap close in.
        protected virtual float HoldTime(float d) => d > 6f ? HoldFar : HoldNear;

        // Primary is the ranged/charge (or a melee fighter's heavy punch), utility a lunge or
        // thrown weapon, secondary a close special and special an up special/recovery.
        protected virtual bool Fits(int slot, float d, bool grounded) => slot switch
        {
            0 => d >= 1.5f && d <= (primaryRanged ? EngageRange : MeleeRange + 1f),
            1 => d <= 9f,
            2 => d >= 1.5f && d <= (utilityRanged ? 14f : 6f),
            3 => d <= (grounded ? 7f : 11f),
            _ => false,
        };

        static uint Button(in MeleeControlLayout l, int slot) => slot switch
        {
            0 => l.Primary,
            1 => l.Secondary,
            2 => l.Utility,
            3 => l.Special,
            _ => l.Extra,
        };

        void Reset()
        {
            heldSlot = -1;
            closeActions = 0;
            seen = null;
            refireGap = 0f;
            evadeLeft = 0f;
            lastHealth = 0f;
        }
    }
}
