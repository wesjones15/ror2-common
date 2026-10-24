using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using RoR2;
using RoR2.Skills;
using RoR2.UI;
using UnityEngine;

namespace Ror2Common
{
    public enum SkillIconSource : byte { None, Effect, Article, ArticleCharacter, Ball, Character, EffectCharacter, Item, Model }

    /// One generated skill portrait's source. Effect is a source efSync/efAlt cue,
    /// which resolves to an effect model or a particle emitter; Article is a fighter
    /// projectile; Ball is the fighter's alternate model (Samus's Morph Ball); Model
    /// is a common effect model (the shield bubble). Character and EffectCharacter
    /// pose the fighter, with the effect placed at Offset in its native units.
    public struct MeleeSkillIcon
    {
        public SkillIconSource source;
        public uint id, state, trail;
        public string clip;
        public float frame, clipFrame, zoom;
        public int trailFrames;
        public Vector3 offset, charOffset, rotation, trailOffset;
        public bool glow, tongue;
        public Color color;
    }

    /// Renders the Melee skill portraits from the player's extracted disc cache and
    /// assigns them to SkillDef.icon. Nothing here ships Nintendo art: the cache is
    /// the same one the fighters already require. The queue waits for a fighter's
    /// extraction to finish, retries on scene changes, and refreshes SkillIcon
    /// components that were built before the texture was ready.
    public static class SkillIcons
    {
        sealed class Job
        {
            public SkillDef def;
            public string defName;
            public uint kind;
            public Func<string> assetsDir;
            public Func<bool> ready;
            public MeleeSkillIcon icon;
        }

        static readonly List<Job> pending = new List<Job>();
        static readonly HashSet<string> queued = new HashSet<string>();
        static Runner runner;

        public static void Request(SkillDef def, string defName, uint kind, Func<string> assetsDir, Func<bool> ready)
        {
            var icon = For(defName);
            if (!def || icon.source == SkillIconSource.None || !queued.Add(defName)) return;
            pending.Add(new Job { def = def, defName = defName, kind = kind, assetsDir = assetsDir, ready = ready, icon = icon });
            EnsureRunner();
        }

        static void EnsureRunner()
        {
            if (runner) return;
            var go = new GameObject("Ror2CommonSkillIcons");
            UnityEngine.Object.DontDestroyOnLoad(go);
            runner = go.AddComponent<Runner>();
        }

        sealed class Runner : MonoBehaviour
        {
            bool rendering;
            float retryAt;

            void Update()
            {
                if (pending.Count > 0 && !rendering && Time.unscaledTime >= retryAt)
                    StartCoroutine(RenderSome());
            }

            IEnumerator RenderSome()
            {
                rendering = true;
                for (int i = pending.Count - 1, done = 0; i >= 0 && done < 2; i--)
                {
                    var job = pending[i];
                    try
                    {
                        if (!job.ready()) continue;
                        var dir = job.assetsDir();
                        if (string.IsNullOrEmpty(dir)) continue;
                        var tex = Render(job, dir);
                        if (!tex) continue;
                        job.def.icon = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                        Dump(tex, job.defName);
                        Refresh(job.def);
                        pending.RemoveAt(i);
                        queued.Remove(job.defName);
                        done++;
                        CommonLog.Info($"skill portrait for {job.defName} ready");
                    }
                    catch (Exception e)
                    {
                        CommonLog.Error($"skill portrait for {job.defName}: {e}");
                        pending.RemoveAt(i);
                        queued.Remove(job.defName);
                    }
                    yield return null;
                }
                retryAt = Time.unscaledTime + 0.25f;
                rendering = false;
            }
        }

