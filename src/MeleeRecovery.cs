using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace Ror2Common
{
    public interface IMeleeRecovery
    {
        bool Recovering {get;}
        bool RecoveryInvulnerable {get;}
        void BeginFallRecovery(Vector3 safeFeet);
    }
    /// Melee-only falloff adapter. Unstuck defines when/where recovery occurs;
    /// the native fighter owns KO/figurine/transform/flash timing and movement.
    /// Ordinary stuck/wall corrections do not run the falloff sequence.
    public sealed class MeleeRecovery:MonoBehaviour
    {
        CharacterBody body;IMeleeRecovery actor;bool protectedBody;
        void Awake(){body=GetComponent<CharacterBody>();foreach(var component in GetComponents<MonoBehaviour>())if(component is IMeleeRecovery source){actor=source;break;}}
        public bool CheckFall(Unstuck guard,Vector3 feet,float height,Unstuck.GroundProbe probe)
        {
            if(!isActiveAndEnabled||actor==null||actor.Recovering||!body||!body.healthComponent||!body.healthComponent.alive||!body.hasEffectiveAuthority||!NetworkServer.active)return false;
            if(!guard.Fell(feet,height,probe,out var safe))return false;
            actor.BeginFallRecovery(safe);Refresh();return true;
        }
        void FixedUpdate(){Refresh();}
        public void Refresh()
        {
            bool protect=isActiveAndEnabled&&actor!=null&&actor.RecoveryInvulnerable&&body&&body.hasEffectiveAuthority&&body.healthComponent&&body.healthComponent.alive&&NetworkServer.active;
            if(protect&&!protectedBody){body.AddBuff(RoR2Content.Buffs.HiddenInvincibility);protectedBody=true;}
            else if(!protect)Release();
        }
        void Release(){if(protectedBody&&body)body.RemoveBuff(RoR2Content.Buffs.HiddenInvincibility);protectedBody=false;}
        void OnDisable(){Release();}
        void OnDestroy(){Release();}
    }
}
