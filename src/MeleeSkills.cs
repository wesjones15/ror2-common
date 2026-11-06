using System;
using System.Collections.Generic;
using RoR2;
using RoR2.ContentManagement;
using RoR2.Skills;
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
        /// A second alternate for the secondary slot (Samus's Morph Ball Roll); `defName` null: none.
        public MeleeSkillInfo secondaryAlt2;
        /// An alternate for the special slot (Luigi's Super Scope in place of the Super Jump Punch); `defName` null: none.
        public MeleeSkillInfo specialAlt;
        public MeleeSkillInfo extra, extraAlt;
        public string extraName;        // GenericSkill name for the fifth slot
        public string extraTitleToken;  // loadout title for that fifth slot
        public string extraTitle;       // displayed loadout title
        public string extraActionName;  // input prompt shown on the extra icon (empty keeps the clone's stock binding)
        /// Stock skill icon to clone for the extra slot's key glyph/cooldown style: 0..3,
        /// or 0 for the default fourth slot. Yoshi's fifth slot is on LT, so he uses 1.
        public int extraGlyphFrom;
        /// Native move id -> skill slot index (0..4), given the body so a loadout choice can
        /// move a move (Luigi's Super Scope may sit on the primary or the special slot). Null
        /// uses the shared default (neutral->primary, side->utility, up->special, down->extra,
        /// else secondary). Fighters whose specials sit on different buttons supply their own.
        public Func<CharacterBody, uint, int> slotForMove;
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
        /// A second secondary alternate (Samus's morph-ball roll), if any.
        public NativeSkillDef SecondaryAlt2;
        /// The special slot's alternate (Luigi's Super Scope in place of the Super Jump Punch), if any.
        public NativeSkillDef SpecialAlt;
        public string ExtraName;
        readonly Func<CharacterBody, uint, int> slotForMove;
        public static readonly SkillSlot[] Slots =
            { SkillSlot.Primary, SkillSlot.Secondary, SkillSlot.Utility, SkillSlot.Special };

        internal MeleeSkillSet(Func<CharacterBody, uint, int> slotForMove) { this.slotForMove = slotForMove; }

        /// The shared default mapping: neutral->primary, side/super->utility,
        /// up->special, down->secondary (the slot that sits on LT), and every other
        /// move (contextual A, grabs) is the extra slot on B.
        public static int DefaultSlot(uint move) =>
            move == 20 ? 0 : move == 21 || move == 22 ? 2 : move == 23 ? 3 : move == 24 ? 1 : 4;

        /// This fighter's native move -> skill slot index (0..4), for `body`.
        public int Slot(CharacterBody body, uint move) => slotForMove(body, move);

        public GenericSkill Get(CharacterBody body, int slot) =>
            slot == 4 ? body.skillLocator.FindSkill(ExtraName) : body.skillLocator.GetSkill(Slots[slot]);

        public bool ShieldSelected(CharacterBody body) =>
            (ExtraAlt != null && Get(body, 4)?.skillDef == ExtraAlt)
            || (SecondaryAlt != null && Get(body, 1)?.skillDef == SecondaryAlt);
        /// The B slot holds its A attack (not the shield alternate).
        public bool AttackSelected(CharacterBody body) => Extra != null && Get(body, 4)?.skillDef == Extra;
        /// The shield alternate is in the B slot.
        public bool ShieldOnExtra(CharacterBody body) => ExtraAlt != null && Get(body, 4)?.skillDef == ExtraAlt;
        /// The shield alternate is in the secondary (LT) slot.
        public bool ShieldOnSecondary(CharacterBody body) => SecondaryAlt != null && Get(body, 1)?.skillDef == SecondaryAlt;

        /// The loadout holds the primary slot's alternate.
        public bool PrimaryAltSelected(CharacterBody body) => PrimaryAlt != null && body && body.skillLocator && Get(body, 0)?.skillDef == PrimaryAlt;
        /// The loadout holds the secondary slot's shield alternate.
        public bool SecondaryAltSelected(CharacterBody body) => SecondaryAlt != null && body && body.skillLocator && Get(body, 1)?.skillDef == SecondaryAlt;
        /// The loadout holds the secondary slot's second alternate (Samus's morph roll).
        public bool SecondaryAlt2Selected(CharacterBody body) => SecondaryAlt2 != null && body && body.skillLocator && Get(body, 1)?.skillDef == SecondaryAlt2;
        /// The loadout holds the special slot's alternate (Luigi's Super Scope in place of the Super Jump Punch).
        public bool SpecialAltSelected(CharacterBody body) => SpecialAlt != null && body && body.skillLocator && Get(body, 3)?.skillDef == SpecialAlt;

        public NativeSkillDef Expected(CharacterBody body, int slot) =>
            slot == 4 ? (ShieldSelected(body) && ExtraAlt != null ? ExtraAlt : Extra)
            : slot == 0 && PrimaryAltSelected(body) ? PrimaryAlt
            : slot == 1 && SecondaryAltSelected(body) ? SecondaryAlt
            : slot == 1 && SecondaryAlt2Selected(body) ? SecondaryAlt2
            : slot == 3 && SpecialAltSelected(body) ? SpecialAlt
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
        static NativeSkillDef MakeAlt(in MeleeSkillInfo info) => NativeSkills.Make(
            info.defName, info.nameToken, info.descriptionToken, null, info.recharge, info.stock, info.combat);

        public static MeleeSkillSet Build(GameObject body, ContentPack pack, in MeleeSkillLayout layout,
            uint fighterKind, Func<string> assetsDir, Func<bool> assetsReady)
        {
            void RequestIcon(SkillDef def, in MeleeSkillInfo info)
            {
                VanillaIcons.Use(def, info.icon);
                SkillIcons.Request(def, info.defName, fighterKind, assetsDir, assetsReady, body);
            }

            var set = new MeleeSkillSet(layout.slotForMove ?? ((body, move) => MeleeSkillSet.DefaultSlot(move))) { ExtraName = layout.extraName };
            MeleeSkillInfo[] stock = { layout.primary, layout.secondary, layout.utility, layout.special };
            for (int i = 0; i < 4; i++)
            {
                var info = stock[i];
                var def = NativeSkills.Make(info.defName, info.nameToken, info.descriptionToken, null,
                    info.recharge, info.stock, info.combat);
                set.Stock[i] = def;
                var alts = new List<SkillDef> { def };
                if (i == 0 && layout.primaryAlt.defName != null)
                {
                    set.PrimaryAlt = MakeAlt(layout.primaryAlt);
                    alts.Add(set.PrimaryAlt);
                }
                if (i == 1 && layout.secondaryAlt.defName != null)
                {
                    set.SecondaryAlt = MakeAlt(layout.secondaryAlt);
                    alts.Add(set.SecondaryAlt);
                }
                if (i == 1 && layout.secondaryAlt2.defName != null)
                {
                    set.SecondaryAlt2 = MakeAlt(layout.secondaryAlt2);
                    alts.Add(set.SecondaryAlt2);
                }
                if (i == 3 && layout.specialAlt.defName != null)
                {
                    set.SpecialAlt = MakeAlt(layout.specialAlt);
                    alts.Add(set.SpecialAlt);
                }
                if (alts.Count > 1)
                {
                    var slot = body.GetComponent<SkillLocator>() ? body.GetComponent<SkillLocator>().GetSkill(MeleeSkillSet.Slots[i]) : null;
                    NativeSkills.SetFamily(slot, body.name + MeleeSkillSet.Slots[i] + "Family", pack, alts.ToArray());
                }
                else NativeSkills.SetFamily(body, MeleeSkillSet.Slots[i], def, pack);
                RequestIcon(def, info);
                if (i == 0 && set.PrimaryAlt != null) RequestIcon(set.PrimaryAlt, layout.primaryAlt);
                if (i == 1 && set.SecondaryAlt != null) RequestIcon(set.SecondaryAlt, layout.secondaryAlt);
                if (i == 1 && set.SecondaryAlt2 != null) RequestIcon(set.SecondaryAlt2, layout.secondaryAlt2);
                if (i == 3 && set.SpecialAlt != null) RequestIcon(set.SpecialAlt, layout.specialAlt);
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
            // The fifth slot is the contextual A attack, not a cooldown: it has no
            // HUD icon of its own. The old clone was placed beside the stock row and
            // could overlap it or the character portrait, and its "Interact" glyph
            // read as the X grab. The loadout hides it too; the pause sheet names it.
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
            foreach (var alt in new[] { layout.primaryAlt, layout.secondaryAlt, layout.secondaryAlt2, layout.specialAlt, layout.extraAlt })
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
