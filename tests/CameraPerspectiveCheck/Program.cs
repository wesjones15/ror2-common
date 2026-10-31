using System;
using System.Collections.Generic;
using System.Reflection;
using Ror2Common;
using RoR2;
using UnityEngine;

static class Program
{
    static void Check(bool value, string why) { if (!value) throw new Exception(why); }
    static void Near(Vector3 a, Vector3 b, string why) => Check((a - b).magnitude < .0001f, why);

    // Independent reconstruction of CameraModePlayerBasic's normal camera output:
    // rotate idealLocalCameraPos, shorten by pitch, use the retained collision
    // distance. This is the destination we used to blend toward by mistake.
    static Vector3 PlayerPosition(CameraTargetParams target, InstanceData instance, Vector3 pivot, float pitch, float yaw)
    {
        var delta = Quaternion.Euler(pitch, yaw) * target.currentCameraParamsData.idealLocalCameraPos;
        float wanted = MathF.Max(.25f, delta.magnitude * MathF.Sqrt((1f + pitch / 90f) * .5f));
        Check(MathF.Abs(wanted - instance.currentCameraDistance) < .0001f, "distance smoothing must already be at the adopted distance");
        return pivot + delta / delta.magnitude * instance.currentCameraDistance;
    }

    static void Main()
    {
        foreach (float pitch in new[] { -45f, -12f, 0f, 35f, 75f })
        foreach (float yaw in new[] { -135f, 0f, 42f, 170f })
        {
            var go = new GameObject();
            var body = go.AddComponent<CharacterBody>();
            var target = go.AddComponent<CameraTargetParams>();
            target.transform.position = new Vector3(100, 12, -50);
            target.cameraPivotTransform = new Transform { position = target.transform.position + Vector3.up * 2f };
            target.currentCameraParamsData.pivotVerticalOffset = .8f;
            var pivot = target.cameraPivotTransform.position + Vector3.up * .8f;
            var rotation = Quaternion.Euler(pitch, yaw);
            var shot = new CameraState { position = pivot + rotation * new Vector3(3, 1.5f, -17), rotation = rotation, fov = 74 };
            var instance = new InstanceData { currentCameraDistance = .1f, cameraDistanceVelocity = 20 };
            var rig = new CameraRigController { cameraMode = new CameraMode { instance = instance } };
            Check(CameraPerspective.Adopt(rig, body, shot), "valid follow camera must accept the shot");
            Near(PlayerPosition(target, instance, pivot, pitch, yaw), shot.position, "first player frame must preserve the exact intro position");
            Check(target.currentCameraParamsData.fov.value == 74 && instance.cameraDistanceVelocity == 0, "preserve FOV and remove the old distance spring");
            var offset = (Vector3)target.currentCameraParamsData.idealLocalCameraPos;
            Near(pivot + Quaternion.Euler(pitch, yaw + 40) * offset / offset.magnitude * instance.currentCameraDistance,
                pivot + Quaternion.Euler(0, 40) * (shot.position - pivot), "normal look input must orbit from the adopted perspective");
            var move = new Vector3(5, 0, -8);
            Near(PlayerPosition(target, instance, pivot + move, pitch, yaw), shot.position + move, "camera must follow body movement");
            var owner = go.GetComponent<CameraPerspective>();
            typeof(CameraPerspective).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(owner, null);
            Check(target.removed == 1 && target.active == 0, "body destruction must release its own framing override");
        }

        var blocked = new GameObject();
        var blockedBody = blocked.AddComponent<CharacterBody>();
        var blockedTarget = blocked.AddComponent<CameraTargetParams>();
        blockedTarget.cameraPivotTransform = new Transform { position = Vector3.up * 4 };
        blockedTarget.currentCameraParamsData.pivotVerticalOffset = 1;
        blockedTarget.currentCameraParamsData.wallCushion = .2f;
        var blockedRig = new CameraRigController { clearPivotDistance = 1 };
        var blockedShot = new CameraState { position = new Vector3(4, 5, -15), rotation = Quaternion.Euler(15, 20), fov = 60 };
        Check(CameraPerspective.Adopt(blockedRig, blockedBody, blockedShot), "obstructed pivot must still adopt");
        Near(PlayerPosition(blockedTarget, (InstanceData)blockedRig.cameraMode.instance, Vector3.up, 15, 20), blockedShot.position,
            "adoption must use the same constrained body-to-pivot ray as the game");
        var missing = new GameObject(); var missingBody = missing.AddComponent<CharacterBody>();
        var missingTarget = missing.AddComponent<CameraTargetParams>();
        Check(!CameraPerspective.Adopt(new CameraRigController { cameraMode = null }, missingBody, blockedShot)
            && missingTarget.active == 0, "unavailable camera API must leave framing alone");
        Console.WriteLine("PASS: 20 exact shot matches, retained FOV/distance, free orbit, body following, constrained pivot and owned cleanup");
    }
}

