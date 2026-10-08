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
    // One render, by hand, of the character alone (on a layer nothing else uses) with a light
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
        }

        static readonly List<Job> pending = new List<Job>();
        static Runner runner;

        /// Gives `bodyPrefab` a `size`-pixel square portrait of what `make` lends out (the same
        /// factory as DisplayModel.Attach), leaving out the draws `hide` picks (company standing
        /// beside the character). Call at content load.
        public static void Request(GameObject bodyPrefab, Func<IDisplayActor> make, Func<ViewDraw, bool> hide = null, int size = 256)
        {
            pending.Add(new Job { body = bodyPrefab, make = make, size = size, hide = hide });
            if (!runner)
            {
                var go = new GameObject("Ror2CommonPortraits");
                UnityEngine.Object.DontDestroyOnLoad(go);
                runner = go.AddComponent<Runner>();
            }
        }

        sealed class Runner : MonoBehaviour
        {
            void OnEnable() => SceneManager.activeSceneChanged += Changed;
            void OnDisable() => SceneManager.activeSceneChanged -= Changed;

            void Changed(Scene from, Scene to)
            {
                if (pending.Count > 0) StartCoroutine(Later());
            }

            IEnumerator Later()
            {
                // Let the scene's lights and the game's own start-up settle.
                for (int i = 0; i < 3; i++) yield return null;
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    var job = pending[i];
                    Texture2D tex = null;
                    try { tex = Render(job); }
                    catch (Exception e) { CommonLog.Error($"portrait for {(job.body ? job.body.name : "?")}: {e}"); }
                    if (!tex) continue;
                    var cb = job.body ? job.body.GetComponent<CharacterBody>() : null;
                    if (cb) cb.portraitIcon = tex;
                    pending.RemoveAt(i);
                    CommonLog.Info($"portrait for {job.body.name} rendered");
                }
            }
        }

        static Texture2D Render(Job job)
        {
            int layer = FreeLayer();
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
                for (int i = 0; i < px.Length; i++)
                {
                    var b = black[i];
                    var w = white[i];
                    float a = Mathf.Clamp01(1f - Mathf.Max(w.r - b.r, Mathf.Max(w.g - b.g, w.b - b.b)));
                    px[i] = a > 0.004f ? new Color(Mathf.Clamp01(b.r / a), Mathf.Clamp01(b.g / a), Mathf.Clamp01(b.b / a), a) : Color.clear;
                }
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

        // A layer with no name: nothing in RoR2 draws or collides on it.
        static int FreeLayer()
        {
            for (int i = 31; i >= 8; i--)
                if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
            return -1;
        }
    }
}
