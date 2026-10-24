using RoR2;
using UnityEngine;
using UnityEngine.Networking;
namespace Ror2Common
{
    public interface IMeleeDefense
    {
        bool MeleeInvulnerable { get; }
        bool BlockMeleeDamage(float healthFraction);
    }
    /// A fighter state that also absorbs damage which normally bypasses blocking (Yoshi's
    /// egg-roll armor). Optional: the actor may implement it alongside {@link IMeleeDefense}.
    public interface IMeleeBypassDefense
    {
        bool BlockBypassDamage(float healthFraction);
    }
    /// Uses RoR2's incoming-damage receiver; no damage detour or global immunity.
    public sealed class MeleeDefense:MonoBehaviour,IOnIncomingDamageServerReceiver
    {
        CharacterBody body;IMeleeDefense actor;
        void Awake(){body=GetComponent<CharacterBody>();foreach(var c in GetComponents<MonoBehaviour>())if(c is IMeleeDefense source){actor=source;break;}}
        void OnEnable(){if(!body)body=GetComponent<CharacterBody>();if(body&&body.healthComponent)body.healthComponent.AddOnIncomingDamageServerReceiver(this);}
        void OnDisable(){if(body&&body.healthComponent)body.healthComponent.RemoveOnIncomingDamageServerReceiver(this);}
        public void OnIncomingDamageServer(DamageInfo info)
        {
            if(!NetworkServer.active||!isActiveAndEnabled||actor==null||!body||!body.healthComponent||!body.healthComponent.alive||info.rejected)return;
            if(actor.MeleeInvulnerable){info.rejected=true;return;}
            float fraction=Mathf.Max(0,info.damage)/Mathf.Max(1,body.maxHealth);
            if((info.damageType.damageType&DamageType.BypassBlock)!=0){
                if(actor is IMeleeBypassDefense bypass&&bypass.BlockBypassDamage(fraction))info.rejected=true;
                return;
            }
            if(actor.BlockMeleeDamage(fraction))info.rejected=true;
        }
    }
}
