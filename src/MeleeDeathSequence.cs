using System;
using UnityEngine;

namespace Ror2Common
{
    /// A detached visual after RoR2 kills its CharacterBody. Each fighter owns
    /// its disc pose, KO effect and audio; the timing and lifetime are shared.
    public interface IMeleeDeathSource : IDisposable
    {
        void Fall(float frame);
        void Vanish();
        void Step(float dt);
        void Draw();
    }

    public sealed class MeleeDeathSequence : MonoBehaviour
    {
        IMeleeDeathSource source;
        float age;
        bool vanished;
        const float FallSeconds=0.5f;
        const float Lifetime=2.2f;
        public static void Begin(IMeleeDeathSource source)
        {
            if(source==null)return;
            var go=new GameObject("Melee fall and KO");
            go.AddComponent<MeleeDeathSequence>().source=source;
        }
        void Update()
        {
            if(source==null)return;
            age+=Time.unscaledDeltaTime;
            try {
                if(age<FallSeconds)source.Fall(age*60f);
                else if(!vanished){source.Vanish();vanished=true;}
                source.Step(Time.unscaledDeltaTime);
            } catch(Exception e){CommonLog.Error("Melee death presentation: "+e);Destroy(gameObject);return;}
            if(age>=Lifetime)Destroy(gameObject);
        }
        void LateUpdate(){try{source?.Draw();}catch(Exception e){CommonLog.Error("Melee death draw: "+e);Destroy(gameObject);}}
        void OnDestroy(){source?.Dispose();source=null;}
    }

    public interface IMeleeRoundSound : IDisposable { void Step(); }
    /// RoR2 end-of-run trigger, with a Melee-style on-screen GAME! burst.
    /// The fighter supplies the source voice through its extracted audio bank.
    public sealed class MeleeGameBanner : MonoBehaviour
    {
        IMeleeRoundSound sound;
        float age;
        GUIStyle front,shadow;
        public static void Show(IMeleeRoundSound sound)
        {
            var go=new GameObject("Melee GAME banner");
            go.AddComponent<MeleeGameBanner>().sound=sound;
        }
        void Update()
        {
            age+=Time.unscaledDeltaTime;
            try{sound?.Step();}catch(Exception e){CommonLog.Error("Melee round sound: "+e);sound?.Dispose();sound=null;}
            if(age>3.2f)Destroy(gameObject);
        }
        void OnGUI()
        {
            if(age>2.8f)return;
            if(front==null){
                front=new GUIStyle(GUI.skin.label){alignment=TextAnchor.MiddleCenter,fontStyle=FontStyle.Bold};
                shadow=new GUIStyle(front);
            }
            float pop=age<0.18f?Mathf.Lerp(2.1f,1f,age/0.18f):1f;
            int size=Mathf.RoundToInt(Mathf.Min(Screen.width*0.15f,150f)*pop);
            front.fontSize=shadow.fontSize=size;
            float alpha=Mathf.Clamp01((3.0f-age)*3f);
            front.normal.textColor=new Color(1f,0.93f,0.60f,alpha);
            shadow.normal.textColor=new Color(0.15f,0.08f,0.06f,alpha);
            var rect=new Rect(0,Screen.height*0.31f,Screen.width,Screen.height*0.28f);
            for(int x=-4;x<=4;x+=4)for(int y=-4;y<=4;y+=4)
                if(x!=0||y!=0)GUI.Label(new Rect(rect.x+x,rect.y+y,rect.width,rect.height),"GAME!",shadow);
            GUI.Label(rect,"GAME!",front);
        }
        void OnDestroy(){sound?.Dispose();sound=null;}
    }
}
