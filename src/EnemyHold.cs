using System;
using EntityStates;
using RoR2;
using RoR2.CharacterAI;
using UnityEngine;
using UnityEngine.Networking;

namespace Ror2Common
{
    /// Holds an enemy helpless where it stands: its AI is switched off, its state machines kept
    /// idle and its motion zeroed until the time runs out. Applying it again extends it, so a
    /// move that keeps landing (Donkey Kong's Hand Slap) holds an enemy for as long as it
    /// lasts, with no falloff. Bosses are held like anything else. Used for Yoshi's egg, a
    /// buried enemy and the slap's stunlock. Server only.
    public sealed class EnemyHold : MonoBehaviour
    {
        CharacterBody body;
        float until;
        BaseAI[] brains = Array.Empty<BaseAI>();
        bool[] brainWasOn = Array.Empty<bool>();
        EntityStateMachine[] machines = Array.Empty<EntityStateMachine>();

        public static bool IsHeld(CharacterBody body) => body && body.GetComponent<EnemyHold>();

        /// Seconds left on the hold.
        public static float Remaining(CharacterBody body)
        {
            var hold = body ? body.GetComponent<EnemyHold>() : null;
            return hold ? Mathf.Max(0f, hold.until - Time.time) : 0f;
        }

        /// Holds `victim` for at least `seconds` from now, or for exactly that long with
        /// `extend: false` (an egg takes over from Yoshi's full belly). Returns false for
        /// players and when not on the server.
        public static bool Hold(CharacterBody victim, float seconds, bool extend = true)
        {
            if (!NetworkServer.active) return MeleeNet.RemoteHold(victim, seconds);
            if (!victim || victim.isPlayerControlled || seconds <= 0f) return false;
            var hold = victim.GetComponent<EnemyHold>();
            if (!hold) hold = victim.gameObject.AddComponent<EnemyHold>();
            hold.until = extend ? Mathf.Max(hold.until, Time.time + seconds) : Time.time + seconds;
            return true;
        }
        /// Frees a held victim immediately (Yoshi dropping an eaten enemy, a death).
        public static void Release(CharacterBody victim)
        {
            if (!victim) return;
            var hold = victim.GetComponent<EnemyHold>();
            if (hold) Destroy(hold);
        }

        void Awake()
        {
            body = GetComponent<CharacterBody>();
            brains = body && body.master ? body.master.GetComponents<BaseAI>() : Array.Empty<BaseAI>();
            brainWasOn = new bool[brains.Length];
            for (int i = 0; i < brains.Length; i++) { brainWasOn[i] = brains[i].enabled; brains[i].enabled = false; }
            machines = GetComponents<EntityStateMachine>();
        }

        void FixedUpdate()
        {
            if (!body || !body.healthComponent || !body.healthComponent.alive || Time.time >= until) { Destroy(this); return; }
            foreach (var machine in machines)
                if (machine && !(machine.state is Idle)) machine.SetNextState(new Idle());
            if (body.characterMotor) body.characterMotor.velocity = Vector3.zero;
        }

        void OnDestroy()
        {
            for (int i = 0; i < brains.Length; i++) if (brains[i]) brains[i].enabled = brainWasOn[i];
            if (body && body.healthComponent && body.healthComponent.alive)
                foreach (var machine in machines) if (machine) machine.SetNextStateToMain();
        }
    }
}
