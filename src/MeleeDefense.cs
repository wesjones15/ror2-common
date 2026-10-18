using RoR2;
using UnityEngine;
using UnityEngine.Networking;
namespace Ror2Common
{
    public interface IMeleeDefense { bool BlockMeleeDamage(float healthFraction); }
    /// Uses RoR2's incoming-damage receiver; no damage detour or global immunity.
    public sealed class MeleeDefense:MonoBehaviour,IOnIncomingDamageServerReceiver
    {
        CharacterBody body;IMeleeDefense actor;
        void Awake(){body=GetComponent<CharacterBody>();foreach(var c in GetComponents<MonoBehaviour>())if(c is IMeleeDefense source){actor=source;break;}}
        void OnEnable(){if(!body)body=GetComponent<CharacterBody>();if(body&&body.healthComponent)body.healthComponent.AddOnIncomingDamageServerReceiver(this);}
        void OnDisable(){if(body&&body.healthComponent)body.healthComponent.RemoveOnIncomingDamageServerReceiver(this);}
        public void OnIncomingDamageServer(DamageInfo info)
        {
            if(!NetworkServer.active||!isActiveAndEnabled||actor==null||!body||!body.healthComponent||!body.healthComponent.alive||info.rejected||(info.damageType.damageType&DamageType.BypassBlock)!=0)return;
            if(actor.BlockMeleeDamage(Mathf.Max(0,info.damage)/Mathf.Max(1,body.maxHealth)))info.rejected=true;
        }
    }
}
