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
                if (WorldProbe.CrossedWall(safe, feet, swept, wallNormalY, out var hit)
                    || WorldProbe.SweptIntoWall(safe, feet, swept, SphereRadius, wallNormalY, out hit))
                {
                    why = $"crossed a wall: {hit.collider.name} " +
                        $"(layer {LayerMask.LayerToName(hit.collider.gameObject.layer)}) at {hit.point}";
                    inside = 0;
                    return true;
                }
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
