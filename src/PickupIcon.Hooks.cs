#if ROR2COMMON_HOOKS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MonoMod.RuntimeDetour;
using RoR2;
using RoR2.Orbs;

namespace Ror2Common
{
    internal static class PickupIconHooks
    {
        static readonly List<Hook> hooks = new List<Hook>();
        const string InstalledKey = "Ror2Common.PickupIconHooks.v2";

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
                CommonLog.Info("Pickup icon sprite binding installed (v2)");
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

        static IEnumerator Item(Func<ItemTakenOrbEffect, IEnumerator> orig, ItemTakenOrbEffect self) =>
            PickupIcon.After(orig(self), () => {
                if (!self || !self.isActiveAndEnabled) return;
                var effect = self.GetComponent<EffectComponent>();
                if (!effect || effect.effectData == null) return;
                var index = (ItemIndex)Util.UintToIntMinusOne(effect.effectData.genericUInt);
                var pickup = PickupCatalog.GetPickupDef(PickupCatalog.FindPickupIndex(index));
                // Use the pickup definition, the same provider available to the HUD.
                AsyncIconProvider provider = pickup ?? (AsyncIconProvider)ItemCatalog.GetItemDef(index);
                PickupIcon.Bind(self.iconSpriteRenderer, provider, "item " + index);
            });

        static IEnumerator Pickup(Func<PickupTakenOrbEffect, IEnumerator> orig, PickupTakenOrbEffect self) =>
            PickupIcon.After(orig(self), () => {
                if (!self || !self.isActiveAndEnabled) return;
                var effect = self.GetComponent<EffectComponent>();
                if (!effect || effect.effectData == null) return;
                var index = new PickupIndex(Util.UintToIntMinusOne(effect.effectData.genericUInt));
                PickupIcon.Bind(self.iconSpriteRenderer, PickupCatalog.GetPickupDef(index), "pickup " + index.value);
            });
    }
}
#endif
