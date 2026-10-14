using System;
using System.Collections.Generic;

namespace Ror2Common
{
    /// Splitting a payload into packets that fit a network message, and putting them back
    /// together however they arrive. No Unity types (tests/MpCheck checks it).
    ///
    /// Every packet starts with a byte: 0 for a whole payload (the rest is the payload), 1 for a
    /// fragment (then the message id, the fragment's index and the count, then its part).
    public static class Fragments
    {
        /// Payloads up to this go as one packet.
        public const int Whole = 1100;
        const int Header = 1 + 2 + 1 + 1;
        public const int PerFragment = 1000;
        public const int MaxFragments = 255;
        public const int MaxPayload = PerFragment * MaxFragments;

        public static List<byte[]> Pack(byte[] payload, ushort id)
        {
            var packets = new List<byte[]>();
            if (payload.Length <= Whole)
            {
                var one = new byte[1 + payload.Length];
                Buffer.BlockCopy(payload, 0, one, 1, payload.Length);
                packets.Add(one);
                return packets;
            }
            if (payload.Length > MaxPayload) throw new ArgumentException("payload too large");
            int count = (payload.Length + PerFragment - 1) / PerFragment;
            for (int i = 0; i < count; i++)
            {
                int offset = i * PerFragment;
                int n = Math.Min(PerFragment, payload.Length - offset);
                var p = new byte[Header + n];
                p[0] = 1;
                p[1] = (byte)id;
                p[2] = (byte)(id >> 8);
                p[3] = (byte)i;
                p[4] = (byte)count;
                Buffer.BlockCopy(payload, offset, p, Header, n);
                packets.Add(p);
            }
            return packets;
        }

        /// Collects fragments, per sender and message type. A newer message from the same
        /// sender throws away an older one still incomplete, so a lost fragment never stalls
        /// the stream.
        public sealed class Assembler
        {
            sealed class Partial
            {
                public ushort id;
                public int count, have;
                public byte[][] parts;
            }

            readonly Dictionary<(int sender, short type), Partial> partial = new Dictionary<(int, short), Partial>();

            /// The whole payload if this packet completes one, or null (also for a malformed
            /// packet, which is dropped).
            public byte[] Add(int sender, short type, byte[] packet)
            {
                if (packet == null || packet.Length < 1) return null;
                if (packet[0] == 0)
                {
                    var whole = new byte[packet.Length - 1];
                    Buffer.BlockCopy(packet, 1, whole, 0, whole.Length);
                    return whole;
                }
                if (packet[0] != 1 || packet.Length < Header) return null;
                ushort id = (ushort)(packet[1] | (packet[2] << 8));
                int index = packet[3], count = packet[4];
                if (count == 0 || index >= count) return null;
                var key = (sender, type);
                if (!partial.TryGetValue(key, out var p) || p.id != id || p.count != count)
                {
                    // A different message: the older one is abandoned (message ids only go up,
                    // so a straggler from an older one would start a new entry; wrapping is fine
                    // because it is replaced by the next real message).
                    p = new Partial { id = id, count = count, parts = new byte[count][] };
                    partial[key] = p;
                }
                if (p.parts[index] == null)
                {
                    var part = new byte[packet.Length - Header];
                    Buffer.BlockCopy(packet, Header, part, 0, part.Length);
                    p.parts[index] = part;
                    p.have++;
                }
                if (p.have < p.count) return null;
                partial.Remove(key);
                int total = 0;
                foreach (var part in p.parts) total += part.Length;
                var all = new byte[total];
                int at = 0;
                foreach (var part in p.parts)
                {
                    Buffer.BlockCopy(part, 0, all, at, part.Length);
                    at += part.Length;
                }
                return all;
            }

            public void Clear() => partial.Clear();
        }
    }
}
