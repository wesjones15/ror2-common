using System;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Ror2Common
{
    // Goobo Jr.'s gummy clones: RoR2's material for them and who counts as one.
    public static class GummyClone
    {
        // A DLC1 addressable (the legacy path is a fallback for builds that had it there).
        const string Key = "RoR2/DLC1/GummyClone/matGummyClone.mat";
        static Material material;
        static bool tried;

        /// RoR2's gummy clone material (a dark jelly with a fresnel rim, no texture of its own),
        /// or null if the game won't give it.
        public static Material Material()
        {
            if (tried) return material;
            tried = true;
            try { material = Addressables.LoadAssetAsync<Material>(Key).WaitForCompletion(); }
            catch (Exception e) { CommonLog.Warn($"matGummyClone (addressable): {e.Message}"); }
            if (!material)
            {
                try { material = LegacyResourcesAPI.Load<Material>("Materials/matGummyClone"); }
                catch (Exception e) { CommonLog.Warn($"matGummyClone (legacy): {e.Message}"); }
            }
            if (!material) CommonLog.Warn("RoR2's gummy clone material didn't load");
            return material;
        }

        /// `body` is a Goobo Jr. clone: it carries RoR2's clone identifier item, or it's run by
        /// an AI master that a player's master owns.
        public static bool Is(CharacterBody body)
        {
            if (!body) return false;
            var inv = body.inventory;
            var id = ByName.Item("GummyCloneIdentifier");
            if (inv && id != ItemIndex.None && inv.GetItemCountEffective(id) > 0) return true;
            var master = body.master;
            return master && !master.playerCharacterMasterController
                && master.minionOwnership && master.minionOwnership.ownerMaster;
        }
    }
}
