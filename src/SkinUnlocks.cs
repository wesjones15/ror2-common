using RoR2;
using RoR2.ContentManagement;
using UnityEngine;

namespace Ror2Common
{
    /// Profile-persistent skin keys, shared by the survivor plugins.
    public static class SkinUnlocks
    {
        /// A dead co-op player still counts as the survivor they brought to the fight.
        public static bool IsPlaying(LocalUser user, string bodyToken)
        {
            if (user == null) return false;
            var body = user.cachedBody;
            if (!body && user.cachedMaster && user.cachedMaster.bodyPrefab)
                body = user.cachedMaster.bodyPrefab.GetComponent<CharacterBody>();
            return body && body.baseNameToken == bodyToken;
        }

        public static UnlockableDef Create(ContentPack pack, string key, string token, string hint, string unlocked)
        {
            var def = ScriptableObject.CreateInstance<UnlockableDef>();
            def.cachedName = key;
            def.nameToken = token;
            def.getHowToUnlockString = () => hint;
            def.getUnlockedString = () => unlocked;
            pack.unlockableDefs.Add(new[] { def });
            return def;
        }

        /// True only for a newly earned key; repeated events never resave it.
        public static bool Grant(LocalUser user, UnlockableDef def)
        {
            if (!def || user?.userProfile == null || user.userProfile.HasUnlockable(def)) return false;
            user.userProfile.GrantUnlockable(def);
            user.userProfile.RequestEventualSave();
            return true;
        }
    }
}
