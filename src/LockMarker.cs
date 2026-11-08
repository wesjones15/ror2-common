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
        readonly Mesh mesh;
        Material material;
        Texture texture;
        Texture2D circle;
        Color circleTint;
        int circleHealth = -1, circleShield = -1;

        public MarkerQuad(string name)
        {
            go = new GameObject(name);
            Object.DontDestroyOnLoad(go);
            transform = go.transform;
            mesh = new Mesh { name = name };
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
            var away = at - eye;
            if (away.sqrMagnitude < 1e-6f) away = Vector3.forward;
            Show(tex, aspect, at, Quaternion.LookRotation(away, Vector3.up) * Quaternion.Euler(0f, 0f, spin), size);
        }

        public void Show(Texture tex, float aspect, Vector3 at, Quaternion rotation, float size)
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
            transform.SetPositionAndRotation(at, rotation);
            transform.localScale = new Vector3(size, size / Mathf.Max(0.01f, aspect), 1f);
            go.SetActive(true);
        }

        // Authored primitive cue. The host supplies the same placement/rotation as
        // its original marker; optional texture availability never decides the pose.
        Texture2D Circle(Color tint, float health, float shield)
        {
            int h = Mathf.CeilToInt(Mathf.Clamp01(health) * 10f), s = Mathf.CeilToInt(Mathf.Clamp01(shield) * 10f);
            if (circle && circleTint == tint && circleHealth == h && circleShield == s) return circle;
            if (!circle) circle = new Texture2D(64, 64, TextureFormat.RGBA32, false) {
                name = "Generated lock circle", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[4096];
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) {
                float dx = (x - 31.5f) / 31.5f, dy = (y - 31.5f) / 31.5f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01((.07f - Mathf.Abs(r - .84f)) * 64f);
                float angle = Mathf.Repeat(Mathf.Atan2(dx, dy) / (2f * Mathf.PI), 1f);
                Color color = angle < h * .1f ? tint : angle < (h + s) * .1f ? Color.cyan : tint * .22f;
                color.a = alpha * tint.a; pixels[y * 64 + x] = color;
            }
            circle.SetPixels(pixels); circle.Apply(false, false);
            circleTint = tint; circleHealth = h; circleShield = s; return circle;
        }
        public void ShowCircle(Vector3 at, Quaternion rotation, float size, Color tint) => Show(Circle(tint, 1f, 0f), 1f, at, rotation, size);
        public void ShowCircle(Vector3 at, Vector3 eye, float size, float spin, Color tint, float health = 1f, float shield = 0f) =>
            Show(Circle(tint, health, shield), 1f, at, eye, size, spin);

        public void Hide() { if (go) go.SetActive(false); }

        public void Dispose()
        {
            if (material) Object.Destroy(material);
            if (circle) Object.Destroy(circle);
            if (mesh) Object.Destroy(mesh);
            if (go) Object.Destroy(go);
        }
    }
}
