using System;
using System.Collections.Generic;
using Ror2Common;
using UnityEngine;
using RoR2;

static class Program
{
    sealed class Fighter:MonoBehaviour,IMeleeArrival {
        public bool Arriving{get;set;}public bool ArrivalWaiting{get;set;}public int released;
        public void ReleaseArrival(){ArrivalWaiting=false;released++;}
    }
    static void Check(bool value,string text){if(!value)throw new Exception(text);}
    static void Main(){
        var go=new GameObject();var body=go.AddComponent<CharacterBody>();var fighter=go.AddComponent<Fighter>();
        var arrival=new MeleeArrival{gameObject=go,characterBody=body};arrival.OnEnter();
        fighter.ArrivalWaiting=true;arrival.fixedAge=2;arrival.FixedUpdate();
        Check(fighter.released==0,"late player assignment mistaken for AI arrival");
        // Both player flag and InteractionDriver arrive after OnEnter.
        body.isPlayerControlled=true;var interaction=go.AddComponent<InteractionDriver>();arrival.Update();
        var prompt=interaction.interactableOverride?.GetComponent<InteractTarget>();
        var interactor=go.AddComponent<Interactor>();
        Check(prompt&&prompt.GetContextString(interactor)=="Start"&&prompt.GetInteractability(interactor)==Interactability.Available,"late player did not receive vanilla Start target");
        Check(prompt.GetInteractability(new GameObject().AddComponent<Interactor>())==Interactability.Disabled,"other player can release this figurine");
        prompt.OnInteractionBegin(interactor);arrival.FixedUpdate();
        Check(fighter.released==1&&interaction.interactableOverride==null,"Interact did not release or clear prompt");
        fighter.Arriving=false;arrival.FixedUpdate();Check(arrival.outer.ended,"arrival did not finish");arrival.OnExit();
        Check(body.buffs==0,"arrival protection leaked");
        // Authority can arrive after the state as well.
        arrival=new MeleeArrival{gameObject=go,characterBody=body,isAuthority=false};arrival.OnEnter();
        fighter.ArrivalWaiting=true;arrival.isAuthority=true;arrival.FixedUpdate();Check(interaction.interactableOverride,"late authority missed prompt");
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
        var facing=go.AddComponent<CharacterDirection>();var move=new Vector3(1,0,0);var aim=Vector3.forward;
        body.shouldAim=false;MeleeAim.FacingTarget(body,facing,move,aim,false,default);Check(facing.moveVector.x==1,"normal facing must use vanilla movement rule");
        body.shouldAim=true;MeleeAim.FacingTarget(body,facing,move,aim,false,default);Check(facing.moveVector.z==1,"aim facing must use real CharacterBody.shouldAim");
        MeleeAim.FacingTarget(body,facing,move,aim,true,move);Check(facing.moveVector.x==1,"authored action lost locked heading");
        MeleeAim.BeginAttack(body,facing,aim);Check(body.aimTimer==3&&facing.moveVector.z==1&&facing.forward.x==1,"attack must set real aim timer/target without snapping vanilla yaw");
        Console.WriteLine("PASS: delayed player/authority/interaction binding, native Start target/release/cleanup, corrected camera ray, owner filtering, crosshair convergence and near-camera obstruction");
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
    public class MonoBehaviour:Object {public GameObject gameObject;public Transform transform=>gameObject.transform;public T GetComponent<T>() where T:class=>gameObject.GetComponent<T>();public T[] GetComponents<T>()=>gameObject.GetComponents<T>();}
    public class Transform:Object {public Vector3 position,forward=Vector3.forward;public void SetParent(Transform parent,bool stay){}public T GetComponent<T>() where T:class=>null;}
    public struct Vector3 {
        public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public static Vector3 forward=>new Vector3(0,0,1);
        public float sqrMagnitude=>x*x+y*y+z*z;public Vector3 normalized=>sqrMagnitude>0?this*(1/MathF.Sqrt(sqrMagnitude)):default;
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
    }
    public struct Ray {public Vector3 origin,direction;public Ray(Vector3 origin,Vector3 direction){this.origin=origin;this.direction=direction.normalized;}}
    public class Collider:MonoBehaviour {public T GetComponentInParent<T>() where T:class=>GetComponent<T>();}
    public struct RaycastHit {public Collider collider;public float distance;public Vector3 point;}
    public enum QueryTriggerInteraction{Ignore}
    public static class Mathf {public const float Infinity=float.PositiveInfinity;}
    public static class Physics {public static RaycastHit[] hits=Array.Empty<RaycastHit>();public static RaycastHit[] RaycastAll(Ray r,float d,int m,QueryTriggerInteraction q)=>hits;}
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
    public class CharacterBody:MonoBehaviour {public bool isPlayerControlled,shouldAim;public float aimTimer;public void SetAimTimer(float seconds){aimTimer=seconds;}public ModelLocator modelLocator;public CharacterMaster master;public int buffs;public void AddBuff(object b){buffs++;}public void RemoveBuff(object b){buffs--;}}
    public class CharacterDirection:MonoBehaviour {public Vector3 moveVector,forward=new Vector3(1,0,0);}
    public class CharacterMaster:MonoBehaviour{}
    public class ModelLocator:UnityEngine.Object {public Transform modelTransform;}
    public class Animator:MonoBehaviour {public bool enabled;}
    public class InteractionDriver:MonoBehaviour {public GameObject interactableOverride;}
    public class InteractionProcFilter:MonoBehaviour {public bool shouldAllowOnInteractionBeginProc;}
    public class Interactor:MonoBehaviour{}
    public enum Interactability{Disabled,Available}
    public interface IInteractable{}
    public class InputBankTest:MonoBehaviour {public Ray ray;public Ray GetAimRay()=>ray;}
    public static class CameraRigController {public static Ray corrected;public static GameObject target;public static Ray ModifyAimRayIfApplicable(Ray ray,GameObject go,out float offset){target=go;offset=0;return corrected;}}
    public class HealthComponent:UnityEngine.Object {public CharacterBody body;}
    public class HurtBox:MonoBehaviour {public HealthComponent healthComponent;}
    public static class LayerIndex {public static readonly Layer entityPrecise=new Layer();public class Layer{public int mask;}}
    public static class RoR2Content {public static class Buffs {public static object HiddenInvincibility=new object();}}
}
namespace RoR2.CharacterAI {public class BaseAI:MonoBehaviour{}}
namespace Ror2Common {
    static class GameSettings {public static void HideRenderers(Transform t,bool h){}}
    static class WorldProbe {public static int Mask;}
}
