using RoR2;
using RoR2.CharacterAI;
using RoR2.ContentManagement;
using UnityEngine;

namespace Ror2Common
{
    // An AI master for a custom survivor, so RoR2 can run the survivor's body without a player:
    // Goobo Jr.'s gummy clone, Artifact of Vengeance's umbra and the like look for an AI master
    // whose body is the survivor's (MasterCatalog.FindAiMasterIndexForBody) and spawn nothing
    // without one. The plugin still has to turn the AI's button presses (InputBankTest) into
    // its sim's input, or decide them itself from what the AI sees (Enemy) and let the master's
    // drivers only walk (Drivers).
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

        /// Replaces `master`'s skill drivers with movement alone: stand and face the enemy while
        /// it is within `holdRange` metres and in sight, otherwise run at it (sprinting, by the
        /// node graph), and with no enemy follow the leader once it is `followRange` away. No
        /// driver presses a skill; the plugin reads its buttons from Enemy.
        public static void Drivers(GameObject master, float holdRange, float followRange = 12f)
        {
            foreach (var d in master.GetComponents<AISkillDriver>())
                Object.DestroyImmediate(d);
            Add(master, "Hold", 0f, holdRange, AISkillDriver.MovementType.Stop, AISkillDriver.TargetType.CurrentEnemy,
                AISkillDriver.AimType.AtCurrentEnemy, true, true, false);
            Add(master, "Chase", 0f, 512f, AISkillDriver.MovementType.ChaseMoveTarget, AISkillDriver.TargetType.CurrentEnemy,
                AISkillDriver.AimType.MoveDirection, false, false, true);
            Add(master, "Follow", followRange, 512f, AISkillDriver.MovementType.ChaseMoveTarget, AISkillDriver.TargetType.CurrentLeader,
                AISkillDriver.AimType.MoveDirection, false, false, false);
        }

        static void Add(GameObject master, string name, float min, float max, AISkillDriver.MovementType move,
            AISkillDriver.TargetType target, AISkillDriver.AimType aim, bool needSight, bool ignoreGraph, bool sprint)
        {
            var d = master.AddComponent<AISkillDriver>();
            d.customName = name;
            d.skillSlot = SkillSlot.None;
            d.requireSkillReady = false;
            d.minDistance = min;
            d.maxDistance = max;
            d.selectionRequiresTargetLoS = needSight;
            d.activationRequiresTargetLoS = false;
            d.activationRequiresAimConfirmation = false;
            d.movementType = move;
            d.moveTargetType = target;
            d.aimType = aim;
            d.ignoreNodeGraph = ignoreGraph;
            d.moveInputScale = 1f;
            d.shouldSprint = sprint;
            d.buttonPressType = AISkillDriver.ButtonPressType.Abstain;
            d.driverUpdateTimerOverride = 0.25f;
            d.minUserHealthFraction = d.minTargetHealthFraction = float.NegativeInfinity;
            d.maxUserHealthFraction = d.maxTargetHealthFraction = float.PositiveInfinity;
        }

        /// The enemy `body`'s AI master is after, or null (no AI, or no enemy in sight).
        public static CharacterBody Enemy(CharacterBody body)
        {
            var ai = body && body.master ? body.master.GetComponent<BaseAI>() : null;
            var target = ai != null ? ai.currentEnemy : null;
            var enemy = target != null ? target.characterBody : null;
            return enemy && enemy.healthComponent && enemy.healthComponent.alive ? enemy : null;
        }

        /// `body` has an AI master (a Mario of Goobo Jr.'s, say, and not a player's).
        public static bool HasAi(CharacterBody body) => body && body.master && body.master.GetComponent<BaseAI>();
    }
}
