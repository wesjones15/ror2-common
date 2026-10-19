// StepSmoother's timed mode: a slow machine batches several sim ticks between two drawn frames,
// and the body must still glide through each of them (no hold, no jump, never ahead).
using System;
using Ror2Common;
using UnityEngine;

namespace UnityEngine
{
    public class Transform
    {
        public Vector3 position;
        public static implicit operator bool(Transform t) => t != null;
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public float sqrMagnitude => x * x + y * y + z * z;
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) =>
            new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
    }
    public static class Mathf { public static float Clamp01(float f) => Math.Clamp(f, 0f, 1f); }
    public static class Time { public static float time, fixedTime, fixedDeltaTime = 0.02f; }
}

static class Program
{
    static int failures;
    static void Check(bool ok, string what) { if (!ok) { failures++; Console.WriteLine("FAIL: " + what); } }

    static void Main()
    {
        // The sim moves 1 m per tick along x; the body is drawn one tick behind the newest state.
        var body = new Transform();
        var s = new StepSmoother(body);
        double ticks = 0;
        // 12 fps: each frame lands ~2.5 ticks (three physics steps, 30 Hz ticks).
        double frameTicks = 30.0 / 12.0, sim = 0, shown = 0;
        float lastX = 0, worstStep = 0, smallest = float.MaxValue;
        for (int frame = 0; frame < 120; frame++)
        {
            sim += frameTicks;
            while (ticks + 1 <= sim)
            {
                ticks++;
                s.Stepped(new Vector3((float)ticks, 0, 0), ticks);
            }
            shown = sim - 1.0;
            var p = s.SmoothAt(shown);
            if (frame > 4)
            {
                float step = p.x - lastX;
                worstStep = Math.Max(worstStep, step);
                smallest = Math.Min(smallest, step);
                Check(p.x <= ticks + 1e-3, "never ahead of the newest tick");
            }
            lastX = p.x;
        }
        // Every frame should advance by the frame's share of ticks (2.5), +/- rounding.
        Check(Math.Abs(worstStep - 2.5) < 0.05 && Math.Abs(smallest - 2.5) < 0.05,
            $"even steps 2.5 (smallest {smallest}, worst {worstStep})");

        body.position = new Vector3(0, 0, 0);
        // Holds at the newest step when the sim stalls; holds at the oldest before history.
        s.Reset();
        s.Stepped(new Vector3(5, 0, 0), 100);
        Check(s.SmoothAt(1000).x == 5, "clamps to newest");
        Check(s.SmoothAt(-5).x == 0, "clamps to the starting point");
        // Reset forgets the history.
        s.Reset();
        Check(s.SmoothAt(100).x == body.position.x, "reset leaves the body where it is");
        Console.WriteLine(failures == 0 ? "StepSmootherCheck: ok" : $"StepSmootherCheck: {failures} failures");
        Environment.Exit(failures == 0 ? 0 : 1);
    }
}
