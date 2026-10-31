using System;
using System.Reflection;
using RoR2;
using UnityEngine;

namespace Ror2Common
{
    /// Adopts a rendered shot as the normal player camera's framing. The game
    /// continues to own look input, following, collision and temporary cameras.
    public sealed class CameraPerspective : MonoBehaviour
    {
        CameraTargetParams target;
        CameraTargetParams.CameraParamsOverrideHandle framing;

        internal static bool Adopt(CameraRigController rig, CharacterBody body, CameraState shot)
        {
            var target = body ? body.GetComponent<CameraTargetParams>() : null;
            var mode = rig ? rig.cameraMode : null;
            var instance = mode?.DebugGetInstanceData(rig);
            if (!target || instance == null) return false;
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var distance = instance.GetType().GetField("currentCameraDistance", fields);
            var velocity = instance.GetType().GetField("cameraDistanceVelocity", fields);
            if (distance?.FieldType != typeof(float) || velocity?.FieldType != typeof(float)) return false;

            var data = target.currentCameraParamsData;
            var pivot = Pivot(rig, target);
            var offset = LocalOffset(shot.position - pivot, shot.rotation);
            if (offset.sqrMagnitude < .0001f) return false;
            var owner = body.GetComponent<CameraPerspective>();
            if (!owner) owner = body.gameObject.AddComponent<CameraPerspective>();
            owner.Clear();
            owner.target = target;
            owner.framing = target.AddParamsOverride(new CameraTargetParams.CameraParamsOverrideRequest {
                cameraParamsData = new CharacterCameraParamsData { idealLocalCameraPos = offset, fov = shot.fov },
                priority = 0f,
            }, 0f);
            // AddParamsOverride is normally evaluated in the target's next Update.
            // Publish these two values now so the first player-camera frame already
            // agrees with the shot. Other camera parameters remain the game's.
            data.idealLocalCameraPos = offset;
            data.fov = shot.fov;
            target.currentCameraParamsData = data;
            distance.SetValue(instance, (shot.position - pivot).magnitude);
            velocity.SetValue(instance, 0f);
            CommonLog.Info($"Camera handoff: adopted existing perspective, distance {(shot.position - pivot).magnitude:0.00}m, FOV {shot.fov:0.0}; no camera move");
            return true;
        }

        // CameraModePlayerBasic.UpdateInternal shortens idealLocalCameraPos by a
        // pitch-dependent factor BEFORE its distance/collision calculation. Undo
        // that factor when adopting; simply assigning the world offset zooms in.
        internal static Vector3 LocalOffset(Vector3 worldOffset, Quaternion rotation)
        {
            var forward = rotation * Vector3.forward;
            float pitch = Mathf.Asin(Mathf.Clamp(-forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            float factor = Mathf.Sqrt(Mathf.Max(.0001f, (1f + pitch / 90f) * .5f));
            return Quaternion.Inverse(rotation) * worldOffset / factor;
        }

        // CameraModePlayerBasic.CalculateTargetPivotPosition, including the game's
        // body-to-pivot obstruction check and authored vertical offset.
        static Vector3 Pivot(CameraRigController rig, CameraTargetParams target)
        {
            var origin = target.transform.position;
            var pivot = target.cameraPivotTransform ? target.cameraPivotTransform.position : origin;
            pivot += Vector3.up * target.currentCameraParamsData.pivotVerticalOffset.value;
            if (target.dontRaycastToPivot) return pivot;
            var delta = pivot - origin;
            float length = delta.magnitude;
            if (length < .0001f) return pivot;
            float clear = rig.Raycast(new Ray(origin, delta), length,
                target.currentCameraParamsData.wallCushion.value);
            return origin + delta / length * clear;
        }

        void Clear()
        {
            if (target && framing.isValid) target.RemoveParamsOverride(framing, 0f);
            target = null;
            framing = default;
        }

        void OnDestroy() => Clear();
    }
}
