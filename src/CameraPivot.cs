using RoR2;
using UnityEngine;

namespace Ror2Common
{
    // A body's camera on a pivot of our own: a plain child of the body over its origin, at the
    // height of the body's own pivot. For plugins that hide the body's model and draw their own
    // character: the stock pivot hangs off that hidden model, which still turns and animates,
    // so the camera sways with a body nobody sees and the drawn character jitters against it
    // (Mario's jumps did, side to side).
    //
    // The camera then follows the body's transform alone, so a driver that smooths the body
    // between physics steps should also (as MarioDriver does):
    // - run its Update before RoR2's camera, which moves in Update
    //   ([DefaultExecutionOrder(-200)] on the driver), so the camera sees this frame's
    //   smoothed position;
    // - leave the body, at the end of FixedUpdate, where the smoothing starts (the previous
    //   step's position), never at the newest one, so nothing reads a position ahead of the
    //   one drawn.
    public sealed class CameraPivot
    {
        readonly CharacterBody body;
        readonly CameraTargetParams target;
        Transform pivot, original;

        public CameraPivot(CharacterBody body)
        {
            this.body = body;
            target = body ? body.GetComponent<CameraTargetParams>() : null;
        }

        /// Puts the camera on our pivot, or back on the body's own.
        public void Use(bool ours)
        {
            if (!target) return;
            if (ours)
            {
                if (pivot && target.cameraPivotTransform == pivot) return;
                original = target.cameraPivotTransform;
                if (!pivot)
                {
                    var t = body.transform;
                    pivot = new GameObject("Ror2CommonCameraPivot").transform;
                    pivot.SetParent(t, false);
                    float h = original ? original.position.y - t.position.y : 1.5f;
                    pivot.localPosition = new Vector3(0f, Mathf.Clamp(h, 0.3f, 3f), 0f);
                }
                target.cameraPivotTransform = pivot;
            }
            else if (original && target.cameraPivotTransform == pivot)
                target.cameraPivotTransform = original;
        }
    }
}
