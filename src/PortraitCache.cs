using System;
using UnityEngine;

namespace Ror2Common
{
    /// Shared disk-cache boundary for generated character, skin and skill portraits.
    /// A hit must never call the factory: it can load native models and render cameras.
    public static class PortraitCache
    {
        // Independent of plugin releases. Bump only when these portraits change.
        public const string CharacterRevision = "character-v1";
        public const string MeleeSkillRevision = "melee-v1";

        public static Texture2D GetOrCreate(string path, string name, Func<Texture2D> generate,
            out bool cached, int size = SkillCardCache.Size)
        {
            cached = SkillCardCache.TryRead(path, out var rgba, size);
            if (cached)
            {
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                texture.LoadRawTextureData(rgba);
                texture.Apply(false, false);
                return texture;
            }
            var result = generate();
            // Assets may still be extracting. Leave the job queued and never cache a failure.
            if (!result || path == null) return result;
            try
            {
                if (result.width != size || result.height != size)
                    throw new ArgumentException("Portrait cache dimensions differ from the recipe");
                var pixels = result.GetPixels32();
                rgba = new byte[pixels.Length * 4];
                for (int i = 0; i < pixels.Length; i++)
                {
                    rgba[i*4] = pixels[i].r; rgba[i*4+1] = pixels[i].g;
                    rgba[i*4+2] = pixels[i].b; rgba[i*4+3] = pixels[i].a;
                }
                SkillCardCache.Write(path, rgba, size);
            }
            catch (Exception e) { CommonLog.Warn($"portrait cache {path}: {e.Message}"); }
            return result;
        }
    }
}
