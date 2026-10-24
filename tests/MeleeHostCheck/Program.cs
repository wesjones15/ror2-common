using System;
using System.Collections.Generic;
using Ror2Common;
using UnityEngine;
using RoR2;

static class Program
{
    sealed class Fighter:MonoBehaviour,IMeleeArrival {
        public bool Arriving{get;set;}
    }
    static void Check(bool value,string text){if(!value)throw new Exception(text);}
    static void Main(){
        var go=new GameObject();var body=go.AddComponent<CharacterBody>();var fighter=go.AddComponent<Fighter>();
        var arrival=new MeleeArrival{gameObject=go,characterBody=body};arrival.OnEnter();
        arrival.fixedAge=2;arrival.FixedUpdate();Check(fighter.Arriving&&!arrival.outer.ended,"arrival did not wait for native return");
        // Player/authority timing must not gate native automatic release or own Interact.
        body.isPlayerControlled=true;var interaction=go.AddComponent<InteractionDriver>();arrival.Update();
        Check(interaction.interactableOverride==null,"automatic arrival stole Interact");
        fighter.Arriving=false;arrival.FixedUpdate();Check(arrival.outer.ended,"arrival did not finish");arrival.OnExit();
        Check(body.buffs==0,"arrival protection leaked");
        // Authority can arrive after the state as well.
        arrival=new MeleeArrival{gameObject=go,characterBody=body,isAuthority=false};arrival.OnEnter();
        arrival.isAuthority=true;arrival.FixedUpdate();Check(fighter.Arriving&&interaction.interactableOverride==null,"late authority required an interaction");
        var other=new GameObject();interaction.interactableOverride=other;arrival.OnExit();Check(interaction.interactableOverride==other,"cleanup cleared another interaction override");

        var input=go.AddComponent<InputBankTest>();input.ray=new Ray(new Vector3(0,1,0),Vector3.forward);
        CameraRigController.corrected=new Ray(new Vector3(0,3,-5),Vector3.forward);
        var ray=MeleeAim.CameraRay(body,input);Check(ray.origin.y==3&&ray.origin.z== -5&&CameraRigController.target==go,"body ray did not receive local camera correction");
        var own=go.AddComponent<Collider>();var enemy=new GameObject().AddComponent<Collider>();
        var muzzle=new Vector3(1,1,0);
        Physics.hits=new[]{new RaycastHit{collider=own,distance=5,point=new Vector3(0,3,0)},new RaycastHit{collider=enemy,distance=25,point=new Vector3(0,3,20)}};
        var direction=MeleeAim.ProjectileDirection(body,muzzle,ray);
        var expected=(new Vector3(0,3,20)-muzzle).normalized;
        Check((direction-expected).sqrMagnitude<1e-8f,"shot missed crosshair target or hit owner");
        Physics.hits=new[]{new RaycastHit{collider=enemy,distance=1,point=new Vector3(0,3,-4)}};
        direction=MeleeAim.ProjectileDirection(body,muzzle,ray);Check(direction.z==1&&direction.x==0,"camera obstruction reversed projectile into owner");
        Physics.hits=Array.Empty<RaycastHit>();direction=MeleeAim.ProjectileDirection(body,muzzle,ray);Check(direction.z>.999f,"empty sky shot did not follow camera");
        // A converging shot that keeps its crosshair point while its muzzle moves (the tongue).
        var point=MeleeAim.ProjectilePoint(body,muzzle,ray,20f);
        Check(Math.Abs(point.z-20f)<1e-4f&&Math.Abs(point.x-muzzle.x)<1e-4f,"empty sky point did not extend from the muzzle");
        Physics.hits=new[]{new RaycastHit{collider=enemy,distance=10,point=new Vector3(0,3,5)}};
        point=MeleeAim.ProjectilePoint(body,muzzle,ray,20f);
        Check(Math.Abs(point.z-5f)<1e-4f&&Math.Abs(point.y-3f)<1e-4f,"crosshair point missed the target");
        Physics.hits=new[]{new RaycastHit{collider=own,distance=5,point=new Vector3(0,3,0)},new RaycastHit{collider=enemy,distance=10,point=new Vector3(0,3,5)}};
        point=MeleeAim.ProjectilePoint(body,muzzle,ray,20f);
        Check(Math.Abs(point.z-5f)<1e-4f,"crosshair point used the owner's own collider");
        var facing=go.AddComponent<CharacterDirection>();var move=new Vector3(1,0,0);var aim=Vector3.forward;
        body.shouldAim=false;MeleeAim.FacingTarget(body,facing,move,aim,false,default);Check(facing.moveVector.x==1,"normal facing must use vanilla movement rule");
        body.shouldAim=true;MeleeAim.FacingTarget(body,facing,move,aim,false,default);Check(facing.moveVector.z==1,"aim facing must use real CharacterBody.shouldAim");
        MeleeAim.FacingTarget(body,facing,move,aim,true,move);Check(facing.moveVector.x==1,"authored action lost locked heading");
        MeleeAim.BeginAttack(body,facing,aim);Check(body.aimTimer==3&&facing.moveVector.z==1&&facing.forward.x==1,"attack must set real aim timer/target without snapping vanilla yaw");
        body.healthComponent=new HealthComponent{body=body,alive=true};body.teamComponent=new TeamComponent{teamIndex=1};body.corePosition=new Vector3(0,1,0);body.radius=.5f;
        var bossObject=new GameObject();var bossBody=bossObject.AddComponent<CharacterBody>();bossBody.isBoss=true;bossBody.teamComponent=new TeamComponent{teamIndex=2};bossBody.characterMotor=new CharacterMotor();bossBody.master=new GameObject().AddComponent<CharacterMaster>();var brain=bossBody.master.gameObject.AddComponent<RoR2.CharacterAI.BaseAI>();brain.enabled=true;
        var bossHealth=new HealthComponent{body=bossBody,alive=true};bossBody.healthComponent=bossHealth;
        var capture=new EnemyCapture(body);Check(!capture.TryCapture(bossHealth)&&brain.enabled,"boss entered capture or lost AI");
        var grapple=new MeleeGrapple(body);var hit=new Vector3(0,1,15);Check(grapple.Attach(bossHealth,hit),"boss grapple failed");
        Check(grapple.Step(true,false,.02f,out var desired)&&desired.z>0&&desired.z<=.501f,"grapple did not move player toward anchor at bounded speed");
        Check(bossBody.transform.position.sqrMagnitude==0&&bossBody.characterMotor.Motor.moves==0&&brain.enabled,"grapple moved or froze boss");
        Check(!grapple.Step(false,false,.02f,out _)&&!grapple.Attached,"B release did not detach");
        grapple.Attach(bossHealth,hit);Check(!grapple.Step(true,true,.02f,out _),"control interruption did not detach");
        bossBody.isBoss=false;Check(!grapple.Attach(bossHealth,hit),"ordinary enemy entered boss grapple");
        Check(capture.TryCapture(bossHealth)&&!brain.enabled,"ordinary capture stopped working");capture.Step(Vector3.forward,.02f);Check(bossBody.characterMotor.Motor.moves==1,"ordinary capture did not move enemy");capture.Dispose();Check(brain.enabled,"release did not restore AI");
        bossBody.isChampion=true;Check(!capture.TryCapture(bossHealth),"champion entered capture");
        var bossCollider=bossObject.AddComponent<Collider>();bossObject.AddComponent<HurtBox>().healthComponent=bossHealth;
        var wall=new GameObject().AddComponent<Collider>();Physics.hits=new[]{new RaycastHit{collider=bossCollider,distance=15,point=hit},new RaycastHit{collider=wall,distance=5,point=new Vector3(0,1,5)}};
        Check(MeleeGrapple.Trace(body,body.corePosition,Vector3.forward,20,out var trace,out var hurt)&&trace.collider==wall&&!hurt,"grapple passed through wall");
        Physics.hits=new[]{new RaycastHit{collider=bossCollider,distance=15,point=hit}};Check(MeleeGrapple.Trace(body,body.corePosition,Vector3.forward,20,out _,out hurt)&&hurt.healthComponent==bossHealth,"camera grapple did not find hurtbox");
        Check(Physics.lastSphereLength==20,"grapple range is not 20m");
        // Damage permission (MeleeDriver.BeginMove): a chained start finds the slot's charge already spent and must still hit.
        int stock=1;bool Spend(){if(stock<1)return false;stock--;return true;}
        bool first=MeleePermit.Allowed(true,true,Spend());bool chained=MeleePermit.Allowed(true,true,Spend());
        Check(first&&stock==0,"first start did not spend the charge");
        Check(chained,"a chained start with no charge left lost its damage permission");
        Check(!MeleePermit.Allowed(false,true,true),"another skill overriding the slot kept damage permission");
        Check(!MeleePermit.Allowed(true,false,true),"a machine that does not own the body got damage permission");
        // A move that defers its charge to success (Yoshi's Egg Lay) must not spend at the
        // start, but an overriding skill still does; ordinary moves are unchanged.
        Check(MeleePermit.SpendsAtStart(true,true),"an ordinary move deferred its charge");
        Check(!MeleePermit.SpendsAtStart(true,false),"the tongue spent its charge on a miss");
        Check(MeleePermit.SpendsAtStart(false,false),"an overriding skill deferred its own charge");
        Console.WriteLine("PASS: native-driven automatic arrival, protection/cleanup without owning Interact, corrected camera ray, owner filtering, crosshair convergence and near-camera obstruction");
        Console.WriteLine("PASS: 20m grapple wall occlusion, boss player-pull without capture/AI/movement, release/control interruption, ordinary hold/release and champion exclusion");
        Console.WriteLine("PASS: damage permission is the slot holding the fighter's own skill plus authority, whatever the charge");
    }
}

