using System;
using System.Collections.Generic;
using System.Reflection;
using Ror2Common;
using UnityEngine;

static class Program
{
    sealed class Fighter:MonoBehaviour,IMeleeRecovery {
        public bool Recovering {get;set;}
        public bool RecoveryInvulnerable {get;set;}
        public int starts;public Vector3 destination;
        public void BeginFallRecovery(Vector3 feet){starts++;destination=feet;Recovering=true;RecoveryInvulnerable=true;}
    }
    static void Check(bool value,string text){if(!value)throw new Exception(text);}
    static bool Floor(Vector3 point,float rise,float drop,out Vector3 feet){feet=new Vector3(point.x,0,point.z);return true;}
    static void Call(object obj,string method)=>obj.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(obj,null);
    static void Main(){
        var components=new List<MonoBehaviour>();var body=new RoR2.CharacterBody();var fighter=new Fighter();var recovery=new MeleeRecovery();
        components.AddRange(new MonoBehaviour[]{body,fighter,recovery});foreach(var c in components)c.components=components;
        Call(recovery,"Awake");var guard=new Unstuck();guard.Step(default,false,true,false,.25f,1.8f,Floor,out _);
        Check(!recovery.CheckFall(guard,new Vector3(0,-149.9f,0),1.8f,Floor),"common drop threshold changed");
        Check(recovery.CheckFall(guard,new Vector3(0,-150,0),1.8f,Floor),"common fall threshold not routed");
        Check(fighter.starts==1&&fighter.destination.y==0&&body.buffs==1,"recovery and immediate protection");
        recovery.Refresh();Check(body.buffs==1,"protection stacked each tick");
        Check(!recovery.CheckFall(guard,new Vector3(0,-200,0),1.8f,Floor)&&fighter.starts==1,"duplicate falloff during KO");
        fighter.Recovering=false;recovery.Refresh();Check(body.buffs==1,"post-return protection removed early");
        fighter.RecoveryInvulnerable=false;recovery.Refresh();Check(body.buffs==0,"protection did not expire");
        body.buffs=1;fighter.RecoveryInvulnerable=true;recovery.Refresh();Check(body.buffs==2,"own protection missing");
        recovery.isActiveAndEnabled=false;Call(recovery,"OnDisable");recovery.Refresh();Call(recovery,"OnDestroy");Check(body.buffs==1,"cleanup removed another owner's invulnerability or re-added a disabled guard");
        recovery.isActiveAndEnabled=true;
        fighter.RecoveryInvulnerable=true;recovery.Refresh();body.healthComponent.alive=false;recovery.Refresh();Check(body.buffs==1,"death cleanup leaked own protection");
        body.healthComponent.alive=true;body.hasEffectiveAuthority=false;
        Check(!recovery.CheckFall(guard,new Vector3(0,-200,0),1.8f,Floor),"remote body started authoritative recovery");
        body.hasEffectiveAuthority=true;UnityEngine.Networking.NetworkServer.active=false;
        Check(!recovery.CheckFall(guard,new Vector3(0,-200,0),1.8f,Floor),"client started server-only recovery");
        Console.WriteLine("PASS: common fall threshold/safe point, duplicate prevention, timed protection ownership and death/disable cleanup");
    }
}
namespace UnityEngine {
    public class Object {public static implicit operator bool(Object obj)=>obj!=null;}
    public class MonoBehaviour:Object {
        public bool isActiveAndEnabled=true;
        public List<MonoBehaviour> components;
        public T GetComponent<T>() where T:class {foreach(var c in components)if(c is T value)return value;return null;}
        public T[] GetComponents<T>() {var result=new List<T>();foreach(var c in components)if(c is T value)result.Add(value);return result.ToArray();}
    }
    public struct Vector3 {
        public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 up=>new Vector3(0,1,0);public float sqrMagnitude=>x*x+y*y+z*z;
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
    }
    public static class Mathf {public const float PI=(float)Math.PI;public static float Max(float a,float b)=>Math.Max(a,b);public static float Sin(float a)=>(float)Math.Sin(a);public static float Cos(float a)=>(float)Math.Cos(a);}
}
namespace UnityEngine.Networking {public static class NetworkServer {public static bool active=true;}}
namespace RoR2 {
    public class HealthComponent:UnityEngine.Object {public bool alive=true;}
    public class CharacterBody:MonoBehaviour {public bool hasEffectiveAuthority=true;public HealthComponent healthComponent=new HealthComponent();public int buffs;public void AddBuff(object buff){buffs++;}public void RemoveBuff(object buff){buffs--;}}
    public static class RoR2Content {public static class Buffs {public static object HiddenInvincibility=new object();}}
    public class MapZone {public string name,zoneType,triggerType;}
}
namespace Ror2Common {
    static class WorldProbe {public static bool InMapZone(Vector3 point,out RoR2.MapZone zone){zone=null;return false;}public static bool Triggers(RoR2.MapZone zone,Vector3 point)=>false;}
    static class CommonLog {public static void Info(string text){}}
}
