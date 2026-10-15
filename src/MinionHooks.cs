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
                var m = typeof(HealthComponent).GetMethod("TakeDamageProcess", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(DamageInfo) }, null);
                bool found = false;
                takeDamage = new ILHook(m, il =>
                {
                    // DLC4 captures damageInfo in local 0 inside TakeDamageProcess.
                    // attackerBody.corePosition - damageInfo.position: the minion's position in
                    // place of the body's while MinionHit.Deal runs.
                    var c = new ILCursor(il);
                    if (!c.TryGotoNext(MoveType.Before,
                            x => x.MatchCallvirt<CharacterBody>("get_corePosition"),
                            x => x.OpCode == Mono.Cecil.Cil.OpCodes.Ldloc_0,
                            x => x.Operand is Mono.Cecil.FieldReference field && field.Name == "damageInfo",
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
                    CommonLog.Warn("MinionHit: TakeDamageProcess's attacker vector not found; minion hits measure from the owner");
                    return;
                }
                hooked = true;
            }
            catch (Exception e)
            {
                CommonLog.Error($"MinionHit: hooking TakeDamageProcess failed: {e}");
            }
        }
    }
}
#endif
