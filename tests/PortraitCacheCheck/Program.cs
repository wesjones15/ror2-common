using System;
using System.IO;
using System.Linq;
using Ror2Common;
using UnityEngine;

static class Program
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Main()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "portrait-cache-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            const int size = 4;
            int generated = 0;
            var pixels = Enumerable.Range(0, size * size).Select(i => new Color32((byte)(i*7), (byte)(i*11), (byte)(i*13), (byte)(i*17))).ToArray();
            Texture2D Generate() { generated++; return new Texture2D(size, size, TextureFormat.RGBA32, false) { Pixels = pixels.ToArray() }; }
            string Key(string name, string revision = PortraitCache.CharacterRevision) => SkillCardCache.FileName(root, revision, name, "portraits");
            var first = PortraitCache.GetOrCreate(Key("Mario"), "Mario", Generate, out var cached, size);
            Check(first && !cached && generated == 1, "first launch must generate");
            var bytes = File.ReadAllBytes(Key("Mario"));
            // A fresh call with no in-memory state represents the next process/launch.
            var second = PortraitCache.GetOrCreate(Key("Mario"), "Mario", () => throw new Exception("native render invoked on cache hit"), out cached, size);
            Check(cached && second.Pixels.SequenceEqual(pixels), "disk load changed pixels or alpha");
            Check(File.ReadAllBytes(Key("Mario")).SequenceEqual(bytes), "hit rewrote cache");
            PortraitCache.GetOrCreate(Key("Luigi"), "Luigi", Generate, out cached, size);
            Check(!cached && generated == 2, "new skin reused another portrait");
            PortraitCache.GetOrCreate(Key("Mario", "character-v2"), "Mario", Generate, out cached, size);
            Check(!cached && generated == 3, "revision change failed to regenerate");
            PortraitCache.GetOrCreate(Key("Mario"), "Mario", Generate, out cached, size);
            Check(cached && generated == 3, "new key/revision invalidated an existing image");
            File.WriteAllBytes(Key("Luigi"), new byte[20]);
            PortraitCache.GetOrCreate(Key("Luigi"), "Luigi", Generate, out cached, size);
            Check(!cached && generated == 4, "corrupt image wasn't regenerated");
            Check(SkillCardCache.TryRead(Key("Luigi"), out _, size), "corrupt cache wasn't repaired on disk");
            Check(!PortraitCache.GetOrCreate(Key("NewFighter"), "new", () => null, out cached, size)
                && !File.Exists(Key("NewFighter")), "unavailable assets were cached");
            PortraitCache.GetOrCreate(Key("NewFighter"), "new", Generate, out cached, size);
            Check(!cached && generated == 5, "missing assets cannot retry");
            // A read-only/misconfigured destination must not discard a valid generated image.
            string blocked = Path.Combine(root, "file"); File.WriteAllText(blocked, "not a directory");
            Check(PortraitCache.GetOrCreate(Path.Combine(blocked, "x.bmp"), "x", Generate, out cached, size)
                && !cached && generated == 6, "write failure lost generated portrait");
            Check(!SkillCardCache.TryRead(Key("Mario"), out _), "wrong requested dimensions accepted");
            Console.WriteLine("PASS: disk hits bypass generation; distinct skins, new characters, revisions, corrupt images, extraction retries and write failures");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}

// Recording texture boundary: tests cache behavior and exact RGBA bytes, not GPU lighting.
namespace UnityEngine
{
    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }
    public enum TextureFormat { RGBA32 }
    public enum TextureWrapMode { Clamp }
    public enum FilterMode { Bilinear }
    public class Texture2D
    {
        public string name; public int width, height;
        public TextureWrapMode wrapMode; public FilterMode filterMode;
        public Color32[] Pixels;
        public Texture2D(int w, int h, TextureFormat format, bool mipmap) { width = w; height = h; }
        public static implicit operator bool(Texture2D value) => value != null;
        public Color32[] GetPixels32() => Pixels;
        public void LoadRawTextureData(byte[] bytes)
        {
            Pixels = new Color32[bytes.Length / 4];
            for (int i = 0; i < Pixels.Length; i++) Pixels[i] = new Color32(bytes[i*4], bytes[i*4+1], bytes[i*4+2], bytes[i*4+3]);
        }
        public void Apply(bool mipmap, bool discard) { }
    }
}
namespace Ror2Common { public static class CommonLog { public static void Warn(string text) => Console.WriteLine(text); } }
