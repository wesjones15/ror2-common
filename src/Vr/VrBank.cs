#if ROR2COMMON_VR
using System;
using System.Collections.Generic;
using System.Reflection;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using RoR2;
using UnityEngine;

namespace Ror2Common
{
    // A character's VR layout is the only thing RoR2 sees of the controllers' buttons. The VR mod
    // maps the motion controllers to RoR2's own actions (its "VRDefault" Rewired map: A interact,
    // B jump, the triggers and grips the skills), and those land on buttons a character's layout
    // gives other moves (Olimar's A jumps, Mario's B dives). So while a character drives the body
    // in its layout, each of RoR2's buttons reads the layout's button instead: interact where the
    // layout interacts, equipment, sprint and ping where it puts them, nothing from the VR mod's
    // map. Move and look (the stick axes) stay the VR mod's, so its turning and the menus are
    // untouched.
    //
    // The swap is where RoR2 samples Rewired (DLC4's PlayerCharacterMasterController
    // .PollInputToButtonBuffers, each frame: Player.GetButton into each button's buffer), so
    // everything RoR2 does with a button afterward is its own, unchanged: the buffering of short
    // presses, the sprint rules in PollButtonInput (toggle sprint, what stops a sprint), the
    // edges, the items that watch them. Only which physical button it is changes.
    public static class VrBank
    {
        public struct Buttons
        {
            public bool Skill1, Skill2, Skill3, Skill4, Interact, Jump, Sprint, Equipment, Ping;
        }

        sealed class Owned
        {
            public Buttons held;
            public float stamp;
        }

        static readonly Dictionary<InputBankTest, Owned> owned = new Dictionary<InputBankTest, Owned>();
        static ILHook hook;
        // PlayerCharacterMasterController.bodyInputs is private.
        static FieldInfo bodyInputs;
        static bool tried;
        // A layout that stops calling Drive (the character changed, VR controls went off) hands
        // the bank back to the VR mod after this long.
        const float Expiry = 0.25f;

        /// Each frame while the character's VR layout drives `bank`: which of RoR2's buttons are down.
        public static void Drive(InputBankTest bank, in Buttons buttons)
        {
            if (!bank || !Install()) return;
            if (!owned.TryGetValue(bank, out var o)) owned[bank] = o = new Owned();
            o.held = buttons;
            o.stamp = Time.unscaledTime;
        }

        /// The bank goes back to the VR mod's buttons now.
        public static void Release(InputBankTest bank)
        {
            if (bank) owned.Remove(bank);
        }

        static bool Install()
        {
            if (hook != null) return true;
            if (tried) return false;
            tried = true;
            try
            {
                const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                var sample = typeof(PlayerCharacterMasterController).GetMethod("PollInputToButtonBuffers", any);
                bodyInputs = typeof(PlayerCharacterMasterController).GetField("bodyInputs", any);
                if (sample == null || bodyInputs == null) { CommonLog.Log?.LogWarning("VR: no PlayerCharacterMasterController.PollInputToButtonBuffers / bodyInputs; the VR mod's buttons stay on"); return false; }
                int swapped = 0;
                hook = new ILHook(sample, il =>
                {
                    var c = new ILCursor(il);
                    // Each `player.GetButton(action)` becomes `Button(player, action, this)`.
                    while (c.TryGotoNext(MoveType.Before, i => i.MatchCallOrCallvirt<Rewired.Player>("GetButton")))
                    {
                        var called = (Mono.Cecil.MethodReference)c.Next.Operand;
                        if (called.Parameters.Count != 1 || called.Parameters[0].ParameterType.MetadataType != Mono.Cecil.MetadataType.Int32) { c.Index++; continue; }
                        c.Remove();
                        c.Emit(OpCodes.Ldarg_0);
                        c.EmitDelegate<Func<Rewired.Player, int, PlayerCharacterMasterController, bool>>(Button);
                        swapped++;
                    }
                });
                if (swapped == 0) { hook.Dispose(); hook = null; CommonLog.Log?.LogWarning("VR: RoR2's button sampling has no GetButton(int) to swap; the VR mod's buttons stay on"); return false; }
                return true;
            }
            catch (Exception e)
            {
                CommonLog.Log?.LogWarning($"VR: the button takeover failed to install: {e.Message}");
                return false;
            }
        }

        static bool Button(Rewired.Player player, int action, PlayerCharacterMasterController self)
        {
            if (owned.Count > 0 && self && bodyInputs.GetValue(self) is InputBankTest bank && bank && owned.TryGetValue(bank, out var o))
            {
                if (Time.unscaledTime - o.stamp > Expiry) owned.Remove(bank);
                else if (Layout(o.held, action, out bool down)) return down;
            }
            return player != null && player.GetButton(action);
        }

        // The layout's button for one of RoR2's actions; false for an action the layout leaves alone.
        static bool Layout(in Buttons b, int action, out bool down)
        {
            switch (action)
            {
                case RewiredConsts.Action.PrimarySkill: down = b.Skill1; return true;
                case RewiredConsts.Action.SecondarySkill: down = b.Skill2; return true;
                case RewiredConsts.Action.UtilitySkill: down = b.Skill3; return true;
                case RewiredConsts.Action.SpecialSkill: down = b.Skill4; return true;
                case RewiredConsts.Action.Interact: down = b.Interact; return true;
                case RewiredConsts.Action.Jump: down = b.Jump; return true;
                case RewiredConsts.Action.Sprint: down = b.Sprint; return true;
                case RewiredConsts.Action.Equipment: down = b.Equipment; return true;
                case RewiredConsts.Action.Ping: down = b.Ping; return true;
                default: down = false; return false;
            }
        }
    }
}
#endif
