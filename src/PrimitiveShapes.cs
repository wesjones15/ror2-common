using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ror2Common
{
    /// Drawing-only primitives, extracted from Olimar's no-assets view. Never adds
    /// physics to the simulation. Owns its materials and reuses objects each frame.
    public sealed class PrimitiveShapes : IDisposable
    {
        readonly Transform root;
        readonly List<GameObject> shapes = new List<GameObject>();
        readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
        Material basis;
        int used;
        public PrimitiveShapes(string name, Transform parent = null)
        {
            root = new GameObject(name).transform;
            if (parent) root.SetParent(parent, false);
            else UnityEngine.Object.DontDestroyOnLoad(root.gameObject);
        }
        public static GameObject Create(PrimitiveType type, Transform parent)
        {
            var go = GameObject.CreatePrimitive(type);
            var collider = go.GetComponent<Collider>();
            if (collider) { collider.enabled = false; UnityEngine.Object.Destroy(collider); }
            go.transform.SetParent(parent, false);
            go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }
        public Material Material(Color color)
        {
            if (materials.TryGetValue(color, out var material) && material) return material;
            if (!basis)
            {
                var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                basis = probe.GetComponent<Renderer>().sharedMaterial;
                UnityEngine.Object.DestroyImmediate(probe);
            }
            material = new Material(basis) { color = color };
            materials[color] = material;
            return material;
        }
        public void Begin() { used = 0; }
        public void Show(PrimitiveType type, Vector3 at, Vector3 size, Quaternion rotation, Color color)
        {
            GameObject go;
            if (used == shapes.Count) { go = Create(type, root); shapes.Add(go); }
            else
            {
                go = shapes[used];
                // A pool slot may change its primitive when a character transforms.
                if (!go || go.name != type.ToString()) { UnityEngine.Object.Destroy(go); go = Create(type, root); shapes[used] = go; }
            }
            go.name = type.ToString();
            used++;
            go.transform.SetPositionAndRotation(at, rotation);
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = Material(color);
            go.SetActive(true);
        }
        public void Body(Vector3 feet, Quaternion facing, Color color, float height = 1.8f, float radius = 0.35f)
        {
            Show(PrimitiveType.Cylinder, feet + Vector3.up * height * 0.5f,
                new Vector3(radius * 2f, height * 0.5f, radius * 2f), facing, color);
            Show(PrimitiveType.Sphere, feet + Vector3.up * height * 0.8f + facing * Vector3.forward * radius,
                Vector3.one * radius * 0.35f, facing, Color.white);
        }
        public void Segment(Vector3 start, Vector3 end, float radius, Color color)
        {
            var delta = end - start;
            Show(PrimitiveType.Cylinder, (start + end) * 0.5f,
                new Vector3(radius * 2f, Mathf.Max(0.01f, delta.magnitude) * 0.5f, radius * 2f),
                delta.sqrMagnitude > 0.000001f ? Quaternion.FromToRotation(Vector3.up, delta) : Quaternion.identity, color);
        }
        public void End() { for (int i = used; i < shapes.Count; i++) if (shapes[i]) shapes[i].SetActive(false); }
        public void Hide() { Begin(); End(); }
        public static Texture2D Portrait(Color color)
        {
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false) { name = "Procedural survivor portrait" };
            var pixels = new Color[64 * 64];
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) {
                bool body = x >= 20 && x < 44 && y >= 8 && y < 55;
                pixels[y * 64 + x] = body ? color : new Color(0.08f, 0.1f, 0.14f, 1f);
                if ((x - 41) * (x - 41) + (y - 45) * (y - 45) < 7) pixels[y * 64 + x] = Color.white;
            }
            texture.SetPixels(pixels); texture.Apply(); return texture;
        }
        public void Dispose()
        {
            if (root) UnityEngine.Object.Destroy(root.gameObject);
            foreach (var material in materials.Values) if (material) UnityEngine.Object.Destroy(material);
            materials.Clear(); shapes.Clear();
        }
    }
    public interface IProceduralDisplayActor { }
    public sealed class PrimitiveDisplay : IDisplayActor, IProceduralDisplayActor
    {
        readonly PrimitiveShapes shapes = new PrimitiveShapes("Procedural survivor display");
        readonly Func<Color> color;
        public PrimitiveDisplay(Color color) : this(() => color) { }
        public PrimitiveDisplay(Func<Color> color) { this.color = color; }
        public ModelView Models => null;
        public float Scale => 1f;
        public void Step(float dt, Transform display)
        {
            shapes.Begin(); shapes.Body(display.position, display.rotation, color()); shapes.End();
        }
        public void Release() => shapes.Dispose();
    }
}
