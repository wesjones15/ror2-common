using System;
using UnityEngine;

namespace Ror2Common
{
    /// Melee's common bone table (`ft/forward.h` `FtPart`, through the fighter's
    /// `FighterPartsTable`) gives every fighter the same named joints, so one mapping
    /// serves all eight without per-fighter data.
    public sealed class MeleeItemAnchors : IItemAnchorSource
    {
        readonly IntPtr sim;
        readonly float[] matrix = new float[16];
        MeleeNative.State state;
        bool posed;

        public MeleeItemAnchors(IntPtr sim) { this.sim = sim; }

        public void BeginFrame()
        {
            posed = sim != IntPtr.Zero && MeleeNative.ssbm_sim_state(sim, out state) == 1;
        }

        public bool TryLocalPosition(ItemAnchor anchor, out Vector3 local)
        {
            local = default;
            uint part = Part(anchor);
            if (!posed || part == uint.MaxValue) return false;
            if (MeleeNative.ssbm_sim_common_bone(sim, part, matrix) != 1) return false;
            // The sim's joint matrices are in fighter-world units; the rig draws the
            // model at the fighter root, so the anchor is relative to it.
            local = new Vector3(matrix[12] - state.position.x, matrix[13] - state.position.y, matrix[14] - state.position.z);
            return true;
        }

        /// `FtPart` ids from ft/forward.h: 4 HipN, 16 BustN, 34 HeadN, 10/15 foot,
        /// 19/37 shoulders, 20/38 arms, 21/39 hands, 8/13 knees.
        static uint Part(ItemAnchor anchor) => anchor switch
        {
            ItemAnchor.Head => 34,
            ItemAnchor.Chest => 16,
            ItemAnchor.Pelvis => 4,
            ItemAnchor.ShoulderL => 19,
            ItemAnchor.ShoulderR => 37,
            ItemAnchor.ElbowL => 20,
            ItemAnchor.ElbowR => 38,
            ItemAnchor.HandL => 21,
            ItemAnchor.HandR => 39,
            ItemAnchor.LegL => 7,
            ItemAnchor.LegR => 12,
            ItemAnchor.KneeL => 8,
            ItemAnchor.KneeR => 13,
            ItemAnchor.FootL => 10,
            ItemAnchor.FootR => 15,
            _ => uint.MaxValue,
        };
    }
}
