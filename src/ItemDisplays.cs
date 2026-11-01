using System;
using System.Collections.Generic;
using System.Reflection;
using RoR2;
using UnityEngine;

namespace Ror2Common
{
    /// The body parts an item display can ride. A host maps these to its own rig;
    /// `ItemDisplayRig` places one anchor object per part for it.
    public enum ItemAnchor
    {
        Head, Chest, Pelvis,
        ShoulderL, ShoulderR, ElbowL, ElbowR, HandL, HandR,
        LegL, LegR, KneeL, KneeR, FootL, FootR,
        Count,
    }

    /// Where a host's animated body puts each anchor, in the model's own space
    /// (the space the body's draws use: +y up, +z forward). The rig mirrors X to
    /// match `ModelView`.
    public interface IItemAnchorSource
    {
        /// Read the sim once per frame; called before any `TryLocalPosition`.
        void BeginFrame();
        /// The part's position this frame, in model units relative to the fighter
        /// root. False while the source has no pose yet.
        bool TryLocalPosition(ItemAnchor anchor, out Vector3 local);
    }

    /// Shows RoR2's cosmetic item displays on a body whose real model is drawn by a
    /// native view: it hides the stock (Commando) renderers, repoints the stock rule
    /// set's child names at anchors driven by `IItemAnchorSource`, and scales the
    /// displays to the fighter's height.
    ///
    /// The stock rule set is reused as-is, so item prefabs, keys and placements stay
    /// the game's; only the child transform each rule names changes.
    public sealed class ItemDisplayRig : IDisposable
    {
        /// The plugin's config; false leaves the stock hide path and hides displays.
        public static bool Enabled = true;

        /// Commando's height in metres, which the stock placements and scales were
        /// authored for.
        const float ReferenceHeight = 1.8f;

