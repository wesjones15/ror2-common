#if ROR2COMMON_HOOKS
using System;
using System.Collections.Generic;
using System.Reflection;
using MonoMod.RuntimeDetour;
using RoR2;

namespace Ror2Common
{
    // RoR2 highlights a new item by drawing a HighlightRect (a screen-space square with a
    // name tag) around the item display's renderer: CharacterMaster.HighlightNewItem calls
    // CharacterModel.HighlightItemDisplay after 0.05 s. Hiding the display through its
    // visibility does not hide that highlight, so a native body with ItemDisplays off shows
    // an empty square where the item would be. Skip the highlight for those bodies only.
    internal static class ItemDisplayHooks
    {
        static readonly List<Hook> hooks = new List<Hook>();
        const string InstalledKey = "Ror2Common.ItemDisplayHooks.v1";

        internal static bool Install()
        {
            // ror2-common is compiled into each plugin. Only one copy needs these global
            // hooks, even with Mario, Olimar and Melee loaded together.
            if (AppDomain.CurrentDomain.GetData(InstalledKey) != null) return true;
            bool installed = false;
            installed |= TryHook("HighlightItemDisplay",
                (Action<Action<CharacterModel, ItemIndex>, CharacterModel, ItemIndex>)Item);
            installed |= TryHook("HighlightEquipentDisplay",
                (Action<Action<CharacterModel, EquipmentIndex>, CharacterModel, EquipmentIndex>)Equipment);
            installed |= TryHook("HighlightKeyItemDisplay",
                (Action<Action<CharacterModel, KeyItemIndex>, CharacterModel, KeyItemIndex>)KeyItem);
            if (installed) AppDomain.CurrentDomain.SetData(InstalledKey, true);
            CommonLog.Info(installed
                ? "item display highlight hooks installed"
                : "item display highlight hooks unavailable");
            return installed;
        }

        static bool TryHook(string method, Delegate to)
        {
            var target = typeof(CharacterModel).GetMethod(method,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (target == null)
            {
                CommonLog.Warn($"item displays: CharacterModel.{method} isn't in this RoR2; its highlight hook is skipped");
                return false;
            }
            try
            {
                hooks.Add(new Hook(target, to));
                return true;
            }
            catch (Exception e)
            {
                CommonLog.Warn($"item displays: hooking {method} failed: {e.Message}");
                return false;
            }
        }

        static void Item(Action<CharacterModel, ItemIndex> orig, CharacterModel self, ItemIndex index)
        {
            if (ItemDisplayRig.HighlightSuppressed(self)) return;
            orig(self, index);
        }

        static void Equipment(Action<CharacterModel, EquipmentIndex> orig, CharacterModel self, EquipmentIndex index)
        {
            if (ItemDisplayRig.HighlightSuppressed(self)) return;
            orig(self, index);
        }

        static void KeyItem(Action<CharacterModel, KeyItemIndex> orig, CharacterModel self, KeyItemIndex index)
        {
            if (ItemDisplayRig.HighlightSuppressed(self)) return;
            orig(self, index);
        }
    }
}
#endif