// Recording boundary. Geometry uses System.Numerics quaternion rotations; no
// Unity render or in-game verification is implied by this check.
namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object value) => value != null; }
    public class Component : Object
    {
        public GameObject gameObject; public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }
    public class MonoBehaviour : Component { }
    public class GameObject : Object
    {
        readonly Dictionary<Type, Component> components = new Dictionary<Type, Component>();
        public Transform transform = new Transform();
        public T AddComponent<T>() where T : Component, new() { var c = new T { gameObject = this }; components[typeof(T)] = c; return c; }
        public T GetComponent<T>() where T : Component => components.TryGetValue(typeof(T), out var c) ? (T)c : null;
    }
    public class Transform : Object { public Vector3 position; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 forward => new Vector3(0, 0, 1);
        public float sqrMagnitude => x * x + y * y + z * z;
        public float magnitude => MathF.Sqrt(sqrMagnitude);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float f) => new Vector3(a.x * f, a.y * f, a.z * f);
        public static Vector3 operator /(Vector3 a, float f) => a * (1f / f);
    }
    public struct Quaternion
    {
        System.Numerics.Quaternion value;
        public static Quaternion Euler(float pitch, float yaw) => new Quaternion { value = System.Numerics.Quaternion.CreateFromYawPitchRoll(yaw * MathF.PI / 180, pitch * MathF.PI / 180, 0) };
        public static Quaternion Inverse(Quaternion q) => new Quaternion { value = System.Numerics.Quaternion.Inverse(q.value) };
        public static Vector3 operator *(Quaternion q, Vector3 v)
        {
            var p = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(v.x, v.y, v.z), q.value);
            return new Vector3(p.X, p.Y, p.Z);
        }
    }
    public struct Ray { public Ray(Vector3 origin, Vector3 direction) { } }
    public static class Mathf
    {
        public const float Rad2Deg = 180f / MathF.PI;
        public static float Clamp(float f, float min, float max) => Math.Clamp(f, min, max);
        public static float Max(float a, float b) => MathF.Max(a, b);
        public static float Sqrt(float f) => MathF.Sqrt(f);
        public static float Asin(float f) => MathF.Asin(f);
    }
}
namespace Ror2Common { public static class CommonLog { public static void Info(string message) { } } }
namespace RoR2
{
    public class CharacterBody : MonoBehaviour { }
    public struct CameraState { public Vector3 position; public Quaternion rotation; public float fov; }
    public struct BlendFloat { public float value; public static implicit operator BlendFloat(float f) => new BlendFloat { value = f }; }
    public struct BlendVector
    {
        public Vector3 value;
        public static implicit operator BlendVector(Vector3 v) => new BlendVector { value = v };
        public static implicit operator Vector3(BlendVector v) => v.value;
    }
    public struct CharacterCameraParamsData { public BlendVector idealLocalCameraPos; public BlendFloat fov, pivotVerticalOffset, wallCushion; }
    public class CameraTargetParams : Component
    {
        public Transform cameraPivotTransform; public bool dontRaycastToPivot;
        CharacterCameraParamsData data;
        public ref CharacterCameraParamsData currentCameraParamsData => ref data;
        public int active, removed;
        public struct CameraParamsOverrideRequest { public CharacterCameraParamsData cameraParamsData; public float priority; }
        public struct CameraParamsOverrideHandle { public bool isValid; }
        public CameraParamsOverrideHandle AddParamsOverride(CameraParamsOverrideRequest r, float time) { active++; return new CameraParamsOverrideHandle { isValid = true }; }
        public CameraParamsOverrideHandle RemoveParamsOverride(CameraParamsOverrideHandle h, float time) { active--; removed++; return default; }
    }
    public class InstanceData { public float currentCameraDistance, cameraDistanceVelocity; }
    public class CameraMode { public object instance = new InstanceData(); public object DebugGetInstanceData(CameraRigController rig) => instance; }
    public class CameraRigController : UnityEngine.Object
    {
        public CameraMode cameraMode = new CameraMode(); public float clearPivotDistance = float.PositiveInfinity;
        public float Raycast(Ray ray, float length, float cushion) => MathF.Min(clearPivotDistance, length);
    }
}
