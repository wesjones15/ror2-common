using RoR2;
using UnityEngine;

namespace Ror2Common
{
    /// Melee's RoR2 camera adaptation. Use the same ray for directional input
    /// and projectile convergence; the cannon origin remains the authored bone.
    public static class MeleeAim
    {
        // Installed DLC4 GenericCharacterMain.HandleMovements selects aim vs move
        // using CharacterBody.shouldAim; CharacterDirection.Update owns smoothing.
        // Keep those real game decisions, including alwaysAim and sprint gating.
        public static void FacingTarget(CharacterBody body,CharacterDirection direction,Vector3 move,Vector3 aim,bool actionLocked,Vector3 actionFacing)
        {
            if(direction)direction.moveVector=actionLocked?actionFacing:(body.shouldAim?aim:move);
        }

        public static void BeginAttack(CharacterBody body,CharacterDirection direction,Vector3 aim)
        {
            // Commando FirePistol2.OnEnter -> StartAimMode(ray, 3f, false).
            // Set the real timer/target; leave CharacterDirection's turn intact.
            body.SetAimTimer(3f);
            if(direction)direction.moveVector=aim;
        }

        public static Ray CameraRay(CharacterBody body,InputBankTest input)
        {
            var ray=input?input.GetAimRay():new Ray(body.transform.position,body.transform.forward);
            return CameraRigController.ModifyAimRayIfApplicable(ray,body.gameObject,out _);
        }

        public static Vector3 ProjectileDirection(CharacterBody body,Vector3 muzzle,Ray cameraRay)
        {
            var forward=cameraRay.direction.normalized;
            var point=CrosshairPoint(body,cameraRay,float.PositiveInfinity,out var hit)
                ?hit:cameraRay.origin+forward*10000f;
            var delta=point-muzzle;
            // Geometry between camera and cannon must not reverse the shot.
            // The projectile's own sweep still handles walls in front of it.
            return Vector3.Dot(delta,forward)>0.05f?delta.normalized:forward;
        }

        /// The world point under the crosshair, for a projectile that keeps converging on it
        /// while its muzzle moves (Yoshi's tongue head bobs through Egg Lay). The nearest
        /// non-owner hit along the camera ray within `range`, else `muzzle + forward * range`;
        /// a hit behind the muzzle (camera obstruction) falls back to the forward point.
        public static Vector3 ProjectilePoint(CharacterBody body,Vector3 muzzle,Ray cameraRay,float range)
        {
            var forward=cameraRay.direction.normalized;
            if(CrosshairPoint(body,cameraRay,range,out var hit)
                &&Vector3.Dot(hit-muzzle,forward)>0.05f)return hit;
            return muzzle+forward*range;
        }

        static bool CrosshairPoint(CharacterBody body,Ray cameraRay,float range,out Vector3 point)
        {
            float closest=range;
            point=default;
            bool found=false;
            foreach(var hit in Physics.RaycastAll(cameraRay,range,WorldProbe.Mask|LayerIndex.entityPrecise.mask,QueryTriggerInteraction.Ignore)){
                // A camera ray can pass through the owner's body before reaching
                // its crosshair target. Never aim back into our own hurtboxes.
                if(!hit.collider||hit.collider.GetComponentInParent<CharacterBody>()==body)continue;
                var hurt=hit.collider.GetComponent<HurtBox>();
                if(hurt&&hurt.healthComponent&&hurt.healthComponent.body==body)continue;
                if(hit.distance<closest){closest=hit.distance;point=hit.point;found=true;}
            }
            return found;
        }
    }
}
