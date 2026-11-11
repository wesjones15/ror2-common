using EntityStates;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
namespace Ror2Common
{
    public interface IMeleeArrival
    {
        bool Arriving {get;set;}
    }
    /// Melee-only initial body state. Native shared return behavior owns figurine,
    /// movement/timeout release, transformation and post-release protection.
    public sealed class MeleeArrival:BaseState
    {
        /// The longest the arrival may last before the fighter is let go, whether or not its native arrival has finished.
        public const float ArrivalTimeoutSeconds=15f;
        IMeleeArrival actor;bool protectedBody;
        public override void OnEnter(){base.OnEnter();foreach(var component in gameObject.GetComponents<MonoBehaviour>())if(component is IMeleeArrival source){actor=source;break;}
            if(actor!=null){actor.Arriving=true;if(characterBody&&characterBody.modelLocator&&characterBody.modelLocator.modelTransform){var model=characterBody.modelLocator.modelTransform;GameSettings.HideRenderers(model,true);var animator=model.GetComponent<Animator>();if(animator)animator.enabled=false;}}
            // Protect loading/ground placement too, before native return begins.
            if(NetworkServer.active&&characterBody){characterBody.AddBuff(RoR2Content.Buffs.HiddenInvincibility);protectedBody=true;}}
        public override void FixedUpdate(){base.FixedUpdate();
            // The driver starts the arrival only once its files are ready (MeleeDriver: `if (AssetsBusy) return`), so a machine still
            // preparing them would hold the fighter hidden and invulnerable here for as long as that takes: let go after a while.
            if(isAuthority&&(actor==null||!actor.Arriving||fixedAge>ArrivalTimeoutSeconds)){
                outer.SetNextStateToMain();}}
        public override void OnExit(){if(protectedBody&&characterBody)characterBody.RemoveBuff(RoR2Content.Buffs.HiddenInvincibility);if(actor!=null)actor.Arriving=false;
            // Undo the OnEnter hide so a fallback (assets missing) leaves the stock
            // model visible; the active driver re-hides it in LateUpdate.
            if(characterBody&&characterBody.modelLocator&&characterBody.modelLocator.modelTransform){var model=characterBody.modelLocator.modelTransform;GameSettings.HideRenderers(model,false);var animator=model.GetComponent<Animator>();if(animator)animator.enabled=true;}
            base.OnExit();}
        public override InterruptPriority GetMinimumInterruptPriority()=>InterruptPriority.Death;
    }
}
