using System;
using RoR2;

namespace Ror2Common
{
    // RoR2 events that items cause but that RoR2 doesn't announce, for native characters to
    // turn into their own resources, plus the item states kept off native bodies:
    //
    // - `Healed`: every heal RoR2 asks for (HealthComponent.Heal, server side) with the amount
    //   asked, before it's clamped to missing health, so a heal at full health still counts
    //   (Bustling Fungus on a healthy survivor). `nonRegen` false is passive regeneration.
    //   Lifesteal heals carry `ProcType.HealOnHit` (Leeching Seed) or `HealOnCrit` (Harvester's
    //   Scythe) in their proc chain.
    // - `AmmoPack`: Bandolier's ammo pickup restocked a body's skills (SkillLocator
    //   .ApplyAmmoPack), where the body has authority.
    // - Headstompers: its stomp (hold interact in the air) and jump boost drive the character
    //   motor; on a body with a `NativeBody` they never start, and the plugin gives the item
    //   to its own move (Mario's ground pound).
    //
    // The hooks need MonoMod (ROR2COMMON_HOOKS, ItemHooks.Hooks.cs); without it `Install`
    // returns false and the events never fire. Each hook is found by name and skipped with a
    // warning if this RoR2 hasn't got the method, so a newer or older build loses only that one.
    public static partial class ItemHooks
    {
        public delegate void HealHandler(HealthComponent target, float amount, ProcChainMask procChainMask, bool nonRegen);

        public static event HealHandler Healed;
        public static event Action<SkillLocator> AmmoPack;

        static bool tried, installed;

        /// Installs the hooks once; true if any went in.
        public static bool Install()
        {
            if (tried) return installed;
            tried = true;
            InstallHooks(ref installed);
            return installed;
        }

        static partial void InstallHooks(ref bool installed);

        static void RaiseHealed(HealthComponent target, float amount, ProcChainMask mask, bool nonRegen)
        {
            var h = Healed;
            if (h == null || !target || !(amount > 0f)) return;
            try { h(target, amount, mask, nonRegen); }
            catch (Exception e) { CommonLog.Error($"ItemHooks.Healed: {e}"); }
        }

        static void RaiseAmmoPack(SkillLocator locator)
        {
            var h = AmmoPack;
            if (h == null || !locator) return;
            try { h(locator); }
            catch (Exception e) { CommonLog.Error($"ItemHooks.AmmoPack: {e}"); }
        }
    }
}
