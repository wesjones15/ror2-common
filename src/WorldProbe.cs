using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace Ror2Common
{
    // Collision queries against RoR2's level geometry (the world layer: terrain and props, not
    // characters), in Unity units. The GameCube games' own ground/roof/wall checks map onto
    // these.
    public static class WorldProbe
    {
        public static int Mask => LayerIndex.world.mask;

        /// The highest floor at or below `from` within `reach` (`checkGround`).
        public static bool Ground(Vector3 from, float reach, out RaycastHit hit) =>
            Physics.Raycast(from + Vector3.up * 0.001f, Vector3.down, out hit, reach, Mask, QueryTriggerInteraction.Ignore);

        /// The lowest ceiling at or above `from` within `reach` (`checkRoof`).
        public static bool Roof(Vector3 from, float reach, out RaycastHit hit) =>
            Physics.Raycast(from - Vector3.up * 0.001f, Vector3.up, out hit, reach, Mask, QueryTriggerInteraction.Ignore);

        /// Pushes a vertical cylinder at `center` (horizontal `radius`) out of walls: faces whose
        /// normal is within `wallNormalY` of horizontal. A ring of `rays` horizontal rays out
        /// to the radius; each wall face they reach inside it pushes the centre out along its
        /// horizontal normal, two passes to settle corners. Adds each distinct wall's horizontal
        /// normal to `walls` (up to `maxWalls`) and returns the corrected centre (Y unchanged).
        /// Sunshine's `isTouchedWallsAndMoveXZ` uses `wallNormalY` 0.2.
        public static Vector3 PushOutOfWalls(Vector3 center, float radius, float wallNormalY,
            List<Vector3> walls, int maxWalls, int rays = 16)
        {
            // Every ray stays inside this thin square, so nothing in it, nothing to push off. Most
            // queries (open ground, droplets in the air) end here for one overlap test.
            if (!Physics.CheckBox(center, new Vector3(radius, 0.01f, radius), Quaternion.identity, Mask, QueryTriggerInteraction.Ignore))
                return center;
            var c = center;
            for (int pass = 0; pass < 2; pass++)
            {
                bool moved = false;
                for (int i = 0; i < rays; i++)
                {
                    float a = i * (2f * Mathf.PI / rays);
                    var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    if (!Physics.Raycast(c, dir, out var h, radius, Mask, QueryTriggerInteraction.Ignore))
                        continue;
                    if (Mathf.Abs(h.normal.y) >= wallNormalY)
                        continue;
                    var nh = new Vector3(h.normal.x, 0f, h.normal.z).normalized;
                    float d = Vector3.Dot(c - h.point, nh);
                    if (d >= radius || d < -radius)
                        continue;
                    c += nh * (radius - d);
                    moved = true;
                    if (walls != null && walls.Count < maxWalls && !Seen(walls, nh))
                        walls.Add(nh);
                }
                if (!moved) break;
            }
            c.y = center.y;
            return c;
        }

        static bool Seen(List<Vector3> walls, Vector3 nh)
        {
            foreach (var w in walls)
                if (w.x * nh.x + w.z * nh.z > 0.99f) return true;
            return false;
        }
    }
}
