#if ROR2COMMON_HOOKS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MonoMod.RuntimeDetour;
using RoR2.Orbs;

namespace Ror2Common
{
    internal static class PickupIconHooks
    {
        static readonly List<Hook> hooks = new List<Hook>();
        const string InstalledKey = "Ror2Common.PickupIconHooks.v1";

        internal static bool Install()
        {
            // ror2-common is compiled into each plugin. Only one copy needs these
            // global effect hooks, even with Mario, Olimar and Melee loaded together.
            if (AppDomain.CurrentDomain.GetData(InstalledKey) != null) return true;
            try
            {
                hooks.Add(new Hook(typeof(ItemTakenOrbEffect).GetMethod("DelayedUpdateSprite",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
                    (Func<Func<ItemTakenOrbEffect, IEnumerator>, ItemTakenOrbEffect, IEnumerator>)Item));
                hooks.Add(new Hook(typeof(PickupTakenOrbEffect).GetMethod("DelayedUpdateSprite",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
                    (Func<Func<PickupTakenOrbEffect, IEnumerator>, PickupTakenOrbEffect, IEnumerator>)Pickup));
                AppDomain.CurrentDomain.SetData(InstalledKey, true);
                return true;
            }
            catch (Exception e)
            {
                foreach (var hook in hooks) hook.Dispose();
                hooks.Clear();
                CommonLog.Warn("Pickup icon hooks unavailable: " + e.Message);
                return false;
            }
        }

        static IEnumerator Item(Func<ItemTakenOrbEffect, IEnumerator> orig, ItemTakenOrbEffect self)
        {
            PickupIcon.Prepare(self.iconSpriteRenderer);
            return orig(self);
        }

        static IEnumerator Pickup(Func<PickupTakenOrbEffect, IEnumerator> orig, PickupTakenOrbEffect self)
        {
            PickupIcon.Prepare(self.iconSpriteRenderer);
            return orig(self);
        }
    }
}
#endif
