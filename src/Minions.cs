using System;
using System.Collections.Generic;
using EntityStates;
using RoR2;
using RoR2.CharacterAI;
using RoR2.ContentManagement;
using UnityEngine;
using UnityEngine.Networking;

namespace Ror2Common
{
    /// What a kind of bodied minion is, for `Minions.Define`.
    public sealed class MinionSpec
    {
        /// Unique per plugin: names the prefabs and their network asset ids.
        public string name;
        /// Its name in RoR2 (the ally card, kill feed); the plugin registers the text.
        public string nameToken;
        public float baseMaxHealth = 100f, levelMaxHealth = 30f, baseRegen = 1f, levelRegen = 0.2f;
        public float baseArmor;
        /// Enemies can see and hit it. False: no hurtboxes or colliders, so AI never targets
        /// it and shots pass through.
        public bool targetable = true;
        /// It never takes damage (HealthComponent.godMode).
        public bool invincible;
        /// Which of the owner's items it gets; on top of RoR2's own copy filter (no
        /// CannotCopy items). Null for all of those.
        public Func<ItemIndex, bool> itemFilter;
        /// It gets the owner's equipment too (it can't use it; some items read it).
        public bool copyEquipment;
    }

    // Bodied minions: a few owned things (an Onion) that each get a real RoR2 master and body,
    // so RoR2's own item code runs on them with their own state, as on Engineer's turrets.
    // - Items: the owner's, through `Inventory.CopyItemsFrom` with the spec's filter, again
    //   whenever the owner's inventory changes.
    // - Moving or not: the plugin drives the body's transform from its sim with `Place`, and
    //   CharacterBody.FixedUpdate compares it with the last position (`GetNotMoving`: Bustling
    //   Fungus on a landed Onion). Place it at the thing's root, not a bobbing pose point.
    // - Airborne: it has no working motor, so RoR2 reads it as in the air.
    // - Health: its own HealthComponent.
    // The body is Commando's (motor off, AI idle, model hidden): the plugin draws its own model
    // where the body is. Server only to spawn; clients see it through RoR2's networking.
    public static class Minions
    {
        static readonly HashSet<ContentPack> statesAdded = new HashSet<ContentPack>();

        /// A kind's prefabs, from `Define`.
        public sealed class Kind
        {
            internal MinionSpec spec;
            internal GameObject master, body;
        }

        /// Builds a kind's prefabs and adds them to `pack`. Call from the plugin's content
        /// fill (ContentProvider.Register); `owner` is the plugin's GUID. Null if Commando's
        /// prefabs aren't there.
        public static Kind Define(ContentPack pack, string owner, MinionSpec spec)
        {
            var commando = LegacyResourcesAPI.Load<GameObject>("Prefabs/CharacterBodies/CommandoBody");
            var commandoMaster = LegacyResourcesAPI.Load<GameObject>("Prefabs/CharacterMasters/CommandoMonsterMaster");
            if (!commando || !commandoMaster)
            {
                CommonLog.Error($"Minions: Commando's prefabs not found (body {(bool)commando}, master {(bool)commandoMaster})");
                return null;
            }

            var body = Prefabs.Clone(commando, spec.name + "Body", owner);
            var cb = body.GetComponent<CharacterBody>();
            cb.baseNameToken = spec.nameToken;
            cb.baseMaxHealth = spec.baseMaxHealth;
            cb.levelMaxHealth = spec.levelMaxHealth;
            cb.baseRegen = spec.baseRegen;
            cb.levelRegen = spec.levelRegen;
            cb.baseArmor = spec.baseArmor;
            cb.levelArmor = 0f;
            cb.baseMaxShield = cb.levelMaxShield = 0f;
            if (!spec.targetable)
            {
                // Out of the bullseye list (AI targeting) and nothing to shoot or bump.
                foreach (var hb in body.GetComponentsInChildren<HurtBox>(true))
                    hb.isBullseye = false;
                foreach (var col in body.GetComponentsInChildren<Collider>(true))
                    col.enabled = false;
            }
            // Its states: standing about. The body never gets input (its AI idles), and Place
            // moves it.
            foreach (var esm in body.GetComponents<EntityStateMachine>())
                if (esm.customName == "Body")
                    esm.mainStateType = esm.initialStateType = new SerializableEntityStateType(typeof(MinionMain));

            var master = Prefabs.Clone(commandoMaster, spec.name + "Master", owner);
            var cm = master.GetComponent<CharacterMaster>();
            cm.bodyPrefab = body;
            cm.destroyOnBodyDeath = true;
            // Its AI does nothing: no skill drivers, and the AI machine stays Idle.
            foreach (var driver in master.GetComponents<AISkillDriver>())
                UnityEngine.Object.DestroyImmediate(driver);
            foreach (var esm in master.GetComponents<EntityStateMachine>())
                esm.mainStateType = esm.initialStateType = new SerializableEntityStateType(typeof(Idle));

            body.AddComponent<MinionHidden>();

            pack.bodyPrefabs.Add(new[] { body });
            pack.masterPrefabs.Add(new[] { master });
            if (statesAdded.Add(pack)) pack.entityStateTypes.Add(new[] { typeof(MinionMain) });
            return new Kind { spec = spec, master = master, body = body };
        }

