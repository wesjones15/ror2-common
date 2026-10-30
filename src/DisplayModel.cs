using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ror2Common
{
    /// A plugin's character for lobby displays: its own native view, posed by its own sim
    /// (standing idle on a flat floor, say). Lent out by the factory given to
    /// DisplayModel.Attach and handed back with Release.
    public interface IDisplayActor
    {
        /// The models to draw.
        ModelView Models { get; }
        /// Metres per native unit.
        float Scale { get; }
        /// Advances the character `dt` seconds and poses it. Its space is the display's: the
        /// origin at the display's root, +Z its forward. `display` is that root (for the camera
        /// in its space).
        void Step(float dt, Transform display);
        /// The display went away: hide the models, and keep them for the next display or free
        /// them.
        void Release();
    }

    // On a survivor's display prefab (the character select screen's mannequin): draws a native
    // character in place of the prefab's own model, standing at the prefab's root and facing
    // its forward, and switches every other renderer under the prefab off, and its animators
    // (so the prefab's intro sound doesn't play). The prefab's own model stays as the stand-in
    // if the character can't be had. A new display is made each time the survivor is picked,
    // and asks the factory again: that's the actor's cue to play its intro.
    public sealed class DisplayModel : MonoBehaviour
    {
        static readonly Dictionary<string, Func<IDisplayActor>> factories = new Dictionary<string, Func<IDisplayActor>>();

        /// Which factory (serialized, so the prefab's instances keep it).
        public string actorId;

        IDisplayActor actor;
        bool failed;
        float retryAt;
        RoR2.CharacterSelectSurvivorPreviewDisplayController pad;

        // The way Enforcer and RoR2's own survivors do it: the display prefab carries a preview controller
        // whose skinChangeResponses name each of the body's skins. The game runs the response of the skin
        // each pad's own player picked, on that pad's own display, so no pad owner has to be found.
        // `bodyPrefab`'s skins must be registered first; response i is skin index i.
        public static void BindSkins(GameObject displayPrefab, GameObject bodyPrefab)
        {
            var model = bodyPrefab ? bodyPrefab.GetComponent<RoR2.ModelLocator>()?.modelTransform : null;
            var skins = model ? model.GetComponent<RoR2.ModelSkinController>()?.skins : null;
            if (skins == null || skins.Length == 0 || !displayPrefab) return;
            var c = displayPrefab.GetComponent<RoR2.CharacterSelectSurvivorPreviewDisplayController>();
            if (!c) c = displayPrefab.AddComponent<RoR2.CharacterSelectSurvivorPreviewDisplayController>();
            c.bodyPrefab = bodyPrefab;
            c.skillChangeResponses = new RoR2.CharacterSelectSurvivorPreviewDisplayController.SkillChangeResponse[0];
            var responses = new RoR2.CharacterSelectSurvivorPreviewDisplayController.SkinChangeResponse[skins.Length];
            for (int i = 0; i < skins.Length; i++)
                responses[i] = new RoR2.CharacterSelectSurvivorPreviewDisplayController.SkinChangeResponse
                    { triggerSkin = skins[i], response = new UnityEngine.Events.UnityEvent() };
            c.skinChangeResponses = responses;
            CommonLog.Info($"Lobby display for {bodyPrefab.name}: {skins.Length} skin responses bound");
        }

        // The skin index this pad's controller last announced (-1: none yet).
        int padSkin = -1;
        bool hooked, refreshed;
        static DisplayModel active;

        void Awake() => Hook();

        void Hook()
        {
            if (hooked) return;
            var c = GetComponent<RoR2.CharacterSelectSurvivorPreviewDisplayController>();
            if (!c || c.skinChangeResponses == null) return;
            hooked = true;
            for (int i = 0; i < c.skinChangeResponses.Length; i++)
            {
                int index = i;
                c.skinChangeResponses[i].response?.AddListener(() => padSkin = index);
            }
        }

        /// The player whose pad is making or stepping a display right now (null: no pad, so the
        /// local player). A lobby shows every player's pad, each in that player's own skin.
        public static RoR2.NetworkUser Owner { get; private set; }

        /// The skin `bodyName`'s loadout selected for the pad being drawn, else for the local
        /// player (0 when none).
        public static uint SelectedSkin(string bodyName)
        {
            var index = RoR2.BodyCatalog.FindBodyIndex(bodyName);
            if (index == RoR2.BodyIndex.None) return 0;
            uint skin = 0;
            string who = "no pad, local player";
            if (active != null && active.padSkin >= 0)
            {
                skin = (uint)active.padSkin;
                who = "the pad's own response";
            }
            else if (Owner)
            {
                var loadout = new RoR2.Loadout();
                Owner.networkLoadout.CopyLoadout(loadout);
                skin = loadout.bodyLoadoutManager.GetSkinIndex(index);
                who = Owner.userName;
            }
            else
                foreach (var user in RoR2.LocalUserManager.readOnlyLocalUsersList)
                    if (user.userProfile != null)
                    {
                        skin = user.userProfile.loadout.bodyLoadoutManager.GetSkinIndex(index);
                        break;
                    }
            // One line whenever a pad's skin changes, to tell whose loadout a lobby pad read.
            var key = bodyName + "|" + who;
            if (!logged.TryGetValue(key, out var last) || last != skin)
            {
                logged[key] = skin;
                CommonLog.Info($"Lobby pad for {bodyName}: skin {skin} ({who})");
            }
            return skin;
        }

        static readonly Dictionary<string, uint> logged = new Dictionary<string, uint>();

        /// Puts a DisplayModel on `displayPrefab` that draws what `make` lends out (null: keep the
        /// prefab's model). `id` names the factory; plugins pick their own.
        public static void Attach(GameObject displayPrefab, string id, Func<IDisplayActor> make)
        {
            factories[id] = make;
            var c = displayPrefab.GetComponent<DisplayModel>();
            if (!c) c = displayPrefab.AddComponent<DisplayModel>();
            c.actorId = id;
        }

        void LateUpdate()
        {
            Hook();
            // The responses may have fired before they were hooked: have the controller announce again, once.
            if (hooked && !refreshed && padSkin < 0)
            {
                refreshed = true;
                var c = GetComponent<RoR2.CharacterSelectSurvivorPreviewDisplayController>();
                if (c && c.isActiveAndEnabled) { c.enabled = false; c.enabled = true; }
            }
            if (!pad) pad = FindPad();
            Owner = pad ? pad.networkUser : null;
            active = this;
            Diagnose();
            try { Draw(); }
            finally { Owner = null; active = null; }
        }

        // The pad controller sits on this display, a child of it, or a neighbour under a nearby ancestor.
        RoR2.CharacterSelectSurvivorPreviewDisplayController FindPad()
        {
            var found = GetComponentInParent<RoR2.CharacterSelectSurvivorPreviewDisplayController>(true);
            if (!found) found = GetComponentInChildren<RoR2.CharacterSelectSurvivorPreviewDisplayController>(true);
            var up = transform.parent;
            for (int i = 0; !found && up && i < 4; i++, up = up.parent)
                found = up.GetComponentInChildren<RoR2.CharacterSelectSurvivorPreviewDisplayController>(true);
            return found;
        }

        float bornAt = -1f;
        bool diagnosed;

        // Once, a second after the display appears: what the pad lookup found, for tracing a wrong skin.
        void Diagnose()
        {
            if (diagnosed) return;
            if (bornAt < 0f) bornAt = Time.unscaledTime;
            if (Time.unscaledTime - bornAt < 1.5f) return;
            diagnosed = true;
            var path = new System.Text.StringBuilder();
            var t = transform;
            for (int i = 0; t && i < 7; i++, t = t.parent)
            {
                path.Append(" < ").Append(t.name).Append('[');
                foreach (var c in t.GetComponents<Component>())
                    if (c) path.Append(c.GetType().Name).Append(' ');
                path.Append(']');
            }
            CommonLog.Info($"Lobby display {actorId}: skin response {padSkin}, pad {(pad ? "found on " + pad.name : "not found")}, owner "
                + (pad && pad.networkUser ? pad.networkUser.userName : "none") + ";" + path);
        }

        void Draw()
        {
            if (actor == null && !failed)
            {
                if(Time.unscaledTime<retryAt)return;
                try
                {
                    actor = factories.TryGetValue(actorId ?? "", out var make) ? make() : null;
                }
                catch (Exception e)
                {
                    CommonLog.Error($"lobby display {actorId}: {e}");
                    actor = null;
                }
                // Native extraction may still be running at first selection.
                // A pending factory must be retried once assets become ready.
                if(actor==null){retryAt=Time.unscaledTime+0.5f;return;}
                failed = actor.Models == null;
                if (failed)
                {
                    actor?.Release();
                    actor = null;
                    return;
                }
            }
            if (actor == null) return;
            // Every frame: the display's animation turns parts and effects on as it plays.
            GameSettings.HideRenderers(transform, true);
            Silence();
            // Lobby previews are UI: a paused/ended run must not freeze them.
            actor.Step(Mathf.Min(Time.unscaledDeltaTime, 0.1f), transform);
            var at = transform;
            actor.Models.Update(actor.Scale, d => (
                at.position + at.rotation * (new Vector3(d.pos.x, d.pos.y, d.pos.z) * actor.Scale),
                at.rotation * new Quaternion(d.rot_x, d.rot_y, d.rot_z, d.rot_w)));
            if(actor is IDisplayEffects effects)effects.Draw(transform);
        }

        // A survivor's display plays a one-off intro clip when picked (Commando's "Display",
        // MUL-T's "PoseForLobby"), then loops an idle; the intro's sounds are animation events
        // (AnimationEvents.PlaySound: Commando's is "play_bandit2_R_load"). With the native
        // character drawn instead, the hidden model's animator stops, so its sounds and effects
        // don't play for the wrong character; the actor plays its own intro. The animator
        // controller loads late (by address), so this checks every frame.
        void Silence()
        {
            if (animators == null || animatorsFoundAt != transform.childCount)
            {
                animators = GetComponentsInChildren<Animator>(true);
                animatorsFoundAt = transform.childCount;
            }
            foreach (var a in animators)
                if (a && a.enabled) a.enabled = false;
        }

        Animator[] animators;
        int animatorsFoundAt = -1;

        void OnDisable() => Release();

        void OnDestroy() => Release();

        void Release()
        {
            if (actor == null) return;
            var a = actor;
            actor = null;
            try { a.Release(); }
            catch (Exception e) { CommonLog.Error($"lobby display {actorId}: {e}"); }
        }
    }
    public interface IDisplayEffects { void Draw(Transform display); }
}
