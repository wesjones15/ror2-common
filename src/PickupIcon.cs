using RoR2.UI;
using UnityEngine;

namespace Ror2Common
{
    // DLC4 ItemTakenOrbEffect.DelayedUpdateSprite and PickupTakenOrbEffect use an
    // AsyncSprite, not SpriteRenderer.sprite. The effect is pooled: reset its prior
    // request and reconnect its callbacks before the vanilla coroutine assigns the
    // next item's address. SetAddress can short-circuit when the address is retained.
    internal static class PickupIcon
    {
        internal static void Prepare(SpriteRenderer renderer)
        {
            if (!renderer) return;
            var loader = renderer.GetComponent<AsyncSprite>();
            if (!loader) loader = renderer.gameObject.AddComponent<AsyncSprite>();
            loader.renderer = renderer;
            loader.loadOnAssigned = true;
            loader.disableWhileLoading = true;
            loader.enabled = false;
            loader.Clear();
            loader.enabled = true;
            renderer.enabled = false;
        }
    }
}
