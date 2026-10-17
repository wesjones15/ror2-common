using System;
using RoR2;
using RoR2.CharacterAI;
using UnityEngine;
using UnityEngine.Networking;
namespace Ror2Common
{
    /// Host-side ownership of one captured enemy. Fighter animation, grab
    /// windows, pummel damage and throw timing are supplied by the native sim.
    public sealed class EnemyCapture : IDisposable
    {
        readonly CharacterBody owner;
        BaseAI[] brains=Array.Empty<BaseAI>();bool[] enabled=Array.Empty<bool>();
        public HealthComponent Target {get;private set;}
        float age;
        public EnemyCapture(CharacterBody owner){this.owner=owner;}
        public bool TryCapture(HealthComponent target)
        {
            if(Target)return Target==target;
            if(!NetworkServer.active||!owner||!target||!target.alive||!target.body||target==owner.healthComponent)return false;
            var body=target.body;
            if(!body.teamComponent||body.teamComponent.teamIndex==owner.teamComponent.teamIndex||body.isPlayerControlled)return false;
            // Stationary encounter structures have no safe movable body.
            if(!body.characterMotor&&!body.GetComponent<Rigidbody>())return false;
            Target=target;age=0;
            brains=body.master?body.master.GetComponents<BaseAI>():Array.Empty<BaseAI>();enabled=new bool[brains.Length];
            for(int i=0;i<brains.Length;i++){enabled[i]=brains[i].enabled;brains[i].enabled=false;}
            return true;
        }
        public bool Step(Vector3 forward,float dt,Vector3? anchor=null)
        {
            if(!Target||!Target.alive||!Target.body||!owner||!owner.healthComponent.alive||owner.currentVehicle||((age+=dt)>8f)){Dispose();return false;}
            var body=Target.body;
            var center=anchor??(owner.corePosition+forward.normalized*(owner.radius+body.radius+0.35f));
            var position=body.transform.position+center-body.corePosition;
            var hurt=body.GetComponent<SetStateOnHurt>();if(hurt)hurt.SetStun(0.2f);
            if(body.characterMotor){body.characterMotor.velocity=Vector3.zero;body.characterMotor.Motor.SetPosition(position);}
            else {var rigid=body.GetComponent<Rigidbody>();if(rigid){rigid.velocity=Vector3.zero;rigid.MovePosition(position);}}
            return true;
        }
        public void Dispose()
        {
            for(int i=0;i<brains.Length;i++)if(brains[i])brains[i].enabled=enabled[i];
            brains=Array.Empty<BaseAI>();enabled=Array.Empty<bool>();Target=null;
        }
    }
}
