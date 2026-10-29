using UnityEngine;

namespace Ror2Common
{
    // Keeps a native-sim body out of walls its sim let it into. A sim's wall pushing is a ring of
    // rays (WorldProbe.PushOutOfWalls), which a thin pole, a corner, a fast step or a body that
    // ends up inside a mesh (a ray started in a mesh hits no back face) gets past, and the body
    // then walks out of the map, with RoR2's own collision off. Two checks after each step:
    //
    // - The step itself: swept from where the body stood to where the sim put it, with rays and
    //   a small sphere (WorldProbe.CrossedWall, SweptIntoWall). Crossing a wall is undone.
    // - The place: inside solid geometry for a few steps in a row (WorldProbe.InsideSolid) is
    //   undone too, whatever put it there (a knock-back, a launch, a push-out that popped through
    //   a thin wall, a teleport into the rock). Needs a few steps so a prop's collider the body
    //   brushes through doesn't throw it back.
    //
    // `strict` is for the moments a body is thrown (hit, knocked down, launched): more sweep
    // heights, longer steps still swept, and one step inside is enough.
    //
    // Use: call `Step` after each sim step with the feet the sim ended on; on true, put the body
    // at `back` (and give the sim that position). Call `Placed` whenever the body is put
    // somewhere on purpose (a resync, a teleporter, a rescue), so the next step is swept from
    // there, not skipped. Everything is in Unity units.
    public sealed class WallGuard
    {
        /// Steps longer than this (metres) are not swept: a teleport, not a move.
        public float MaxStep = 3f;
        public float MaxStrictStep = 8f;
        public float SphereRadius = 0.15f;
        /// Steps in a row inside geometry before it is undone (strict: one).
        public int InsideSteps = 3;
        /// How high a ledge the body's sim steps up on its own (Sunshine finds floors from 0.3 m
        /// above the feet, Melee lands from 0.35 m). Sweep rays below it, and spheres reaching
        /// below it, are skipped: a lip or a steep facet of lumpy terrain the sim legitimately
        /// walked up read as a crossed wall and snapped the body back, which made uphills sticky.
        public float StepHeight;
        float[] lastHeights, lastRays, lastSpheres;
        float lastStep = -1f;
        // Both checks below act only once at least WorldProbe.BodyInside of the body (half) is in
        // the terrain, never because a foot clipped a slope or the edge of a prop.
        //
        // Sweep rays at that fraction of the body's height or higher count every face they
        // cross, not only walls (WorldProbe.CrossedWall's `surfacesAbove`): a slope or overhang
        // the body was thrown or jumped into. Lower rays keep the wall-only rule.
        //
        // Feet that far or more under a floor are lifted back onto it. Shallower sinks are the
        // sims' own business (Sunshine probes ground from 0.3 m up, Melee lands from 0.35 m up),
        // and a body sunk less than half way is not in the floor yet.

        Vector3 safe;
        bool haveSafe;
        bool armed;
        int inside;

        public bool HaveSafe => haveSafe;
        public Vector3 Safe => safe;

        public void Reset()
        {
            haveSafe = false;
            armed = false;
            inside = 0;
        }

        // The sweep heights at or above StepHeight: rays from it, spheres whose bottom clears it.
        // Cached for the plain (non-strict) heights, which are the same array every step.
        void Above(float[] heights, out float[] rays, out float[] spheres)
        {
            if (ReferenceEquals(heights, lastHeights) && lastStep == StepHeight)
            {
                rays = lastRays;
                spheres = lastSpheres;
                return;
            }
            var r = new System.Collections.Generic.List<float>();
            var s = new System.Collections.Generic.List<float>();
            foreach (float h in heights)
            {
                if (h >= StepHeight) r.Add(h);
                if (h - SphereRadius >= StepHeight) s.Add(h);
            }
            rays = r.ToArray();
            spheres = s.ToArray();
            lastHeights = heights;
            lastRays = rays;
            lastSpheres = spheres;
            lastStep = StepHeight;
        }

        /// The body was put at `feet` on purpose: the next step is swept from here. If it is
        /// inside geometry (a spawn in a prop), the inside check waits for the first step that
        /// is not, so the body is not thrown back to the same spot for ever.
        public void Placed(Vector3 feet)
        {
            safe = feet;
            haveSafe = true;
            armed = false;
            inside = 0;
        }

        /// The sim ended a step with the feet at `feet`. True: it crossed a wall or sat in one,
        /// `back` is the last good place to put it and `why` says what was found.
        /// `exempt`: the sim moves the body by clip (a ledge climb), so the step is not judged.
        public bool Step(Vector3 feet, float bodyHeight, float[] heights, float wallNormalY, bool strict,
            bool exempt, out Vector3 back, out string why)
        {
            back = safe;
            why = null;
            if (!haveSafe || exempt)
            {
                safe = feet;
                haveSafe = true;
                inside = 0;
                return false;
            }
            float sqr = (feet - safe).sqrMagnitude;
            float max = strict ? MaxStrictStep : MaxStep;
            if (sqr < max * max && sqr > 1e-6f)
            {
                var swept = heights;
                if (strict)
                {
                    swept = new float[heights.Length + 3];
                    heights.CopyTo(swept, 0);
                    swept[heights.Length] = 0.1f;
                    swept[heights.Length + 1] = 1.2f;
                    swept[heights.Length + 2] = Mathf.Max(1.8f, bodyHeight);
                }
                Above(swept, out var rays, out var spheres);
                if (WorldProbe.CrossedWall(safe, feet, rays, wallNormalY, bodyHeight * WorldProbe.BodyInside, out var hit)
                    || WorldProbe.SweptIntoWall(safe, feet, spheres, SphereRadius, wallNormalY, out hit))
                {
                    why = $"crossed a wall: {hit.collider.name} " +
                        $"(layer {LayerMask.LayerToName(hit.collider.gameObject.layer)}) at {hit.point}";
                    inside = 0;
                    return true;
                }
            }
            // A step that did not rise and ended with the feet under a floor (a landing or a
            // knock-back into a slope or a log's side): put the body back on that floor now,
            // before the sim, whose ground ray starts inside the mesh and sees none, lets it fall
            // in. A rising body (a jump up under a ledge) is left to the sweep.
            if (feet.y <= safe.y + 0.001f
                && WorldProbe.SunkFloor(feet, bodyHeight * 0.95f, bodyHeight * WorldProbe.BodyInside, wallNormalY, out float top))
            {
                back = new Vector3(feet.x, top + 0.01f, feet.z);
                why = $"sank {top - feet.y:F2} m under the floor at {feet}";
                safe = back;
                inside = 0;
                return true;
            }
            bool in3d = WorldProbe.InsideSolid(feet + Vector3.up * (bodyHeight * 0.5f));
            if (in3d)
            {
                inside++;
                if (armed && inside >= (strict ? 1 : InsideSteps))
                {
                    why = $"inside solid geometry for {inside} step(s) at {feet}";
                    inside = 0;
                    return true;
                }
                return false;
            }
            inside = 0;
            armed = true;
            safe = feet;
            return false;
        }
    }
}
