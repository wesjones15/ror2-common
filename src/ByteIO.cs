using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Ror2Common
{
    /// A growable little-endian byte buffer for network payloads. No Unity types, so it can be
    /// checked outside the game (tests/MpCheck). Pair with `ByteReader`.
    public sealed class ByteWriter
    {
        byte[] buffer;
        int length;

        public ByteWriter(int capacity = 128) { buffer = new byte[Math.Max(16, capacity)]; }

        public int Length => length;

        public void Clear() => length = 0;

        public byte[] ToArray()
        {
            var copy = new byte[length];
            Buffer.BlockCopy(buffer, 0, copy, 0, length);
            return copy;
        }

        void Need(int n)
        {
            if (length + n <= buffer.Length) return;
            Array.Resize(ref buffer, Math.Max(buffer.Length * 2, length + n));
        }

        public void U8(byte v) { Need(1); buffer[length++] = v; }
        public void Bool(bool v) => U8(v ? (byte)1 : (byte)0);
        public void I8(sbyte v) => U8((byte)v);

        public void U16(ushort v)
        {
            Need(2);
            buffer[length++] = (byte)v;
            buffer[length++] = (byte)(v >> 8);
        }

        public void I16(short v) => U16((ushort)v);

        public void U32(uint v)
        {
            Need(4);
            for (int i = 0; i < 4; i++) buffer[length++] = (byte)(v >> (8 * i));
        }

        public void I32(int v) => U32((uint)v);

        public unsafe void F32(float v) => U32(*(uint*)&v);

        public void Bytes(byte[] data, int offset, int count)
        {
            Need(count);
            Buffer.BlockCopy(data, offset, buffer, length, count);
            length += count;
        }

        /// Another writer's bytes, as they are.
        public void Append(ByteWriter other) => Bytes(other.buffer, 0, other.length);

        /// A length-prefixed string (UTF-8, up to 255 bytes).
        public void Str(string s)
        {
            var b = Encoding.UTF8.GetBytes(s ?? "");
            if (b.Length > 255) throw new ArgumentException("string too long");
            U8((byte)b.Length);
            Bytes(b, 0, b.Length);
        }

        /// A blittable struct's raw bytes (the native structs, whose layout both ends share
        /// because the plugin refuses a native library with a different ABI).
        public unsafe void Struct<T>(in T value) where T : unmanaged
        {
            int n = sizeof(T);
            Need(n);
            fixed (byte* p = &buffer[length])
                *(T*)p = value;
            length += n;
        }
    }

    public sealed class ByteReader
    {
        readonly byte[] data;
        int pos;
        readonly int end;

        public ByteReader(byte[] data) : this(data, 0, data?.Length ?? 0) { }

        public ByteReader(byte[] data, int offset, int count)
        {
            this.data = data ?? new byte[0];
            pos = offset;
            end = offset + count;
        }

        /// Reading past the end (a corrupt or truncated packet) sets this and returns zeros.
        public bool Failed { get; private set; }
        /// The whole buffer this reads from (for relaying a payload untouched).
        public byte[] Raw => data;
        public int Remaining => end - pos;
        public bool Done => pos >= end;

        bool Take(int n)
        {
            if (Failed || pos + n > end)
            {
                Failed = true;
                return false;
            }
            return true;
        }

        public byte U8() => Take(1) ? data[pos++] : (byte)0;
        public bool Bool() => U8() != 0;
        public sbyte I8() => (sbyte)U8();

        public ushort U16()
        {
            if (!Take(2)) return 0;
            ushort v = (ushort)(data[pos] | (data[pos + 1] << 8));
            pos += 2;
            return v;
        }

        public short I16() => (short)U16();

        public uint U32()
        {
            if (!Take(4)) return 0;
            uint v = 0;
            for (int i = 0; i < 4; i++) v |= (uint)data[pos + i] << (8 * i);
            pos += 4;
            return v;
        }

        public int I32() => (int)U32();

        public unsafe float F32()
        {
            uint v = U32();
            return *(float*)&v;
        }

        public string Str()
        {
            int n = U8();
            if (!Take(n)) return "";
            var s = Encoding.UTF8.GetString(data, pos, n);
            pos += n;
            return s;
        }

        public unsafe T Struct<T>() where T : unmanaged
        {
            int n = sizeof(T);
            if (!Take(n)) return default;
            T value;
            fixed (byte* p = &data[pos])
                value = *(T*)p;
            pos += n;
            return value;
        }
    }
}
