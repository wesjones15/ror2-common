using System;
using Ror2Common;
using UnityEngine;

static class Program
{
    static void Check(bool value, string why) { if (!value) throw new Exception(why); }
    static void Main()
    {
        var root = new GameObject().transform;
        var model = new Renderer(31, root);
        var water = new Renderer(31, root);
        var otherPlugin = new Renderer(31);
        var pendingDestroy = new Renderer(31);
        var alreadyHidden = new Renderer(31) { enabled = false };
        var world = new Renderer(0);
        Resources.renderers = new[] { model, water, otherPlugin, pendingDestroy, alreadyHidden, world };
        try
        {
            using (new PortraitIsolation(root, 31))
            {
                Check(model.enabled && water.enabled, "owned models/effects must render");
                Check(!otherPlugin.enabled && !pendingDestroy.enabled, "foreign and deferred-destroy meshes leaked into capture");
                Check(!alreadyHidden.enabled && world.enabled, "unrelated renderer state changed");
                throw new InvalidOperationException("capture failed");
            }
        }
        catch (InvalidOperationException) { }
        Check(otherPlugin.enabled && pendingDestroy.enabled, "render failure didn't restore foreign meshes");
        Check(model.enabled && water.enabled && world.enabled && !alreadyHidden.enabled, "original visibility wasn't preserved");
        Console.WriteLine("PASS: portrait owns its water, excludes other plugins and deferred destruction, restores visibility on failure");
    }
}

// Recording Unity boundary: verifies visibility ownership, not GPU appearance.
namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object value) => value != null; }
    public class GameObject : Object { public int layer; public readonly Transform transform; public GameObject() { transform = new Transform(this); } }
    public class Transform : Object
    {
        public Transform parent; public GameObject gameObject;
        public Transform(GameObject owner) { gameObject = owner; }
        public bool IsChildOf(Transform root) => this == root || (parent != null && parent.IsChildOf(root));
    }
    public class Renderer : Object
    {
        public bool enabled = true; public GameObject gameObject = new GameObject(); public Transform transform => gameObject.transform;
        public Renderer(int layer, Transform parent = null) { gameObject.layer = layer; transform.parent = parent; }
    }
    public static class Resources { public static Renderer[] renderers; public static T[] FindObjectsOfTypeAll<T>() => (T[])(object)renderers; }
}
