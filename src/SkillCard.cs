using System;
using System.Collections.Generic;
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

        /// Called with the actual portrait camera; set native billboard axes and layers here.
        public Action<Camera, int> BeforeRender;

        /// Override the automatic framing. Radius is the half-size of the subject.
        public Vector3? Center;
        public float? Radius;
        public float Zoom = 1f;
        /// Camera orbit in radians: 0 looks from +Z (front for a +Z-facing character).
        public float Yaw = 0.42f, Pitch = 0.2f;
        /// Camera distance as a multiple of the radius.
        public float Distance = 1.55f;
        /// Absolute world distance, when matching an approved viewer composition.
        public float? CameraDistance;
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
            if (Root) Root.gameObject.SetActive(false);
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
            public Func<string> assetsDir;
            public string revision;
        }

        static readonly List<Job> pending = new List<Job>();
        static readonly HashSet<string> queued = new HashSet<string>();
        static Runner runner;

        /// Queues a portrait for `def`. `build` returns null if the disc files aren't usable
        /// yet; it will be called again on a later retry.
        public static void Request(SkillDef def, string defName, Func<bool> ready, Func<SkillCardStage> build,
            Func<string> assetsDir = null, string revision = SkillCardCache.Revision)
        {
            if (!def || build == null || !queued.Add(defName)) return;
            pending.Add(new Job
            {
                defName = defName, ready = ready, build = build,
                assign = s => def.icon = s, refresh = () => Refresh(def),
                assetsDir = assetsDir, revision = revision,
            });
            EnsureRunner();
        }

        /// An icon with no SkillDef (a passive slot): the caller assigns the sprite itself.
        public static void Request(Action<Sprite> assign, string defName, Func<bool> ready, Func<SkillCardStage> build,
            Func<string> assetsDir = null, string revision = SkillCardCache.Revision)
        {
            if (assign == null || build == null || !queued.Add(defName)) return;
            pending.Add(new Job
            {
                defName = defName, ready = ready, build = build, assign = assign,
                assetsDir = assetsDir, revision = revision,
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
                        string cache = SkillCardCache.FileName(job.assetsDir?.Invoke(), job.revision, job.defName);
                        Texture2D tex;
                        bool cached = SkillCardCache.TryRead(cache, out var rgba);
                        if (cached)
                        {
                            tex = new Texture2D(SkillCardCache.Size, SkillCardCache.Size, TextureFormat.RGBA32, false)
                            { name = job.defName, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                            tex.LoadRawTextureData(rgba); tex.Apply(false, false);
                        }
                        else
                        {
                            stage = job.build();
                            if (stage == null) continue;
                            tex = Render(stage);
                            if (tex && cache != null)
                            {
                                try
                                {
                                    var colors = tex.GetPixels32();
                                    rgba = new byte[colors.Length * 4];
                                    for (int p = 0; p < colors.Length; p++)
                                    { rgba[p*4] = colors[p].r; rgba[p*4+1] = colors[p].g; rgba[p*4+2] = colors[p].b; rgba[p*4+3] = colors[p].a; }
                                    SkillCardCache.Write(cache, rgba);
                                }
                                catch (Exception e) { CommonLog.Warn($"skill portrait cache {cache}: {e.Message}"); }
                            }
                        }
                        if (!tex) continue;
                        var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                        job.assign(sprite);
                        job.refresh?.Invoke();
                        pending.RemoveAt(i);
                        queued.Remove(job.defName);
                        CommonLog.Info($"skill portrait for {job.defName} {(cached ? "loaded" : "generated")}: {cache ?? "memory"}");
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
                float distance = stage.CameraDistance ?? (radius * Mathf.Max(1f, stage.Distance) / zoom);
                distance = Mathf.Max(0.05f, distance);
                float yaw = stage.Yaw;
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
                cam.depthTextureMode = DepthTextureMode.Depth;
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
                Action before = () => {
                    stage.BeforeRender?.Invoke(cam, layer);
                    // Reframing may create an effect group after the initial layer pass.
                    foreach (var t in stage.Root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                };
                // Render directly on the approved backdrop. Black/white alpha recovery
                // discards additive-only fire and cannot recover scene-refraction ribbons.
                Color[] px;
                before();
                using (new PortraitIsolation(stage.Root, layer))
                    px = Grab(cam, rt, stage.Tint);
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
                if (camGo) { camGo.SetActive(false); UnityEngine.Object.Destroy(camGo); }
                if (lightGo) { lightGo.SetActive(false); UnityEngine.Object.Destroy(lightGo); }
                if (rt) { rt.Release(); UnityEngine.Object.Destroy(rt); }
            }
        }

        static Color[] Grab(Camera cam, RenderTexture rt, Color background)
        {
            cam.backgroundColor = background;
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
