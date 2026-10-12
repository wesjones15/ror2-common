using RoR2;
using RoR2.ContentManagement;
using UnityEngine;

namespace Ror2Common
{
    // An AI master for a custom survivor, so RoR2 can run the survivor's body without a player:
    // Goobo Jr.'s gummy clone, Artifact of Vengeance's umbra and the like look for an AI master
    // whose body is the survivor's (MasterCatalog.FindAiMasterIndexForBody) and spawn nothing
    // without one. The plugin still has to turn the AI's button presses (InputBankTest) into
    // its sim's input.
    public static class SurvivorAi
    {
        /// Clones the AI master at `template` (a LegacyResourcesAPI path, e.g.
        /// "Prefabs/CharacterMasters/CommandoMonsterMaster": its skill drivers come along) for
        /// `body`, and adds it to `pack`. Call at content load. Null if there's no template.
        public static GameObject Register(ContentPack pack, GameObject body, string template, string name, string owner)
        {
            var src = LegacyResourcesAPI.Load<GameObject>(template);
            if (!src || !src.GetComponent<CharacterMaster>())
            {
                CommonLog.Warn($"SurvivorAi: no AI master at {template}; {name} has no AI (no Goobo Jr. clone)");
                return null;
            }
            var master = Prefabs.Clone(src, name, owner);
            master.GetComponent<CharacterMaster>().bodyPrefab = body;
            pack.masterPrefabs.Add(new[] { master });
            return master;
        }
    }
}
