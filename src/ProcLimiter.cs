using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace Ror2Common
{
    // On-hit items for an attack that hits many times a second (a stream of water): damage
    // lands every hit, but item procs at most once per `interval` seconds on each target,
    // shared by everything the attacker sends through this limiter. Between procs a hit lands
    // with a proc coefficient of 0, which RoR2's on-hit items skip.
    public sealed class ProcLimiter
    {
        readonly float interval;
        readonly Dictionary<int, float> last = new Dictionary<int, float>();
        float pruned;

        public ProcLimiter(float interval)
        {
            this.interval = Mathf.Max(0f, interval);
        }

        /// The proc coefficient for a hit on `victim` now: `coefficient` if its last proc was
        /// at least `interval` ago (and this one counts), else 0.
        public float Coefficient(HealthComponent victim, float coefficient)
        {
            if (!victim || coefficient <= 0f) return 0f;
            float now = Time.fixedTime;
            if (now - pruned > 10f)
            {
                Prune(now);
                pruned = now;
            }
            int id = victim.GetInstanceID();
            if (last.TryGetValue(id, out float t) && now - t < interval) return 0f;
            last[id] = now;
            return coefficient;
        }

        static readonly List<int> stale = new List<int>();

        void Prune(float now)
        {
            stale.Clear();
            foreach (var kv in last)
                if (now - kv.Value >= interval) stale.Add(kv.Key);
            foreach (int k in stale) last.Remove(k);
        }

        public void Clear() => last.Clear();
    }
}
