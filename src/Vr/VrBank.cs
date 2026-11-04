#if ROR2COMMON_VR
using System;
using System.Collections.Generic;
using System.Reflection;
using MonoMod.RuntimeDetour;
using RoR2;
using UnityEngine;

namespace Ror2Common
{
    // A character's VR layout is the only thing RoR2 sees of the controllers. The VR mod maps the
    // motion controllers to RoR2's own actions (its "VRDefault" Rewired map: A interact, B jump,
    // the triggers and grips the skills, the stick clicks sprint and ping), and those land on the
    // same buttons a character's layout gives other moves (Olimar's A jumps, Mario's B dives). So
    // while a character drives the body in its layout, what RoR2's input bank reads for each button
    // is the character's: interact where the layout interacts, equipment, sprint and ping where
    // the layout puts them, nothing from the VR mod's map. Move and look (the stick axes) stay the
    // VR mod's, so its turning and the menus are untouched.
    //
    // DLC4's PlayerCharacterMasterController buffers Rewired's buttons each frame and pushes them
    // into the bank each fixed step (PollButtonInput: PushState, which keeps the step before as
    // wasDown). After that push, an owned bank's buttons are set to the layout's, so the edges
    // (justPressed) are the layout's own. A press shorter than a fixed step still lands.
    public static class VrBank
    {
        public struct Buttons
        {
            public bool Skill1, Skill2, Skill3, Skill4, Interact, Jump, Sprint, Equipment, Ping;
        }

        sealed class Owned
        {
            public Buttons held, tapped;
            public float stamp;
        }

        static readonly Dictionary<InputBankTest, Owned> owned = new Dictionary<InputBankTest, Owned>();
        static Hook hook;
        // PlayerCharacterMasterController.bodyInputs is private.
        static FieldInfo bodyInputs;
        static bool tried;
        // A layout that stops calling Drive (the character changed, VR controls went off) hands
        // the bank back to the VR mod after this long.
        const float Expiry = 0.25f;

        /// Each frame while the character's VR layout drives `bank`: what RoR2 reads of each button.
        public static void Drive(InputBankTest bank, in Buttons buttons)
        {
            if (!bank || !Install()) return;
            if (!owned.TryGetValue(bank, out var o)) owned[bank] = o = new Owned();
            o.held = buttons;
            o.tapped = Or(o.tapped, buttons);
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
                var poll = typeof(PlayerCharacterMasterController).GetMethod("PollButtonInput", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                bodyInputs = typeof(PlayerCharacterMasterController).GetField("bodyInputs", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (poll == null || bodyInputs == null) { CommonLog.Log?.LogWarning("VR: no PlayerCharacterMasterController.PollButtonInput / bodyInputs; the VR mod's buttons stay on"); return false; }
                hook = new Hook(poll, (Action<Action<PlayerCharacterMasterController>, PlayerCharacterMasterController>)AfterPoll);
                return true;
            }
            catch (Exception e)
            {
                CommonLog.Log?.LogWarning($"VR: the button takeover failed to install: {e.Message}");
                return false;
            }
        }

        static void AfterPoll(Action<PlayerCharacterMasterController> orig, PlayerCharacterMasterController self)
        {
            orig(self);
            if (owned.Count == 0 || !self) return;
            var bank = bodyInputs.GetValue(self) as InputBankTest;
            if (!bank || !owned.TryGetValue(bank, out Owned o)) return;
            if (Time.unscaledTime - o.stamp > Expiry) { owned.Remove(bank); return; }
            var b = Or(o.held, o.tapped);
            o.tapped = default;
            bank.skill1.down = b.Skill1;
            bank.skill2.down = b.Skill2;
            bank.skill3.down = b.Skill3;
            bank.skill4.down = b.Skill4;
            bank.interact.down = b.Interact;
            bank.jump.down = b.Jump;
            bank.sprint.down = b.Sprint;
            bank.rawSprintInput.down = b.Sprint;
            bank.activateEquipment.down = b.Equipment;
            bank.ping.down = b.Ping;
        }

        static Buttons Or(in Buttons a, in Buttons b) => new Buttons
        {
            Skill1 = a.Skill1 || b.Skill1, Skill2 = a.Skill2 || b.Skill2, Skill3 = a.Skill3 || b.Skill3, Skill4 = a.Skill4 || b.Skill4,
            Interact = a.Interact || b.Interact, Jump = a.Jump || b.Jump, Sprint = a.Sprint || b.Sprint,
            Equipment = a.Equipment || b.Equipment, Ping = a.Ping || b.Ping,
        };
    }
}
#endif
