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
            DontDestroyOnLoad(go);
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

    public interface IMeleeRoundEffect : IDisposable
    {
        /// Advance the burst animation and pump the round voice.
        void Step(float dt);
        /// Render the burst model after the camera has moved for this frame.
        void Draw();
    }
    /// RoR2 end-of-run trigger, with the original Melee on-screen GAME! burst.
    /// The fighter supplies the source voice through its extracted audio bank
    /// and the burst model through its extracted results scene (IfAll.usd).
    public sealed class MeleeGameBanner : MonoBehaviour
    {
        IMeleeRoundEffect effect;
        float age;
        public static void Show(IMeleeRoundEffect effect)
        {
            var go=new GameObject("Melee GAME banner");
            DontDestroyOnLoad(go);
            go.AddComponent<MeleeGameBanner>().effect=effect;
        }
        void Update()
        {
            age+=Time.unscaledDeltaTime;
            try{effect?.Step(Time.unscaledDeltaTime);}catch(Exception e){CommonLog.Error("Melee round effect: "+e);effect?.Dispose();effect=null;}
            if(age>3.2f)Destroy(gameObject);
        }
        void LateUpdate()
        {
            try{effect?.Draw();}catch(Exception e){CommonLog.Error("Melee round draw: "+e);effect?.Dispose();effect=null;}
        }
        void OnDestroy(){effect?.Dispose();effect=null;}
    }
}
