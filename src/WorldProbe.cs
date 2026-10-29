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
        static int bodyMask;
        static bool haveBodyMask;

        /// What the probes hit: the layers RoR2's own bodies collide with once `UseBodyCollision`
        /// has read them, else just the world layer. The world layer alone misses what vanilla
        /// characters stand on and are stopped by: a lake bed, the invisible walls round a stage
        /// (their own layer), so a native-sim body falls through the one and walks out through
        /// the other.
        public static int Mask => haveBodyMask ? bodyMask : LayerIndex.world.mask;

        /// Takes the probes' layers from `motor`, the body's character motor: the layers it
        /// collides with (the physics layer matrix for the body's own layer, as its motor builds
        /// them), always with the world layer, never the body's own layer (its colliders, and every
        /// other body's, are not terrain). Call once the motor exists; the motor can stay off.
        public static void UseBodyCollision(CharacterMotor motor, GameObject body)
        {
            if (!motor || !body) return;
            int layers = motor.Motor.CollidableLayers.value | LayerIndex.world.mask;
            layers &= ~(1 << body.layer);
            bodyMask = layers;
            haveBodyMask = true;
            var names = new System.Collections.Generic.List<string>();
            for (int i = 0; i < 32; i++)
                if ((layers & (1 << i)) != 0) names.Add(LayerMask.LayerToName(i) is var n && n.Length > 0 ? n : i.ToString());
            CommonLog.Info($"WorldProbe: probes hit layers {string.Join(", ", names)} (the body's collision layers, minus its own)");
        }

        /// The highest floor at or below `from` within `reach` (`checkGround`). A false return is a
        /// pit with nothing under it. A sim that reads that as the edge of the world (Sunshine's
        /// illegal floor, which turns a body back) makes every gap in a map an invisible wall, where
        /// RoR2's own characters walk or jump over it and the kill zone below deals with a miss, so
        /// answer it with a floor far below instead (see MarioTest's `UnityHost.Ground`).
        public static bool Ground(Vector3 from, float reach, out RaycastHit hit) =>
            Physics.Raycast(from + Vector3.up * 0.001f, Vector3.down, out hit, reach, Mask, QueryTriggerInteraction.Ignore);

        /// Clear standing ground near `sample`, for putting a body back (`Unstuck`'s probe): a floor
        /// at most `rise` above and `drop` below it that is level (normal.y 0.7 or more), with room
        /// for a capsule of `height` and `radius` standing on it, and floor under all four sides of
        /// it, so a body is never put on a narrow edge or a door's corner. `feet` is where it stands.
        public static bool StandingGround(Vector3 sample, float rise, float drop, float height,
            float radius, out Vector3 feet)
        {
            feet = default;
            if (!Ground(sample + Vector3.up * rise, rise + drop, out var hit) || hit.normal.y < 0.7f)
                return false;
            float skin = Mathf.Max(0.02f, height * 0.02f);
            feet = hit.point + Vector3.up * skin;
            if (Physics.CheckCapsule(feet + Vector3.up * radius, feet + Vector3.up * (height - radius),
                radius, Mask, QueryTriggerInteraction.Ignore)) return false;
            for (int i = 0; i < 4; i++)
            {
                var offset = (i < 2 ? Vector3.right : Vector3.forward) * (i % 2 == 0 ? radius : -radius);
                if (!Ground(feet + offset + Vector3.up * (height * 0.1f), height * 0.2f + skin, out var support)
                    || support.normal.y < 0.7f)
                    return false;
            }
            return true;
        }

        /// Whether `point` is where one of the stage's own map zones (RoR2's kill and keep-in
        /// volumes) would send a player back: inside an enter-type zone, or outside an exit-type
        /// one. RoR2 tests the body's collider against these; a native-sim body asks here, so it
        /// is returned at the moment vanilla characters are.
        public static bool InMapZone(Vector3 point) => InMapZone(point, out _);

        /// As `InMapZone`, and the zone that fired.
        public static bool InMapZone(Vector3 point, out MapZone zone)
        {
            foreach (var z in InstanceTracker.GetInstancesList<MapZone>())
            {
                if (!z || !Triggers(z, point)) continue;
                zone = z;
                return true;
            }
            zone = null;
            return false;
        }

        /// Whether `point` is where `zone` fires: inside a trigger-enter zone, outside a trigger-exit one.
        public static bool Triggers(MapZone zone, Vector3 point)
        {
            var col = zone.GetComponent<Collider>();
            if (!col) return false;
            bool inside = Inside(col, point);
            return zone.triggerType == MapZone.TriggerType.TriggerExit ? !inside : inside;
        }

        static bool Inside(Collider col, Vector3 point)
        {
            // Boxes, spheres, capsules and convex meshes know their own shape; the rest, their bounds.
            if (col is BoxCollider || col is SphereCollider || col is CapsuleCollider
                || (col is MeshCollider mesh && mesh.convex))
                return (col.ClosestPoint(point) - point).sqrMagnitude < 1e-6f;
            return col.bounds.Contains(point);
        }

        static readonly RaycastHit[] crossHits = new RaycastHit[8];

        /// Whether a body whose centre line went from `from` to `to` (feet, in Unity units) passed
        /// through a wall, a face whose normal is within `wallNormalY` of horizontal, at any of
        /// the `heights` above its feet; `hit` is the nearest such face. A body's own wall pushing
        /// (`PushOutOfWalls`) is a ring of rays at a few heights, blind from inside a wall (a ray
        /// started in a mesh collider hits no back face), so a body that gets past it, by a
        /// fast step, a corner or a thin pole between rays, walks out of the map unchecked. This
        /// sweep runs from where it stood to where it ended up, so it catches the crossing itself.
        public static bool CrossedWall(Vector3 from, Vector3 to, float[] heights, float wallNormalY,
            out RaycastHit hit) => CrossedWall(from, to, heights, wallNormalY, float.PositiveInfinity, out hit);

        /// As `CrossedWall`, and at heights of `surfacesAbove` or more any face counts, not only
        /// walls: a jump or a knock-back that carries the body diagonally into a slope (a log's
        /// upper side, a boulder) or up into an overhang crosses a face no wall ring sees. Low
        /// rays keep the wall-only rule, so the floor under a walking body's feet is never a
        /// crossing.
        public static bool CrossedWall(Vector3 from, Vector3 to, float[] heights, float wallNormalY,
            float surfacesAbove, out RaycastHit hit)
        {
            hit = default;
            var move = to - from;
            float distance = move.magnitude;
            if (distance < 0.001f) return false;
            var dir = move / distance;
            bool found = false;
            foreach (float h in heights)
            {
                int n = Physics.RaycastNonAlloc(from + Vector3.up * h, dir, crossHits, distance, Mask,
                    QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var c = crossHits[i];
                    if (Mathf.Abs(c.normal.y) >= wallNormalY && h < surfacesAbove) continue;
                    if (!found || c.distance < hit.distance)
                    {
                        hit = c;
                        found = true;
                    }
                }
            }
            return found;
        }

        /// Whether feet at `feet` have sunk under a floor (a face with normal.y over `wallNormalY`)
        /// at most `depth` above them; `top` is that floor's height. A ray from inside a mesh
        /// misses it, so a body pushed a little way into a slope finds no ground under its feet
        /// and falls on into the solid; this asks from above, where the face is seen.
        /// Only a sink of `minSink` or more counts; shallower ones the sims' own ground probes,
        /// which start that far above the feet, already resolve.
        public static bool SunkFloor(Vector3 feet, float depth, float minSink, float wallNormalY, out float top)
        {
            top = feet.y;
            if (!Physics.Raycast(feet + Vector3.up * depth, Vector3.down, out var h, depth, Mask,
                QueryTriggerInteraction.Ignore)) return false;
            if (h.normal.y <= wallNormalY || h.point.y < feet.y + minSink) return false;
            top = h.point.y;
            return true;
        }

        /// `CrossedWall` with a sphere of `radius` instead of a ray, so a thin pole or a corner
        /// between the rays' lines is caught too. A sphere that starts touching something finds
        /// nothing there (distance 0), so a body resting against a wall is never "crossing" it.
        public static bool SweptIntoWall(Vector3 from, Vector3 to, float[] heights, float radius,
            float wallNormalY, out RaycastHit hit)
        {
            hit = default;
            var move = to - from;
            float distance = move.magnitude;
            if (distance < 0.001f) return false;
            var dir = move / distance;
            bool found = false;
            foreach (float h in heights)
            {
                int n = Physics.SphereCastNonAlloc(from + Vector3.up * h, radius, dir, crossHits, distance, Mask,
                    QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var c = crossHits[i];
                    if (c.distance <= 0f || Mathf.Abs(c.normal.y) >= wallNormalY) continue;
                    if (!found || c.distance < hit.distance)
                    {
                        hit = c;
                        found = true;
                    }
                }
            }
            return found;
        }

        static readonly Vector3[] insideDirs =
        {
            Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back,
        };

        /// Whether `point` is inside solid geometry: overlapping a box, sphere, capsule or convex
        /// mesh, or, for a concave mesh (whose inside no overlap test sees), looking out through
        /// the back of its faces in at least two of six directions and through the front of none.
        /// A ray started inside a mesh normally hits no back face, which is how a body that gets
        /// into a wall stays in it; this asks for them.
        public static bool InsideSolid(Vector3 point, float reach = 8f)
        {
            if (Physics.CheckSphere(point, 0.08f, Mask, QueryTriggerInteraction.Ignore)) return true;
            bool before = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            int back = 0, front = 0;
            try
            {
                foreach (var d in insideDirs)
                {
                    if (!Physics.Raycast(point, d, out var h, reach, Mask, QueryTriggerInteraction.Ignore))
                        continue;
                    if (Vector3.Dot(h.normal, d) > 0.01f) back++;
                    else front++;
                }
            }
            finally { Physics.queriesHitBackfaces = before; }
            return back >= 2 && front == 0;
        }

        /// Whether a body standing at `feet` is wedged in geometry: at least `BodyInside` of it is
        /// inside solid (`SolidFraction`). A body a native sim moves can end up like this (sunk
        /// into a log, between rocks) where the sim's ray probes can't see out, and neither falls
        /// nor walks free.
        public static bool Wedged(Vector3 feet, float height, float radius) =>
            SolidFraction(feet, height, radius) >= BodyInside;

        /// How much of a body has to be in terrain before it counts as in it: a foot or a hand
        /// clipping a prop never does, only a body sunk to the waist or more.
        public const float BodyInside = 0.5f;
        const int FractionSamples = 6;

        /// The fraction of a body standing at `feet` that is inside solid geometry: points up its
        /// middle (`InsideSolid`), evenly from ankle to head. Open air costs one capsule test.
        public static float SolidFraction(Vector3 feet, float height, float radius)
        {
            float r = Mathf.Max(0.05f, radius * 0.6f);
            var bottom = feet + Vector3.up * r;
            var top = feet + Vector3.up * Mathf.Max(height - r, r);
            if (!Physics.CheckCapsule(bottom, top, r, Mask, QueryTriggerInteraction.Ignore)
                && !InsideSolid(feet + Vector3.up * (height * 0.5f)))
                return 0f;
            int inside = 0;
            for (int i = 0; i < FractionSamples; i++)
                if (InsideSolid(feet + Vector3.up * (height * (i + 0.5f) / FractionSamples))) inside++;
            return inside / (float)FractionSamples;
        }

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
