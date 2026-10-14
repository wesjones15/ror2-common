using System;
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
        Console.WriteLine("MpCheck: ok");
    }
}