namespace UnityEngine {
    public class Object {public static implicit operator bool(Object o)=>o!=null;public static void Destroy(Object o){}}
    public class GameObject:Object {
        readonly List<MonoBehaviour> components=new List<MonoBehaviour>();public Transform transform=new Transform();
        public GameObject(string name=""){}
        public T AddComponent<T>() where T:MonoBehaviour,new(){var c=new T{gameObject=this};components.Add(c);return c;}
        public T GetComponent<T>() where T:class {foreach(var c in components)if(c is T t)return t;return null;}
        public T[] GetComponents<T>() {var result=new List<T>();foreach(var c in components)if(c is T t)result.Add(t);return result.ToArray();}
    }
    public class MonoBehaviour:Object {public bool enabled=true;public GameObject gameObject;public Transform transform=>gameObject.transform;public T GetComponent<T>() where T:class=>gameObject.GetComponent<T>();public T[] GetComponents<T>()=>gameObject.GetComponents<T>();}
    public class Transform:Object {public Vector3 position,forward=Vector3.forward;public Vector3 TransformPoint(Vector3 p)=>p+position;public Vector3 InverseTransformPoint(Vector3 p)=>p-position;public void SetParent(Transform parent,bool stay){}public T GetComponent<T>() where T:class=>null;}
    public struct Vector3 {
        public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public static Vector3 forward=>new Vector3(0,0,1);
        public float sqrMagnitude=>x*x+y*y+z*z;public Vector3 normalized=>sqrMagnitude>0?this*(1/MathF.Sqrt(sqrMagnitude)):default;
        public static Vector3 zero=>default;public static Vector3 MoveTowards(Vector3 a,Vector3 b,float d){var v=b-a;return v.sqrMagnitude<=d*d?b:a+v.normalized*d;}
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
    }
    public struct Ray {public Vector3 origin,direction;public Ray(Vector3 origin,Vector3 direction){this.origin=origin;this.direction=direction.normalized;}}
    public class Collider:MonoBehaviour {public bool isTrigger;public T GetComponentInParent<T>() where T:class=>GetComponent<T>();}
    public class Rigidbody:MonoBehaviour{public Vector3 velocity;public void MovePosition(Vector3 p){transform.position=p;}}
    public struct RaycastHit {public Collider collider;public float distance;public Vector3 point;}
    public enum QueryTriggerInteraction{Ignore,Collide}
    public static class Mathf {public const float Infinity=float.PositiveInfinity;public static float Max(float a,float b)=>Math.Max(a,b);public static float Min(float a,float b)=>Math.Min(a,b);}
    public static class Physics {public static float lastSphereLength;public static RaycastHit[] hits=Array.Empty<RaycastHit>();public static RaycastHit[] RaycastAll(Ray r,float d,int m,QueryTriggerInteraction q)=>hits;public static RaycastHit[] SphereCastAll(Vector3 o,float radius,Vector3 dir,float length,int mask,QueryTriggerInteraction q){lastSphereLength=length;return hits;}}
}
namespace UnityEngine.Networking {public static class NetworkServer {public static bool active=true;}}
namespace EntityStates {
    public enum InterruptPriority{Death}
    public class Machine {public bool ended;public void SetNextStateToMain(){ended=true;}}
    public class BaseState:MonoBehaviour {
        public CharacterBody characterBody;public bool isAuthority=true;public float fixedAge;public Machine outer=new Machine();
        public virtual void OnEnter(){}public virtual void Update(){}public virtual void FixedUpdate(){}public virtual void OnExit(){}public virtual InterruptPriority GetMinimumInterruptPriority()=>default;
    }
}
namespace RoR2 {
    public class CharacterBody:MonoBehaviour {public bool isBoss,isChampion;public UnityEngine.Object currentVehicle;public HealthComponent healthComponent;public TeamComponent teamComponent;public CharacterMotor characterMotor;public Vector3 corePosition,footPosition;public float radius;public bool isPlayerControlled,shouldAim;public float aimTimer;public void SetAimTimer(float seconds){aimTimer=seconds;}public ModelLocator modelLocator;public CharacterMaster master;public int buffs;public void AddBuff(object b){buffs++;}public void RemoveBuff(object b){buffs--;}}
    public class TeamComponent:UnityEngine.Object{public int teamIndex;}
    public class CharacterMotor:UnityEngine.Object{public Vector3 velocity;public Motor Motor=new Motor();}public class Motor{public int moves;public void SetPosition(Vector3 p){moves++;}}
    public class SetStateOnHurt:MonoBehaviour{public void SetStun(float seconds){}}
    public class CharacterDirection:MonoBehaviour {public Vector3 moveVector,forward=new Vector3(1,0,0);}
    public class CharacterMaster:MonoBehaviour{}
    public class ModelLocator:UnityEngine.Object {public Transform modelTransform;}
    public class Animator:MonoBehaviour {}
    public class InteractionDriver:MonoBehaviour {public GameObject interactableOverride;}
    public class InteractionProcFilter:MonoBehaviour {public bool shouldAllowOnInteractionBeginProc;}
    public class Interactor:MonoBehaviour{}
    public enum Interactability{Disabled,Available}
    public interface IInteractable{}
    public class InputBankTest:MonoBehaviour {public Ray ray;public Ray GetAimRay()=>ray;}
    public static class CameraRigController {public static Ray corrected;public static GameObject target;public static Ray ModifyAimRayIfApplicable(Ray ray,GameObject go,out float offset){target=go;offset=0;return corrected;}}
    public class HealthComponent:UnityEngine.Object {public bool alive;public CharacterBody body;}
    public class HurtBox:MonoBehaviour {public HealthComponent healthComponent;}
    public static class LayerIndex {public static readonly Layer entityPrecise=new Layer();public class Layer{public int mask;}}
    public static class RoR2Content {public static class Buffs {public static object HiddenInvincibility=new object();}}
}
namespace RoR2.CharacterAI {public class BaseAI:MonoBehaviour{}}
namespace Ror2Common {
    static class GameSettings {public static void HideRenderers(Transform t,bool h){}}
    static class WorldProbe {public static int Mask;}
}
