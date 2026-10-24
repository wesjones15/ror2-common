using System;
using System.IO;
using System.Linq;
using Ror2Common;

static class Program
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Main()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "skill-card-cache-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = SkillCardCache.FileName(root, SkillCardCache.Revision, "MarioFludd");
            Check(path.StartsWith(Path.Combine(root, "skill-icons")), "cache escaped assets");
            Check(!SkillCardCache.TryRead(path, out _), "missing cache must regenerate");
            Check(!SkillCardCache.TryRead(null, out _), "unconfigured cache must generate");
            var pixels = new byte[SkillCardCache.Size * SkillCardCache.Size * 4];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)((i * 37 + i / 1024) % 251);
            SkillCardCache.Write(path, pixels);
            Check(SkillCardCache.TryRead(path, out var read) && pixels.SequenceEqual(read), "RGBA/row orientation changed");
            var disk = File.ReadAllBytes(path);
            Check(disk[54] == pixels[2] && disk[56] == pixels[0], "BMP must store BGRA");
            var old = SkillCardCache.FileName(root, "approved-v2", "OlimarWhistle");
            SkillCardCache.Write(old, pixels);
            Check(!SkillCardCache.TryRead(SkillCardCache.FileName(root, SkillCardCache.Revision, "OlimarWhistle"), out _), "old contaminated whistle reused");
            pixels[0] ^= 42; SkillCardCache.Write(path, pixels);
            Check(SkillCardCache.TryRead(path, out read) && pixels.SequenceEqual(read), "atomic overwrite failed");
            bool rejected = false;
            try { SkillCardCache.Write(path, new byte[3]); } catch (ArgumentException) { rejected = true; }
            Check(rejected && SkillCardCache.TryRead(path, out read) && pixels.SequenceEqual(read), "invalid write damaged good cache");
            disk = File.ReadAllBytes(path); disk[18] = 0; disk[19] = 0; File.WriteAllBytes(path, disk);
            Check(!SkillCardCache.TryRead(path, out _), "wrong dimensions must regenerate");
            File.WriteAllBytes(path, new byte[17]);
            Check(!SkillCardCache.TryRead(path, out _), "truncated cache must regenerate");
            rejected = false;
            try { SkillCardCache.FileName(root, "../outside", "MarioFludd"); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "unsafe cache key accepted");
            Check(Directory.GetFiles(Path.GetDirectoryName(path), "*.tmp-*").Length == 0, "temporary write left behind");
            Console.WriteLine("PASS: first-run miss, exact cached pixels, stale/corrupt regeneration, atomic replacement and path boundaries");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
