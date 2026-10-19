using System;
using System.Collections.Generic;
using RoR2;
using RoR2.ContentManagement;
using UnityEngine;

namespace Ror2Common
{
    /// One costume's per-fighter identity. The skin machinery (SkinDef creation,
    /// unlock progression, selection) is shared.
    public struct MeleeSkinInfo
    {
        public string nameToken, displayName;
        public Color32 color;
    }

    /// A fighter's costume set. `skins` is the source order (index 0 is the
    /// default costume); names, colours and unlock gating are the only
    /// per-fighter data.
    public struct MeleeSkinLayout
    {
        public string fighterName;      // display name used in unlock strings
        public string bodyNameToken;    // baseNameToken that gates unlock grants
        public string bodyName;         // BodyCatalog body name for loadout selection
        public string defPrefix;        // e.g. "MeleeSamusSkin"
        public string nameTokenPrefix;  // e.g. "SSBM_SKIN_"
        public string unlockPrefix;     // e.g. "Skins.MeleeSamus."
        public MeleeSkinInfo[] skins;
    }

    public static class MeleeSkins
    {
        sealed class Entry
        {
            public MeleeSkinLayout layout;
            public UnlockableDef[] unlocks;
            public string[] names;
        }

        static readonly List<Entry> registered = new List<Entry>();
        static bool installed;

        public static void Add(GameObject body, ContentPack pack, in MeleeSkinLayout layout)
        {
            string fighterName = layout.fighterName;
            var model = body.GetComponent<ModelLocator>().modelTransform;
            var controller = model.GetComponent<ModelSkinController>();
            if (!controller) controller = model.gameObject.AddComponent<ModelSkinController>();
            var character = model.GetComponent<CharacterModel>();
            var skins = new SkinDef[layout.skins.Length];
            var entry = new Entry { layout = layout, unlocks = new UnlockableDef[layout.skins.Length], names = new string[layout.skins.Length] };
            for (int i = 0; i < layout.skins.Length; i++)
            {
                var info = layout.skins[i];
                var skin = ScriptableObject.CreateInstance<SkinDef>();
                skin.name = layout.defPrefix + i;
                skin.nameToken = layout.nameTokenPrefix + i;
                skin.rootObject = model.gameObject;
                skin.baseSkins = Array.Empty<SkinDef>();
                skin.icon = Icon(info.color);
                var p = ScriptableObject.CreateInstance<SkinDefParams>();
                p.name = skin.name + "Params";
                p.rendererInfos = character.baseRendererInfos;
                p.gameObjectActivations = Array.Empty<SkinDefParams.GameObjectActivation>();
                p.meshReplacements = Array.Empty<SkinDefParams.MeshReplacement>();
                p.projectileGhostReplacements = Array.Empty<SkinDefParams.ProjectileGhostReplacement>();
                p.minionSkinReplacements = Array.Empty<SkinDefParams.MinionSkinReplacement>();
                p.lightReplacements = Array.Empty<CharacterModel.LightInfo>();
                skin.skinDefParams = p;
                skin.optimizedSkinDefParams = p;
                entry.names[i] = info.displayName;
                if (i > 0)
                {
                    int stage = i;
                    var u = ScriptableObject.CreateInstance<UnlockableDef>();
                    u.cachedName = layout.unlockPrefix + i;
                    u.nameToken = skin.nameToken;
                    u.getHowToUnlockString = () => "Complete stage " + stage + "'s teleporter event as " + fighterName + ".";
                    u.getUnlockedString = () => fighterName + " costume unlocked.";
                    skin.unlockableDef = u;
                    entry.unlocks[i] = u;
                    pack.unlockableDefs.Add(new[] { u });
                }
                skins[i] = skin;
            }
            controller.skins = skins;
            registered.Add(entry);
            // SkinCatalog enumerates registered body ModelSkinControllers.
        }

        static Sprite Icon(Color32 suit)
        {
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var pixels = new Color32[64];
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) pixels[y * 8 + x] = y >= 3 && y <= 4 && x > 1 && x < 6 ? new Color32(50, 220, 115, 255) : suit;
            texture.SetPixelData(pixels, 0);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f));
        }

        public static void Install()
        {
            if (installed) return;
            installed = true;
            TeleporterInteraction.onTeleporterChargedGlobal += Charged;
        }

        public static void Uninstall()
        {
            installed = false;
            TeleporterInteraction.onTeleporterChargedGlobal -= Charged;
        }

        static void Charged(TeleporterInteraction teleporter)
        {
            if (!Run.instance) return;
            int stage = Run.instance.stageClearCount + 1;
            foreach (var user in LocalUserManager.readOnlyLocalUsersList)
            {
                if (user.cachedBody == null || user.userProfile == null) continue;
                foreach (var entry in registered)
                {
                    if (user.cachedBody.baseNameToken != entry.layout.bodyNameToken) continue;
                    for (int i = 1; i < entry.unlocks.Length && i <= stage; i++)
                        if (entry.unlocks[i] && !user.userProfile.HasUnlockable(entry.unlocks[i]))
                        {
                            user.userProfile.GrantUnlockable(entry.unlocks[i]);
                            user.userProfile.RequestEventualSave();
                            CommonLog.Info("Unlocked " + entry.layout.fighterName + " costume: " + entry.names[i]);
                        }
                }
            }
        }

        public static uint Selected(string bodyName)
        {
            var index = BodyCatalog.FindBodyIndex(bodyName);
            if (index == BodyIndex.None) return 0;
            foreach (var user in LocalUserManager.readOnlyLocalUsersList)
                if (user.userProfile != null)
                    return Math.Min(4, user.userProfile.loadout.bodyLoadoutManager.GetSkinIndex(index));
            return 0;
        }

        public static void Tokens(Language l, in MeleeSkinLayout layout)
        {
            for (int i = 0; i < layout.skins.Length; i++)
                l.SetStringByToken(layout.skins[i].nameToken, layout.skins[i].displayName);
        }
    }
}
