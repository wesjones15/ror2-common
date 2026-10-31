using System;
using System.Collections;
using RoR2;
using RoR2.ContentManagement;
using RoR2.UI;
using UnityEngine;

namespace Ror2Common
{
    // Keep the vanilla coroutine's data delay and tier-colour work. Bind the icon
    // afterward so a pooled AsyncSprite cannot clear/replace our assignment.
    internal static class PickupIcon
    {
        internal static IEnumerator After(IEnumerator original, Action assign)
        {
            try { while (original.MoveNext()) yield return original.Current; }
            finally { (original as IDisposable)?.Dispose(); }
            assign();
        }

        internal static void Bind(SpriteRenderer renderer, AsyncIconProvider provider, string label)
        {
            if (!renderer || provider == null) return;
            var binding = renderer.GetComponent<PickupIconBinding>();
            if (!binding) binding = renderer.gameObject.AddComponent<PickupIconBinding>();
            binding.Bind(renderer, provider, label);
        }
    }

    // Own one request per active effect. The icon's SpriteRenderer receives the
    // actual game sprite directly, independently of AsyncSprite's pooled lifecycle.
    internal sealed class PickupIconBinding : MonoBehaviour
    {
        SpriteRenderer renderer;
        AssetOrDirectReference<Sprite> request;
        int generation;
        bool resolved;
        string label;
        static int reports, failures;

        internal void Bind(SpriteRenderer renderer, AsyncIconProvider provider, string label)
        {
            Cancel();
            this.renderer = renderer;
            this.label = label;
            var old = renderer.GetComponent<AsyncSprite>();
            if (old) { old.Clear(); old.enabled = false; }
            renderer.sprite = null;
            renderer.enabled = false;
            renderer.forceRenderingOff = false;
            int version = generation;
            var direct = provider.GetIcon();
            if (direct) { Show(direct, version); return; }
            var address = provider.GetIconReference();
            if (address == null || !address.RuntimeKeyIsValid()) {
                if (failures++ < 8) CommonLog.Warn("Pickup icon has no sprite/address: " + label);
                return;
            }
            request = AssetOrDirectReference<Sprite>.GetNewInstance(address, true, null,
                sprite => Show(sprite, version), null);
        }

        void Show(Sprite sprite, int version)
        {
            if (!this || version != generation || !renderer || !sprite) return;
            resolved = true;
            renderer.sprite = sprite;
            renderer.color = Color.white;
            renderer.forceRenderingOff = false;
            renderer.enabled = true;
            if (reports++ < 8) CommonLog.Info("Pickup icon assigned: " + label + " -> " + sprite.name);
        }

        void Cancel()
        {
            generation++;
            request?.Reset();
            request = null;
            resolved = false;
        }

        void OnDisable()
        {
            if (request != null && !resolved && failures++ < 8)
                CommonLog.Warn("Pickup icon did not resolve before effect recycle: " + label);
            Cancel();
            if (renderer) { renderer.sprite = null; renderer.enabled = false; }
        }
        void OnDestroy() => Cancel();
    }
}
