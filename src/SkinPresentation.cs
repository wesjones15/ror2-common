using System;
using System.Collections.Generic;
using RoR2;
using RoR2.Skills;
using UnityEngine;
#if ROR2COMMON_HOOKS
using System.Reflection;
using System.Runtime.CompilerServices;
using RoR2.UI;
using MonoMod.RuntimeDetour;
#endif

namespace Ror2Common
{
    /// Per-loadout artwork. Only Mario/Olimar register; never replaces a skill's behavior.
    public static class SkinPresentation
    {
        sealed class Skin
        {
            public string nameToken;
            public readonly Dictionary<SkillDef, Sprite> icons = new Dictionary<SkillDef, Sprite>();
        }
        static readonly Dictionary<GameObject, Dictionary<uint, Skin>> bodies = new Dictionary<GameObject, Dictionary<uint, Skin>>();
        static int revision;

        public static void Register(GameObject body, uint skinIndex, string nameToken)
        {
            if (!bodies.TryGetValue(body, out var skins)) bodies[body] = skins = new Dictionary<uint, Skin>();
            skins[skinIndex] = new Skin { nameToken = nameToken };
            revision++;
#if ROR2COMMON_HOOKS
            Install();
#endif
        }

        public static void SetIcon(GameObject body, uint skinIndex, SkillDef skill, Sprite icon)
        {
            if (!skill || !icon || !bodies.TryGetValue(body, out var skins) || !skins.TryGetValue(skinIndex, out var skin)) return;
            skin.icons[skill] = icon;
            revision++;
        }

        static Skin Find(GameObject body, uint index) => body && bodies.TryGetValue(body, out var skins)
            && skins.TryGetValue(index, out var skin) ? skin : null;

        public static string NameToken(GameObject body, uint index) => Find(body, index)?.nameToken;
        public static Sprite Icon(GameObject body, uint index, SkillDef skill) => skill && Find(body, index) is Skin skin
            && skin.icons.TryGetValue(skill, out var icon) && icon ? icon : (skill ? skill.icon : null);

        // Vanilla menu builders copy SkillDef.icon into their own images. Scope the
        // substitution to that synchronous build and restore even if a builder throws.
        public static IDisposable MenuIcons(GameObject body, uint index) => new IconScope(Find(body, index));
        sealed class IconScope : IDisposable
        {
            readonly List<(SkillDef skill, Sprite icon)> saved = new List<(SkillDef, Sprite)>();
            public IconScope(Skin skin)
            {
                if (skin == null) return;
                foreach (var pair in skin.icons)
                {
                    if (!pair.Key || !pair.Value) continue;
                    saved.Add((pair.Key, pair.Key.icon)); pair.Key.icon = pair.Value;
                }
            }
            public void Dispose() { foreach (var pair in saved) if (pair.skill) pair.skill.icon = pair.icon; saved.Clear(); }
        }

#if ROR2COMMON_HOOKS
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly FieldInfo localUser = typeof(CharacterSelectController).GetField("localUser", Fields);
        static readonly FieldInfo displayData = typeof(LoadoutPanelController).GetField("currentDisplayData", Fields);
        static readonly MethodInfo rebuildSelect = typeof(CharacterSelectController).GetMethod("RebuildLocal", Fields);
        static readonly MethodInfo rebuildLoadout = typeof(LoadoutPanelController).GetMethod("Rebuild", Fields);
        sealed class Stamp
        {
            public Stamp() { }
            public GameObject body;
            public uint skin;
            public int revision = -1;
        }
        static readonly ConditionalWeakTable<CharacterSelectController, Stamp> selects = new ConditionalWeakTable<CharacterSelectController, Stamp>();
        static readonly ConditionalWeakTable<LoadoutPanelController, Stamp> loadouts = new ConditionalWeakTable<LoadoutPanelController, Stamp>();
        static bool installed;
        static readonly List<Hook> hooks = new List<Hook>();
        static void Install()
        {
            if (installed) return;
            if (localUser == null || displayData == null || rebuildSelect == null || rebuildLoadout == null)
            { CommonLog.Warn("Skin presentation: menu API changed; keeping vanilla presentation"); return; }
            try
            {
                hooks.Add(new Hook(rebuildSelect, (Action<Action<CharacterSelectController, bool>, CharacterSelectController, bool>)Select));
                hooks.Add(new Hook(typeof(CharacterSelectController).GetMethod("Update", Fields), (Action<Action<CharacterSelectController>, CharacterSelectController>)SelectUpdate));
                hooks.Add(new Hook(rebuildLoadout, (Action<Action<LoadoutPanelController>, LoadoutPanelController>)Loadout));
                hooks.Add(new Hook(typeof(LoadoutPanelController).GetMethod("Update", Fields), (Action<Action<LoadoutPanelController>, LoadoutPanelController>)LoadoutUpdate));
                hooks.Add(new Hook(typeof(SkillIcon).GetMethod("LateUpdate", Fields), (Action<Action<SkillIcon>, SkillIcon>)Hud));
                installed = true;
            }
            catch (Exception e)
            {
                foreach (var hook in hooks) hook.Dispose(); hooks.Clear();
                CommonLog.Warn("Skin presentation hooks: " + e.Message);
            }
        }

