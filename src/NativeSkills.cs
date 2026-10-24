using System;
using System.Reflection;
using EntityStates;
using RoR2;
using RoR2.ContentManagement;
using RoR2.Skills;
using UnityEngine;

namespace Ror2Common
{
    /// A skill whose move lives in a native sim (Mario's ground pound), held in one of RoR2's
    /// skill slots only so RoR2's own code keeps its charges: the slot's stock, recharge and
    /// cooldown scale are what Backup Magazine, Hardlight Afterburner, Lysate Cell, Alien Head,
    /// Purity, Bandolier and Brainstalks already change. Nothing executes it (the body's main
    /// state never calls `ExecuteIfReady`); the plugin spends a charge when the sim makes the
    /// move (`NativeSkills.Spend`). `RechargeGate`, if set, holds the recharge while it says
    /// false.
    public class NativeSkillDef : SkillDef
    {
        public Func<GenericSkill, bool> RechargeGate;

        public override void OnFixedUpdate(GenericSkill skillSlot, float deltaTime)
        {
            if (RechargeGate != null && !RechargeGate(skillSlot)) return;
            base.OnFixedUpdate(skillSlot, deltaTime);
        }
    }

    // Puts native moves in a survivor's skill slots, and spends their charges.
    public static class NativeSkills
    {
        static FieldInfo familyField;
        static BuffIndex noCooldowns = BuffIndex.None;
        static bool noCooldownsLooked;

        /// A skill for slot `name`: `stock` charges, each back `recharge` seconds after the last
        /// (one at a time). `combat`: using it ends RoR2's out-of-combat time (Red Whip).
        public static NativeSkillDef Make(string name, string nameToken, string descriptionToken,
            Sprite icon, float recharge, int stock, bool combat)
        {
            var def = ScriptableObject.CreateInstance<NativeSkillDef>();
            def.skillName = name;
            ((ScriptableObject)def).name = name;
            def.skillNameToken = nameToken;
            def.skillDescriptionToken = descriptionToken;
            def.keywordTokens = Array.Empty<string>();
            def.icon = icon;
            def.activationStateMachineName = "Weapon";
            def.activationState = new SerializableEntityStateType(typeof(Idle));
            def.interruptPriority = InterruptPriority.Any;
            def.baseRechargeInterval = Mathf.Max(0f, recharge);
            def.baseMaxStock = Mathf.Max(0, stock);
            def.rechargeStock = 1;
            def.requiredStock = 1;
            def.stockToConsume = 1;
            def.resetCooldownTimerOnUse = false;
            def.fullRestockOnAssign = true;
            def.dontAllowPastMaxStocks = false;
            def.beginSkillCooldownOnSkillEnd = false;
            def.cancelSprintingOnActivation = false;
            def.forceSprintDuringState = false;
            def.canceledFromSprinting = false;
            def.isCombatSkill = combat;
            def.mustKeyPress = false;
            return def;
        }

        /// Gives `body`'s skill slot `slot` a family of just `def`, and adds both to `pack`. The
        /// slot's old family (Commando's, on a clone of his body) is left as it was. Call at
        /// content load. Returns false if the body has no such slot.
        public static bool SetFamily(GameObject body, SkillSlot slot, SkillDef def, ContentPack pack)
        {
            var locator = body ? body.GetComponent<SkillLocator>() : null;
            var skill = locator ? locator.GetSkill(slot) : null;
            if (!skill)
            {
                CommonLog.Warn($"NativeSkills: {(body ? body.name : "null")} has no {slot} slot");
                return false;
            }
            return SetFamily(skill, body.name + slot + "Family", pack, def);
        }

        /// Additional GenericSkills are included by RoR2's loadout panel and skill catalog.
        /// Add to the inactive content prefab before its runtime SkillLocator.Awake caches them.
        public static GenericSkill AddExtra(GameObject body, string name, string titleToken,
            ContentPack pack, params SkillDef[] variants)
        {
            var skill = body.AddComponent<GenericSkill>();
            skill.skillName = name;
            skill.loadoutTitleToken = titleToken;
            // The fifth Melee slot is the contextual A attack, not a loadout choice
            // and not a cooldown. Keep it off the character-select/loadout rows; the
            // pause control sheet still names it. Its old row was the icon that sat
            // on top of the character portrait and reported no input binding.
            skill.hideInCharacterSelect = skill.hideInLoadoutSelect = true;
            if (!SetFamily(skill, body.name + name + "Family", pack, variants))
                throw new InvalidOperationException("Cannot register extra native skill " + name);
            return skill;
        }

