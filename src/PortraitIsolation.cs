using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ror2Common
{
    // ManualRender is shared by every plugin (each compiles its own Common assembly).
    // Only this stage may contribute, including when another job was disposed in the
    // same frame and Unity has not yet destroyed its objects.
    public sealed class PortraitIsolation : IDisposable
    {
        readonly List<Renderer> hidden = new List<Renderer>();

        public PortraitIsolation(Transform root, int layer)
        {
            foreach (var renderer in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                if (!renderer || !renderer.enabled || renderer.gameObject.layer != layer
                    || renderer.transform.IsChildOf(root)) continue;
                hidden.Add(renderer);
                renderer.enabled = false;
            }
        }

        public void Dispose()
        {
            foreach (var renderer in hidden) if (renderer) renderer.enabled = true;
        }
    }
}
