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
            if (actor == null && !failed)
            {
                try
                {
                    actor = factories.TryGetValue(actorId ?? "", out var make) ? make() : null;
                }
                catch (Exception e)
                {
                    CommonLog.Error($"lobby display {actorId}: {e}");
                    actor = null;
                }
                failed = actor == null || actor.Models == null;
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
