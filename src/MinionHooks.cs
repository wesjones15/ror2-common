#if ROR2COMMON_HOOKS
using System;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using RoR2;
using UnityEngine;

namespace Ror2Common
{
    // MinionHit's IL hook (see MinionHit.cs): built only with ROR2COMMON_HOOKS.
    public static partial class MinionHit
    {
        static ILHook takeDamage;

        static partial void InstallHooks(ref bool hooked)
        {
            try
            {
                var m = typeof(HealthComponent).GetMethod(nameof(HealthComponent.TakeDamage), new[] { typeof(DamageInfo) });
                bool found = false;
                takeDamage = new ILHook(m, il =>
                {
                    // attackerBody.corePosition - damageInfo.position: the minion's position in
                    // place of the body's while MinionHit.Deal runs.
                    var c = new ILCursor(il);
                    if (!c.TryGotoNext(MoveType.Before,
                            x => x.MatchCallvirt<CharacterBody>("get_corePosition"),
                            x => x.MatchLdarg(1),
                            x => x.MatchLdfld<DamageInfo>(nameof(DamageInfo.position)),
                            x => x.MatchCall<Vector3>("op_Subtraction")))
                        return;
                    c.Index++;
                    c.EmitDelegate<Func<Vector3, Vector3>>(AttackerPosition);
                    found = true;
                });
                if (!found)
                {
                    takeDamage.Dispose();
                    takeDamage = null;
                    CommonLog.Warn("MinionHit: TakeDamage's attacker vector not found; minion hits measure from the owner");
                    return;
                }
                hooked = true;
            }
            catch (Exception e)
            {
                CommonLog.Error($"MinionHit: hooking TakeDamage failed: {e}");
            }
        }
    }
}
#endif