        // Local review copy of the actual Unity render; never packaged.
        // 32-bit BMP so this works without Unity's ImageConversion module.
        static void Dump(Texture2D tex, string name)
        {
            try
            {
                var px = tex.GetPixels32();
                int w = tex.width, h = tex.height;
                string dir = System.IO.Path.Combine(Application.persistentDataPath, "MeleeSkillIcons");
                System.IO.Directory.CreateDirectory(dir);
                using (var stream = new System.IO.FileStream(System.IO.Path.Combine(dir, name + ".bmp"), System.IO.FileMode.Create))
                using (var bw = new System.IO.BinaryWriter(stream))
                {
                    bw.Write((byte)'B'); bw.Write((byte)'M');
                    bw.Write(54 + w * h * 4); bw.Write(0); bw.Write(54);
                    bw.Write(40); bw.Write(w); bw.Write(h); bw.Write((short)1); bw.Write((short)32);
                    bw.Write(0); bw.Write(w * h * 4); bw.Write(2835); bw.Write(2835); bw.Write(0); bw.Write(0);
                    foreach (var c in px) { bw.Write(c.b); bw.Write(c.g); bw.Write(c.r); bw.Write(c.a); }
                }
            }
            catch (Exception e) { CommonLog.Warn($"skill portrait dump {name}: {e.Message}"); }
        }

        static void Refresh(SkillDef def)
        {
            foreach (var icon in Resources.FindObjectsOfTypeAll<SkillIcon>())
            {
                if (!icon || !icon.targetSkill || icon.targetSkill.skillDef != def) continue;
                icon.enabled = false;
                icon.enabled = true;
                icon.SendMessage("Update", SendMessageOptions.DontRequireReceiver);
            }
        }

