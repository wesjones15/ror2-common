using System;
using Ror2Common;

// Engine/device stand-ins: exercise the production display policy without
// launching Unity or opening an actual Windows sound device.
namespace UnityEngine { public class Transform {} public static class Time { public static float timeScale=1; } }
namespace Ror2Common
{
    public class ModelView {}
    public interface IDisplayActor { ModelView Models {get;} float Scale {get;} void Step(float dt,UnityEngine.Transform display); void Release(); }
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
        public bool UpSpecialFinished {get;set;}
        public int begun,stepped,released,sounded;
        public void BeginUpSpecial(){begun++;}
        public void Step(float dt){stepped++;}
        public bool RenderAudio(float[] stereo){sounded++;return begun==1;}
        public void Dispose(){released++;}
    }
    static void Check(bool ok,string what){if(!ok)throw new Exception(what);}
    static void Main()
    {
        var source=new Source();var display=new MeleeSelectDisplay(source);
        for(int i=0;i<10;i++)display.Step(1f/144,null);
        Check(source.begun==1&&source.sounded==10,"selection must play one up-special and its PCM");
        UnityEngine.Time.timeScale=0;display.Step(0,null);
        Check(PcmStream.paused&&source.sounded==10,"pause must pause sound");UnityEngine.Time.timeScale=1;
        source.UpSpecialFinished=true;display.Step(1f/60,null);display.Step(1f/60,null);
        Check(PcmStream.disposed==1&&source.sounded==10,"completed attack must stop its sound device");
        display.Release();display.Release();display.Step(0,null);
        Check(source.released==1&&PcmStream.disposed==1,"release must be idempotent");
        var portrait=new Source();var idle=new MeleeSelectDisplay(portrait,intro:false);
        idle.Step(1f/30,null);idle.Release();
        Check(portrait.begun==0&&portrait.sounded==0&&PcmStream.opened==1,"portrait must idle silently");
        var selectedAgain=new Source();var repeat=new MeleeSelectDisplay(selectedAgain);
        PcmStream.fail=true;repeat.Step(1f/60,null);repeat.Step(1f/60,null);repeat.Release();
        Check(selectedAgain.begun==1&&selectedAgain.stepped==2&&selectedAgain.released==1,"reselection/audio failure must preserve animation and cleanup");
        Console.WriteLine("PASS: shared up-B selection, one-shot sound, pause, idle portrait, reselection and cleanup");
    }
}
