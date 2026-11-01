using System;
using System.IO;

namespace Ror2Common
{
    // Unity-independent cache codec. Pixel rows are bottom first, matching Texture2D and
    // positive-height BMPs. No extracted artwork is shipped; these are first-run renders.
    public static class SkillCardCache
    {
        public const int Size = 256;
        public const string Revision = "approved-v3";

        public static string FileName(string assetsDir, string revision, string name, string folder = "skill-icons")
        {
            if (string.IsNullOrWhiteSpace(assetsDir)) return null;
            foreach (var value in new[] { revision, name, folder })
            {
                if (string.IsNullOrEmpty(value)) throw new ArgumentException("Missing portrait cache key");
                foreach (char c in value)
                    if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_'))
                        throw new ArgumentException("Invalid portrait cache key");
            }
            return Path.Combine(assetsDir, folder, revision, name + ".bmp");
        }

        public static bool TryRead(string path, out byte[] rgba, int size = Size)
        {
            rgba = null;
            if (string.IsNullOrEmpty(path) || size <= 0 || size > 2048) return false;
            try
            {
                using var stream = File.OpenRead(path);
                using var reader = new BinaryReader(stream);
                int bytes = size * size * 4;
                if (stream.Length != 54 + bytes || reader.ReadUInt16() != 0x4d42
                    || reader.ReadInt32() != 54 + bytes || reader.ReadInt32() != 0
                    || reader.ReadInt32() != 54 || reader.ReadInt32() != 40
                    || reader.ReadInt32() != size || reader.ReadInt32() != size
                    || reader.ReadInt16() != 1 || reader.ReadInt16() != 32
                    || reader.ReadInt32() != 0 || reader.ReadInt32() != bytes) return false;
                stream.Position = 54;
                var pixels = reader.ReadBytes(bytes);
                if (pixels.Length != bytes) return false;
                for (int i = 0; i < pixels.Length; i += 4)
                { byte red = pixels[i + 2]; pixels[i + 2] = pixels[i]; pixels[i] = red; }
                rgba = pixels;
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        public static void Write(string path, byte[] rgba, int size = Size)
        {
            if (size <= 0 || size > 2048 || rgba == null || rgba.Length != size * size * 4) throw new ArgumentException("Invalid portrait pixels");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = File.Create(temporary))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write((ushort)0x4d42); writer.Write(54 + rgba.Length);
                    writer.Write(0); writer.Write(54); writer.Write(40);
                    writer.Write(size); writer.Write(size); writer.Write((short)1); writer.Write((short)32);
                    writer.Write(0); writer.Write(rgba.Length); writer.Write(2835); writer.Write(2835);
                    writer.Write(0); writer.Write(0);
                    for (int i = 0; i < rgba.Length; i += 4)
                    { writer.Write(rgba[i + 2]); writer.Write(rgba[i + 1]); writer.Write(rgba[i]); writer.Write(rgba[i + 3]); }
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
