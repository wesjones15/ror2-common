using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ror2Common
{
    /// A cached set of held-item motion views, shared by play and attack portraits.
    /// Rust owns visibility, state, animation, attachment and model scale; this
    /// only selects the view and applies the same host transform as the fighter.
    public sealed class MeleeHeldArticle : IDisposable
    {
        readonly IMeleeObjectView[] views;
        readonly Func<int, IMeleeObjectView> create;
        readonly Func<IntPtr, int> state;
        readonly Func<IntPtr, IntPtr, uint> pose;
        IMeleeObjectView visible;
        bool disposed;

        MeleeHeldArticle(int states, Func<int, IMeleeObjectView> create,
            Func<IntPtr, int> state, Func<IntPtr, IntPtr, uint> pose)
        {
            views = new IMeleeObjectView[states];
            this.create = create; this.state = state; this.pose = pose;
        }

        public static MeleeHeldArticle ForFighter(string assetsDir, uint kind)
        {
            if (kind != MeleeNative.FighterLink || !MeleeNative.HasLinkBow) return null;
            // ftlinkspecialn.c::isDrawback / itlinkbow.c::it_803F6E98:
            // PlLk.dat slot 4, public article 3; six ground/air bow motions.
            return new MeleeHeldArticle(6,
                s => new MeleeFighterView(assetsDir, kind, 3, (uint)s),
                MeleeNative.ssbm_sim_link_bow_state, MeleeNative.ssbm_link_bow_update);
        }

        /// Pose before enumerating Views so portrait isolation includes the held item.
        /// A negative state or failed update hides every previously created view.
        public IMeleeObjectView Pose(IntPtr sim)
        {
            if (disposed) return null;
            int next = state(sim);
            if (next < 0) { Hide(); return null; }
            if (next >= views.Length) { Hide(); throw new InvalidOperationException("Invalid held-article state " + next); }
            var view = views[next] ?? (views[next] = create(next));
            if (visible != view) { Hide(); view.Models.HideAll(); }
            if (pose(view.Handle, sim) != 1) { Hide(); return null; }
            visible = view;
            return view;
        }

        public void Draw(IntPtr sim, float scale, Vector3 position, Quaternion rotation)
        {
            var view = Pose(sim);
            view?.Models.Update(scale, d => (position, rotation));
        }

        public IEnumerable<IMeleeObjectView> Views
        {
            get { foreach (var view in views) if (view != null) yield return view; }
        }

        public void Hide() { foreach (var view in views) view?.Models.HideAll(); visible = null; }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            for (int i = 0; i < views.Length; i++) { views[i]?.Dispose(); views[i] = null; }
            visible = null;
        }
    }
}