        public static bool SetFamily(GenericSkill skill, string name, ContentPack pack, params SkillDef[] variants)
        {
            if (!skill || variants == null || variants.Length == 0) return false;
            if (familyField == null)
                familyField = typeof(GenericSkill).GetField("_skillFamily", BindingFlags.Instance | BindingFlags.NonPublic);
            if (familyField == null)
            {
                CommonLog.Error("NativeSkills: GenericSkill._skillFamily is gone in this RoR2; skills stay as they were");
                return false;
            }
            var family = ScriptableObject.CreateInstance<SkillFamily>();
            ((ScriptableObject)family).name = name;
            family.variants = new SkillFamily.Variant[variants.Length];
            for (int i = 0; i < variants.Length; i++)
                family.variants[i] = new SkillFamily.Variant { skillDef = variants[i],
                    viewableNode = new ViewablesCatalog.Node(variants[i].skillNameToken, false, null) };
            familyField.SetValue(skill, family);
            pack.skillFamilies.Add(new[] { family });
            pack.skillDefs.Add(variants);
            return true;
        }

        /// RoR2's no-cooldowns buff (Brainstalks): every charge is free while it lasts.
        public static bool Unlimited(CharacterBody body)
        {
            if (!noCooldownsLooked && BuffCatalog.buffCount > 0)
            {
                noCooldownsLooked = true;
                noCooldowns = BuffCatalog.FindBuffIndex("NoCooldowns");
            }
            return body && noCooldowns != BuffIndex.None && body.HasBuff(noCooldowns);
        }

        /// The slot holds `own` (not an override: Heresy's or another mod's skill).
        public static bool Holds(GenericSkill slot, SkillDef own) => slot && slot.skillDef == own;

        /// Charges on hand in `slot` for the native move `own`: its stock, or none while
        /// another skill overrides the slot.
        public static int Charges(GenericSkill slot, SkillDef own) => Holds(slot, own) ? slot.stock : 0;

        /// Is there a charge for the native move `own` in `slot`, without spending it (a move spent
        /// when it lands, not when it starts)? With another skill overriding the slot (Heresy):
        /// whether that one is ready.
        public static bool Ready(GenericSkill slot, SkillDef own)
        {
            if (!slot) return false;
            if (slot.skillDef != own) return slot.IsReady();
            return Unlimited(slot.characterBody) || slot.stock >= 1;
        }

        /// The sim makes the move held in `slot`. With `own` in the slot, spends a charge if
        /// there is one (free under Brainstalks) and tells RoR2 the skill was used, as using
        /// any skill does (its on-use items, the out-of-combat clock). With another skill
        /// overriding the slot (Heresy), that skill runs instead, as for any survivor: it's
        /// spent only if it was ready. Returns whether a charge was there. Run it where the
        /// body has authority (where its sim runs).
        public static bool Spend(GenericSkill slot, SkillDef own)
        {
            if (!slot) return false;
            var body = slot.characterBody;
            if (slot.skillDef != own) return slot.ExecuteIfReady();
            if (Unlimited(body))
            {
                if (body) body.OnSkillActivated(slot);
                return true;
            }
            if (slot.stock < 1) return false;
            slot.DeductStock(1);
            if (body) body.OnSkillActivated(slot);
            return true;
        }

        /// Spend the charge of a move that deferred it from its start to its success
        /// (`MeleeDriver.SpendsOnStart`, Yoshi's Egg Lay spends when the tongue swallows):
        /// a miss leaves the stock and its cooldown untouched. Only this fighter's own
        /// skill is spent; an overriding skill was already spent when the move started.
        public static bool SpendDeferred(MeleeSkillSet set, CharacterBody body, uint moveId)
        {
            if (set == null || !body) return false;
            int slot = set.Slot(body, moveId);
            var skill = set.Get(body, slot);
            var expected = set.Expected(body, slot);
            return Holds(skill, expected) && Spend(skill, expected);
        }
    }
}
