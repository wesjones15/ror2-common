using System;
using Ror2Common;
using RoR2.UI;
using UnityEngine;

static class Program
{
    static void Check(bool value, string why) { if (!value) throw new Exception(why); }
    static void Main()
    {
        PickupIcon.Prepare(null);
        var renderer = new SpriteRenderer();
        PickupIcon.Prepare(renderer);
        var loader = renderer.GetComponent<AsyncSprite>();
        Check(loader != null && loader.renderer == renderer, "missing loader must bind to the icon renderer");
        Check(loader.loadOnAssigned && loader.disableWhileLoading && loader.enabled, "icon must load and re-enable after arrival");
        Check(!renderer.enabled && renderer.sprite == null, "pooled icon must stay blank while waiting for the new item");
        loader.Assign("item-a");
        var oldRequest = loader.Pending();
        oldRequest();
        Check(renderer.sprite == "item-a" && renderer.enabled, "first pickup must show its icon");

        // Emulate a pool resetting the sprite without resetting the retained async address.
        renderer.sprite = null;
        loader.Assign("item-a");
        Check(renderer.sprite == null, "fixture must reproduce the retained-address fast path");
        PickupIcon.Prepare(renderer);
        loader.Assign("item-a");
        loader.Pending()();
        Check(renderer.sprite == "item-a" && renderer.enabled, "same item on a reused effect must reload");

        loader.Assign("slow-old-item");
        var staleRequest = loader.Pending();
        PickupIcon.Prepare(renderer);
        loader.Assign("new-item");
        staleRequest();
        Check(renderer.sprite == null && !renderer.enabled, "old completion must not repaint a reused effect");
        loader.Pending()();
        Check(renderer.sprite == "new-item" && renderer.enabled, "current completion must show the current item");
        Check(renderer.gameObject.adds == 1, "reuse must not accumulate async components");
        Console.WriteLine("PASS: initial icon, same-item pool reuse, stale async completion, loader ownership");
    }
}

// Recording Unity/RoR2 boundary; models AsyncViewable's same-address early return
// and Clear resetting its request. This does not claim a Unity render/play test.
namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object value) => value != null; }
    public class GameObject
    {
        public AsyncSprite loader; public int adds;
        public T AddComponent<T>() where T : AsyncSprite, new() { adds++; loader = new T(); return (T)loader; }
    }
    public class SpriteRenderer : Object
    {
        public GameObject gameObject = new GameObject(); public bool enabled = true; public string sprite;
        public T GetComponent<T>() where T : AsyncSprite => gameObject.loader as T;
    }
}
namespace RoR2.UI
{
    public class AsyncSprite : UnityEngine.Object
    {
        public SpriteRenderer renderer;
        public bool enabled, loadOnAssigned, disableWhileLoading;
        string address; int generation;
        public void Clear() { generation++; address = null; renderer.sprite = null; }
        public void Assign(string value) { if (address == value) return; address = value; generation++; }
        public Action Pending()
        {
            int request = generation; string value = address;
            return () => {
                if (request != generation || !enabled || !loadOnAssigned) return;
                renderer.sprite = value; if (disableWhileLoading) renderer.enabled = true;
            };
        }
    }
}
