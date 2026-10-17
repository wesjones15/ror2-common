// Records Unity API calls; this verifies renderer data/ownership, not GPU output.
using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public class Object
    {
        public string name;
        public bool destroyed;
        public static bool doubleDestroyed;
        public static int searches;
        public static implicit operator bool(Object value) => value != null && !value.destroyed;
        public static void DontDestroyOnLoad(Object value) { }
        public static void Destroy(Object value)
        {
            if (value == null) return;
            if (value.destroyed) doubleDestroyed = true;
            value.destroyed = true;
            if (value is GameObject go) go.transform.destroyed = true;
        }
        public static void DestroyImmediate(Object value) => Destroy(value);
        public static T[] FindObjectsOfType<T>() { searches++; return Array.Empty<T>(); }
        public static Mesh Instantiate(Mesh value) => new Mesh { name = value.name, vertices = value.vertices, uv = value.uv, subMeshCount = value.subMeshCount };
    }
    public class Transform : Object
    {
        public GameObject gameObject;
        public Vector3 position, localScale, forward = Vector3.up;
        public Quaternion rotation;
        public void SetParent(Transform parent, bool world) { }
    }
    public enum PrimitiveType { Cube }
    public class GameObject : Object
    {
        readonly Dictionary<Type, Object> components = new Dictionary<Type, Object>();
        public Transform transform;
        public bool activeSelf;
        public GameObject(string name = "") { this.name = name; transform = new Transform { gameObject = this }; }
        public T AddComponent<T>() where T : Object, new() { var value = new T(); components[typeof(T)] = value; return value; }
        public T GetComponent<T>() where T : class => components.TryGetValue(typeof(T), out var o) ? o as T : null;
        public T GetComponentInChildren<T>() where T : class => GetComponent<T>();
        public void SetActive(bool active) { activeSelf = active; }
        public static GameObject CreatePrimitive(PrimitiveType p) { var go = new GameObject(); go.AddComponent<MeshRenderer>(); return go; }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 up => new Vector3(0, 1, 0);
        public Vector3 normalized => this;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => a * -1;
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.x * b, a.y * b, a.z * b);
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
    }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Vector3 operator *(Quaternion q, Vector3 v) => v;
    }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1, 1, 1, 1);
        public static Color operator *(Color c, float f) => new Color(c.r * f, c.g * f, c.b * f, c.a * f);
    }
    public struct Bounds { public Bounds(Vector3 center, Vector3 size) { } }
    public static class Mathf
    {
        public static float Clamp01(float f) => Math.Clamp(f, 0, 1);
        public static float Clamp(float f, float min, float max) => Math.Clamp(f, min, max);
        public static float Max(float a, float b) => Math.Max(a, b);
    }
    public class Mesh : Object
    {
        public static int uploads, normalUploads, uvUploads, colorUploads, boxes;
        public Vector3[] vertices;
        public Vector2[] uv;
        public Color[] colors;
        public int subMeshCount;
        public Bounds bounds;
        public Rendering.IndexFormat indexFormat;
        public void MarkDynamic() { }
        public void SetTriangles(int[] indices, int sub, bool recalc) { }
        public void SetVertices(Vector3[] v, int first, int count) { uploads++; vertices = new Vector3[count]; Array.Copy(v, first, vertices, 0, count); }
        public void SetNormals(Vector3[] v, int first, int count) { normalUploads++; }
        public void SetUVs(int channel, Vector2[] v, int first, int count) { uvUploads++; }
        public void SetColors(Color[] v, int first, int count) { colorUploads++; }
        public void RecalculateBounds() { boxes++; }
    }
    public class MeshFilter : Object { public Mesh sharedMesh; }
    public class Renderer : Object { public Material sharedMaterial = new Material(); }
    public class MeshRenderer : Renderer
    {
        public static int propertyUpdates;
        public Material[] sharedMaterials;
        public Rendering.ShadowCastingMode shadowCastingMode;
        public bool receiveShadows;
        public void SetPropertyBlock(MaterialPropertyBlock block, int sub) { propertyUpdates++; }
    }
    public class MaterialPropertyBlock { public void Clear() { } public void SetFloat(int id, float value) { } public void SetColor(int id, Color value) { } }
    public class Shader : Object { public static int PropertyToID(string name) => name.GetHashCode(); }
    public class Texture : Object { }
    public enum TextureFormat { RGBA32 }
    public enum FilterMode { Bilinear }
    public enum TextureWrapMode { Clamp, Mirror, Repeat }
    public class Texture2D : Texture
    {
        public FilterMode filterMode;
        public TextureWrapMode wrapModeU, wrapModeV;
        public static Texture2D whiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        public Texture2D(int w, int h, TextureFormat format, bool mip) { }
        public void SetPixelData(byte[] pixels, int mip) { }
        public void Apply(bool mip, bool immutable) { }
    }
    public class Material : Object
    {
        public Shader shader = new Shader();
        public int renderQueue;
        public Material() { }
        public Material(Material src) { shader = src.shader; }
        public Material(Shader shader) { this.shader = shader; }
        public void SetFloat(string name, float value) { }
        public void SetColor(string name, Color value) { }
        public void SetTexture(string name, Texture value) { }
        public void EnableKeyword(string name) { }
    }
    public enum LightType { Directional }
    public class Light : Object { public LightType type; public bool isActiveAndEnabled; public float intensity; public Transform transform = new Transform(); public Color color; }
    public static class RenderSettings { public static Light sun; public static Color ambientLight; }
    public static class Time { public static float unscaledTime; }
}
namespace UnityEngine.Rendering
{
    public enum IndexFormat { UInt32 }
    public enum ShadowCastingMode { On }
    public enum BlendMode { SrcAlpha, One, OneMinusSrcAlpha }
    public enum CompareFunction { LessEqual }
}
namespace UnityEngine.AddressableAssets
{
    public static class Addressables { public static Handle<T> LoadAssetAsync<T>(string key) where T : new() => new Handle<T>(); }
    public class Handle<T> where T : new() { public T WaitForCompletion() => new T(); }
}
namespace RoR2
{
    public static class LegacyResourcesAPI { public static T Load<T>(string path) where T : class => null; }
    public static class LegacyShaderAPI { public static UnityEngine.Shader Find(string name) => new UnityEngine.Shader(); }
    public class CharacterModel : UnityEngine.Object { public Info[] baseRendererInfos; public struct Info { public UnityEngine.Material defaultMaterial; } }
}
namespace Ror2Common
{
    public static class CommonLog
    {
        public static void Info(string text) { }
        public static void Warn(string text) { }
        public static void Error(string text) { throw new Exception(text); }
    }
}
