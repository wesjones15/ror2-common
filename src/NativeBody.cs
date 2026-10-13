using System.Reflection;
using EntityStates;
using RoR2;
using UnityEngine;

namespace Ror2Common
{
    // A survivor body moved by a native sim instead of RoR2's character motor (its
    // KinematicCharacterMotor off, its main state not GenericCharacterMain), and the parts of
    // RoR2's own movement and skill code that items lean on, done for it:
    //
    // - Velocity: RoR2 throws bodies by writing `CharacterMotor.velocity` (jump pads and
    //   geysers, knockback through `ApplyForce`, Milky Chrysalis, grapples). The plugin writes
    //   the sim's velocity there after each step (`WroteVelocity`); anything else that changes
    //   it by the next step is the world's (`TakeExternalVelocity`), for the sim to take as a
    //   launch, so it's applied once and not overwritten.
    // - Crowd control: stuns, freezes and shocks put the body's state machine in their own
    //   state; roots (Tar's entangle, a void jailer's nullify, Hooks of Heresy) are buffs.
    //   `Control` says which applies, for the plugin to hold the sim's input.
    // - Sprinting: GenericCharacterMain's rule (a sprint press holds until the body stops
    //   moving), so sprint items (Energy Drink, Wax Quail, Weeping Fungus) see it.
    // - Combat: RoR2 counts out-of-combat time (Red Whip) from the last combat skill used;
    //   native attacks aren't skills, so `MarkCombat` restarts it.
    // - The primary: on-primary items (Shuriken) listen for the primary skill being used;
    //   `PulsePrimary` reports a use, no more often than asked.
    //
    // It also marks the body for ror2-common's item hooks (ItemHooks.cs), which keep RoR2's
    // motor-driven item states (Headstompers' stomp) off it: the plugin does those itself.
    public sealed class NativeBody : MonoBehaviour
    {
        public enum Hold
        {
            /// Free to act.
            None,
            /// Rooted: may act in place, not move.
            Move,
            /// Stunned, frozen or shocked: no input at all.
            All,
        }

        CharacterBody body;
        CharacterMotor motor;
        EntityStateMachine bodyMachine;
        Vector3 written;
        bool wrote;
        bool sprinting;
        float lastPrimary = float.NegativeInfinity;

        static FieldInfo combatClock;
        static bool combatLooked;

        void Awake()
        {
            body = GetComponent<CharacterBody>();
            motor = GetComponent<CharacterMotor>();
            bodyMachine = EntityStateMachine.FindByCustomName(gameObject, "Body");
        }

        public static NativeBody Of(GameObject go) => go ? go.GetComponent<NativeBody>() : null;

        // ---- velocity --------------------------------------------------------------------

        /// Puts the sim's velocity (m/s) on the motor, for RoR2's animation, camera and AI, and
        /// remembers it.
        public void WroteVelocity(Vector3 v)
        {
            if (motor) motor.velocity = v;
            written = v;
            wrote = true;
        }

        /// Something other than the plugin changed the motor's velocity since `WroteVelocity`
        /// by more than `threshold` m/s: `v` is what it is now, for the sim to take. Taking it
        /// counts as writing it.
        public bool TakeExternalVelocity(out Vector3 v, float threshold = 0.5f)
        {
            v = motor ? motor.velocity : Vector3.zero;
            if (!wrote || !motor) return false;
            if ((v - written).sqrMagnitude <= threshold * threshold) return false;
            written = v;
            return true;
        }

        /// The body was moved by other means (a teleport): whatever's on the motor now is the
        /// start, not a throw.
        public void ForgetVelocity() => wrote = false;

        /// JumpVolume marks its throws by disabling air control until the next collision.
        /// Native hosts preserve the pad's trajectory using RoR2's gravity for that flight.
        public bool JumpPadLaunch => motor && motor.disableAirControlUntilCollision;

        /// The native controller reported ground contact: finish the pad's air-control hold.
        public void Landed()
        {
            if (motor) motor.disableAirControlUntilCollision = false;
        }

        // ---- crowd control ---------------------------------------------------------------

        static readonly string[] heldStates =
        {
            "EntityStates.StunState", "EntityStates.FrozenState", "EntityStates.ShockState",
            "EntityStates.HurtState",
        };
        static readonly string[] rootBuffs = { "Entangle", "Nullified", "LunarSecondaryRoot" };

        public Hold Control()
        {
            var state = bodyMachine ? bodyMachine.state : null;
            if (state != null)
            {
                string name = state.GetType().FullName;
                foreach (var s in heldStates)
                    if (name == s) return Hold.All;
            }
            foreach (var b in rootBuffs)
                if (ByName.Has(body, b)) return Hold.Move;
            return Hold.None;
        }

        // ---- sprinting -------------------------------------------------------------------

        /// GenericCharacterMain's sprint: a press starts it, and it lasts until the body stops
        /// moving (or can't). Sets `CharacterBody.isSprinting` where the body has authority.
        public bool UpdateSprint(bool pressed, bool moving, bool allowed = true)
        {
            if (pressed) sprinting = true;
            if (!moving || !allowed) sprinting = false;
            if (body && body.hasEffectiveAuthority && body.isSprinting != sprinting)
                body.isSprinting = sprinting;
            return sprinting;
        }

        // ---- combat ----------------------------------------------------------------------

        /// Restarts `body`'s out-of-combat clock, as using a combat skill does.
        public static void MarkCombat(CharacterBody body)
        {
            if (!body) return;
            if (!combatLooked)
            {
                combatLooked = true;
                combatClock = ByName.Field(typeof(CharacterBody), "outOfCombatStopwatch");
                if (combatClock == null) CommonLog.Warn("NativeBody: no outOfCombatStopwatch in this RoR2; Red Whip ignores native attacks");
            }
            combatClock?.SetValue(body, 0f);
        }

        /// Reports a use of the primary skill (Shuriken's trigger), at most once per
        /// `interval` seconds. Returns whether it did.
        public bool PulsePrimary(float interval)
        {
            var slot = body && body.skillLocator ? body.skillLocator.primary : null;
            if (!slot) return false;
            float now = Time.fixedTime;
            if (now - lastPrimary < interval) return false;
            lastPrimary = now;
            body.OnSkillActivated(slot);
            return true;
        }
    }
}
