using System;
using RoR2;
using UnityEngine;

namespace Ror2Common
{
    // A native object (an Onion, a pipe) as a RoR2 interactable, so RoR2 shows its own interact
    // prompt for it and the interact button calls back. It has no collider, so RoR2's aim and
    // proximity search never find it (`ShouldIgnoreSpherecastForInteractibility`): the plugin
    // decides when it's in reach, from its own sim, and hands it to the player's
    // InteractionDriver as `interactableOverride` (which `FindBestInteractableObject` returns
    // first) with `Override`.
    //
    // Host or single player only: `Interactor.AttemptInteraction` performs the interaction
    // directly on the server, but a client sends it by network id (`CmdInteract`), and these
    // have none.
    public sealed class InteractTarget : MonoBehaviour, IInteractable
    {
        /// The prompt's text ("Open the Onion").
        public string context;
        /// Whether `activator` may use it now; null for always. The prompt shows only when true.
        public Func<Interactor, bool> canUse;
        /// The interact button, on the server.
        public Action<Interactor> onUse;

        /// A target named `name` at `at` under `parent`. `procItems`: whether using it counts
        /// for on-interact items (Fireworks, Squid Polyp...), as a chest does.
        public static InteractTarget Make(string name, Transform parent, Vector3 at, string context,
            Func<Interactor, bool> canUse, Action<Interactor> onUse, bool procItems = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            if (!procItems)
                go.AddComponent<InteractionProcFilter>().shouldAllowOnInteractionBeginProc = false;
            var t = go.AddComponent<InteractTarget>();
            t.context = context;
            t.canUse = canUse;
            t.onUse = onUse;
            return t;
        }

        /// Points `driver`'s interact prompt at `target`; with null, takes the prompt back if
        /// it's on one of ours, leaving any other override alone. Call every frame.
        public static void Override(InteractionDriver driver, InteractTarget target)
        {
            if (!driver) return;
            if (target)
                driver.interactableOverride = target.gameObject;
            else if (driver.interactableOverride && driver.interactableOverride.GetComponent<InteractTarget>())
                driver.interactableOverride = null;
        }

        public string GetContextString(Interactor activator) => context;

        public Interactability GetInteractability(Interactor activator) =>
            canUse == null || canUse(activator) ? Interactability.Available : Interactability.Disabled;

        public void OnInteractionBegin(Interactor activator)
        {
            if (canUse == null || canUse(activator)) onUse?.Invoke(activator);
        }

        public bool ShouldIgnoreSpherecastForInteractibility(Interactor activator) => true;

        public bool ShouldShowOnScanner() => false;
    }
}