        /// Spawns a `kind` minion owned by `owner` at `at`, with the owner's items. Server
        /// only; null on a client or if it didn't spawn.
        public static Minion Spawn(Kind kind, CharacterBody owner, Vector3 at, Quaternion rotation)
        {
            if (kind == null || !NetworkServer.active || !owner || !owner.master) return null;
            var spec = kind.spec;
            var filter = Filter(spec);
            var master = new MasterSummon
            {
                masterPrefab = kind.master,
                position = at,
                rotation = rotation,
                summonerBodyObject = owner.gameObject,
                ignoreTeamMemberLimit = true,
                inventoryToCopy = owner.inventory,
                inventoryItemCopyFilter = filter,
            }.Perform();
            if (!master) return null;
            var m = new Minion(master, owner.master, spec, filter);
            m.Setup();
            return m;
        }

        static Func<ItemIndex, bool> Filter(MinionSpec spec)
        {
            var standard = Inventory.defaultItemCopyFilterDelegate;
            var own = spec.itemFilter;
            return i => (standard == null || standard(i)) && (own == null || own(i));
        }
    }

    /// A bodied minion, from `Minions.Spawn`.
    public sealed class Minion
    {
        readonly CharacterMaster master, owner;
        readonly MinionSpec spec;
        readonly Func<ItemIndex, bool> filter;
        Inventory ownerInventory;

        internal Minion(CharacterMaster master, CharacterMaster owner, MinionSpec spec, Func<ItemIndex, bool> filter)
        {
            this.master = master;
            this.owner = owner;
            this.spec = spec;
            this.filter = filter;
        }

        public CharacterMaster Master => master;
        public CharacterBody Body => master ? master.GetBody() : null;
        public bool Alive => Body && Body.healthComponent && Body.healthComponent.alive;

        internal void Setup()
        {
            ownerInventory = owner ? owner.inventory : null;
            if (ownerInventory) ownerInventory.onInventoryChanged += CopyItems;
            if (spec.copyEquipment && ownerInventory && master.inventory) master.inventory.CopyEquipmentFrom(ownerInventory, false);
            var body = Body;
            if (!body) return;
            if (spec.invincible && body.healthComponent) body.healthComponent.godMode = true;
            var motor = body.characterMotor;
            if (motor && motor.Motor) motor.Motor.enabled = false;
        }

        void CopyItems()
        {
            if (!master || !master.inventory || !ownerInventory) return;
            master.inventory.CopyItemsFrom(ownerInventory, filter);
            if (spec.copyEquipment) master.inventory.CopyEquipmentFrom(ownerInventory, false);
        }

        /// Puts it at `at` (its root on the ground or in the air) facing `rotation`. Call
        /// every FixedUpdate it exists, on the server; standing still there makes it count as
        /// not moving.
        public void Place(Vector3 at, Quaternion rotation)
        {
            var body = Body;
            if (!body) return;
            var motor = body.characterMotor;
            if (motor && motor.Motor && motor.Motor.enabled) motor.Motor.enabled = false;
            body.transform.SetPositionAndRotation(at, rotation);
            if (body.characterDirection) body.characterDirection.forward = rotation * Vector3.forward;
        }

        /// Explicit conditions for host-driven bodies: hovering counts as moving even
        /// at a fixed point. Grounded and stationary conditions belong to this entity.
        public void Place(Vector3 at, Quaternion rotation, bool moving, bool grounded)
        {
            var body = Body;
            if (!body) return;
            var motion = body.GetComponent<MinionMotion>() ?? body.gameObject.AddComponent<MinionMotion>();
            motion.Set(moving);
            if (body.characterMotor)
            {
                body.characterMotor.velocity = (at - body.transform.position) / Mathf.Max(Time.fixedDeltaTime, 0.001f);
                if (body.characterMotor.Motor)
                    body.characterMotor.Motor.GroundingStatus.IsStableOnGround = grounded;
            }
            Place(at, rotation);
        }

        /// Removes it (no death, no kill credit).
        public void Release()
        {
            if (ownerInventory) ownerInventory.onInventoryChanged -= CopyItems;
            ownerInventory = null;
            if (!NetworkServer.active) return;
            var body = Body;
            if (body) NetworkServer.Destroy(body.gameObject);
            if (master) NetworkServer.Destroy(master.gameObject);
        }
    }

    // Keeps Commando's model hidden on every machine, item displays included (they come and
    // go with the inventory).
    public sealed class MinionHidden : MonoBehaviour
    {
        CharacterBody body;

        void Awake() => body = GetComponent<CharacterBody>();

        void LateUpdate()
        {
            var model = body && body.modelLocator ? body.modelLocator.modelTransform : null;
            if (model) GameSettings.HideRenderers(model, true);
        }
    }

    // A bodied minion's main state: it stands where Place puts it.
    public class MinionMain : GenericCharacterMain
    {
        public override void HandleMovements() { }
        public override void ProcessJump() { }
#if PUBLICIZED_GAME_LIBS
        public override bool CanExecuteSkill(GenericSkill skillSlot) => false;
#else
        protected override bool CanExecuteSkill(GenericSkill skillSlot) => false;
#endif
    }
}
