using System;
using System.Collections.Generic;
using System.Reflection;
using Ror2Common;
using RoR2;
using UnityEngine;

static class Program
{
    static void Check(bool value, string why) { if (!value) throw new Exception(why); }
    static void Update(CameraHandoff hold) => typeof(CameraHandoff).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hold, null);
    static void Main()
    {
        var body = new CharacterBody();
        var pod = new Provider();
        var other = new Provider();
        var rig = new CameraRigController { target = body.gameObject, provider = pod };
        rig.sceneCam.transform.position = 123;
        rig.sceneCam.transform.rotation = 45;
        rig.sceneCam.fieldOfView = 60;
        CameraRigController.readOnlyInstancesList.Add(rig);
        Check(CameraHandoff.Hold(body, other) == null && rig.provider == pod, "unrelated override must be left alone");
        var hold = CameraHandoff.Hold(body, pod);
        Check(hold != null && rig.IsOverrideCam(hold) && rig.duration == 0, "must take over the pod without moving the shot");
        // SurvivorPodController.UpdateCameras on passenger exit clears only its own override.
        if (rig.IsOverrideCam(pod)) rig.SetOverrideCam(null, .05f);
        rig.sceneCam.transform.position = 999; // TeleportHelper / seat exit can move the live camera.
        var state = new CameraState();
        hold.GetCameraState(rig, ref state);
        Check(state.position == 123 && state.rotation == 45 && state.fov == 60, "ejection must not move the held shot");
        Check(!hold.IsUserLookAllowed(rig) && !hold.IsUserControlAllowed(rig) && hold.IsHudAllowed(rig), "hold must retain intro control rules");
        Update(hold);
        Check(rig.provider == hold, "must wait for the first playable draw");
        hold.Release();
        Check(rig.provider == null && rig.duration == .35f, "ready view must blend into the player camera");

        rig.provider = pod;
        hold = CameraHandoff.Hold(body, pod);
        rig.provider = other;
        hold.Release();
        Check(rig.provider == other, "release cannot steal a newer override");
        rig.provider = pod;
        hold = CameraHandoff.Hold(body, pod);
        Time.unscaledTime = 2.01f;
        Update(hold);
        Check(rig.provider == null, "failed native startup cannot trap the camera");
        Console.WriteLine("PASS: pod exit ordering, exact held shot, ready-view blend, override ownership and timeout");
    }
    sealed class Provider : ICameraStateProvider { }
}

namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object value) => value != null; }
    public class MonoBehaviour : Object { protected static void Destroy(Object value) { } }
    public class GameObject : Object { public T AddComponent<T>() where T : new() => new T(); }
    public class Transform { public float position, rotation; }
    public class Camera : Object { public Transform transform = new Transform(); public float fieldOfView; }
    public static class Time { public static float unscaledTime; }
}
namespace RoR2
{
    public interface ICameraStateProvider { }
    public struct CameraState { public float position, rotation, fov; }
    public class HealthComponent : UnityEngine.Object { public bool alive = true; }
    public class CharacterBody : UnityEngine.Object
    {
        public GameObject gameObject = new GameObject(); public HealthComponent healthComponent = new HealthComponent();
    }
    public class CameraRigController : UnityEngine.Object
    {
        public static readonly List<CameraRigController> readOnlyInstancesList = new List<CameraRigController>();
        public Camera sceneCam = new Camera(); public GameObject target; public ICameraStateProvider provider; public float duration;
        public bool IsOverrideCam(ICameraStateProvider candidate) => provider == candidate;
        public void SetOverrideCam(ICameraStateProvider next, float time) { provider = next; duration = time; }
    }
}
