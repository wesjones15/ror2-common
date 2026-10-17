using System;
using System.Collections;
using System.Collections.Generic;
using RoR2;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ror2Common
{
    // A survivor's portrait (CharacterBody.portraitIcon: the character select grid, ally cards,
    // the scoreboard) rendered from the plugin's own lobby character (an IDisplayActor). The
    // native models need the disc's files, which may not be there until extraction finishes,
    // so it's tried as each scene starts until it works.
    //
    // One render, by hand, of the character alone on RoR2's ManualRender layer with a light
    // of its own, framed on the upper body and seen from the front; done over a black and a
    // white background so the difference gives the alpha.
    public static class Portrait
    {
        sealed class Job
        {
            public GameObject body;
            public Func<IDisplayActor> make;
            public int size;
            public Func<ViewDraw, bool> hide;
            public Func<Texture2D> texture;
        }

        static readonly List<Job> pending = new List<Job>();
        static Runner runner;

        /// Gives `bodyPrefab` a `size`-pixel square portrait of what `make` lends out (the same
        /// factory as DisplayModel.Attach), leaving out the draws `hide` picks (company standing
        /// beside the character). Call at content load.
        public static void Request(GameObject bodyPrefab, Func<IDisplayActor> make, Func<ViewDraw, bool> hide = null, int size = 256)
        {
            pending.Add(new Job { body = bodyPrefab, make = make, size = size, hide = hide });
            EnsureRunner();
        }

        /// Prefer the game's existing portrait artwork when available. The factory
        /// returns null while first-run extraction is pending; the shared queue
        /// retries and refreshes already-visible survivor icons as with model renders.
        public static void RequestTexture(GameObject bodyPrefab, Func<Texture2D> make)
        {
            pending.Add(new Job { body = bodyPrefab, texture = make });
            EnsureRunner();
        }
        /// Top-first decoded artwork, padded to square without changing its
        /// aspect ratio. Native GX images and other source UI photos can share it.
        public static Texture2D FromRgba(string name, int width, int height, byte[] rgba)
        {
            if(width<=0||height<=0||width>2048||height>2048||rgba==null||rgba.Length!=checked(width*height*4))
                throw new ArgumentException("Invalid portrait RGBA dimensions");
            int side=Math.Max(width,height),left=(side-width)/2,bottom=(side-height)/2;
            var square=new byte[checked(side*side*4)];
            for(int y=0;y<height;y++)Buffer.BlockCopy(rgba,y*width*4,square,((bottom+height-1-y)*side+left)*4,width*4);
            var texture=new Texture2D(side,side,TextureFormat.RGBA32,false) {name=name,wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            texture.LoadRawTextureData(square);texture.Apply(false,true);return texture;
        }
        static void EnsureRunner()
        {
            if (!runner)
            {
                var go = new GameObject("Ror2CommonPortraits");
                UnityEngine.Object.DontDestroyOnLoad(go);
                runner = go.AddComponent<Runner>();
            }
        }

        sealed class Runner : MonoBehaviour
        {
            bool rendering;
            float retryAt;
            void OnEnable() => SceneManager.activeSceneChanged += Changed;
            void OnDisable() => SceneManager.activeSceneChanged -= Changed;

            void Update()
            {
                if (pending.Count > 0 && !rendering && Time.unscaledTime >= retryAt)
                    StartCoroutine(Later());
            }

            void Changed(Scene from, Scene to)
            {
                if (pending.Count > 0) StartCoroutine(Later());
            }

            IEnumerator Later()
            {
                if (rendering) yield break;
                rendering = true;
                // Let the scene's lights and the game's own start-up settle.
                for (int i = 0; i < 3; i++) yield return null;
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    var job = pending[i];
                    Texture2D tex = null;
                    try { tex = job.texture != null ? job.texture() : Render(job); }
                    catch (Exception e) { CommonLog.Error($"portrait for {(job.body ? job.body.name : "?")}: {e}"); }
                    if (!tex) continue;
                    var cb = job.body ? job.body.GetComponent<CharacterBody>() : null;
                    if (cb) cb.portraitIcon = tex;
                    // A late asset extraction can finish while character select is open.
                    // Its icon controllers cache the texture when their grid is built.
                    foreach (var icon in UnityEngine.Object.FindObjectsOfType<RoR2.UI.SurvivorIconController>())
                        if (icon.survivorDef && icon.survivorDef.bodyPrefab == job.body && icon.survivorIcon)
                            icon.survivorIcon.texture = tex;
                    pending.RemoveAt(i);
                    CommonLog.Info($"portrait for {job.body.name} ready");
                }
                retryAt = Time.unscaledTime + 2f;
                rendering = false;
            }
        }

        static Texture2D Render(Job job)
        {
            int layer = PortraitLayer();
            if (layer < 0) return null;
            var actor = job.make();
            if (actor == null || actor.Models == null || !actor.Models.Root)
            {
                actor?.Release();
                return null;
            }
            var stage = new GameObject("portrait stage").transform;
            stage.position = new Vector3(0f, -2000f, 0f);
            var layers = new List<(GameObject, int)>();
            GameObject camGo = null, lightGo = null;
            RenderTexture rt = null;
            try
            {
                // A moment of idling, then posed at the stage, as DisplayModel does.
                for (int i = 0; i < 4; i++) actor.Step(1f / 30f, stage);
                actor.Models.Update(actor.Scale, d => (
                    stage.position + stage.rotation * (new Vector3(d.pos.x, d.pos.y, d.pos.z) * actor.Scale),
                    stage.rotation * new Quaternion(d.rot_x, d.rot_y, d.rot_z, d.rot_w)), job.hide);

                // The posed vertices themselves: the meshes' bounds are padded for animation and
                // would frame a lot of empty space.
                var points = new List<Vector3>();
                foreach (var mf in actor.Models.Root.GetComponentsInChildren<MeshFilter>())
                {
                    var r = mf.GetComponent<Renderer>();
                    if (!mf.sharedMesh || !r || !r.enabled || !mf.gameObject.activeInHierarchy) continue;
                    var m = mf.transform.localToWorldMatrix;
                    foreach (var v in mf.sharedMesh.vertices) points.Add(m.MultiplyPoint3x4(v));
                }
                if (points.Count == 0) return null;
                float top = float.MinValue, bottom = float.MaxValue;
                foreach (var p in points)
                {
                    top = Mathf.Max(top, p.y);
                    bottom = Mathf.Min(bottom, p.y);
                }
                foreach (var t in actor.Models.Root.GetComponentsInChildren<Transform>(true))
                {
                    layers.Add((t.gameObject, t.gameObject.layer));
                    t.gameObject.layer = layer;
                }

                // Head and shoulders, as RoR2's portraits are: a square from just above the top
                // of the head down through the top half of the character, centred on that part.
                float h = top - bottom;
                float cut = top - h * 0.5f;
                var slice = new Bounds();
                bool any = false;
                foreach (var p in points)
                {
                    if (p.y < cut) continue;
                    if (any) slice.Encapsulate(p);
                    else slice = new Bounds(p, Vector3.zero);
                    any = true;
                }
                float view = Mathf.Max(h * 0.5f, Mathf.Max(slice.size.x, slice.size.z)) * 1.08f;
                var centre = new Vector3(slice.center.x, top + view * 0.04f - view * 0.5f, slice.center.z);
                const float fov = 20f;
                float dist = view * 0.5f / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) + slice.extents.z;

                camGo = new GameObject("portrait camera");
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.cullingMask = 1 << layer;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.fieldOfView = fov;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = dist * 4f;
                cam.allowHDR = false;
                cam.allowMSAA = false;
                // HGStandard supplies a deferred pass; a standalone camera must use it too.
                cam.renderingPath = RenderingPath.DeferredShading;
                camGo.transform.position = centre + stage.forward * dist;
                camGo.transform.rotation = Quaternion.LookRotation(-stage.forward, Vector3.up);

                lightGo = new GameObject("portrait light");
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
                light.color = new Color(1f, 0.97f, 0.92f);
                light.cullingMask = 1 << layer;
                lightGo.transform.rotation = Quaternion.LookRotation(-stage.forward + Vector3.down * 0.6f + stage.right * 0.4f);

                rt = new RenderTexture(job.size, job.size, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                var black = Grab(cam, rt, Color.black);
                var white = Grab(cam, rt, Color.white);
                var px = new Color[black.Length];
                int visible = 0;
                for (int i = 0; i < px.Length; i++)
                {
                    var b = black[i];
                    var w = white[i];
                    float a = Mathf.Clamp01(1f - Mathf.Max(w.r - b.r, Mathf.Max(w.g - b.g, w.b - b.b)));
                    px[i] = a > 0.004f ? new Color(Mathf.Clamp01(b.r / a), Mathf.Clamp01(b.g / a), Mathf.Clamp01(b.b / a), a) : Color.clear;
                    if (a > 0.1f && Mathf.Max(b.r, Mathf.Max(b.g, b.b)) > 0.01f) visible++;
                }
                // Keep the job queued if startup produced an empty or entirely black render.
                if (visible < px.Length / 1000) return null;
                var tex = new Texture2D(job.size, job.size, TextureFormat.RGBA32, false)
                {
                    name = job.body.name + " portrait",
                    wrapMode = TextureWrapMode.Clamp,
                };
                tex.SetPixels(px);
                tex.Apply(false, false);
                return tex;
            }
            finally
            {
                foreach (var (go, l) in layers)
                    if (go) go.layer = l;
                if (camGo) UnityEngine.Object.Destroy(camGo);
                if (lightGo) UnityEngine.Object.Destroy(lightGo);
                if (rt) { rt.Release(); UnityEngine.Object.Destroy(rt); }
                UnityEngine.Object.Destroy(stage.gameObject);
                actor.Release();
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

        // DLC4 occupies every layer from 8 through 31. ManualRender is reserved for
        // offscreen cameras; the remote stage and short clip range isolate this actor.
        static int PortraitLayer()
        {
            int manual = LayerMask.NameToLayer("ManualRender");
            if (manual >= 0) return manual;
            for (int i = 31; i >= 8; i--)
                if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
            CommonLog.Warn("portrait skipped: no ManualRender or unused layer");
            return -1;
        }
    }
}
