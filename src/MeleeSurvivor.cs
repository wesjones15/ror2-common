using System;
using System.Collections.Generic;
using EntityStates;
using RoR2;
using RoR2.ContentManagement;
using UnityEngine;

namespace Ror2Common
{
    /// One melee survivor's per-fighter identity: its RoR2 stats, tokens, skill
    /// and skin layouts, driver/view factories and control glyphs. `MeleeSurvivor`
    /// builds and registers everything else.
    public struct MeleeFighterDef
    {
        public string cachedName, bodyName, displayName;
        public string nameToken, subtitleToken, descriptionToken, outroToken, failToken;
        public string nameStr, subtitleStr, descriptionStr, outroStr, failStr;
        public Color bodyColor;
        public float baseMaxHealth, levelMaxHealth, baseArmor;
        public int baseJumpCount;
        public float sortPosition;
        public Type mainStateType, driverType;
        public MeleeSkillLayout skills;
        public MeleeSkinLayout skins;
        public Func<IDisplayActor> displayFactory;
        public Func<Texture2D> portraitFactory;
        public Dictionary<SkillSlot, PadButton[]> glyphs;
        /// The native fighter kind, and the plugin assets cache the generated skill portraits read. 
        public uint fighterKind;
        public Func<string> assetsDir;
        public Func<bool> assetsReady;
        /// Unfinished: not registered, offered or extracted unless `Experimental.Enabled`. Delete it when done.
        public bool experimental;
    }

    public static class MeleeSurvivor
    {
        // The shared arrival state is registered once per plugin; every fighter
        // reuses the same type, so a per-fighter Add would collide on the second
        // survivor and abort its skill/skin registration.
        static bool arrivalRegistered;
        /// Clones the Commando body/display, applies the fighter's stats and
        /// layouts, registers its skills/skins/survivor, and returns the built
        /// skill set for the driver to read charges from.
        public static MeleeSkillSet Register(ContentPack pack, string guid, in MeleeFighterDef def)
        {
            var commando = LegacyResourcesAPI.Load<GameObject>("Prefabs/CharacterBodies/CommandoBody");
            var display = LegacyResourcesAPI.Load<GameObject>("Prefabs/CharacterDisplays/CommandoDisplay");
            if (!commando || !display) throw new InvalidOperationException("Commando prefabs unavailable");
            var body = Prefabs.Clone(commando, def.bodyName, guid);
            // The clone inherits Commando's RagdollController, so vanilla's death state would
            // drop a Commando ragdoll corpse at the death spot. Melee owns its own death
            // presentation (the blast-zone KO at the fighter's location); remove the controller
            // so no vanilla corpse is spawned and nothing is left after the KO effect finishes.
            foreach (var ragdoll in body.GetComponentsInChildren<RagdollController>(true))
                UnityEngine.Object.DestroyImmediate(ragdoll);
            var cb = body.GetComponent<CharacterBody>();
            cb.baseNameToken = def.nameToken;
            cb.subtitleNameToken = def.subtitleToken;
            cb.bodyColor = def.bodyColor;
            cb.baseMaxHealth = def.baseMaxHealth;
            cb.levelMaxHealth = def.levelMaxHealth;
            cb.baseArmor = def.baseArmor;
            cb.baseJumpCount = def.baseJumpCount;
            bool customArrival = !OptionalAssets.BuiltinOnly && def.assetsReady();
            if (customArrival) {
                cb.preferredPodPrefab = null;
                cb.preferredInitialStateType = new SerializableEntityStateType(typeof(MeleeArrival));
            }
            foreach (var state in body.GetComponents<EntityStateMachine>())
                if (state.customName == "Body")
                {
                    state.mainStateType = new SerializableEntityStateType(def.mainStateType);
                    if (customArrival) state.initialStateType = new SerializableEntityStateType(typeof(MeleeArrival));
                }
            body.AddComponent<NativeBody>();
            body.AddComponent(def.driverType);
            body.AddComponent<MeleeRecovery>();
            body.AddComponent<MeleeDefense>();
            var skills = MeleeSkills.Build(body, pack, def.skills, def.fighterKind, def.assetsDir, def.assetsReady);
            // Goobo Jr., Artifact of Vengeance and the like need an AI master whose body is
            // this survivor's. It only walks; MeleeDriver's brain presses the fighter's buttons.
            var monster = SurvivorAi.Register(pack, body, "Prefabs/CharacterMasters/CommandoMonsterMaster",
                def.bodyName + "MonsterMaster", guid);
            // Goobo's Melee clone is a brawler: get in close and re-decide almost every frame.
            if (monster) SurvivorAi.Drivers(monster, 1.8f, 12f, aggressive: true);
            var lobby = Prefabs.Clone(display, def.displayName, guid);
            // The display factory id must be per-fighter: it is the key in the
            // shared DisplayModel factory table, so a shared guid would let one
            // fighter overwrite another's lobby display.
            var displayDef = def;
            DisplayModel.Attach(lobby, def.bodyName, () => !OptionalAssets.BuiltinOnly && displayDef.assetsReady()
                ? displayDef.displayFactory() : new PrimitiveDisplay(displayDef.bodyColor));
            // Same idle-only source and orientation as character select; the common
            // camera supplies Mario/Olimar's 45-degree left framing. The crop is a fraction
            // of each fighter's height so a wide idle pose cannot zoom the body out; Pikachu,
            // Yoshi and DK have larger heads and need a taller crop for the same composition.
            // Viewer proof: examples/character_portrait.rs, Wait 0.
            var portraitDef = def;
            Portrait.Request(body, () => portraitDef.assetsReady() ?
                new MeleeSelectDisplay(new MeleeSelectSource(portraitDef.assetsDir(), portraitDef.fighterKind,
                    () => 0, 1f, idleOnly: true), intro: false) : null,
                framing: PortraitFramingFor(def.fighterKind), assetsDir: def.assetsDir,
                revision: PortraitCache.MeleeCharacterRevision, fallback: def.portraitFactory);
            MeleeSkins.Add(body, pack, def.skins);
            DisplayModel.BindSkins(lobby, body);
            var survivor = ScriptableObject.CreateInstance<SurvivorDef>();
            survivor.cachedName = def.cachedName;
            survivor.bodyPrefab = body;
            survivor.displayPrefab = lobby;
            survivor.displayNameToken = def.nameToken;
            survivor.descriptionToken = def.descriptionToken;
            survivor.outroFlavorToken = def.outroToken;
            survivor.mainEndingEscapeFailureFlavorToken = def.failToken;
            survivor.primaryColor = cb.bodyColor;
            survivor.desiredSortPosition = def.sortPosition;
            pack.bodyPrefabs.Add(new[] { body });
            pack.survivorDefs.Add(new[] { survivor });
            pack.entityStateTypes.Add(new[] { def.mainStateType });
            if (!arrivalRegistered)
            {
                pack.entityStateTypes.Add(new[] { typeof(MeleeArrival) });
                arrivalRegistered = true;
            }
            GlyphOverride.Set(body.name, () => true, def.glyphs);
            return skills;
        }

