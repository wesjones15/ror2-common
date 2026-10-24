using System;
using System.Collections.Generic;
using System.IO;
using RoR2.Skills;
using RoR2.UI;
using UnityEngine;

namespace Ror2Common
{
    /// One generated skill portrait's scene: the native models and effects already posed,
    /// the points they occupy, and how the camera should frame them. Built on demand after
    /// the disc cache is ready, rendered through an offscreen camera, then disposed.
    public sealed class SkillCardStage : IDisposable
    {
        public readonly Transform Root;

        /// Posed points in world metres (the model renderers are added automatically too).
        public readonly List<Vector3> Points = new List<Vector3>();

        /// Called before each of the two background renders; set effect object layers here.
        public Action<Camera, int> BeforeRender;

        /// Override the automatic framing. Radius is the half-size of the subject.
        public Vector3? Center;
        public float? Radius;
        public float Zoom = 1f;
        /// Camera orbit: 0 looks from +Z (the character's front, if it faces +Z).
        public float Yaw = 0.42f, Pitch = 0.2f;
        /// Camera distance as a multiple of the radius.
        public float Distance = 1.55f;
        public float FieldOfView = 30f;
        public Color Tint = Color.white;

        /// Releases the native views, effects and sims the builder made.
        public Action Disposing;

        public SkillCardStage(string name)
        {
            Root = new GameObject(name).transform;
            Root.position = Vector3.zero;
        }

        public void Dispose()
        {
            try { Disposing?.Invoke(); }
            catch (Exception e) { CommonLog.Warn($"skill card release: {e.Message}"); }
            if (Root) UnityEngine.Object.Destroy(Root.gameObject);
        }
    }

    /// Renders skill portraits from each plugin's own native view on first run. Nothing ships
    /// Nintendo art: the scene is built from the user's extracted disc cache, exactly as the
    /// Melee portraits are (SkillIcons). A job whose assets are still extracting stays queued
    /// and retries; when its texture is ready, every visible SkillIcon for that skill refreshes.
    public static class SkillCard
    {
        sealed class Job
        {
            public string defName;
            public Func<bool> ready;
            public Func<SkillCardStage> build;
            public Action<Sprite> assign;
            public Action refresh;
        }

        static readonly List<Job> pending = new List<Job>();
        static readonly HashSet<string> queued = new HashSet<string>();
        static Runner runner;

        /// Queues a portrait for `def`. `build` returns null if the disc files aren't usable
        /// yet; it will be called again on a later retry.
        public static void Request(SkillDef def, string defName, Func<bool> ready, Func<SkillCardStage> build)
        {
            if (!def || build == null || !queued.Add(defName)) return;
            pending.Add(new Job
            {
                defName = defName, ready = ready, build = build,
                assign = s => def.icon = s, refresh = () => Refresh(def),
            });
            EnsureRunner();
        }

        /// An icon with no SkillDef (a passive slot): the caller assigns the sprite itself.
        public static void Request(Action<Sprite> assign, string defName, Func<bool> ready, Func<SkillCardStage> build)
        {
            if (assign == null || build == null || !queued.Add(defName)) return;
            pending.Add(new Job
            {
                defName = defName, ready = ready, build = build, assign = assign,
            });
            EnsureRunner();
        }

        static void EnsureRunner()
        {
            if (runner) return;
            var go = new GameObject("Ror2CommonSkillCards");
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

            System.Collections.IEnumerator RenderSome()
            {
                rendering = true;
                // One portrait per frame: each builds its own native view and bakes textures.
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    var job = pending[i];
                    SkillCardStage stage = null;
                    try
                    {
                        if (job.ready != null && !job.ready()) continue;
                        stage = job.build();
                        if (stage == null) continue;
                        var tex = Render(stage);
                        if (!tex) continue;
                        var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                        job.assign(sprite);
                        Dump(tex, job.defName);
                        job.refresh?.Invoke();
                        pending.RemoveAt(i);
                        queued.Remove(job.defName);
                        CommonLog.Info($"skill portrait for {job.defName} ready");
                    }
                    catch (Exception e)
                    {
                        CommonLog.Error($"skill portrait for {job.defName}: {e}");
                        pending.RemoveAt(i);
                        queued.Remove(job.defName);
                    }
                    finally
                    {
                        stage?.Dispose();
                    }
                    break;
                }
                retryAt = Time.unscaledTime + 0.25f;
                rendering = false;
                yield return null;
            }
        }