        static bool Selection(CharacterSelectController self, out GameObject body, out uint skin)
        {
            var user = localUser.GetValue(self) as LocalUser;
            var network = user?.currentNetworkUser;
            var survivor = network ? network.GetSurvivorPreference() : null;
            body = survivor ? survivor.bodyPrefab : null;
            var index = body ? BodyCatalog.FindBodyIndex(body) : BodyIndex.None;
            skin = index != BodyIndex.None && user?.userProfile != null ? user.userProfile.loadout.bodyLoadoutManager.GetSkinIndex(index) : 0;
            return Find(body, skin) != null;
        }
        static void Select(Action<CharacterSelectController, bool> orig, CharacterSelectController self, bool deselect)
        {
            if (!Selection(self, out var body, out uint skin)) { orig(self, deselect); return; }
            using (MenuIcons(body, skin)) orig(self, deselect);
            if (self.survivorName) self.survivorName.text = Language.GetString(NameToken(body, skin));
        }
        static bool Changed(Stamp stamp, GameObject body, uint skin)
        {
            if (stamp.body == body && stamp.skin == skin && stamp.revision == revision) return false;
            stamp.body = body; stamp.skin = skin; stamp.revision = revision; return true;
        }
        static void SelectUpdate(Action<CharacterSelectController> orig, CharacterSelectController self)
        {
            orig(self);
            if (Selection(self, out var body, out uint skin) && Changed(selects.GetOrCreateValue(self), body, skin))
                rebuildSelect.Invoke(self, new object[] { false });
        }
        static LoadoutPanelController.DisplayData Data(LoadoutPanelController self) => (LoadoutPanelController.DisplayData)displayData.GetValue(self);
        static void Loadout(Action<LoadoutPanelController> orig, LoadoutPanelController self)
        {
            var data = Data(self);
            if (data.bodyIndex == BodyIndex.None) { orig(self); return; }
            var body = BodyCatalog.GetBodyPrefab(data.bodyIndex);
            uint skin = data.userProfile?.loadout.bodyLoadoutManager.GetSkinIndex(data.bodyIndex) ?? 0;
            using (MenuIcons(body, skin)) orig(self);
        }
        static void LoadoutUpdate(Action<LoadoutPanelController> orig, LoadoutPanelController self)
        {
            orig(self);
            var data = Data(self);
            if (data.bodyIndex == BodyIndex.None) return;
            var body = BodyCatalog.GetBodyPrefab(data.bodyIndex);
            uint skin = data.userProfile?.loadout.bodyLoadoutManager.GetSkinIndex(data.bodyIndex) ?? 0;
            if (Find(body, skin) != null && Changed(loadouts.GetOrCreateValue(self), body, skin)) rebuildLoadout.Invoke(self, null);
        }
        static void Hud(Action<SkillIcon> orig, SkillIcon self)
        {
            orig(self);
            if (!self.targetSkill || !self.iconImage) return;
            var body = self.targetSkill.GetComponent<CharacterBody>();
            if (!body || body.bodyIndex == BodyIndex.None) return;
            var prefab = BodyCatalog.GetBodyPrefab(body.bodyIndex);
            if (Find(prefab, body.skinIndex) == null) return;
            var icon = Icon(prefab, body.skinIndex, self.targetSkill.skillDef);
            if (icon) self.iconImage.sprite = icon;
        }
#endif
    }
}
