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
        public override void OnEnter(){base.OnEnter();foreach(var component in gameObject.GetComponents<MonoBehaviour>())if(component is IMeleeArrival source){actor=source;break;}
            if(actor!=null)actor.Arriving=true;
            if(NetworkServer.active&&characterBody){characterBody.AddBuff(RoR2Content.Buffs.HiddenInvincibility);protectedBody=true;}}
        public override void FixedUpdate(){base.FixedUpdate();
            if(actor==null){if(isAuthority)outer.SetNextStateToMain();return;}
            if(isAuthority&&actor.ArrivalWaiting&&inputBank&&inputBank.interact.justPressed)actor.ReleaseArrival();
            if(isAuthority&&!actor.Arriving)outer.SetNextStateToMain();}
        public override void OnExit(){if(protectedBody&&characterBody)characterBody.RemoveBuff(RoR2Content.Buffs.HiddenInvincibility);if(actor!=null)actor.Arriving=false;base.OnExit();}
        public override InterruptPriority GetMinimumInterruptPriority()=>InterruptPriority.Death;
    }
}
