using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Ror2Common;

[StructLayout(LayoutKind.Sequential)]
struct Sample { public float a, b; public byte c, d; public short e; public uint f; }

static class Program
{
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    static void Main()
    {
        var w = new ByteWriter(4);
        w.U8(7); w.Bool(true); w.I8(-3); w.U16(0xBEEF); w.I16(-1234); w.U32(0xDEADBEEF); w.I32(-77);
        w.F32(3.5f); w.F32(float.NaN); w.Str("Mario ☆"); w.Struct(new Sample { a = 1.25f, b = -2f, c = 9, d = 8, e = -5, f = 42 });
        var bytes = w.ToArray();
        var r = new ByteReader(bytes);
        Check(r.U8() == 7, "u8"); Check(r.Bool(), "bool"); Check(r.I8() == -3, "i8");
        Check(r.U16() == 0xBEEF, "u16"); Check(r.I16() == -1234, "i16");
        Check(r.U32() == 0xDEADBEEF, "u32"); Check(r.I32() == -77, "i32");
        Check(r.F32() == 3.5f, "f32"); Check(float.IsNaN(r.F32()), "nan");
        Check(r.Str() == "Mario ☆", "str");
        var s = r.Struct<Sample>();
        Check(s.a == 1.25f && s.b == -2f && s.c == 9 && s.d == 8 && s.e == -5 && s.f == 42, "struct");
        Check(r.Done && !r.Failed, "consumed exactly");

        // A truncated packet fails cleanly and returns zeros, never throws.
        var cut = new ByteReader(bytes, 0, 3);
        cut.U8(); cut.Bool(); cut.I8();
        Check(!cut.Failed, "exact end is fine");
        Check(cut.U16() == 0 && cut.Failed, "past the end fails");
        Check(cut.U32() == 0 && cut.Str() == "" && cut.F32() == 0f, "stays zero once failed");

        // Offsets: a reader over a slice of a bigger buffer.
        var big = new byte[10]; big[4] = 1; big[5] = 2;
        var slice = new ByteReader(big, 4, 2);
        Check(slice.U16() == 0x0201 && slice.Done, "slice");

        // Growth past the initial capacity, and Clear reuse.
        var g = new ByteWriter(1);
        for (int i = 0; i < 1000; i++) g.U32((uint)i);
        Check(g.Length == 4000, "grew");
        var gr = new ByteReader(g.ToArray());
        for (int i = 0; i < 1000; i++) Check(gr.U32() == (uint)i, "regrow " + i);
        g.Clear(); g.U8(5); Check(g.ToArray().Length == 1, "clear");

        // Append and Raw.
        var head = new ByteWriter();
        head.U8(1);
        var tail = new ByteWriter();
        tail.U16(0x0302);
        head.Append(tail);
        Check(head.ToArray().Length == 3 && new ByteReader(head.ToArray()).Raw.Length == 3, "append");

        FragmentChecks();
        Console.WriteLine("MpCheck: ok");
    }

    static byte[] Payload(int n, int seed)
    {
        var data = new byte[n];
        for (int i = 0; i < n; i++) data[i] = (byte)(i * 31 + seed);
        return data;
    }

    static bool Same(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    static void FragmentChecks()
    {
        var asm = new Fragments.Assembler();
        // Small payloads are one packet.
        var small = Payload(10, 1);
        var one = Fragments.Pack(small, 7);
        Check(one.Count == 1 && one[0][0] == 0, "small goes whole");
        Check(Same(asm.Add(1, 5, one[0]), small), "whole round trip");
        var empty = Fragments.Pack(new byte[0], 1);
        Check(empty.Count == 1 && asm.Add(1, 5, empty[0]).Length == 0, "empty payload");
        Check(Fragments.Pack(Payload(Fragments.Whole, 0), 1).Count == 1, "the whole limit is whole");
        Check(Fragments.Pack(Payload(Fragments.Whole + 1, 0), 1).Count == 2, "one over splits");

        // A big one in pieces, delivered in any order, with a duplicate.
        var big = Payload(12345, 9);
        var packets = Fragments.Pack(big, 100);
        Check(packets.Count == 13, "13 fragments, got " + packets.Count);
        foreach (var p in packets) Check(p.Length <= 5 + Fragments.PerFragment, "each fits");
        byte[] done = null;
        int completed = 0;
        for (int i = packets.Count - 1; i >= 0; i--)
        {
            var got = asm.Add(2, 6, packets[i]);
            if (i == 3 && got == null) Check(asm.Add(2, 6, packets[i]) == null, "a repeated fragment adds nothing");
            if (got != null) { done = got; completed++; }
        }
        Check(completed == 1 && Same(done, big), "reassembled in reverse order");

        // Two senders don't mix.
        var other = Payload(3000, 4);
        var otherPackets = Fragments.Pack(other, 100);
        byte[] a = null, b = null;
        for (int i = 0; i < packets.Count; i++)
        {
            var x = asm.Add(1, 6, packets[i]);
            if (x != null) a = x;
            if (i < otherPackets.Count)
            {
                var y = asm.Add(2, 6, otherPackets[i]);
                if (y != null) b = y;
            }
        }
        Check(Same(a, big) && Same(b, other), "senders are kept apart");

        // A newer message abandons an older one that never finished.
        var older = Fragments.Pack(Payload(5000, 1), 200);
        var newer = Fragments.Pack(Payload(2500, 2), 201);
        asm.Add(3, 6, older[0]);
        asm.Add(3, 6, older[1]);
        byte[] result = null;
        foreach (var p in newer) result = asm.Add(3, 6, p) ?? result;
        Check(Same(result, Payload(2500, 2)), "the newer one completes");
        Check(asm.Add(3, 6, older[2]) == null, "a straggler from the abandoned one completes nothing");

        // Malformed packets are dropped.
        Check(asm.Add(1, 1, null) == null && asm.Add(1, 1, new byte[0]) == null, "null and empty");
        Check(asm.Add(1, 1, new byte[] { 9, 1, 2 }) == null, "unknown kind");
        Check(asm.Add(1, 1, new byte[] { 1, 0, 0, 5, 3, 1 }) == null, "index past the count");
        Check(asm.Add(1, 1, new byte[] { 1, 0, 0, 0, 0 }) == null, "zero count");
        Check(asm.Add(1, 1, new byte[] { 1, 0 }) == null, "short header");
        bool threw = false;
        try { Fragments.Pack(new byte[Fragments.MaxPayload + 1], 1); } catch (ArgumentException) { threw = true; }
        Check(threw, "too large is refused");
    }
}
