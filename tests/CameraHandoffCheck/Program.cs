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
        Check(rig.provider == hold && hold.IsUserControlAllowed(rig), "ready view must start blending with player control");
        Time.deltaTime = .6f; // Native startup stalls longer than the whole requested blend.
        Time.frameCount++;
        state = new CameraState { position = 23, rotation = 45, fov = 70 };
        hold.GetCameraState(rig, ref state);
        Check(state.position == 123, "loading frame must not consume the blend before a ready frame is shown");
        Time.frameCount++;
        state.position = 23;
        hold.GetCameraState(rig, ref state);
        Check(state.position > 120 && state.position < 123, "a long next frame must move a small distance, not snap to the endpoint");
        float first = state.position;
        state.position = 23;
        hold.GetCameraState(rig, ref state);
        Check(state.position == first, "multiple camera samples in one rendered frame cannot advance the blend twice");
        Time.deltaTime = 1f / 60f;
        float previous = first;
        for (int i = 0; i < 24; i++)
        {
            Time.frameCount++;
            state = new CameraState { position = 23, rotation = 45, fov = 70 };
            hold.GetCameraState(rig, ref state);
            Check(state.position <= previous && state.position >= 23, "camera must approach the current player view without overshoot");
            previous = state.position;
        }
        Check(state.position == 23 && state.fov == 70, "blend must reach the player view exactly");
        Update(hold);
        Check(rig.provider == null, "completed blend must return camera ownership");

        rig.provider = pod;
        hold = CameraHandoff.Hold(body, pod);
        rig.provider = other;
        hold.Release();
        Check(rig.provider == other, "release cannot steal a newer override");
        rig.provider = pod;
        hold = CameraHandoff.Hold(body, pod);
        Time.unscaledTime = 2.01f;
        Update(hold);
        Check(rig.provider == hold, "failed startup must begin fallback blend");
        for (int i = 0; i < 24; i++) { Time.frameCount++; hold.GetCameraState(rig, ref state); }
        Update(hold);
        Check(rig.provider == null, "failed native startup cannot trap the camera");
        Console.WriteLine("PASS: seat exit, 600ms loading hitch, bounded multi-frame blend, exact endpoint, ownership and timeout");
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
    public static class Time { public static float unscaledTime, deltaTime; public static int frameCount; }
    public static class Mathf
    {
        public static float Clamp(float v, float min, float max) => Math.Clamp(v, min, max);
        public static float Clamp01(float v) => Clamp(v, 0, 1);
    }
}
namespace Ror2Common { public static class CommonLog { public static void Info(string message) { } } }
namespace RoR2
{
    public interface ICameraStateProvider { }
    public struct CameraState
    {
        public float position, rotation, fov;
        public static CameraState Lerp(ref CameraState a, ref CameraState b, float t) => new CameraState {
            position = a.position + (b.position - a.position) * t,
            rotation = a.rotation + (b.rotation - a.rotation) * t,
            fov = a.fov + (b.fov - a.fov) * t };
    }
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
