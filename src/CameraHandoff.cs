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
            if (rig && rig.IsOverrideCam(this)) rig.SetOverrideCam(null, seconds);
            rig = null;
            Destroy(this);
        }

        void Update()
        {
            if (!rig || !body || rig.target != body.gameObject || !rig.IsOverrideCam(this)
                || body.healthComponent && !body.healthComponent.alive || Time.unscaledTime >= expires)
                Release();
        }

        void OnDestroy()
        {
            if (rig && rig.IsOverrideCam(this)) rig.SetOverrideCam(null, 0.35f);
            rig = null;
        }

        public void GetCameraState(CameraRigController rig, ref CameraState state) => state = shot;
        public bool IsUserLookAllowed(CameraRigController rig) => false;
        public bool IsUserControlAllowed(CameraRigController rig) => false;
        public bool IsHudAllowed(CameraRigController rig) => true;
    }
}
