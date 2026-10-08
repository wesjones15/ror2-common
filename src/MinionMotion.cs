using System;
using RoR2;
using UnityEngine;
#if ROR2COMMON_HOOKS
using MonoMod.RuntimeDetour;
#endif

namespace Ror2Common
{
    // RoR2's GetNotMoving uses a one-second stopwatch. Hosts with external motors
    // provide their own motion state, including airborne hovering, through Minion.Place.
    public sealed class MinionMotion : MonoBehaviour
    {
        bool moving = true;
        float stoppedAt;
#if ROR2COMMON_HOOKS
        static Hook hook;
        void Awake()
        {
            if (hook == null)
                hook = new Hook(typeof(CharacterBody).GetMethod(nameof(CharacterBody.GetNotMoving)),
                    (Func<Func<CharacterBody, bool>, CharacterBody, bool>)NotMoving);
        }
        static bool NotMoving(Func<CharacterBody, bool> orig, CharacterBody body)
        {
            var motion = body.GetComponent<MinionMotion>();
            return motion ? !motion.moving && Time.fixedTime - motion.stoppedAt >= 1f : orig(body);
        }
#endif
        public void Set(bool value)
        {
            if (moving && !value) stoppedAt = Time.fixedTime;
            moving = value;
        }
    }
}
