using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Ror2Common;
using RoR2;
using RoR2.ContentManagement;
using RoR2.UI;
using UnityEngine;
using UnityEngine.AddressableAssets;

static class Program
{
    static void Check(bool value, string why) { if (!value) throw new Exception(why); }
    static bool vanillaFinished, disposed;
    static IEnumerator Vanilla() { try { yield return 0; vanillaFinished = true; } finally { disposed = true; } }
    static void Main()
    {
        var go = new GameObject();
        var renderer = go.AddComponent<SpriteRenderer>();
        var old = go.AddComponent<AsyncSprite>();
        var first = new Sprite { name = "Hunter's Harpoon" };
        var direct = new Provider { sprite = first };
        var wrapped = PickupIcon.After(Vanilla(), () => {
            Check(vanillaFinished, "bind must run after effect data/tier setup");
            PickupIcon.Bind(renderer, direct, "first");
        });
        Check(wrapped.MoveNext() && renderer.sprite == null, "preserve the vanilla effect-data delay");
        Check(!wrapped.MoveNext() && disposed && renderer.sprite == first && renderer.enabled,
            "direct sprite must be assigned after vanilla setup completes");
        Check(!old.enabled && old.clears == 1, "stop the pooled AsyncSprite from competing with the binding");
        var binding = go.GetComponent<PickupIconBinding>();

        renderer.forceRenderingOff = true;
        var address = new AssetReferenceT<Sprite>();
        var provider = new Provider { address = address };
        PickupIcon.Bind(renderer, provider, "async");
        var request = AssetOrDirectReference<Sprite>.last;
        Check(renderer.sprite == null && !renderer.enabled && !renderer.forceRenderingOff,
            "hide a stale sprite while loading and clear a retained hide flag");
        request.Complete(first);
        Check(renderer.sprite == first && renderer.enabled && renderer.color.a == 1,
            "async completion must explicitly assign and enable the world sprite");

        PickupIcon.Bind(renderer, provider, "same item");
        var same = AssetOrDirectReference<Sprite>.last;
        Check(same != request && request.released, "same item on a reused effect needs a fresh owned request");
        request.Complete(new Sprite { name = "late old result" });
        Check(renderer.sprite == null && !renderer.enabled, "stale callbacks cannot paint the new activation");
        same.Complete(first);
        Check(renderer.sprite == first && renderer.enabled, "same-item pool reuse must show the icon again");

        PickupIcon.Bind(renderer, provider, "pending");
        var pending = AssetOrDirectReference<Sprite>.last;
        typeof(PickupIconBinding).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(binding, null);
        pending.Complete(first);
        Check(pending.released && renderer.sprite == null && !renderer.enabled, "pool return must cancel a pending result");
        var cached = new Sprite { name = "already loaded" };
        PickupIcon.Bind(renderer, new Provider { address = new AssetReferenceT<Sprite> { cached = cached } }, "cached");
        Check(renderer.sprite == cached && renderer.enabled, "synchronous cached completion must survive Bind returning");
        typeof(PickupIconBinding).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(binding, null);
        Check(renderer.sprite == null && !renderer.enabled, "a recycled effect cannot flash its previous item's icon before new effect data arrives");
        Check(go.bindingAdds == 1, "effect reuse must not accumulate binding components");

        bool assigned = false;
        var cancelled = PickupIcon.After(Vanilla(), () => assigned = true);
        cancelled.MoveNext();
        ((IDisposable)cancelled).Dispose();
        Check(!assigned, "a coroutine cancelled by effect recycling must not bind later");
        Console.WriteLine("PASS: original coroutine timing, direct/cached/async sprite assignment, same-item reuse, stale callbacks and pool cancellation");
    }
    sealed class Provider : AsyncIconProvider
    {
        public Sprite sprite; public AssetReferenceT<Sprite> address;
        public Sprite GetIcon() => sprite;
        public AssetReferenceT<Sprite> GetIconReference() => address;
    }
}

// Recording boundary: callbacks write the real binding's SpriteRenderer, without
// relying on Unity's generic AsyncSprite lifecycle. Does not claim a GPU render.
namespace UnityEngine
{
    public class Object { public string name; public static implicit operator bool(Object o) => o != null; }
    public class Component : Object
    {
        public GameObject gameObject;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }
    public class MonoBehaviour : Component { }
    public class GameObject : Object
    {
        readonly Dictionary<Type, Component> components = new Dictionary<Type, Component>();
        public int bindingAdds;
        public T GetComponent<T>() where T : Component => components.TryGetValue(typeof(T), out var c) ? (T)c : null;
        public T AddComponent<T>() where T : Component, new()
        {
            var c = new T { gameObject = this }; components[typeof(T)] = c;
            if (c is PickupIconBinding) bindingAdds++;
            return c;
        }
    }
    public class Sprite : Object { }
    public struct Color { public float a; public static Color white => new Color { a = 1 }; }
    public class SpriteRenderer : Component { public Sprite sprite; public bool enabled, forceRenderingOff; public Color color; }
}
namespace UnityEngine.AddressableAssets
{
    public class AssetReferenceT<T> where T : UnityEngine.Object
    {
        public bool valid = true; public T cached;
        public bool RuntimeKeyIsValid() => valid;
    }
}
namespace RoR2
{
    public interface AsyncIconProvider { Sprite GetIcon(); AssetReferenceT<Sprite> GetIconReference(); }
}
namespace RoR2.UI
{
    public class AsyncSprite : Component { public bool enabled = true; public int clears; public void Clear() { clears++; } }
}
namespace Ror2Common
{
    public static class CommonLog { public static void Info(string message) { } public static void Warn(string message) { } }
}
namespace RoR2.ContentManagement
{
    public class AssetOrDirectReference<T> where T : UnityEngine.Object
    {
        public static AssetOrDirectReference<T> last;
        Action<T> found;
        public bool released;
        public static AssetOrDirectReference<T> GetNewInstance(AssetReferenceT<T> address, bool load, T direct, Action<T> found, Action<T> lost)
        {
            last = new AssetOrDirectReference<T> { found = found };
            if (address.cached != null) found(address.cached);
            return last;
        }
        // Deliberately allow even a released request to call back: exercises the
        // binding's generation guard against a completion already queued elsewhere.
        public void Complete(T sprite) => found(sprite);
        public void Reset() { released = true; }
    }
}
