#if ROR2COMMON_HOOKS
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using MonoMod.RuntimeDetour;
using RoR2;
using UnityEngine.Networking;

namespace Ror2Common
{
    // ItemHooks' detours (see ItemHooks.cs): built only with ROR2COMMON_HOOKS.
    public static partial class ItemHooks
    {
        static readonly List<Hook> hooks = new List<Hook>();
        static FieldInfo stomperBody;

        static partial void InstallHooks(ref bool installed)
        {
            installed |= PickupIconHooks.Install();
            installed |= ItemDisplayHooks.Install();
            installed |= TryHook("HealthComponent.Heal",
                typeof(HealthComponent).GetMethod(nameof(HealthComponent.Heal),
                    new[] { typeof(float), typeof(ProcChainMask), typeof(bool) }),
                (Func<Func<HealthComponent, float, ProcChainMask, bool, float>, HealthComponent, float, ProcChainMask, bool, float>)OnHeal);
            installed |= TryHook("SkillLocator.ApplyAmmoPack",
                typeof(SkillLocator).GetMethod(nameof(SkillLocator.ApplyAmmoPack), Type.EmptyTypes),
                (Action<Action<SkillLocator>, SkillLocator>)OnAmmoPack);
            try { installed |= HookHeadstompers(); }
            catch (Exception e) { CommonLog.Warn($"ItemHooks: Headstompers' states aren't as expected in this RoR2 ({e.Message})"); }
        }

        // Its own method, so a RoR2 without these types fails here (caught) and nowhere else.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool HookHeadstompers()
        {
            stomperBody = ByName.Field(typeof(EntityStates.Headstompers.BaseHeadstompersState), "body");
            if (stomperBody == null)
            {
                CommonLog.Warn("ItemHooks: Headstompers' state has no body field in this RoR2; it isn't kept off native bodies");
                return false;
            }
            return TryHook("HeadstompersIdle.FixedUpdateAuthority",
                typeof(EntityStates.Headstompers.HeadstompersIdle).GetMethod("FixedUpdateAuthority",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public),
                (Action<Action<EntityStates.Headstompers.HeadstompersIdle>, EntityStates.Headstompers.HeadstompersIdle>)OnStomperIdle);
        }

        static bool TryHook(string what, MethodInfo target, Delegate to)
        {
            if (target == null)
            {
                CommonLog.Warn($"ItemHooks: {what} isn't in this RoR2; that hook is skipped");
                return false;
            }
            try
            {
                hooks.Add(new Hook(target, to));
                return true;
            }
            catch (Exception e)
            {
                CommonLog.Error($"ItemHooks: hooking {what} failed: {e.Message}");
                return false;
            }
        }

        static float OnHeal(Func<HealthComponent, float, ProcChainMask, bool, float> orig, HealthComponent self,
            float amount, ProcChainMask mask, bool nonRegen)
        {
            if (NetworkServer.active && self && self.alive) RaiseHealed(self, amount, mask, nonRegen);
            return orig(self, amount, mask, nonRegen);
        }

        static void OnAmmoPack(Action<SkillLocator> orig, SkillLocator self)
        {
            orig(self);
            // On the server for a body a client owns, RoR2 only forwards the pickup; the client
            // applies it (SkillLocator.HandleAmmoPackPickup) and this runs there.
            var id = self ? self.GetComponent<NetworkIdentity>() : null;
            bool forwarded = NetworkServer.active && id && !id.hasAuthority;
            if (!forwarded) RaiseAmmoPack(self);
        }

        static void OnStomperIdle(Action<EntityStates.Headstompers.HeadstompersIdle> orig,
            EntityStates.Headstompers.HeadstompersIdle self)
        {
            var body = stomperBody?.GetValue(self) as CharacterBody;
            if (body && body.GetComponent<NativeBody>()) return;
            orig(self);
        }
    }
}
#endif
