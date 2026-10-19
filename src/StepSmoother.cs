using System;
using UnityEngine;

namespace Ror2Common
{
    // A body that a native sim moves once per physics step, drawn smoothly between steps, and
    // how far behind the sim that drawing is.
    //
    // The sim runs in batches (RoR2's 60 Hz FixedUpdate; libsms takes two 120 Hz ticks each), and
    // the body is blended from the last step's position to the newest one over the display
    // frames in between. The body is then drawn up to a step behind the sim. If the native view
    // poses the character at the sim's newest state, each pose and facing is held for several
    // display frames while the body glides on, and fast moves (Mario's triple-jump roll at 165 Hz)
    // read as a ghostly stack of copies. The fix: hand the view LagTicks, so it poses the clip and
    // facing that far back too (libsms: SmsViewFrame.lag, AnimState::frame_before and the yaw
    // between the last two ticks). The view half is the native library's own.
    //
    // Use (as MarioDriver does), with CameraPivot's rules:
    // - FixedUpdate: if MovedElsewhere (a teleporter, an out-of-bounds rescue), teleport the sim
    //   to the body and Reset(); step the sim; Stepped(where the sim has the body now).
    // - Update (before RoR2's camera): Smooth().
    // - LateUpdate: update the view with LagTicks(the sim's tick rate) and draw.
    public sealed class StepSmoother
    {
        readonly Transform body;
        Vector3 prev, cur, written;

        public StepSmoother(Transform body)
        {
            this.body = body;
            Reset();
        }

        /// Where the last step put the body and the step before it.
        public Vector3 Previous => prev;
        public Vector3 Current => cur;

        /// Something other than this moved the body since it last placed it (by over half a
        /// metre).
        public bool MovedElsewhere => body && (body.position - written).sqrMagnitude > 0.25f;

        /// Starts over from where the body is, with nothing to blend.
        public void Reset()
        {
            kept = 0;
            if (body) prev = cur = written = body.position;
        }

        /// A physics step moved the sim: `position` is where it has the body now. Leaves the
        /// body where the blending starts (the step before), never ahead of what's drawn.
        public void Stepped(Vector3 position)
        {
            prev = cur;
            cur = position;
            Place(prev);
        }

        /// This display frame's position, between the last two steps; moves the body there.
        public Vector3 Smooth() => Smooth(Alpha);

        // Timed mode, for a machine too slow to draw a frame per step: several steps can land
        // between two drawn frames, and blending only the last pair would skip the ones before
        // it. Each step is kept with the sim time it was at, and a frame samples the line
        // through them at the time it wants to show.
        const int Kept = 8;
        readonly double[] times = new double[Kept];
        readonly Vector3[] points = new Vector3[Kept];
        int kept;

        /// A step as `Stepped(position)`, remembered as the sim's state at `time` (any steady
        /// unit; ticks are fine).
        public void Stepped(Vector3 position, double time)
        {
            if (kept == 0)
            {
                times[0] = time - 1;
                points[0] = cur;
                kept = 1;
            }
            Stepped(position);
            if (kept == Kept)
            {
                Array.Copy(times, 1, times, 0, Kept - 1);
                Array.Copy(points, 1, points, 0, Kept - 1);
                kept--;
            }
            times[kept] = time;
            points[kept++] = position;
        }

        /// The body at sim time `time` (clamped to the steps it has), moved there.
        public Vector3 SmoothAt(double time)
        {
            if (kept == 0) return Smooth();
            Vector3 p;
            if (time <= times[0]) p = points[0];
            else if (time >= times[kept - 1]) p = points[kept - 1];
            else
            {
                int i = 1;
                while (times[i] < time) i++;
                float t = (float)((time - times[i - 1]) / Math.Max(1e-6, times[i] - times[i - 1]));
                p = Vector3.Lerp(points[i - 1], points[i], t);
            }
            Place(p);
            return p;
        }

        /// A sim with its own clock (for example Pikmin's 30 Hz clock) supplies its alpha.
        public Vector3 Smooth(float alpha)
        {
            var p = Vector3.Lerp(prev, cur, Mathf.Clamp01(alpha));
            Place(p);
            return p;
        }

        void Place(Vector3 p)
        {
            if (body) body.position = p;
            written = p;
        }

        /// How far this display frame is between the last physics step and the next, 0..1.
        public static float Alpha =>
            Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);

        /// How many sim ticks (at `tickHz`) behind the sim's newest state the body is drawn this
        /// frame: what the native view should pose the character that far back by.
        public static float LagTicks(float tickHz) => LagTicks(Alpha, Time.fixedDeltaTime * tickHz);

        /// The same, for a sim stepped outside FixedUpdate: `alpha` of the way from the
        /// previous state to the newest, `ticksPerStep` ticks apart.
        public static float LagTicks(float alpha, float ticksPerStep) =>
            (1f - Mathf.Clamp01(alpha)) * ticksPerStep;
    }
}
