using RoR2;
using UnityEngine;

namespace Ror2Common
{
    // The mark that floats on a locked-on enemy (libpikmin's `lock_marker`, which Olimar's
    // cursor follows, in RoR2's terms): on the line from the owner's eye to the middle of the
    // enemy's whole body (the box around every hurt box), just in front of the first hurt box
    // that line enters, so it hangs on the side the player sees and never sinks into the body.
    // Where the line misses every one it hangs at the middle. Faces the eye.
    public static class LockMarker
    {
        /// Where the marker floats for `target`, seen from `eye`: `margin` metres clear of the
        /// surface and never closer to the eye than `minDistance`.
        public static Vector3 Place(Vector3 eye, CharacterBody target, float margin, float minDistance)
        {
            var colliders = new System.Collections.Generic.List<Collider>();
            var group = target.hurtBoxGroup;
            if (group != null && group.hurtBoxes != null)
                foreach (var box in group.hurtBoxes)
                {
                    var c = box ? box.GetComponent<Collider>() : null;
                    if (c && c.enabled) colliders.Add(c);
                }
            if (colliders.Count == 0) return target.corePosition;
            var bounds = colliders[0].bounds;
            for (int i = 1; i < colliders.Count; i++) bounds.Encapsulate(colliders[i].bounds);
            var to = bounds.center - eye;
            float dist = to.magnitude;
            var dir = dist > 1e-4f ? to / dist : Vector3.forward;
            float enter = dist;
            bool hit = false;
            var ray = new Ray(eye, dir);
            foreach (var c in colliders)
                if (c.Raycast(ray, out var h, dist) && (!hit || h.distance < enter))
                {
                    enter = h.distance;
                    hit = true;
                }
            float t = hit ? enter - margin : dist;
            return eye + dir * Mathf.Max(t, Mathf.Min(minDistance, dist));
        }
    }

    // A flat picture standing in the world, facing `eye` and turning about the line to it:
    // the lock-on marker's body. One quad on ModelView's see-through material.
    public sealed class MarkerQuad
    {
        readonly GameObject go;
        readonly Transform transform;
        readonly MeshRenderer renderer;
        Material material;
        Texture texture;

        public MarkerQuad(string name)
        {
            go = new GameObject(name);
            Object.DontDestroyOnLoad(go);
            transform = go.transform;
            var mesh = new Mesh { name = name };
            mesh.vertices = new[]
            {
                new Vector3(-.5f, -.5f, 0f), new Vector3(.5f, -.5f, 0f),
                new Vector3(-.5f, .5f, 0f), new Vector3(.5f, .5f, 0f),
            };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(false);
        }

        /// Stands the picture `tex` at `at`, `size` metres wide (`aspect` is width over height),
        /// facing `eye` and turned `spin` degrees about the line to it.
        public void Show(Texture tex, float aspect, Vector3 at, Vector3 eye, float size, float spin)
        {
            if (!tex) { Hide(); return; }
            if (tex != texture || !material)
            {
                if (material) Object.Destroy(material);
                material = Shaders.SeeThrough(go.name, tex, false, false);
                if (!material) { Hide(); return; }
                renderer.sharedMaterial = material;
                texture = tex;
            }
            var away = at - eye;
            if (away.sqrMagnitude < 1e-6f) away = Vector3.forward;
            transform.SetPositionAndRotation(at,
                Quaternion.LookRotation(away, Vector3.up) * Quaternion.Euler(0f, 0f, spin));
            transform.localScale = new Vector3(size, size / Mathf.Max(0.01f, aspect), 1f);
            go.SetActive(true);
        }

        public void Hide() { if (go) go.SetActive(false); }

        public void Dispose()
        {
            if (material) Object.Destroy(material);
            if (go) Object.Destroy(go);
        }
    }
}
