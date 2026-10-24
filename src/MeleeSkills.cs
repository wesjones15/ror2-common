using System;
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
        /// An alternate for the primary slot (Luigi's Super Scope in place of the Fireball); `defName` null: none.
        public MeleeSkillInfo primaryAlt;
        /// An alternate for the secondary slot (Yoshi's Shield / Air Feint in place of the Bomb); `defName` null: none.
        public MeleeSkillInfo secondaryAlt;
        public MeleeSkillInfo extra, extraAlt;
        public string extraName;        // GenericSkill name for the fifth slot
        public string extraTitleToken;  // loadout title for that fifth slot
        public string extraTitle;       // displayed loadout title
        public string extraActionName;  // input prompt shown on the extra icon (empty keeps the clone's stock binding)
        /// Stock skill icon to clone for the extra slot's key glyph/cooldown style: 0..3,
        /// or 0 for the default fourth slot. Yoshi's fifth slot is on LT, so he uses 1.
        public int extraGlyphFrom;
        /// Native move id -> skill slot index (0..4). Null uses the shared
        /// default (neutral->primary, side->utility, up->special, down->extra,
        /// else secondary). Fighters whose specials sit on different buttons
        /// supply their own mapping.
        public Func<uint, int> slotForMove;
    }

    /// The built skills for one fighter, plus the shared slot helpers every
    /// `MeleeDriver` uses to spend charges and read which move a slot owns.
    public sealed class MeleeSkillSet
    {
        public readonly NativeSkillDef[] Stock = new NativeSkillDef[4];
        public NativeSkillDef Extra, ExtraAlt;
        /// The primary slot's alternate, if the fighter has one.
        public NativeSkillDef PrimaryAlt;
        /// The secondary slot's alternate (Yoshi's shield), if the fighter has one.
        public NativeSkillDef SecondaryAlt;
        public string ExtraName;
        readonly Func<uint, int> slotForMove;
        public static readonly SkillSlot[] Slots =
            { SkillSlot.Primary, SkillSlot.Secondary, SkillSlot.Utility, SkillSlot.Special };

        internal MeleeSkillSet(Func<uint, int> slotForMove) { this.slotForMove = slotForMove; }

        /// The shared default mapping: neutral->primary, side/super->utility,
        /// up->special, down->secondary (the slot that sits on LT), and every other
        /// move (contextual A, grabs) is the extra slot on B.
        public static int DefaultSlot(uint move) =>
            move == 20 ? 0 : move == 21 || move == 22 ? 2 : move == 23 ? 3 : move == 24 ? 1 : 4;

        /// This fighter's native move -> skill slot index (0..4).
        public int Slot(uint move) => slotForMove(move);

        public GenericSkill Get(CharacterBody body, int slot) =>
            slot == 4 ? body.skillLocator.FindSkill(ExtraName) : body.skillLocator.GetSkill(Slots[slot]);

        public bool ShieldSelected(CharacterBody body) =>
            (ExtraAlt != null && Get(body, 4)?.skillDef == ExtraAlt)
            || (SecondaryAlt != null && Get(body, 1)?.skillDef == SecondaryAlt);

        /// The loadout holds the primary slot's alternate.
        public bool PrimaryAltSelected(CharacterBody body) => PrimaryAlt != null && body && body.skillLocator && Get(body, 0)?.skillDef == PrimaryAlt;
        /// The loadout holds the secondary slot's alternate (Yoshi's shield).
        public bool SecondaryAltSelected(CharacterBody body) => SecondaryAlt != null && body && body.skillLocator && Get(body, 1)?.skillDef == SecondaryAlt;

        public NativeSkillDef Expected(CharacterBody body, int slot) =>
            slot == 4 ? (ShieldSelected(body) && ExtraAlt != null ? ExtraAlt : Extra)
            : slot == 0 && PrimaryAltSelected(body) ? PrimaryAlt
            : slot == 1 && SecondaryAltSelected(body) ? SecondaryAlt
            : Stock[slot];

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
            var set = new MeleeSkillSet(layout.slotForMove ?? MeleeSkillSet.DefaultSlot) { ExtraName = layout.extraName };
            MeleeSkillInfo[] stock = { layout.primary, layout.secondary, layout.utility, layout.special };
            for (int i = 0; i < 4; i++)
            {
                var info = stock[i];
                var def = NativeSkills.Make(info.defName, info.nameToken, info.descriptionToken, null,
                    info.recharge, info.stock, info.combat);
                set.Stock[i] = def;
                var alt = i == 0 ? layout.primaryAlt : i == 1 ? layout.secondaryAlt : default;
                if (alt.defName != null)
                {
                    var altDef = NativeSkills.Make(alt.defName, alt.nameToken, alt.descriptionToken, null,
                        alt.recharge, alt.stock, alt.combat);
                    if (i == 0) set.PrimaryAlt = altDef; else set.SecondaryAlt = altDef;
                    var slot = body.GetComponent<SkillLocator>() ? body.GetComponent<SkillLocator>().GetSkill(MeleeSkillSet.Slots[i]) : null;
                    NativeSkills.SetFamily(slot, body.name + MeleeSkillSet.Slots[i] + "Family", pack, def, altDef);
                    VanillaIcons.Use(altDef, alt.icon);
                }
                else NativeSkills.SetFamily(body, MeleeSkillSet.Slots[i], def, pack);
                VanillaIcons.Use(def, info.icon);
            }
            set.Extra = NativeSkills.Make(layout.extra.defName, layout.extra.nameToken, layout.extra.descriptionToken, null,
                layout.extra.recharge, layout.extra.stock, layout.extra.combat);
            set.ExtraAlt = layout.extraAlt.defName != null
                ? NativeSkills.Make(layout.extraAlt.defName, layout.extraAlt.nameToken, layout.extraAlt.descriptionToken, null,
                    layout.extraAlt.recharge, layout.extraAlt.stock, layout.extraAlt.combat)
                : null;
            if (set.ExtraAlt != null)
                NativeSkills.AddExtra(body, layout.extraName, layout.extraTitleToken, pack, set.Extra, set.ExtraAlt);
            else
                NativeSkills.AddExtra(body, layout.extraName, layout.extraTitleToken, pack, set.Extra);
            var hud = body.AddComponent<ExtraSkillIcon>();
            hud.skillName = layout.extraName;
            hud.actionName = layout.extraActionName;
            hud.glyphFrom = layout.extraGlyphFrom > 0 ? layout.extraGlyphFrom : 3;
            VanillaIcons.Use(set.Extra, layout.extra.icon);
            if (set.ExtraAlt != null) VanillaIcons.Use(set.ExtraAlt, layout.extraAlt.icon);
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
            foreach (var alt in new[] { layout.primaryAlt, layout.secondaryAlt, layout.extraAlt })
            {
                if (alt.defName == null) continue;
                l.SetStringByToken(alt.nameToken, alt.displayName);
                l.SetStringByToken(alt.descriptionToken, alt.description);
            }
            l.SetStringByToken(layout.extraTitleToken, layout.extraTitle);
            l.SetStringByToken(layout.extra.nameToken, layout.extra.displayName);
            l.SetStringByToken(layout.extra.descriptionToken, layout.extra.description);
        }
    }
}
