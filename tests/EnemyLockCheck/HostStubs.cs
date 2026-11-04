using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object o) => o != null; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
        public float sqrMagnitude => x*x+y*y+z*z;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public Vector3 normalized => magnitude > 0 ? new Vector3(x/magnitude,y/magnitude,z/magnitude) : default;
        public static Vector3 up => new Vector3(0,1,0);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static float Dot(Vector3 a, Vector3 b) => a.x*b.x+a.y*b.y+a.z*b.z;
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y*b.z-a.z*b.y, a.z*b.x-a.x*b.z, a.x*b.y-a.y*b.x);
    }
    public struct Ray
    {
        public Vector3 origin, direction;
        public Ray(Vector3 origin, Vector3 direction) { this.origin=origin; this.direction=direction.normalized; }
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x=x; this.y=y; }
        public float sqrMagnitude => x*x+y*y;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public static Vector2 right => new Vector2(1,0);
        public Vector2 normalized { get { var result=this; result.Normalize(); return result; } }
        public void Normalize() { float length=magnitude; if (length>0) { x/=length; y/=length; } }
        public static float Dot(Vector2 a, Vector2 b) => a.x*b.x+a.y*b.y;
    }
    public class Transform : Object
    {
        public Vector3 forward=new Vector3(0,0,1), right=new Vector3(1,0,0), up=Vector3.up;
        public Camera camera;
        public T GetComponent<T>() where T : class => camera as T;
    }
    public static class Mathf { public static float Max(float a, float b) => Math.Max(a, b); }
    public class Camera : Object
    {
        public float aspect = 1f;
        public Vector3 WorldToViewportPoint(Vector3 point) => new Vector3(Math.Abs(point.x)>5 ? 2 : .5f,.5f,point.z);
    }
    public enum QueryTriggerInteraction { Ignore }
    public class Collider { public string name="wall"; }
    public struct RaycastHit { public Collider collider; }
    public static class Physics
    {
        public static bool blockWorld, entityOccludes;
        public static int lastMask;
        public static bool Linecast(Vector3 a, Vector3 b, int mask, QueryTriggerInteraction q)
        { lastMask=mask; return (mask & 1)!=0 && blockWorld || (mask & 2)!=0 && entityOccludes; }
        public static bool Linecast(Vector3 a, Vector3 b, out RaycastHit hit, int mask, QueryTriggerInteraction q)
        { hit=new RaycastHit { collider=new Collider() }; return Linecast(a,b,mask,q); }
    }
    public static class Time { public static float unscaledTime=10; }
}
namespace RoR2
{
    using UnityEngine;
    public enum TeamIndex { None, Player, Monster }
    public class TeamComponent : Object { public TeamIndex teamIndex; }
    public class HealthComponent : Object { public bool alive=true; }
    public class CharacterBody : Object
    {
        public static readonly List<CharacterBody> readOnlyInstancesList=new List<CharacterBody>();
        public HealthComponent healthComponent;
        public TeamComponent teamComponent;
        public Vector3 corePosition;
        public Transform transform=new Transform();
    }
    public struct LayerIndex { public int mask; public static LayerIndex world => new LayerIndex { mask=1 }; }
}
namespace Ror2Common
{
    public static class GameSettings { public static UnityEngine.Transform camera; public static UnityEngine.Transform PlayerCamera() => camera; }
    public static class WorldProbe { public static int Mask => 3; }
    public static class CommonLog { public static void Info(string text) { } }
}
