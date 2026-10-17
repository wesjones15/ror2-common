using EntityStates;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
namespace Ror2Common
{
    public interface IMeleeArrival
    {
        bool Arriving {get;set;}
        bool ArrivalWaiting {get;}
        void ReleaseArrival();
    }
    /// Shared grounded figurine arrival. The fighter supplies its original entry
    /// pose/platform/animation/audio; Interact replaces the vanilla pod exit.
    public sealed class MeleeArrival:BaseState
    {
        IMeleeArrival actor;bool protectedBody;
        InteractionDriver interaction;InteractTarget startPrompt;
        public override void OnEnter(){base.OnEnter();foreach(var component in gameObject.GetComponents<MonoBehaviour>())if(component is IMeleeArrival source){actor=source;break;}
            if(actor!=null){actor.Arriving=true;if(characterBody&&characterBody.modelLocator&&characterBody.modelLocator.modelTransform){var model=characterBody.modelLocator.modelTransform;GameSettings.HideRenderers(model,true);var animator=model.GetComponent<Animator>();if(animator)animator.enabled=false;}}
            if(isAuthority&&actor!=null&&characterBody&&characterBody.isPlayerControlled){
                interaction=GetComponent<InteractionDriver>();
                // ContextManager supplies the actual bound key/controller glyph.
                startPrompt=InteractTarget.Make("Melee arrival",transform,transform.position,"Start",
                    activator=>actor.Arriving&&actor.ArrivalWaiting&&activator&&activator.gameObject==gameObject,
                    activator=>actor.ReleaseArrival());
            }
            if(NetworkServer.active&&characterBody){characterBody.AddBuff(RoR2Content.Buffs.HiddenInvincibility);protectedBody=true;}}
        public override void FixedUpdate(){base.FixedUpdate();
            if(actor==null){if(isAuthority)outer.SetNextStateToMain();return;}
            if(interaction&&startPrompt){
                if(actor.ArrivalWaiting)InteractTarget.Override(interaction,startPrompt);
                else ClearPrompt();
            }
            if(isAuthority&&actor.ArrivalWaiting&&fixedAge>1f&&characterBody&&!characterBody.isPlayerControlled)actor.ReleaseArrival();
            if(isAuthority&&!actor.Arriving)outer.SetNextStateToMain();}
        void ClearPrompt(){if(interaction&&startPrompt&&interaction.interactableOverride==startPrompt.gameObject)interaction.interactableOverride=null;}
        public override void OnExit(){ClearPrompt();if(startPrompt)Object.Destroy(startPrompt.gameObject);if(protectedBody&&characterBody)characterBody.RemoveBuff(RoR2Content.Buffs.HiddenInvincibility);if(actor!=null)actor.Arriving=false;base.OnExit();}
        public override InterruptPriority GetMinimumInterruptPriority()=>InterruptPriority.Death;
    }
}