        static Texture2D Render(Job job, string assetsDir)
        {
            int layer = PortraitLayer();
            if (layer < 0) return null;
            var icon = job.icon;
            var stage = new GameObject("skill icon stage").transform;
            stage.position = new Vector3(0f, -2000f, 0f);
            var views = new List<MeleeFighterView>();
            var materials = new List<Material>();
            MeleeParticles particles = null;
            GameObject camGo = null, lightGo = null;
            RenderTexture rt = null;
            var layers = new List<(GameObject, int)>();
            try
            {
                bool wantCharacter = icon.source == SkillIconSource.Character || icon.source == SkillIconSource.EffectCharacter || icon.source == SkillIconSource.ArticleCharacter;
                bool wantEffect = icon.source == SkillIconSource.Effect || icon.source == SkillIconSource.EffectCharacter
                    || icon.source == SkillIconSource.Article || icon.source == SkillIconSource.ArticleCharacter || icon.source == SkillIconSource.Ball
                    || icon.source == SkillIconSource.Item || icon.source == SkillIconSource.Model;

                Vector3 at = stage.position + icon.offset;
                var rot = Quaternion.Euler(icon.rotation);
                if (wantCharacter)
                {
                    var view = new MeleeFighterView(assetsDir, job.kind, 0);
                    views.Add(view);
                    try { view.ClipPose(icon.clip, icon.clipFrame); }
                    catch { view.IdlePose(); }
                    view.Models.Update(1f, d => (stage.position + icon.charOffset + new Vector3(d.pos.x, d.pos.y, d.pos.z),
                        stage.rotation * new Quaternion(d.rot_x, d.rot_y, d.rot_z, d.rot_w)));
                    if (icon.tongue) AddTongue(stage, layer, icon, job.kind, layers, materials);
                }
                if (icon.source == SkillIconSource.Article)
                {
                    var view = new MeleeFighterView(assetsDir, job.kind, icon.id, icon.state);
                    views.Add(view);
                    view.ObjectPose(icon.frame);
                    if (icon.glow) { view.BlackToGlow = icon.color; view.ForceAdditive = true; }
                    view.Models.Update(1f, d => (at + rot * new Vector3(d.pos.x, d.pos.y, d.pos.z),
                        stage.rotation * rot * new Quaternion(d.rot_x, d.rot_y, d.rot_z, d.rot_w)));
                }
                else if (icon.source == SkillIconSource.ArticleCharacter)
                {
                    var view = new MeleeFighterView(assetsDir, job.kind, icon.id, icon.state);
                    views.Add(view);
                    view.ObjectPose(icon.frame);
                    view.Models.Update(1f, d => (at + rot * new Vector3(d.pos.x, d.pos.y, d.pos.z),
                        stage.rotation * rot * new Quaternion(d.rot_x, d.rot_y, d.rot_z, d.rot_w)));
                }
                else if (icon.source == SkillIconSource.Ball)
                {
                    var view = MeleeFighterView.FighterChoice(assetsDir, job.kind, 0, 1);
                    views.Add(view);
                    Pose(view, "Wait", 0f);
                    view.Models.Update(1f, d => (at + rot * new Vector3(d.pos.x, d.pos.y, d.pos.z),
                        stage.rotation * rot * new Quaternion(d.rot_x, d.rot_y, d.rot_z, d.rot_w)));
                }
                else if (icon.source == SkillIconSource.Item)
                {
                    var view = MeleeFighterView.Item(assetsDir, icon.id, icon.state);
                    views.Add(view);
                    view.ObjectPose(icon.frame);
                    view.Models.Update(1f, d => (at + rot * new Vector3(d.pos.x, d.pos.y, d.pos.z),
                        stage.rotation * rot * new Quaternion(d.rot_x, d.rot_y, d.rot_z, d.rot_w)));
                }
                else if (icon.source == SkillIconSource.Model)
                {
                    var view = MeleeFighterView.Effect(assetsDir, job.kind, icon.id);
                    views.Add(view);
                    view.ObjectPose(icon.frame);
                    view.Models.Update(1f, d => (at + rot * new Vector3(d.pos.x, d.pos.y, d.pos.z),
                        stage.rotation * rot * new Quaternion(d.rot_x, d.rot_y, d.rot_z, d.rot_w)));
                }
                else if (icon.source == SkillIconSource.Effect || icon.source == SkillIconSource.EffectCharacter)
                {
                    int target = MeleeNative.ssbm_effect_target(icon.id);
                    if (target < 0 && target > int.MinValue + 1000)
                    {
                        var view = MeleeFighterView.Effect(assetsDir, job.kind, (uint)(-target - 1));
                        views.Add(view);
                        view.ObjectPose(icon.frame);
                        view.Models.Update(1f, d => (at + rot * new Vector3(d.pos.x, d.pos.y, d.pos.z),
                            stage.rotation * rot * new Quaternion(d.rot_x, d.rot_y, d.rot_z, d.rot_w)));
                    }
                    else if (target >= 0)
                    {
                        particles = new MeleeParticles(assetsDir, 1f);
                        particles.Spawn((uint)target, MeleeParticles.At(at + icon.trailOffset, Quaternion.identity, 1f), 1);
                        for (int i = 0; i < Mathf.Max(1, icon.trailFrames); i++) particles.Step(1f / 60f);
                    }
                }
                // A trail on a projectile (the missile's exhaust, the jolt's arc).
                if (icon.trail != 0 && particles == null)
                {
                    particles = new MeleeParticles(assetsDir, 1f);
                    particles.Spawn(icon.trail, MeleeParticles.At(at + icon.trailOffset, Quaternion.identity, 1f), 1);
                    for (int i = 0; i < Mathf.Max(1, icon.trailFrames); i++) particles.Step(1f / 60f);
                }

                // The posed vertices themselves; mesh bounds are padded for animation.
                var points = new List<Vector3>();
                foreach (var view in views)
                {
                    if (!view.Models.Root) continue;
                    foreach (var mf in view.Models.Root.GetComponentsInChildren<MeshFilter>())
                    {
                        var r = mf.GetComponent<Renderer>();
                        if (!mf.sharedMesh || !r || !r.enabled || !mf.gameObject.activeInHierarchy) continue;
                        var m = mf.transform.localToWorldMatrix;
                        foreach (var v in mf.sharedMesh.vertices) points.Add(m.MultiplyPoint3x4(v));
                    }
                }
                Vector3 center = at;
                float radius = 6f;
                if (points.Count > 0)
                {
                    var lo = points[0]; var hi = points[0];
                    foreach (var p in points) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
                    center = (lo + hi) * 0.5f;
                    radius = Mathf.Max(1f, (hi - lo).magnitude * 0.5f);
                }
                float zoom = icon.zoom > 0f ? icon.zoom : 1f;
                float distance = radius * 1.55f / zoom;
                const float yaw = 0.42f;
                var cameraAt = center + new Vector3(Mathf.Sin(yaw) * distance, distance * 0.2f, Mathf.Cos(yaw) * distance);

                foreach (var view in views)
                {
                    if (!view.Models.Root) continue;
                    foreach (var t in view.Models.Root.GetComponentsInChildren<Transform>(true))
                    {
                        layers.Add((t.gameObject, t.gameObject.layer));
                        t.gameObject.layer = layer;
                    }
                }

                camGo = new GameObject("skill icon camera");
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.cullingMask = 1 << layer;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.fieldOfView = 30f;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = distance * 4f;
                cam.allowHDR = false;
                cam.allowMSAA = false;
                cam.renderingPath = RenderingPath.DeferredShading;
                camGo.transform.position = cameraAt;
                camGo.transform.rotation = Quaternion.LookRotation(center - cameraAt, Vector3.up);

                lightGo = new GameObject("skill icon light");
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.15f;
                light.color = new Color(1f, 0.97f, 0.92f);
                light.cullingMask = 1 << layer;
                lightGo.transform.rotation = Quaternion.LookRotation(-(center - cameraAt) + Vector3.down * 0.6f + Vector3.right * 0.4f);

                rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                Action drawParticles = () =>
                {
                    if (particles != null) particles.Draw(cam, layer, Matrix4x4.identity);
                };
                var black = Grab(cam, rt, Color.black, drawParticles);
                var white = Grab(cam, rt, Color.white, drawParticles);
                var px = new Color[black.Length];
                for (int y = 0; y < 256; y++)
                {
                    for (int x = 0; x < 256; x++)
                    {
                        int i = y * 256 + x;
                        var b = black[i]; var w = white[i];
                        float a = Mathf.Clamp01(1f - Mathf.Max(w.r - b.r, Mathf.Max(w.g - b.g, w.b - b.b)));
                        var fg = a > 0.004f ? new Color(Mathf.Clamp01(b.r / a), Mathf.Clamp01(b.g / a), Mathf.Clamp01(b.b / a), a) : Color.clear;
                        var bg = Backdrop(x, y, icon.color);
                        px[i] = new Color(fg.r * a + bg.r * (1f - a), fg.g * a + bg.g * (1f - a), fg.b * a + bg.b * (1f - a), 1f);
                    }
                }
                var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false)
                {
                    name = job.defName + " skill portrait",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                tex.SetPixels(px);
                tex.Apply(false, false);
                return tex;
            }
            finally
            {
                foreach (var (go, l) in layers) if (go) go.layer = l;
                if (camGo) UnityEngine.Object.Destroy(camGo);
                if (lightGo) UnityEngine.Object.Destroy(lightGo);
                if (rt) { rt.Release(); UnityEngine.Object.Destroy(rt); }
                particles?.Dispose();
                foreach (var view in views) view.Dispose();
                UnityEngine.Object.Destroy(stage.gameObject);
            }
        }

