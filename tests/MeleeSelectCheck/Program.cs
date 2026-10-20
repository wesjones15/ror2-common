using System;
using Ror2Common;

// Engine/device stand-ins: exercise the production display policy without
// launching Unity or opening an actual Windows sound device.
namespace UnityEngine {
    public class Transform { public Vector3 position; public static implicit operator bool(Transform t)=>t!=null; }
    public class Camera { public Transform transform=new Transform(); public static Camera main=>null; public static implicit operator bool(Camera c)=>c!=null; }
    public struct Vector3 {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 zero=>default;
        public static Vector3 up=>new Vector3(0,1,0);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
    }
    public static class Mathf {
        public static float Lerp(float a,float b,float t)=>a+(b-a)*t;
        public static float Max(float a,float b)=>a>b?a:b;
        public static float Clamp01(float v)=>v<0?0:v>1?1:v;
    }
    public static class Time { public static float timeScale=1; public static float unscaledDeltaTime=1f/60f; }
}
namespace Ror2Common
{
    public class ModelView {}
    public interface IDisplayActor { ModelView Models {get;} float Scale {get;} void Step(float dt,UnityEngine.Transform display); void Release(); }
    public interface IDisplayEffects { void Draw(UnityEngine.Transform display); }
    public static class CommonLog { public static void Warn(string text) {} }
    public static class GameSettings { public static float SfxVolume()=>0.25f; }
    public class PcmStream : IDisposable
    {
        public static int opened,disposed,rendered;
        public static bool paused,fail;
        public PcmStream(int rate) { if(rate!=32000)throw new Exception("rate");opened++; }
        public void Pump(Func<float[],bool> render,float gain,bool pause)
        {
            paused=pause;if(pause)return;
            if(fail)throw new Exception("device failure");
            if(gain!=0.25f||!render(new float[1024]))throw new Exception("PCM/gain");rendered++;
        }
        public void Dispose(){disposed++;}
    }
}
class Program
{
    class Source : IMeleeSelectSource
    {
        public ModelView Models {get;}=new ModelView();
        public float Scale=>0.1f;
        public int AudioRate=>32000;
        public bool Speaking;
        public bool AudioActive=>Speaking||!IntroFinished;
        public bool IntroFinished {get;set;}
        public UnityEngine.Vector3 PoseAnchor=>new UnityEngine.Vector3(0,stepped*10,0);
        public UnityEngine.Vector3 DrawOffset {get;set;}
        public float RiseMetres=>stepped>0?stepped:0f;
        public int begun,stepped,released,sounded;
        public void BeginIntro(){begun++;IntroFinished=false;}
        public void Step(float dt){stepped++;}
        public bool RenderAudio(float[] stereo){sounded++;return begun>0;}
        public void Dispose(){released++;}
    }
    static void Check(bool ok,string what){if(!ok)throw new Exception(what);}
    static void Main()
    {
        var source=new Source();var display=new MeleeSelectDisplay(source);
        for(int i=0;i<10;i++)display.Step(1f/144,null);
        Check(source.begun==1&&source.sounded==10,"selection must play one intro and its PCM");
        Check(source.DrawOffset.y==0,"authored intro motion must remain visible above the display floor");
        UnityEngine.Time.timeScale=0;display.Step(0,null);
        Check(!PcmStream.paused&&source.sounded==11,"paused world must not freeze lobby audio");UnityEngine.Time.timeScale=1;
        source.IntroFinished=true;display.Step(1f/60,null);display.Step(1f/60,null);
        Check(PcmStream.disposed==1&&source.sounded==11,"completed attack must stop its sound device");
        display.Release();display.Release();display.Step(0,null);
        Check(source.released==1&&PcmStream.disposed==1,"release must be idempotent");
        var portrait=new Source();var idle=new MeleeSelectDisplay(portrait,intro:false);
        idle.Step(1f/30,null);idle.Release();
        Check(portrait.begun==0&&portrait.sounded==0&&PcmStream.opened==1,"portrait must idle silently");
        var selectedAgain=new Source();var repeat=new MeleeSelectDisplay(selectedAgain);
        PcmStream.fail=true;repeat.Step(1f/60,null);repeat.Step(1f/60,null);repeat.Release();
        Check(selectedAgain.begun==1&&selectedAgain.stepped==2&&selectedAgain.released==1,"reselection/audio failure must preserve animation and cleanup");
        PcmStream.fail=false;var loopSource=new Source();var loop=new MeleeSelectDisplay(loopSource);
        loop.Step(1f/60,null);loopSource.IntroFinished=true;
        for(int i=0;i<600;i++)loop.Step(1f/60,null);
        Check(loopSource.begun==1&&loopSource.IntroFinished&&loopSource.stepped==601,"completed preview must continue idling without replaying its intro");loop.Release();
        // A name call that outlives the intro keeps the stream open until it ends.
        int disposedBefore=PcmStream.disposed;
        var talking=new Source{Speaking=true};var talk=new MeleeSelectDisplay(talking);
        talk.Step(1f/60,null);talking.IntroFinished=true;talk.Step(1f/60,null);talk.Step(1f/60,null);
        Check(PcmStream.disposed==disposedBefore&&talking.sounded>=3,"the announcer name call must play to its end after the intro finishes");
        talking.Speaking=false;talk.Step(1f/60,null);
        Check(PcmStream.disposed==disposedBefore+1,"the sound device must close once the name call ends");talk.Release();
        Console.WriteLine("PASS: shared selection intro, one-shot sound, pause, idle portrait, reselection and cleanup");
    }
}