        static Texture2D Render(SkillCardStage stage)
        {
            int layer = PortraitLayer();
            if (layer < 0 || !stage.Root) return null;
            var layers = new List<(GameObject, int)>();
            GameObject camGo = null, lightGo = null;
            RenderTexture rt = null;
            try
            {
                var points = new List<Vector3>(stage.Points);
                foreach (var mf in stage.Root.GetComponentsInChildren<MeshFilter>(true))
                {
                    var r = mf.GetComponent<Renderer>();
                    if (!mf.sharedMesh || !r || !r.enabled || !mf.gameObject.activeInHierarchy) continue;
                    var m = mf.transform.localToWorldMatrix;
                    foreach (var v in mf.sharedMesh.vertices) points.Add(m.MultiplyPoint3x4(v));
                }
                if (points.Count == 0) return null;

                Vector3 center;
                float radius;
                if (stage.Center.HasValue && stage.Radius.HasValue)
                {
                    center = stage.Center.Value;
                    radius = Mathf.Max(0.05f, stage.Radius.Value);
                }
                else
                {
                    var lo = points[0];
                    var hi = points[0];
                    foreach (var p in points) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
                    center = stage.Center ?? ((lo + hi) * 0.5f);
                    radius = stage.Radius ?? Mathf.Max(0.1f, (hi - lo).magnitude * 0.5f);
                }
                float zoom = stage.Zoom > 0f ? stage.Zoom : 1f;
                float distance = radius * Mathf.Max(1f, stage.Distance) / zoom;
                float yaw = stage.Yaw * Mathf.Deg2Rad;
                float pitch = Mathf.Clamp(stage.Pitch, -1.35f, 1.35f);
                var dir = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));
                var cameraAt = center + dir * distance;

                foreach (var t in stage.Root.GetComponentsInChildren<Transform>(true))
                {
                    layers.Add((t.gameObject, t.gameObject.layer));
                    t.gameObject.layer = layer;
                }

                camGo = new GameObject("skill card camera");
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.cullingMask = 1 << layer;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.fieldOfView = stage.FieldOfView;
                cam.nearClipPlane = Mathf.Max(0.01f, distance * 0.01f);
                cam.farClipPlane = distance * 6f + 10f;
                cam.allowHDR = false;
                cam.allowMSAA = false;
                cam.renderingPath = RenderingPath.DeferredShading;
                camGo.transform.position = cameraAt;
                camGo.transform.rotation = Quaternion.LookRotation(center - cameraAt, Vector3.up);

                lightGo = new GameObject("skill card light");
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.15f;
                light.color = new Color(1f, 0.97f, 0.92f);
                light.cullingMask = 1 << layer;
                lightGo.transform.rotation = Quaternion.LookRotation(-(center - cameraAt) + Vector3.down * 0.6f + Vector3.right * 0.4f);

                rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                Action before = () => stage.BeforeRender?.Invoke(cam, layer);
                var black = Grab(cam, rt, Color.black, before);
                var white = Grab(cam, rt, Color.white, before);
                var px = new Color[256 * 256];
                for (int y = 0; y < 256; y++)
                {
                    for (int x = 0; x < 256; x++)
                    {
                        int i = y * 256 + x;
                        var b = black[i];
                        var w = white[i];
                        float a = Mathf.Clamp01(1f - Mathf.Max(w.r - b.r, Mathf.Max(w.g - b.g, w.b - b.b)));
                        var fg = a > 0.004f ? new Color(Mathf.Clamp01(b.r / a), Mathf.Clamp01(b.g / a), Mathf.Clamp01(b.b / a), a) : Color.clear;
                        var bg = Backdrop(x, y, stage.Tint);
                        px[i] = new Color(fg.r * a + bg.r * (1f - a), fg.g * a + bg.g * (1f - a), fg.b * a + bg.b * (1f - a), 1f);
                    }
                }
                var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false)
                {
                    name = "skill portrait",
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
            }
        }

        // RoR2's skill icons are square. A full-bleed card with a soft centre and a thin
        // square frame, matching the Melee portraits.
        static Color Backdrop(int x, int y, Color tint)
        {
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

        static void Refresh(SkillDef def)
        {
            foreach (var icon in Resources.FindObjectsOfTypeAll<SkillIcon>())
            {
                if (!icon || !icon.targetSkill || icon.targetSkill.skillDef != def) continue;
                Bump(icon);
            }
        }

        static void Bump(SkillIcon icon)
        {
            icon.enabled = false;
            icon.enabled = true;
            icon.SendMessage("Update", SendMessageOptions.DontRequireReceiver);
        }

        // Local review copy of the actual Unity render; never packaged. 32-bit BMP so this
        // works without Unity's ImageConversion module. Beside the plugin that requested it.
        static void Dump(Texture2D tex, string name)
        {
            try
            {
                var px = tex.GetPixels32();
                int w = tex.width, h = tex.height;
                string dir = null;
                try
                {
                    string loc = typeof(SkillCard).Assembly.Location;
                    if (!string.IsNullOrEmpty(loc)) dir = Path.Combine(Path.GetDirectoryName(loc), "skill-icons");
                }
                catch { }
                if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.persistentDataPath, "SkillIcons");
                Directory.CreateDirectory(dir);
                using (var stream = new FileStream(Path.Combine(dir, name + ".bmp"), FileMode.Create))
                using (var bw = new BinaryWriter(stream))
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

        static int PortraitLayer()
        {
            int manual = LayerMask.NameToLayer("ManualRender");
            if (manual >= 0) return manual;
            for (int i = 31; i >= 8; i--)
                if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
            CommonLog.Warn("skill portrait skipped: no ManualRender or unused layer");
            return -1;
        }
    }
}