        static void AddTongue(Transform stage, int layer, MeleeSkillIcon icon, uint kind, List<(GameObject, int)> layers, List<Material> materials)
        {
            bool yoshi = kind == MeleeNative.FighterYoshi;
            float len = yoshi ? 4.5f : 3.2f;
            Vector3 mouth = yoshi ? new Vector3(0f, 9f, 3f) : new Vector3(0f, 7f, 2.5f);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "skill icon tongue";
            go.transform.position = stage.position + icon.charOffset + mouth + new Vector3(0f, 0f, len * 0.5f);
            go.transform.localScale = new Vector3(0.28f, 0.28f, len);
            go.layer = layer;
            var collider = go.GetComponent<Collider>(); if (collider) UnityEngine.Object.Destroy(collider);
            var mat = new Material(Shaders.HgStandard()) { name = "skill icon tongue" };
            mat.SetColor("_Color", new Color(0.85f, 0.10f, 0.10f));
            var renderer = go.GetComponent<Renderer>(); if (renderer) renderer.sharedMaterial = mat;
            materials.Add(mat);
            layers.Add((go, 0));
        }

        static void Pose(MeleeFighterView view, string clip, float frame)
        {
            try { view.ClipPose(clip, frame); return; } catch { }
            try { view.IdlePose(); } catch { }
        }

