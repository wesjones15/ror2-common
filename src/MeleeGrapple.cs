using RoR2;
using UnityEngine;
namespace Ror2Common
{
    /// Shared host grapple policy: bosses anchor the player and never enter capture.
    public sealed class MeleeGrapple
    {
        public const float Range=20f,PullSpeed=25f;
        readonly CharacterBody owner;Vector3 localAnchor;
        public HealthComponent Boss {get;private set;}
        public bool Attached=>Boss;
        public Vector3 Anchor=>Boss&&Boss.body?Boss.body.transform.TransformPoint(localAnchor):Vector3.zero;
        public MeleeGrapple(CharacterBody owner){this.owner=owner;}
        public static bool IsBoss(CharacterBody body)=>body&&(body.isBoss||body.isChampion);
        public bool Attach(HealthComponent target,Vector3 hit)
        {
            if(!owner||!target||!target.alive||!IsBoss(target.body)||target.body.isPlayerControlled||!target.body.teamComponent||target.body.teamComponent.teamIndex==owner.teamComponent.teamIndex)return false;
            Boss=target;localAnchor=target.body.transform.InverseTransformPoint(hit);return true;
        }
        public bool Step(bool held,bool interrupted,float dt,out Vector3 desiredFeet)
        {
            desiredFeet=owner?owner.footPosition:Vector3.zero;
            if(!held||interrupted||!Boss||!Boss.alive||!Boss.body||!owner||!owner.healthComponent||!owner.healthComponent.alive||owner.currentVehicle){Release();return false;}
            Vector3 anchor=Anchor;var delta=anchor-owner.corePosition;
            if(delta.sqrMagnitude>(Range+5f)*(Range+5f)){Release();return false;}
            var offset=owner.corePosition-owner.footPosition;
            var destination=anchor-delta.normalized*(owner.radius+0.15f)-offset;
            desiredFeet=Vector3.MoveTowards(owner.footPosition,destination,PullSpeed*Mathf.Max(0,dt));return true;
        }
        public void Release(){Boss=null;}
        public static bool Trace(CharacterBody owner,Vector3 origin,Vector3 direction,float length,out RaycastHit contact,out HurtBox target)
        {
            contact=default;target=null;float nearest=float.PositiveInfinity;
            foreach(var hit in Physics.SphereCastAll(origin,0.15f,direction,Mathf.Min(Range,length),WorldProbe.Mask|LayerIndex.entityPrecise.mask,QueryTriggerInteraction.Collide)){
                if(!hit.collider||hit.collider.GetComponentInParent<CharacterBody>()==owner)continue;
                var hurt=hit.collider.GetComponent<HurtBox>();
                if(hurt&&hurt.healthComponent&&hurt.healthComponent.body){var body=hurt.healthComponent.body;if(body==owner||body.teamComponent&&body.teamComponent.teamIndex==owner.teamComponent.teamIndex)continue;}
                // Trigger volumes aren't solid obstacles; enemy hurtboxes are valid triggers.
                if(hit.collider.isTrigger&&!hurt)continue;
                if(hit.distance<nearest){nearest=hit.distance;contact=hit;target=hurt;}
            }
            return nearest<float.PositiveInfinity;
        }
    }
}
