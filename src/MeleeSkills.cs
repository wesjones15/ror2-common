using RoR2;
using RoR2.ContentManagement;
using UnityEngine;

namespace Ror2Common
{
    /// One native skill's per-fighter identity: its unique asset name, the tokens
    /// for its visible name/description, and the RoR2 tuning that backs its slot.
    public struct MeleeSkillInfo
    {
        public string defName;           // unique SkillDef asset name (e.g. "MeleeSamus0")
        public string nameToken;         // token carrying the displayed name
        public string descriptionToken;  // token carrying the description
        public string displayName;       // name shown in the HUD/loadout
        public string description;
        public string icon;              // vanilla RoR2 SkillDef asset name to reuse
        public float recharge;
        public int stock;
        public bool combat;
    }

    /// A fighter's skill layout: the four stock slots plus the fifth (B) slot's
    /// two variants. Names, descriptions, icons and tuning are the only
    /// per-fighter data; `MeleeSkills` builds and wires everything else.
    public struct MeleeSkillLayout
    {
        public MeleeSkillInfo primary, secondary, utility, special;
        public MeleeSkillInfo extra, extraAlt;
        public string extraName;        // GenericSkill name for the fifth slot
        public string extraTitleToken;  // loadout title for that fifth slot
        public string extraTitle;       // displayed loadout title
        public string extraActionName;  // input prompt shown on the extra icon
    }

    /// The built skills for one fighter, plus the shared slot helpers every
    /// `MeleeDriver` uses to spend charges and read which move a slot owns.
    public sealed class MeleeSkillSet
    {
        public readonly NativeSkillDef[] Stock = new NativeSkillDef[4];
        public NativeSkillDef Extra, ExtraAlt;
        public string ExtraName;
        public static readonly SkillSlot[] Slots =
            { SkillSlot.Primary, SkillSlot.Secondary, SkillSlot.Utility, SkillSlot.Special };

        /// Native move id to skill slot index (0..4). Moves 20..24 are the shared
        /// special slots (neutral, side/super, up, down); every other move is the
        /// contextual-A secondary slot.
        public static int Slot(uint move) =>
            move == 20 ? 0 : move == 21 || move == 22 ? 2 : move == 23 ? 3 : move == 24 ? 4 : 1;

        public GenericSkill Get(CharacterBody body, int slot) =>
            slot == 4 ? body.skillLocator.FindSkill(ExtraName) : body.skillLocator.GetSkill(Slots[slot]);

        public bool ShieldSelected(CharacterBody body) => Get(body, 4)?.skillDef == ExtraAlt;

        public NativeSkillDef Expected(CharacterBody body, int slot) =>
            slot == 4 ? (ShieldSelected(body) ? ExtraAlt : Extra) : Stock[slot];

        public uint Allowed(CharacterBody body)
        {
            uint bits = 0;
            for (int i = 0; i < 5; i++)
                if (NativeSkills.Ready(Get(body, i), Expected(body, i))) bits |= 1u << i;
            return bits;
        }
    }

    /// Builds a fighter's native skills from its layout and registers them with
    /// RoR2. The shared `NativeSkills`/`VanillaIcons`/`ExtraSkillIcon` machinery
    /// stays here; only the layout is per-fighter.
    public static class MeleeSkills
    {
        public static MeleeSkillSet Build(GameObject body, ContentPack pack, in MeleeSkillLayout layout)
        {
            var set = new MeleeSkillSet { ExtraName = layout.extraName };
            MeleeSkillInfo[] stock = { layout.primary, layout.secondary, layout.utility, layout.special };
            for (int i = 0; i < 4; i++)
            {
                var info = stock[i];
                var def = NativeSkills.Make(info.defName, info.nameToken, info.descriptionToken, null,
                    info.recharge, info.stock, info.combat);
                set.Stock[i] = def;
                NativeSkills.SetFamily(body, MeleeSkillSet.Slots[i], def, pack);
                VanillaIcons.Use(def, info.icon);
            }
            set.Extra = NativeSkills.Make(layout.extra.defName, layout.extra.nameToken, layout.extra.descriptionToken, null,
                layout.extra.recharge, layout.extra.stock, layout.extra.combat);
            set.ExtraAlt = NativeSkills.Make(layout.extraAlt.defName, layout.extraAlt.nameToken, layout.extraAlt.descriptionToken, null,
                layout.extraAlt.recharge, layout.extraAlt.stock, layout.extraAlt.combat);
            NativeSkills.AddExtra(body, layout.extraName, layout.extraTitleToken, pack, set.Extra, set.ExtraAlt);
            var hud = body.AddComponent<ExtraSkillIcon>();
            hud.skillName = layout.extraName;
            hud.actionName = layout.extraActionName;
            VanillaIcons.Use(set.Extra, layout.extra.icon);
            VanillaIcons.Use(set.ExtraAlt, layout.extraAlt.icon);
            return set;
        }

        public static void Tokens(Language l, in MeleeSkillLayout layout)
        {
            MeleeSkillInfo[] stock = { layout.primary, layout.secondary, layout.utility, layout.special };
            for (int i = 0; i < 4; i++)
            {
                l.SetStringByToken(stock[i].nameToken, stock[i].displayName);
                l.SetStringByToken(stock[i].descriptionToken, stock[i].description);
            }
            l.SetStringByToken(layout.extraTitleToken, layout.extraTitle);
            l.SetStringByToken(layout.extra.nameToken, layout.extra.displayName);
            l.SetStringByToken(layout.extra.descriptionToken, layout.extra.description);
            l.SetStringByToken(layout.extraAlt.nameToken, layout.extraAlt.displayName);
            l.SetStringByToken(layout.extraAlt.descriptionToken, layout.extraAlt.description);
        }
    }
}