        public static void Tokens(Language l, in MeleeFighterDef def)
        {
            l.SetStringByToken(def.nameToken, def.nameStr);
            l.SetStringByToken(def.subtitleToken, def.subtitleStr);
            l.SetStringByToken(def.descriptionToken, def.descriptionStr);
            l.SetStringByToken(def.outroToken, def.outroStr);
            l.SetStringByToken(def.failToken, def.failStr);
            MeleeSkills.Tokens(l, def.skills);
            MeleeSkins.Tokens(l, def.skins);
        }

        /// Head-and-shoulders portrait framing, shared by every Melee fighter. The crop is a
        /// fraction of the fighter's own height, not its upper-body width, so Samus's cannon,
        /// DK's arms and a spread idle pose cannot pull the camera back to a full body. Pikachu,
        /// Yoshi and DK have proportionally larger heads: a taller crop keeps the same head and
        /// shoulders in frame instead of only the face. The pan keeps a little headroom.
        static PortraitFraming PortraitFramingFor(uint kind)
        {
            float height = 0.58f, pan = 0.03f;
            if (kind == MeleeNative.FighterPikachu) { height = 0.75f; pan = 0.05f; }
            else if (kind == MeleeNative.FighterYoshi) { height = 0.66f; pan = 0.04f; }
            else if (kind == MeleeNative.FighterDonkey) { height = 0.90f; pan = 0.08f; }
            return new PortraitFraming { TurnLeftDegrees = 45f, VisibleHeight = height, PanUp = pan };
        }
    }
}
