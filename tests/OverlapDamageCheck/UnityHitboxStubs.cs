using System;
namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object value) => value != null; }
    public class GameObject : Object
    {
        public bool activeInHierarchy = true;
        public object component;
        public T GetComponent<T>() where T : class => component as T;
    }
    public class Transform : Object
    {
        public Vector3 position, lossyScale = new Vector3(1, 1, 1);
        public Quaternion rotation = Quaternion.identity;
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude => x*x + y*y + z*z;
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x+b.x, a.y+b.y, a.z+b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x-b.x, a.y-b.y, a.z-b.z);
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.x*b, a.y*b, a.z*b);
        public static float Distance(Vector3 a, Vector3 b) => MathF.Sqrt((a-b).sqrMagnitude);
    }
    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x=x; this.y=y; this.z=z; this.w=w; }
        public static Quaternion identity => new Quaternion(0, 0, 0, 1);
        static System.Numerics.Quaternion Numerics(Quaternion q) => new System.Numerics.Quaternion(q.x,q.y,q.z,q.w);
        public static Quaternion Inverse(Quaternion q)
        {
            var n = System.Numerics.Quaternion.Inverse(Numerics(q));
            return new Quaternion(n.X,n.Y,n.Z,n.W);
        }
        public static Vector3 operator *(Quaternion q, Vector3 v)
        {
            var n = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(v.x,v.y,v.z), Numerics(q));
            return new Vector3(n.X,n.Y,n.Z);
        }
    }
    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a,b);
        public static int Max(int a, int b) => Math.Max(a,b);
        public static float Min(float a, float b) => Math.Min(a,b);
        public static int Min(int a, int b) => Math.Min(a,b);
        public static float Abs(float a) => Math.Abs(a);
        public static int NextPowerOfTwo(int value)
        {
            if (value <= 0) return 0;
            int p = 1;
            while (p < value) p <<= 1;
            return p;
        }
    }
    public static class Time { public static float realtimeSinceStartup; }
    public enum QueryTriggerInteraction { Ignore }
    public static class Physics
    {
        public static bool Blocked;
        public static bool Linecast(Vector3 start, Vector3 end, int mask, QueryTriggerInteraction trigger) => Blocked;
    }
}
