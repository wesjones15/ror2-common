using System.Collections.Generic;
namespace Ror2Common
{
    /// Negotiated gameplay support, independent of renderer availability and catalogs.
    public sealed class SessionFeaturePolicy
    {
        readonly Dictionary<int,uint> peers = new Dictionary<int,uint>();
        public uint Active { get; private set; }
        public bool Locked { get; private set; }
        public void Report(int peer, uint supported) => peers[peer] = supported;
        public uint Reconcile(uint local, IEnumerable<int> required)
        {
            if (Locked) return Active;
            uint common = local;
            var live = new HashSet<int>();
            foreach (int peer in required) { live.Add(peer); common &= peers.TryGetValue(peer, out uint mask) ? mask : 0; }
            var stale = new List<int>(); foreach (int peer in peers.Keys) if (!live.Contains(peer)) stale.Add(peer);
            foreach (int peer in stale) peers.Remove(peer);
            return Active = common;
        }
        public void Lock() => Locked = true;
        public bool Compatible(uint supported) => !Locked || (supported & Active) == Active;
        public void Reset() { Locked = false; Active = 0; peers.Clear(); }
    }
}
