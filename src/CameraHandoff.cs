using RoR2;
using UnityEngine;

namespace Ror2Common
{
    /// Holds the last rendered camera shot across seat ejection/native-view setup.
    /// Release only after the replacement character and its camera pivot are ready.
    public sealed class CameraHandoff : MonoBehaviour, ICameraStateProvider
    {
        CameraRigController rig;
        CharacterBody body;
        CameraState shot;
        float expires;
        bool blending, firstSample, completed;
        float elapsed, duration;
        int sampledFrame = -1;

        public static CameraHandoff Hold(CharacterBody body, ICameraStateProvider previous)
        {
            if (!body || previous == null) return null;
            foreach (var rig in CameraRigController.readOnlyInstancesList)
            {
                if (!rig || rig.target != body.gameObject || !rig.sceneCam || !rig.IsOverrideCam(previous)) continue;
                var hold = body.gameObject.AddComponent<CameraHandoff>();
                hold.body = body;
                hold.rig = rig;
                var camera = rig.sceneCam;
                hold.shot = new CameraState { position = camera.transform.position,
                    rotation = camera.transform.rotation, fov = camera.fieldOfView };
                hold.expires = Time.unscaledTime + 2f;
                // Replace the pod BEFORE EjectPassenger: SurvivorPodController's exit
                // callback clears only its own override (normally with a 0.05s lerp).
                rig.SetOverrideCam(hold, 0f);
                return hold;
            }
            return null;
        }

        public void Release(float seconds = 0.35f)
        {
            if (!rig || !rig.IsOverrideCam(this)) { Finish(); return; }
            if (blending) return;
            if (seconds <= 0f) { Finish(); return; }
            // SetOverrideCam(null) matches the player's orbit to the held rotation.
            // Retain the override to blend the normal camera state supplied to
            // GetCameraState. RoR2's own lerp uses the full next frame delta: a
            // native-view loading hitch can consume its entire duration at once.
            rig.SetOverrideCam(null, 0f);
            rig.SetOverrideCam(this, 0f);
            duration = seconds;
            elapsed = 0f;
            blending = firstSample = true;
            CommonLog.Info($"Camera handoff: blending {duration:0.00}s after playable draw (ready frame {Time.deltaTime * 1000f:0}ms)");
        }

        void Update()
        {
            if (!rig || !body || rig.target != body.gameObject || !rig.IsOverrideCam(this)
                || body.healthComponent && !body.healthComponent.alive) Finish();
            else if (completed) Finish();
            else if (!blending && Time.unscaledTime >= expires) Release();
        }

        void Finish()
        {
            if (rig && rig.IsOverrideCam(this)) rig.SetOverrideCam(null, 0f);
            rig = null;
            Destroy(this);
        }

        void OnDestroy()
        {
            if (rig && rig.IsOverrideCam(this)) rig.SetOverrideCam(null, 0.35f);
            rig = null;
        }

        public void GetCameraState(CameraRigController rig, ref CameraState state)
        {
            if (!blending) { state = shot; return; }
            if (sampledFrame != Time.frameCount)
            {
                sampledFrame = Time.frameCount;
                // The ready notification can land after a slow load in this frame.
                // Display one complete ready frame before starting the camera move.
                if (firstSample) firstSample = false;
                else elapsed += Mathf.Clamp(Time.deltaTime, 0f, 1f / 30f);
            }
            float t = Mathf.Clamp01(elapsed / duration);
            float ease = t * t * (3f - 2f * t);
            state = CameraState.Lerp(ref shot, ref state, ease);
            // Leave the endpoint installed for this camera render. On the next
            // Update, MatchState can read that endpoint rather than the prior frame.
            if (t >= 1f) completed = true;
        }
        public bool IsUserLookAllowed(CameraRigController rig) => blending;
        public bool IsUserControlAllowed(CameraRigController rig) => blending;
        public bool IsHudAllowed(CameraRigController rig) => true;
    }
}