        static readonly FieldInfo PairsField = typeof(ChildLocator).GetField("transformPairs",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        static readonly FieldInfo PairNameField = PairField("name");
        static readonly FieldInfo PairTransformField = PairField("transform");

        static FieldInfo PairField(string name)
        {
            var pair = typeof(ChildLocator).GetNestedType("NameTransformPair", BindingFlags.Public | BindingFlags.NonPublic);
            return pair?.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        readonly CharacterBody body;
        readonly Transform model;
        readonly CharacterModel characterModel;
        readonly ChildLocator locator;
        readonly IItemAnchorSource source;

        readonly List<Renderer> stock = new List<Renderer>();
        readonly GameObject[] anchors = new GameObject[(int)ItemAnchor.Count];
        readonly Quaternion[] bindRotation = new Quaternion[(int)ItemAnchor.Count];
        readonly List<(int index, object original)> repointed = new List<(int, object)>();

        bool disposed;
        float anchorScale = 1f;
        float pendingHeight = -1f;
        bool scaled;
        Vector3 modelScaleAtBind = Vector3.one;

        ItemDisplayRig(CharacterBody body, Transform model, CharacterModel characterModel, ChildLocator locator,
            IItemAnchorSource source)
        {
            this.body = body;
            this.model = model;
            this.characterModel = characterModel;
            this.locator = locator;
            this.source = source;
        }

        /// True when this model's item displays are hidden by the plugin config, so RoR2's
        /// pickup highlight would draw its square around an empty spot. Only native bodies
        /// (ours) are affected; a vanilla survivor in the same run keeps its highlight.
        public static bool HighlightSuppressed(CharacterModel model)
        {
            if (Enabled || !model) return false;
            var body = model.GetComponentInParent<CharacterBody>();
            return body && body.GetComponent<NativeBody>();
        }

        /// Attaches to a body. Null when disabled, or when the body has no stock
        /// model/child locator (then the caller keeps its normal hide path).
        public static ItemDisplayRig Attach(CharacterBody body, IItemAnchorSource source)
        {
            if (!Enabled || !body || source == null) return null;
            var model = body.modelLocator ? body.modelLocator.modelTransform : null;
            if (!model) return null;
            var characterModel = model.GetComponent<CharacterModel>();
            var locator = model.GetComponent<ChildLocator>();
            if (!characterModel || !locator || PairsField == null || PairNameField == null || PairTransformField == null) return null;
            try
            {
                var rig = new ItemDisplayRig(body, model, characterModel, locator, source);
                rig.Build();
                return rig;
            }
            catch (Exception e)
            {
                CommonLog.Warn("item displays: " + e.Message);
                return null;
            }
        }

        void Build()
        {
            modelScaleAtBind = model.lossyScale;
            for (int i = 0; i < anchors.Length; i++)
            {
                var go = new GameObject("ItemAnchor" + (ItemAnchor)i);
                go.transform.SetParent(model, false);
                anchors[i] = go;
                bindRotation[i] = Quaternion.identity;
            }

            // Commando's bind-pose bone frames keep the stock local angles valid.
            var pairs = (Array)PairsField.GetValue(locator);
            if (pairs == null) throw new InvalidOperationException("no child locator pairs");
            var bound = new bool[anchors.Length];
            for (int i = 0; i < pairs.Length; i++)
            {
                var pair = pairs.GetValue(i);
                var name = PairNameField.GetValue(pair) as string;
                var bone = PairTransformField.GetValue(pair) as Transform;
                var anchor = Map(name);
                if (anchor == null) continue;
                int a = (int)anchor.Value;
                if (!bound[a] && bone)
                {
                    bindRotation[a] = Quaternion.Inverse(model.rotation) * bone.rotation;
                    bound[a] = true;
                }
                repointed.Add((i, pair));
                PairTransformField.SetValue(pair, anchors[a].transform);
                pairs.SetValue(pair, i);
            }
            PairsField.SetValue(locator, pairs);

            // Hide the stock body, but never a renderer an ItemDisplay owns (a clone
            // may already have items when the rig attaches).
            stock.Clear();
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (!r || r.GetComponentInParent<ItemDisplay>()) continue;
                stock.Add(r);
                r.forceRenderingOff = true;
            }

            // Height ratio relative to Commando, so items authored for him keep their
            // proportions on a small or large body. The units per model unit arrive
            // with the first Pose, so keep the raw height until then.
            source.BeginFrame();
            if (source.TryLocalPosition(ItemAnchor.Head, out var head) && source.TryLocalPosition(ItemAnchor.FootL, out var foot))
                pendingHeight = Mathf.Abs(head.y - foot.y);

            // Respawn what the body already carries on the new anchors.
            try
            {
                characterModel.DisableAllItemDisplays();
                if (body.inventory) characterModel.UpdateItemDisplay(body.inventory);
            }
            catch (Exception e) { CommonLog.Warn("item displays: respawn: " + e.Message); }
            CommonLog.Info($"item displays: {stock.Count} stock renderers hidden, {repointed.Count} child names retargeted");
        }

        /// Places the anchors for this frame. `at`, `rotation` and `units` are the same
        /// values the body's `ModelView.Update` is drawn with; `units` converts the
        /// sim's model units to metres.
        public void Pose(Vector3 at, Quaternion rotation, float units)
        {
            if (disposed) return;
            source.BeginFrame();
            if (!scaled)
            {
                scaled = true;
                if (pendingHeight > 0f) anchorScale = Mathf.Clamp(pendingHeight * units / ReferenceHeight, 0.2f, 3f);
            }
            float parent = modelScaleAtBind.x != 0f ? Mathf.Abs(modelScaleAtBind.x) : 1f;
            for (int i = 0; i < anchors.Length; i++)
            {
                if (!source.TryLocalPosition((ItemAnchor)i, out var local)) continue;
                var t = anchors[i].transform;
                // ModelView mirrors every model across its own X (localScale.x = -scale),
                // so the anchor's place is mirrored too; the display itself is not flipped.
                var world = at + rotation * new Vector3(-local.x, local.y, local.z) * units;
                t.SetPositionAndRotation(world, rotation * bindRotation[i]);
                t.localScale = Vector3.one * (anchorScale / parent);
            }
        }

        /// Hides or shows the item displays with the body they ride (the anchors' children).
        public void SetVisible(bool visible)
        {
            if (disposed) return;
            foreach (var go in anchors)
                if (go && go.activeSelf != visible) go.SetActive(visible);
        }

        /// Keeps the stock body hidden as its renderers come and go.
        public void HideStock()
        {
            if (disposed) return;
            for (int i = 0; i < stock.Count; i++)
                if (stock[i] && !stock[i].forceRenderingOff) stock[i].forceRenderingOff = true;
        }

        /// Shows the stock body again (the wrapper is not drawing a replacement).
        public void ShowStock()
        {
            if (disposed) return;
            for (int i = 0; i < stock.Count; i++)
                if (stock[i] && stock[i].forceRenderingOff) stock[i].forceRenderingOff = false;
        }

        /// <inheritdoc/>
        public void Dispose() => Dispose(true);

        /// `showStock` is false when the body is dying and the wrapper's own death presentation
        /// owns the corpse (the Melee fighters' blast-zone KO).
        public void Dispose(bool showStock)
        {
            if (disposed) return;
            disposed = true;
            try
            {
                var pairs = (Array)PairsField.GetValue(locator);
                if (pairs != null)
                {
                    foreach (var (index, pair) in repointed)
                    {
                        if (index < 0 || index >= pairs.Length) continue;
                        // `pair` was boxed before the repoint, so it still holds the stock bone.
                        pairs.SetValue(pair, index);
                    }
                    PairsField.SetValue(locator, pairs);
                }
            }
            catch (Exception) { }
            foreach (var go in anchors)
                if (go) go.SetActive(false);
            if (showStock)
                foreach (var r in stock)
                    if (r) r.forceRenderingOff = false;
        }

        /// The stock child name a rule uses to the anchor that should replace it.
        public static ItemAnchor? Map(string childName)
        {
            if (string.IsNullOrEmpty(childName)) return null;
            string n = childName.ToLowerInvariant();
            bool left = n.EndsWith("l") || n.Contains("left");
            bool right = n.EndsWith("r") || n.Contains("right");
            if (n.Contains("head")) return ItemAnchor.Head;
            if (n.Contains("neck")) return ItemAnchor.Head;
            if (n.Contains("chest") || n.Contains("bust") || n.Contains("torso")) return ItemAnchor.Chest;
            if (n.Contains("pelvis") || n.Contains("hip") || n.Contains("waist")) return ItemAnchor.Pelvis;
            if (n.Contains("clavicle") || n.Contains("shoulder")) return left ? ItemAnchor.ShoulderL : ItemAnchor.ShoulderR;
            if (n.Contains("elbow") || n.Contains("forearm") || n.Contains("lowerarm")) return left ? ItemAnchor.ElbowL : ItemAnchor.ElbowR;
            if (n.Contains("upperarm")) return left ? ItemAnchor.ShoulderL : ItemAnchor.ShoulderR;
            if (n.Contains("hand") || n.Contains("wrist")) return left ? ItemAnchor.HandL : ItemAnchor.HandR;
            if (n.Contains("knee")) return left ? ItemAnchor.KneeL : ItemAnchor.KneeR;
            if (n.Contains("foot") || n.Contains("ankle") || n.Contains("toe")) return left ? ItemAnchor.FootL : ItemAnchor.FootR;
            if (n.Contains("thigh") || n.Contains("leg")) return left ? ItemAnchor.LegL : ItemAnchor.LegR;
            if (n.Contains("arm")) return left ? ItemAnchor.ElbowL : ItemAnchor.ElbowR;
            return null;
        }
    }
}