        static Color Backdrop(int x, int y, Color tint)
        {
            // RoR2's skill icons are square. A full-bleed card with a soft
            // centre and a thin square frame, never a circular mask.
            float dx = (x + 0.5f) / 256f * 2f - 1f;
            float dy = (y + 0.5f) / 256f * 2f - 1f;
            float core = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) * 0.72f);
            var dark = new Color(0.035f, 0.050f, 0.075f);
            var c = Color.Lerp(dark, tint, 0.18f + 0.58f * core);
            int edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(255 - x, 255 - y));
            if (edge < 3) c = Color.Lerp(c, tint, 0.75f);
            return c;
        }

        static Color[] Grab(Camera cam, RenderTexture rt, Color background, Action before)
        {
            cam.backgroundColor = background;
            before?.Invoke();
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var read = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
            read.Apply(false, false);
            RenderTexture.active = prev;
            var px = read.GetPixels();
            UnityEngine.Object.Destroy(read);
            return px;
        }

        static int PortraitLayer()
        {
            int manual = LayerMask.NameToLayer("ManualRender");
            if (manual >= 0) return manual;
            for (int i = 31; i >= 8; i--)
                if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
            CommonLog.Warn("skill portrait skipped: no ManualRender or unused layer");
            return -1;
        }

        // ---- Per-skill source table -------------------------------------------------
        static readonly Color Orange = new Color(0.95f, 0.45f, 0.10f);
        static readonly Color Yellow = new Color(0.95f, 0.78f, 0.18f);
        static readonly Color Green = new Color(0.30f, 0.70f, 0.35f);
        static readonly Color Blue = new Color(0.25f, 0.55f, 0.95f);
        static readonly Color Red = new Color(0.90f, 0.25f, 0.20f);
        static readonly Color Brown = new Color(0.65f, 0.42f, 0.22f);

        static MeleeSkillIcon E(uint id, Color color, float zoom = 1f, float frame = 4f, uint trail = 0, int trailFrames = 20) =>
            new MeleeSkillIcon { source = SkillIconSource.Effect, id = id, color = color, zoom = zoom, frame = frame, trail = trail, trailFrames = trailFrames };
        static MeleeSkillIcon A(uint article, uint state, Color color, float zoom = 1f, float frame = 4f, uint trail = 0, int trailFrames = 20, Vector3 rotation = default, Vector3 trailOffset = default, bool glow = false) =>
            new MeleeSkillIcon { source = SkillIconSource.Article, id = article, state = state, color = color, zoom = zoom, frame = frame, trail = trail, trailFrames = trailFrames, rotation = rotation, trailOffset = trailOffset, glow = glow };
        static MeleeSkillIcon AC(uint article, uint state, string clip, float clipFrame, Color color, float zoom = 1f, float frame = 4f, Vector3 offset = default, Vector3 charOffset = default) =>
            new MeleeSkillIcon { source = SkillIconSource.ArticleCharacter, id = article, state = state, clip = clip, clipFrame = clipFrame, color = color, zoom = zoom, frame = frame, offset = offset, charOffset = charOffset };
        static MeleeSkillIcon B(Color color, float zoom = 1f) =>
            new MeleeSkillIcon { source = SkillIconSource.Ball, color = color, zoom = zoom };
        static MeleeSkillIcon C(string clip, float clipFrame, Color color, float zoom = 1f, bool tongue = false, uint trail = 0, int trailFrames = 20, Vector3 trailOffset = default) =>
            new MeleeSkillIcon { source = SkillIconSource.Character, clip = clip, clipFrame = clipFrame, color = color, zoom = zoom, tongue = tongue, trail = trail, trailFrames = trailFrames, trailOffset = trailOffset };
        static MeleeSkillIcon EC(uint id, string clip, float clipFrame, Color color, float zoom = 1f, float frame = 4f, Vector3 offset = default, Vector3 charOffset = default) =>
            new MeleeSkillIcon { source = SkillIconSource.EffectCharacter, id = id, clip = clip, clipFrame = clipFrame, color = color, zoom = zoom, frame = frame, offset = offset, charOffset = charOffset };
        static MeleeSkillIcon M(uint model, Color color, float zoom = 1f, float frame = 4f) =>
            new MeleeSkillIcon { source = SkillIconSource.Model, id = model, color = color, zoom = zoom, frame = frame };
        static MeleeSkillIcon I(uint item, uint state, Color color, float zoom = 1f) =>
            new MeleeSkillIcon { source = SkillIconSource.Item, id = item, state = state, color = color, zoom = zoom };

        static MeleeSkillIcon For(string defName)
        {
            switch (defName)
            {
                // Samus
                case "MeleeSamus0": return A(1, 0, Orange, 1.5f, 4);
                case "MeleeSamusBomb": return B(Orange);
                case "MeleeShield": return M(14, Blue, 1.2f, 4);
                case "MeleeSamus2": return A(2, 0, Red, 1.4f, 4, 2014, 12, new Vector3(0f, -1.45f, 0.22f), new Vector3(0f, 0f, -6f));
                case "MeleeSamus3": return EC(1154, "SpecialHi", 10, Green, 1f, 16, new Vector3(0f, 6f, 0f), new Vector3(0f, 8f, 0f));
                // Fox
                case "MeleeFox0": return A(0, 0, Red, 2.0f, 4);
                case "MeleeFoxReflector": return E(1160, Blue, 1.1f, 4);
                case "MeleeFoxShield": return M(14, Blue, 1.2f, 4);
                case "MeleeFox2": return A(2, 0, Blue, 1f, 4);
                case "MeleeFox3": return E(1163, Orange, 1f, 4);
                // Luigi
                case "MeleeLuigi0": return AC(0, 0, "SpecialN", 4, Green, 1.4f, 4, new Vector3(0f, 6f, 4f));
                case "MeleeLuigiScope": return I(0x15, 0, Green, 1.1f);
                case "MeleeLuigiCyclone": return E(1289, Green, 1f, 4);
                case "MeleeLuigiShield": return M(14, Blue, 1.2f, 4);
                case "MeleeLuigi2": return C("SpecialS", 8, Green, 0.9f, false, 66, 3, new Vector3(0f, 0f, 1f));
                case "MeleeLuigi3": return C("SpecialHi", 6, Red);
                case "MeleeLuigiScopeSpecial": return I(0x15, 0, Green, 1.1f);
                // Pikachu
                case "MeleePikachu0": return A(1, 0, Yellow, 1f, 4, 7000, 10);
                case "MeleePikachuEggLay": return C("SpecialLwStart", 15, Green, 1.7f, true);
                case "MeleePikachu2": return A(0, 0, Yellow, 1.2f, 4, 0, 20, default, default, true);
                case "MeleePikachuShield": return M(14, Blue, 1.2f, 4);
                case "MeleePikachuSkullBash": return C("SpecialS", 8, Yellow, 0.9f, false, 66, 3, new Vector3(0f, 0f, 1f));
                case "MeleePikachu3": return EC(1012, "SpecialHiStart", 4, Yellow, 0.9f, 4, new Vector3(0f, 6f, 0f));
                // Captain Falcon
                case "MeleeCaptain0": return E(1167, Red, 1f, 6);
                case "MeleeCaptainKick": return E(1169, Orange, 1f, 6);
                case "MeleeCaptainShield": return M(14, Blue, 1.2f, 4);
                case "MeleeCaptain2": return E(1170, Blue, 1f, 6);
                case "MeleeCaptain3": return E(1168, Blue, 1f, 6);
                // Link
                case "MeleeLink0": return A(0, 0, Green, 1f, 4);
                case "MeleeLinkBomb": return A(2, 0, Green, 1f, 4);
                case "MeleeLinkShield": return M(14, Blue, 1.2f, 4);
                case "MeleeLink2": return A(1, 0, Green, 1f, 4);
                case "MeleeLink3": return EC(1211, "SpecialHi", 8, Green, 1.2f, 4, new Vector3(0f, 6f, 0f));
                // Yoshi
                case "MeleeYoshi0": return C("SpecialN1", 14, Green, 1.7f, true);
                case "MeleeYoshiBomb": return A(1, 0, Green, 1f, 4);
                case "MeleeYoshiShield": return M(14, Blue, 1.2f, 4);
                case "MeleeYoshi2": return E(1230, Green, 1f, 4);
                case "MeleeYoshi3": return A(0, 0, Green, 1f, 4);
                // Donkey Kong
                case "MeleeDonkey0": return E(1224, Brown, 1f, 4);
                case "MeleeDonkeySlap": return E(1222, Brown, 1f, 4);
                case "MeleeDonkeyShield": return M(14, Blue, 1.2f, 4);
                case "MeleeDonkey2": return C("SpecialS", 6, Brown);
                case "MeleeDonkey3": return E(1226, Brown, 1f, 4);
                default: return default;
            }
        }
    }
}
