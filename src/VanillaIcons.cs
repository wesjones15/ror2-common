using System;
using System.Collections.Generic;
using RoR2;
using RoR2.Skills;
using UnityEngine;

namespace Ror2Common
{
    // A mod skill wearing the icon of one RoR2 ships (Olimar's whistle as Captain's DIABLO
    // strike). The vanilla SkillDef is found by its asset name (`EngiBodyPlaceSpiderMine`,
    // `CallAirstrikeAlt`) once every catalog has loaded, so nothing is looked up by an
    // Addressables key; quarter turns clockwise are baked into a copy of the sprite.
    public static class VanillaIcons
    {
        struct Job
        {
            public string vanilla;
            public int quarterTurns;
            public Action<Sprite> set;
        }

        static readonly List<Job> pending = new List<Job>();
        static bool hooked;

        /// Fills an empty `def.icon` with the icon of RoR2's SkillDef named `vanilla`, turned `quarterTurnsCw`
        /// quarter turns clockwise. Call at content load.
        public static void Use(SkillDef def, string vanilla, int quarterTurnsCw = 0)
        {
            // Cached skill portraits can arrive before the catalogs finish loading.
            if (def) Request(vanilla, quarterTurnsCw, s => { if (def && !def.icon) def.icon = s; });
        }

        /// The same for `bodyPrefab`'s passive (the loadout's fifth row).
        public static void UsePassive(GameObject bodyPrefab, string vanilla, int quarterTurnsCw = 0)
        {
            var loc = bodyPrefab ? bodyPrefab.GetComponent<SkillLocator>() : null;
            if (loc) Request(vanilla, quarterTurnsCw, s => loc.passiveSkill.icon = s);
        }

        /// Calls `set` with RoR2's SkillDef `vanilla`'s icon once the catalogs have loaded.
        public static void Request(string vanilla, int quarterTurnsCw, Action<Sprite> set)
        {
            pending.Add(new Job { vanilla = vanilla, quarterTurns = ((quarterTurnsCw % 4) + 4) % 4, set = set });
            if (hooked) return;
            hooked = true;
            RoR2Application.onLoad += Apply;
        }

        static void Apply()
        {
            var byName = new Dictionary<string, SkillDef>();
            foreach (var def in SkillCatalog.allSkillDefs)
            {
                if (!def || !def.icon) continue;
                string asset = ((ScriptableObject)def).name;
                if (!string.IsNullOrEmpty(asset) && !byName.ContainsKey(asset)) byName[asset] = def;
                if (!string.IsNullOrEmpty(def.skillName) && !byName.ContainsKey(def.skillName)) byName[def.skillName] = def;
            }
            var turned = new Dictionary<(Sprite, int), Sprite>();
            foreach (var job in pending)
            {
                if (!byName.TryGetValue(job.vanilla, out var def))
                {
                    CommonLog.Warn($"VanillaIcons: no skill named {job.vanilla}; the icon stays as it was");
                    continue;
                }
                var icon = def.icon;
                if (job.quarterTurns != 0)
                {
                    if (!turned.TryGetValue((icon, job.quarterTurns), out var t))
                    {
                        try { t = Turn(icon, job.quarterTurns); }
                        catch (Exception e) { CommonLog.Warn($"VanillaIcons: couldn't turn {job.vanilla}'s icon ({e.Message})"); }
                        turned[(icon, job.quarterTurns)] = t;
                    }
                    if (t) icon = t;
                }
                try { job.set(icon); }
                catch (Exception e) { CommonLog.Error($"VanillaIcons: {job.vanilla}: {e}"); }
            }
            pending.Clear();
        }

        // The sprite's texels read back through a render texture (RoR2's are not readable),
        // then turned on the CPU.
        static Sprite Turn(Sprite s, int quarterTurnsCw)
        {
            var r = s.textureRect;
            int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
            var tex = s.texture;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Color32[] src;
            try
            {
                Graphics.Blit(tex, rt, new Vector2(r.width / tex.width, r.height / tex.height),
                    new Vector2(r.x / tex.width, r.y / tex.height));
                RenderTexture.active = rt;
                var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                src = read.GetPixels32();
                UnityEngine.Object.Destroy(read);
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
            bool swap = (quarterTurnsCw & 1) == 1;
            int ow = swap ? h : w, oh = swap ? w : h;
            var dst = new Color32[src.Length];
            // Rows run bottom to top. A clockwise quarter turn sends (x, y) to (y, w - 1 - x).
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int nx = x, ny = y;
                    for (int q = 0; q < quarterTurnsCw; q++)
                    {
                        int qw = (q & 1) == 0 ? w : h;
                        int tx = ny, ty = qw - 1 - nx;
                        nx = tx;
                        ny = ty;
                    }
                    dst[ny * ow + nx] = src[y * w + x];
                }
            var outTex = new Texture2D(ow, oh, TextureFormat.RGBA32, false)
            {
                name = tex.name + " turned",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = tex.filterMode,
            };
            outTex.SetPixels32(dst);
            outTex.Apply(false, true);
            return Sprite.Create(outTex, new Rect(0, 0, ow, oh), new Vector2(0.5f, 0.5f), s.pixelsPerUnit);
        }
    }
}
