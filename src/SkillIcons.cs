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
    public enum SkillIconSource : byte { None, Effect, Article, ArticleCharacter, Ball, Character, EffectCharacter, Item, Model, ModelCharacter, Attack }

    /// One generated skill portrait's source. Effect is a source efSync/efAlt cue,
    /// which resolves to an effect model or a particle emitter; Article is a fighter
    /// projectile; Ball is the fighter's alternate model (Samus's Morph Ball); Model
    /// is a common effect model (the shield bubble). Character and EffectCharacter
    /// pose the fighter, with the effect placed at Offset in its native units.
    /// Attack runs the real fighter sim into a move and stills it (MeleeAttackStage):
    /// the pose, effects and articles all come from the sim, and `color` is its backdrop.
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
        public MeleeAttack attack;
        /// Frame the subject's largest extent with this margin (1.2: a fifth of room around it),
        /// instead of the default zoom framing. 0: off.
        public float fit;
        /// Fighters whose icons share a key render once and reuse the picture (the shield bubble).
        public string share;
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
            public GameObject body;
            public SkillDef def;
            public string defName;
            public uint kind;
            public Func<string> assetsDir;
            public Func<bool> ready;
            public MeleeSkillIcon icon;
        }

        static readonly List<Job> pending = new List<Job>();
        static readonly HashSet<string> queued = new HashSet<string>();
        static readonly Dictionary<string, Texture2D> shared = new Dictionary<string, Texture2D>();
        static Runner runner;
        // Generated art is cached beside the extracted files it is rendered from (SharedFiles assets/melee/skill-icons,
        // as Mario's and Olimar's are), never in the plugin's own folder, which a mod manager replaces on update.

        public static void Request(SkillDef def, string defName, uint kind, Func<string> assetsDir, Func<bool> ready) =>
            Request(def, defName, kind, assetsDir, ready, null);

        internal static void Request(SkillDef def, string defName, uint kind, Func<string> assetsDir, Func<bool> ready, GameObject body)
        {
            var icon = For(defName);
            if (!def || icon.source == SkillIconSource.None || !queued.Add(defName)) return;
            pending.Add(new Job { body = body, def = def, defName = defName, kind = kind, assetsDir = assetsDir, ready = ready, icon = icon });
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
                        var dir = job.assetsDir();
                        if (string.IsNullOrEmpty(dir)) continue;
                        Texture2D tex = null;
                        string share = job.icon.share;
                        if (share != null && shared.TryGetValue(share, out tex) && !tex) tex = null;
                        if (!tex)
                        {
                            string cache = SkillCardCache.FileName(dir,PortraitCache.MeleeSkillRevision, job.defName);
                            tex = PortraitCache.GetOrCreate(cache, job.defName,
                                () => job.ready() ? Render(job, dir) : null, out bool cached);
                            if (tex) CommonLog.Info($"Melee skill portrait {job.defName} {(cached ? "loaded" : "generated")}: {cache}");
                            if (tex && share != null) shared[share] = tex;
                        }
                        if (!tex) continue;
                        job.def.icon = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                        Refresh(job.def);
                        SkinPresentation.RefreshMenus(job.body);
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
            MeleeAttackStage attack = null;
            GameObject camGo = null;
            PortraitLight light = null;
            RenderTexture rt = null;
            var layers = new List<(GameObject, int)>();
            try
            {
                if (icon.source == SkillIconSource.Attack)
                {
                    attack = new MeleeAttackStage(assetsDir, job.kind, icon.attack);
                    attack.Run();
                }
                bool wantCharacter = icon.source == SkillIconSource.Character || icon.source == SkillIconSource.EffectCharacter || icon.source == SkillIconSource.ArticleCharacter || icon.source == SkillIconSource.ModelCharacter;
                bool wantEffect = icon.source == SkillIconSource.Effect || icon.source == SkillIconSource.EffectCharacter
                    || icon.source == SkillIconSource.Article || icon.source == SkillIconSource.ArticleCharacter || icon.source == SkillIconSource.Ball
                    || icon.source == SkillIconSource.Item || icon.source == SkillIconSource.Model || icon.source == SkillIconSource.ModelCharacter;

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
                    if (icon.glow) { view.BlackToGlow = icon.color; view.ForceAdditive = true; }
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
                else if (icon.source == SkillIconSource.Model || icon.source == SkillIconSource.ModelCharacter)
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
                float radius = 6f, extent = 0f;
                if (points.Count > 0)
                {
                    var lo = points[0]; var hi = points[0];
                    foreach (var p in points) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
                    center = (lo + hi) * 0.5f;
                    radius = Mathf.Max(1f, (hi - lo).magnitude * 0.5f);
                    extent = Mathf.Max(hi.x - lo.x, Mathf.Max(hi.y - lo.y, hi.z - lo.z));
                }
                float zoom = icon.zoom > 0f ? icon.zoom : 1f;
                float distance = radius * 1.55f / zoom;
                const float yaw = 0.42f;
                var cameraAt = center + new Vector3(Mathf.Sin(yaw) * distance, distance * 0.2f, Mathf.Cos(yaw) * distance);
                // The vertical field of view is 30 degrees: half the shown height is distance * tan(15).
                const float tanHalfFov = 0.26795f;
                if (attack != null)
                {
                    // Framing is part of the attack's description: look at its focus from its yaw/pitch.
                    var spec = icon.attack;
                    center = stage.position + spec.Focus + (spec.Follow ? attack.FighterPosition : Vector3.zero)
                        + (spec.FocusShot ? attack.ShotPosition : Vector3.zero);
                    distance = Mathf.Max(1f, spec.Half) / tanHalfFov;
                    float pitch = Mathf.Clamp(spec.Pitch, -1.2f, 1.2f);
                    cameraAt = center + new Vector3(Mathf.Sin(spec.Yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(spec.Yaw) * Mathf.Cos(pitch)) * distance;
                }
                else if (icon.fit > 0f && extent > 0f)
                {
                    distance = extent * 0.5f * icon.fit / tanHalfFov;
                    cameraAt = center + new Vector3(Mathf.Sin(yaw) * 0.98f, 0.2f, Mathf.Cos(yaw) * 0.98f).normalized * distance;
                }

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
                cam.depthTextureMode = DepthTextureMode.Depth;
                cam.renderingPath = RenderingPath.DeferredShading;
                camGo.transform.position = cameraAt;
                camGo.transform.rotation = Quaternion.LookRotation(center - cameraAt, Vector3.up);

                // The card's own studio (PortraitLight): key on the camera's side of the fighter, so the
                // side the player sees is the lit one; the scene's lights and ambient stay out of it.
                // Fighters are drawn FighterBrightness times brighter than the source's colour, so the
                // studio comes down by the same factor.
                light = new PortraitLight(layer, 1f / Mathf.Max(1f, MeleeFighterView.FighterBrightness));
                light.Aim(center, cameraAt);

                rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                Color[] px;
                if (attack != null)
                {
                    // Place the models once the camera exists (billboards face it), then render straight
                    // onto the backdrop colour: the black/white alpha recovery below cannot keep additive
                    // glows and exhaust, which this still is mostly made of.
                    GameSettings.CameraOverride = camGo.transform;
                    attack.Draw(stage.position);
                    foreach (var root in attack.Roots())
                        foreach (var t in root.GetComponentsInChildren<Transform>(true))
                        {
                            layers.Add((t.gameObject, t.gameObject.layer));
                            t.gameObject.layer = layer;
                        }
                    if (icon.attack.AutoCenter)
                    {
                        // The models are placed now: frame what they really cover.
                        Vector3? lo = null, hi = null;
                        foreach (var root in icon.attack.CenterOnHeld ? attack.HeldRoots() : attack.Roots())
                            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
                            {
                                var r = mf.GetComponent<Renderer>();
                                if (!mf.sharedMesh || !r || !r.enabled || !mf.gameObject.activeInHierarchy) continue;
                                var toWorld = mf.transform.localToWorldMatrix;
                                foreach (var v in mf.sharedMesh.vertices)
                                {
                                    var w = toWorld.MultiplyPoint3x4(v);
                                    lo = lo.HasValue ? Vector3.Min(lo.Value, w) : w;
                                    hi = hi.HasValue ? Vector3.Max(hi.Value, w) : w;
                                }
                            }
                        if (lo.HasValue)
                        {
                            var span = hi.Value - lo.Value;
                            center = (lo.Value + hi.Value) * 0.5f;
                            distance = Mathf.Max(Mathf.Max(1f, icon.attack.Half), Mathf.Max(span.x, span.y) * icon.attack.FitScale) / tanHalfFov;
                            float pitch = Mathf.Clamp(icon.attack.Pitch, -1.2f, 1.2f);
                            cameraAt = center + new Vector3(Mathf.Sin(icon.attack.Yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(icon.attack.Yaw) * Mathf.Cos(pitch)) * distance;
                            cam.farClipPlane = distance * 4f;
                            camGo.transform.position = cameraAt;
                            camGo.transform.rotation = Quaternion.LookRotation(center - cameraAt, Vector3.up);
                            light.Aim(center, cameraAt);
                        }
                    }
                    var placement = MeleeAttackStage.Placement(stage.position);
                    px = Grab(cam, rt, AttackBackdrop(icon.color), () => attack.Particles.Draw(cam, layer, placement));
                    for (int y = 0; y < 256; y++)
                        for (int x = 0; x < 256; x++)
                        {
                            int edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(255 - x, 255 - y));
                            if (edge < 3) px[y * 256 + x] = Color.Lerp(px[y * 256 + x], icon.color, 0.75f);
                        }
                }
                else
                {
                Action drawParticles = () =>
                {
                    if (particles != null) particles.Draw(cam, layer, Matrix4x4.identity);
                };
                var black = Grab(cam, rt, Color.black, drawParticles);
                var white = Grab(cam, rt, Color.white, drawParticles);
                px = new Color[black.Length];
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
                GameSettings.CameraOverride = null;
                foreach (var (go, l) in layers) if (go) go.layer = l;
                if (camGo) UnityEngine.Object.Destroy(camGo);
                light?.Dispose();
                if (rt) { rt.Release(); UnityEngine.Object.Destroy(rt); }
                particles?.Dispose();
                attack?.Dispose();
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
            // Parent it to the stage: the capture's finally only destroys the stage, so a root
            // tongue would survive every capture and hang in the live scene for good.
            go.transform.SetParent(stage, true);
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

        // The flat colour an attack still is rendered on: the soft centre of the other cards' backdrop.
        static Color AttackBackdrop(Color tint) => Color.Lerp(new Color(0.035f, 0.050f, 0.075f), tint, 0.42f);

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
        static readonly Color Purple = new Color(0.55f, 0.28f, 0.75f);

        static MeleeSkillIcon E(uint id, Color color, float zoom = 1f, float frame = 4f, uint trail = 0, int trailFrames = 20) =>
            new MeleeSkillIcon { source = SkillIconSource.Effect, id = id, color = color, zoom = zoom, frame = frame, trail = trail, trailFrames = trailFrames };
        static MeleeSkillIcon A(uint article, uint state, Color color, float zoom = 1f, float frame = 4f, uint trail = 0, int trailFrames = 20, Vector3 rotation = default, Vector3 trailOffset = default, bool glow = false) =>
            new MeleeSkillIcon { source = SkillIconSource.Article, id = article, state = state, color = color, zoom = zoom, frame = frame, trail = trail, trailFrames = trailFrames, rotation = rotation, trailOffset = trailOffset, glow = glow };
        static MeleeSkillIcon AC(uint article, uint state, string clip, float clipFrame, Color color, float zoom = 1f, float frame = 4f, Vector3 offset = default, Vector3 charOffset = default, uint trail = 0, int trailFrames = 20, Vector3 trailOffset = default, Vector3 rotation = default, bool glow = false) =>
            new MeleeSkillIcon { source = SkillIconSource.ArticleCharacter, id = article, state = state, clip = clip, clipFrame = clipFrame, color = color, zoom = zoom, frame = frame, offset = offset, charOffset = charOffset, trail = trail, trailFrames = trailFrames, trailOffset = trailOffset, rotation = rotation, glow = glow };
        static MeleeSkillIcon B(Color color, float zoom = 1f) =>
            new MeleeSkillIcon { source = SkillIconSource.Ball, color = color, zoom = zoom };
        static MeleeSkillIcon C(string clip, float clipFrame, Color color, float zoom = 1f, bool tongue = false, uint trail = 0, int trailFrames = 20, Vector3 trailOffset = default) =>
            new MeleeSkillIcon { source = SkillIconSource.Character, clip = clip, clipFrame = clipFrame, color = color, zoom = zoom, tongue = tongue, trail = trail, trailFrames = trailFrames, trailOffset = trailOffset };
        static MeleeSkillIcon EC(uint id, string clip, float clipFrame, Color color, float zoom = 1f, float frame = 4f, Vector3 offset = default, Vector3 charOffset = default) =>
            new MeleeSkillIcon { source = SkillIconSource.EffectCharacter, id = id, clip = clip, clipFrame = clipFrame, color = color, zoom = zoom, frame = frame, offset = offset, charOffset = charOffset };
        static MeleeSkillIcon M(uint model, Color color, float zoom = 1f, float frame = 4f) =>
            new MeleeSkillIcon { source = SkillIconSource.Model, id = model, color = color, zoom = zoom, frame = frame };
        static MeleeSkillIcon MC(uint model, string clip, float clipFrame, Color color, float zoom = 1f, float frame = 4f, Vector3 offset = default, Vector3 charOffset = default) =>
            new MeleeSkillIcon { source = SkillIconSource.ModelCharacter, id = model, clip = clip, clipFrame = clipFrame, color = color, zoom = zoom, frame = frame, offset = offset, charOffset = charOffset };
        static MeleeSkillIcon I(uint item, uint state, Color color, float zoom = 1f) =>
            new MeleeSkillIcon { source = SkillIconSource.Item, id = item, state = state, color = color, zoom = zoom };

        /// An attack still (MeleeAttackStage): the sim is driven into the move and stilled `Ticks` steps in.
        static MeleeSkillIcon X(MeleeAttack attack, Color background) =>
            new MeleeSkillIcon { source = SkillIconSource.Attack, attack = attack, color = background };

        /// Every fighter's Shield / Air Feint icon: the shield bubble alone (Yoshi's included), rendered
        /// once and shared.
        static MeleeSkillIcon Shield() =>
            new MeleeSkillIcon { source = SkillIconSource.Model, id = 14, color = Blue, zoom = 1f, frame = 4f, fit = 1.25f, share = "shield" };

        // Fox's Blaster releases its laser on the move's Hit event (Samus/Fox/FoxCombat.cs); it has no source cues.
        static MeleeAttackShot? FoxShot(MeleeNative.Event e) =>
            e.move_id == 20 ? new MeleeAttackShot { Article = 0, State = 0, Aimed = true } : (MeleeAttackShot?)null;

        // The other fighters' Hit-event articles (each fighter's Combat.cs). Luigi's fireball is article 0, thrown from his left hand (common part 23).
        static MeleeAttackShot? LuigiShot(MeleeNative.Event e) =>
            e.move_id == 20 ? new MeleeAttackShot { Article = 0, State = 0, Aimed = true, Bone = 23 } : (MeleeAttackShot?)null;

        // Pikachu's Thunder Jolt is article 1 (the ground jolt), a particle with no model of its own.
        static MeleeAttackShot? PikachuShot(MeleeNative.Event e) =>
            e.move_id == 20 ? new MeleeAttackShot { Article = 1, State = 0, Aimed = true, Trail = 7000, Offset = new Vector3(0f, 5f, 3f) } : (MeleeAttackShot?)null;
        // Fox Illusion's blue copy is the Illusion article (Fox article 2: a Fox-shaped model of the dash pose). Rather than
        // time the dash, three copies are set down behind him on the path, fading with distance.
        static MeleeAttackProp[] IllusionCopies()
        {
            var copies = new MeleeAttackProp[3];
            for (int i = 0; i < 3; i++)
                copies[i] = new MeleeAttackProp { Article = 2, State = 0, Position = new Vector3(0f, 0f, 10f - 6f * i), Frame = 2f, Tint = new Color(0.45f, 0.65f, 1f, 0.85f - 0.2f * i) };
            return copies;
        }

        // Thunder's bolt is four stacked article-0 quads of 40 units above Pikachu (Pikachu/PikachuCombat.cs ThunderBolt),
        // each a segment-delay (8 frames) behind the one above; `frame` is how far the top one has played.
        static MeleeAttackProp[] ThunderColumn(float frame)
        {
            var column = new MeleeAttackProp[4];
            for (int i = 0; i < 4; i++)
                column[i] = new MeleeAttackProp { Article = 0, State = 0, Position = new Vector3(0f, 20f + 40f * (3 - i), 0f), Frame = frame + 8f * i, Tint = new Color(1f, 0.92f, 0.3f, 1f), Glow = true, GlowColor = new Color(1f, 0.78f, 0.12f, 1f) };
            return column;
        }

        // Link's articles by host kind: Arrow 0, Boomerang 1, Bomb 2 (the carried bomb has no projectile).
        static MeleeAttackShot? LinkShot(MeleeNative.Event e)
        {
            switch (e.move_id)
            {
                case 20: return new MeleeAttackShot { Article = 0, State = 0, Charge = e.strength, Aimed = true };
                case 21: return new MeleeAttackShot { Article = 1, State = 0, Aimed = true };
                case 24: return new MeleeAttackShot { Article = 2, State = 0, Held = true, Bone = 25 };
                default: return null;
            }
        }

        // Yoshi's thrown egg: speed 4.5, lifted 22 degrees above the aim, thrown from his raised hand (common part 49).
        static MeleeAttackShot? YoshiShot(MeleeNative.Event e) =>
            e.move_id == 23 ? new MeleeAttackShot { Article = 0, State = 0, Charge = 4.5f, Aimed = true, Lift = 22f, Bone = 49 } : (MeleeAttackShot?)null;

        // The articles Samus's moves release on their Hit event (Samus/Combat.cs Attack).
        static MeleeAttackShot? SamusShot(MeleeNative.Event e)
        {
            switch (e.move_id)
            {
                case 20: return new MeleeAttackShot { Article = 1, State = (uint)Mathf.Clamp(Mathf.RoundToInt(e.strength * 7f) + 1, 1, 8), Charge = e.strength, Aimed = true, Cues = true };
                case 21: return new MeleeAttackShot { Article = 2, State = 0, Aimed = true, Cues = true };
                case 22: return new MeleeAttackShot { Article = 2, State = 1, Aimed = true, Cues = true };
                case 24: return new MeleeAttackShot { Article = 0, State = 0, Cues = true };
                default: return null;
            }
        }

        static MeleeSkillIcon For(string defName)
        {
            switch (defName)
            {
                // Samus: the attacks are stills of the real moves, aimed toward the camera and the right of the icon.
                // Ticks is how far into the move the still is taken (60 Hz steps from the press).
                // Charge Shot: held two seconds, nearly full; the ball sits on the cannon, Samus cropped by the frame.
                case "MeleeSamus0": return X(new MeleeAttack { Buttons = 256, Ticks = 120, ChargeArticle = 1, Shot = SamusShot, Focus = new Vector3(0f, 9.7f, 1.4f), Half = 7f }, Orange);
                // Morph Ball Bomb: the ball mid-roll with the dropped bomb just behind it, before it goes off.
                case "MeleeSamusBomb": return X(new MeleeAttack { Buttons = 2048, HostFlags = 4, Stick = new Vector3(0f, 0f, 1f), Ticks = 10, Shot = SamusShot, Focus = new Vector3(0f, 5.5f, 13.8f), Half = 6.8f }, Orange);
                // Missile: just launched, the arm still out, the missile and its exhaust coming at the camera.
                case "MeleeSamus2": return X(new MeleeAttack { Buttons = 512, Hold = 1, Ticks = 24, ShotAdvance = 2, Shot = SamusShot, Focus = new Vector3(0.5f, 14f, 11f), Half = 7f }, Red);
                case "MeleeShield": return Shield();
                // Screw Attack: rising mid-spin, the spin effect around her.
                case "MeleeSamus3": return X(new MeleeAttack { Buttons = 1024, Hold = 1, Ticks = 30, AutoCenter = true, FitScale = 0.3f, Half = 6f, HideModels = new uint[] { 3 } }, Green);
                // Fox
                // Blaster: the gun out, the muzzle flash and the red laser leaving its tip.
                case "MeleeFox0": return X(new MeleeAttack { Buttons = 256, Hold = 1, Ticks = 13, ShotAdvance = 1, Shot = FoxShot, Focus = new Vector3(1.5f, 9f, 5f), Half = 8f, Yaw = 1f }, Red);
                // Reflector: the dome and its core, the dust ring at the feet; the card keeps the old framing, zoomed out.
                case "MeleeFoxReflector": return X(new MeleeAttack { Buttons = 2048, Ticks = 8, Focus = new Vector3(0f, 7f, 0f), Half = 13f, Yaw = 0.42f, Pitch = 0.2f }, Blue);
                case "MeleeFoxShield": return Shield();
                // Illusion: the blue copies at the launch, coming at the camera, angled 45 degrees toward the right of the icon.
                case "MeleeFox2": return X(new MeleeAttack { Buttons = 512, Hold = 1, Ticks = 21, Follow = true, Focus = new Vector3(0f, 6f, -6f), Half = 9f, Yaw = 0.785f, Pitch = 0.1f, Props = IllusionCopies() }, Purple);
                // Fire Fox: launched, in the flame.
                case "MeleeFox3": return X(new MeleeAttack { Buttons = 1024, Hold = 1, Ticks = 47, Aim = new Vector3(0f, 0.5f, 1f), Focus = new Vector3(0f, 19f, 27.2f), Half = 12f }, Orange);
                // Luigi
                case "MeleeLuigi0": return X(new MeleeAttack { Buttons = 256, Hold = 1, Ticks = 19, Shot = LuigiShot, Follow = true, Focus = new Vector3(0f, 8f, 5f), Half = 9.5f }, Green);
                case "MeleeLuigiScope": return X(new MeleeAttack { Buttons = 256, Ticks = 56, Scope = true, DustEffect = 0x3FF, Focus = new Vector3(0f, 8f, 5f), Half = 7f }, Yellow);
                case "MeleeLuigiCyclone": return X(new MeleeAttack { Buttons = 2048, Ticks = 24, Focus = new Vector3(0f, 8f, 0f), Half = 9f }, Purple);
                case "MeleeLuigiShield": return Shield();
                case "MeleeLuigi2": return X(new MeleeAttack { Buttons = 512, Hold = 40, Ticks = 47, Follow = true, Focus = new Vector3(0f, 7f, 0f), Half = 10f }, Orange);
                case "MeleeLuigi3": return X(new MeleeAttack { Buttons = 1024, Hold = 1, Ticks = 10, Follow = true, Focus = new Vector3(0f, 8f, 0f), Half = 8f }, Red);
                case "MeleeLuigiScopeSpecial": return X(new MeleeAttack { Buttons = 256, Ticks = 56, Scope = true, DustEffect = 0x3FF, Focus = new Vector3(0f, 8f, 5f), Half = 7f }, Yellow);
                // Pikachu
                case "MeleePikachu0": return X(new MeleeAttack { Buttons = 256, Hold = 1, Ticks = 25, Shot = PikachuShot, FocusShot = true, Half = 5.5f }, Yellow);
                case "MeleePikachuEggLay": return X(new MeleeAttack { Buttons = 256, Hold = 1, HostFlags = 1, Ticks = 20, TongueBone = 16, TongueLength = 20f, Focus = new Vector3(-1f, 5f, 8f), Half = 13f }, Green);
                case "MeleePikachu2": return X(new MeleeAttack { Buttons = 2048, Hold = 1, Ticks = 45, Follow = true, Focus = new Vector3(0f, 22f, 0f), Half = 22f, Props = ThunderColumn(7f) }, Purple);
                case "MeleePikachuShield": return Shield();
                case "MeleePikachuSkullBash": return X(new MeleeAttack { Buttons = 512, Hold = 40, Ticks = 49, Follow = true, Focus = new Vector3(0f, 6f, 2f), Half = 14f }, Orange);
                case "MeleePikachu3": return X(new MeleeAttack { Buttons = 1024, Hold = 1, Ticks = 19, Follow = true, Focus = new Vector3(0f, 6f, 0f), Half = 10f }, Red);
                // Captain Falcon
                case "MeleeCaptain0": return X(new MeleeAttack { Buttons = 256, Hold = 1, Ticks = 36, Follow = true, Focus = new Vector3(0f, 10f, 2f), Half = 11f }, Red);
                case "MeleeCaptainKick": return X(new MeleeAttack { Buttons = 2048, Hold = 1, Ticks = 24, Follow = true, Focus = new Vector3(0f, 9f, 3f), Half = 8f }, Orange);
                case "MeleeCaptainShield": return Shield();
                case "MeleeCaptain2": return X(new MeleeAttack { Buttons = 512, Hold = 1, Ticks = 22, Follow = true, Focus = new Vector3(0f, 9f, 0f), Half = 13f }, Blue);
                case "MeleeCaptain3": return X(new MeleeAttack { Buttons = 1024, Hold = 1, Ticks = 19, Follow = true, Focus = new Vector3(0f, 9f, 2f), Half = 15f }, Blue);
                // Link
                case "MeleeLink0": return X(new MeleeAttack { Buttons = 256, Hold = 60, Ticks = 53, Shot = LinkShot, AutoCenter = true, Half = 9f, Yaw = -0.5f }, Green);
                case "MeleeLinkBomb": return X(new MeleeAttack { Buttons = 2048, Hold = 1, Ticks = 20, Shot = LinkShot, FocusShot = true, Half = 9f, Yaw = -0.5f }, Red);
                case "MeleeLinkShield": return Shield();
                case "MeleeLink2": return X(new MeleeAttack { Buttons = 512, Hold = 1, Ticks = 24, Shot = LinkShot, FocusShot = true, Half = 8f }, Yellow);
                case "MeleeLink3": return X(new MeleeAttack { Buttons = 1024, Hold = 1, Ticks = 12, Follow = true, Focus = new Vector3(0f, 9f, 2f), Half = 15f }, Purple);
                // Yoshi
                case "MeleeYoshi0": return X(new MeleeAttack { Buttons = 256, Hold = 1, Ticks = 19, Focus = new Vector3(0f, 8f, 12f), Half = 10f }, Green);
                case "MeleeYoshiBomb": return X(new MeleeAttack { Buttons = 2048, Hold = 1, Ticks = 34, Follow = true, Focus = new Vector3(0f, 6f, 2f), Half = 9f }, Orange);
                case "MeleeYoshiShield": return Shield();
                case "MeleeYoshi2": return X(new MeleeAttack { Buttons = 512, Hold = 30, Ticks = 30, Follow = true, Focus = new Vector3(0f, 6f, 0f), Half = 9.5f }, Yellow);
                case "MeleeYoshi3": return X(new MeleeAttack { Buttons = 1024, Hold = 1, Ticks = 6, Shot = YoshiShot, Follow = true, Focus = new Vector3(0f, 7f, 0f), Half = 11f }, Purple);
                // Donkey Kong
                case "MeleeDonkey0": return X(new MeleeAttack { Buttons = 256, Hold = 30, Ticks = 50, Follow = true, Focus = new Vector3(0f, 11f, 8f), Half = 17f }, Red);
                case "MeleeDonkeySlap": return X(new MeleeAttack { Buttons = 2048, Hold = 20, Ticks = 24, Follow = true, Focus = new Vector3(0f, 10f, 6f), Half = 17f }, Orange);
                case "MeleeDonkeyShield": return Shield();
                case "MeleeDonkey2": return X(new MeleeAttack { Buttons = 512, Hold = 1, Ticks = 21, Follow = true, Focus = new Vector3(0f, 12f, 10f), Half = 11.5f }, Purple);
                case "MeleeDonkey3": return X(new MeleeAttack { Buttons = 1024, Hold = 1, Ticks = 42, Follow = true, Focus = new Vector3(0f, 10f, 3f), Half = 19f }, Green);
                default: return default;
            }
        }
    }
}
