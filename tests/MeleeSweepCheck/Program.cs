using System;
using RoR2;
using UnityEngine;
using Ror2Common;
class Program
{
    static void Check(bool ok,string message) { if(!ok)throw new Exception(message); }
    static void Main()
    {
        var owner=new CharacterBody { teamComponent=new TeamComponent {teamIndex=1} };
        var enemy=new CharacterBody { teamComponent=new TeamComponent {teamIndex=2} };
        var target=new Collider { hurt=new HurtBox {healthComponent=new HealthComponent {body=enemy,alive=true}} };
        var wall=new Collider();
        Physics.Query=(radius,mask)=> {
            if(mask==1) {Check(radius<=0.1f,"large damage sphere used for terrain");return Array.Empty<RaycastHit>();}
            Check(radius==3f,"enemy radius shrank");
            return new[]{new RaycastHit {collider=target,distance=4}};
        };
        Check(MeleeSweep.Ranged(owner,default,3,default,10,out var hit)&&hit.collider==target,"missed enemy above nearby floor");
        // The drawn lash and Samus's Charge Shot share the narrow terrain probe.
        Physics.Query=(radius,mask)=>{Check(radius==MeleeSweep.TerrainRadius,"lash terrain probe is not the narrow centreline");return new[]{new RaycastHit {collider=wall,distance=2.5f}};};
        Check(MeleeSweep.Terrain(owner,default,default,10,out hit)&&hit.collider==wall&&hit.distance==2.5f,"lash did not stop at terrain");
        Physics.Query=(radius,mask)=>new[]{new RaycastHit {collider=mask==1?wall:target,distance=mask==1?2:4}};
        Check(MeleeSweep.Ranged(owner,default,3,default,10,out hit)&&hit.collider==wall,"enemy assist bypassed nearer wall");
        Physics.Query=(radius,mask)=>new[]{new RaycastHit {collider=mask==1?wall:target,distance=mask==1?6:4}};
        Check(MeleeSweep.Ranged(owner,default,3,default,10,out hit)&&hit.collider==target,"farther wall stole enemy contact");
        enemy.teamComponent.teamIndex=1;
        Check(MeleeSweep.Ranged(owner,default,3,default,10,out hit)&&hit.collider==wall,"ally blocked shot");
        Physics.Query=(radius,mask)=>new[]{new RaycastHit {collider=wall,distance=0,normal=Vector3.zero}};
        Check(!MeleeSweep.Ranged(owner,default,3,default,10,out hit),"initial-overlap sentinel blocked shot");
        Check(MeleeSweep.Terrain(owner,default,default,10,out hit)&&hit.collider==wall,"a lash starting inside geometry must stop, not pass through");
        Console.WriteLine("PASS: shared energy-shot terrain radius, full enemy radius, nearest wall/enemy ordering, ally filtering and overlap sentinel");
        Console.WriteLine("PASS: the drawn lash reuses the narrow terrain probe and stops at the contact, including when it starts inside geometry");
    }
}
namespace UnityEngine
{
    public class Object {public static implicit operator bool(Object v)=>v!=null;}
    public struct Vector3 {public static Vector3 zero=>default;public static bool operator ==(Vector3 a,Vector3 b)=>true;public static bool operator !=(Vector3 a,Vector3 b)=>false;public override bool Equals(object o)=>o is Vector3;public override int GetHashCode()=>0;}
    public class Collider:Object {public HurtBox hurt;public CharacterBody body;public T GetComponent<T>() where T:class=>hurt as T;public T GetComponentInParent<T>() where T:class=>body as T;}
    public struct RaycastHit {public Collider collider;public float distance;public Vector3 normal;}
    public enum QueryTriggerInteraction {Ignore}
    public static class Mathf {public static float Min(float a,float b)=>Math.Min(a,b);}
    public static class Physics {public static Func<float,int,RaycastHit[]> Query;public static int SphereCastNonAlloc(Vector3 from,float radius,Vector3 dir,RaycastHit[] buffer,float length,int mask,QueryTriggerInteraction q){var hits=Query(radius,mask);Array.Copy(hits,buffer,hits.Length);return hits.Length;}}
}
namespace RoR2
{
    public class CharacterBody:UnityEngine.Object {public TeamComponent teamComponent;}
    public class TeamComponent {public int teamIndex;public static implicit operator bool(TeamComponent v)=>v!=null;}
    public class HealthComponent:UnityEngine.Object {public CharacterBody body;public bool alive;}
    public class HurtBox:UnityEngine.Object {public HealthComponent healthComponent;}
    public struct LayerIndex {public int mask;public static LayerIndex entityPrecise=>new LayerIndex {mask=2};}
}
namespace Ror2Common {public static class WorldProbe {public static int Mask=>1;}}
