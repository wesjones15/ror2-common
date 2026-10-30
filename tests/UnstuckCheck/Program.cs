using System;
using Ror2Common;
using UnityEngine;

static class Program
{
    const float Height = 1.6f;
    static readonly Vector3 Origin = Vector3.zero;
    static Unstuck.GroundProbe probe = Floor;
    static int searches;

    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    static bool Floor(Vector3 sample, float rise, float drop, out Vector3 feet)
    {
        searches++;
        feet = new Vector3(sample.x, 0.03f, sample.z);
        return true;
    }

    static bool Blocked(Vector3 sample, float rise, float drop, out Vector3 feet)
    {
        searches++;
        feet = default;
        return false;
    }

    static bool Step(Unstuck monitor, Vector3 feet, bool fallen = true,
        bool grounded = false, bool held = false, float dt = 1f / 60f) =>
        monitor.Step(feet, fallen, grounded, held, dt, Height, probe, out _);

    static void Main()
    {
        // Use the production detector and search policy with a host probe supplied by each
        // scenario. No Unity runtime, game assets or native sim are needed.
        foreach (int hz in new[] { 30, 60, 120 })
        {
            var monitor = new Unstuck();
            searches = 0;
            for (int i = 0; i < hz * 3 - 1; i++)
                Check(!Step(monitor, Origin, dt: 1f / hz), "Recovered before three seconds");
            Check(searches == 0, "Searched before the timeout");
            Check(Step(monitor, Origin, dt: 1f / hz), "Missed three-second timeout");
        }

        var m = new Unstuck();
        for (int i = 0; i < 600; i++)
            Check(!Step(m, new Vector3(i * 0.01f, 0f, 0f)), "Per-step motion mistaken for stuck");
        m.Reset();
        for (int i = 0; i < 600; i++)
            Check(!Step(m, new Vector3(0f, -i * 0.01f, 0f)), "Normal free fall interrupted");
        m.Reset();
        for (int i = 0; i < 600; i++)
            Check(!Step(m, Origin, fallen: false), "Idle/steady hover interrupted");
        m.Reset();
        for (int i = 0; i < 179; i++)
            Check(!Step(m, new Vector3(i % 2 * 0.01f, 0f, 0f)), "Jitter timeout too early");
        Check(Step(m, Origin), "Small collision jitter prevented recovery");

        foreach (bool held in new[] { false, true })
        {
            m.Reset();
            for (int i = 0; i < 150; i++) Step(m, Origin);
            Check(!Step(m, Origin, fallen: held, grounded: !held, held: held), "Idle/held recovery");
            for (int i = 0; i < 179; i++)
                Check(!Step(m, Origin), "Idle/held time carried into timeout");
            Check(Step(m, Origin), "Timeout did not restart after idle/held");
        }
        m.Reset();
        for (int i = 0; i < 150; i++) Step(m, Origin);
        m.Reset(); // Resync, pod entry/exit, death, authority loss.
        for (int i = 0; i < 179; i++) Check(!Step(m, Origin), "Reset kept elapsed time");
        Check(Step(m, Origin), "Reset disabled future recovery");

        // A clear current position is rejected: escape to a different spot on the first ring.
        m.Reset();
        Vector3 target = default;
        for (int i = 0; i < 180; i++)
            m.Step(Origin, true, false, false, 1f / 60f, Height, Floor, out target);
        Check(target.sqrMagnitude >= Height * Height, "Recovered into the same trap");

        // No local solution: return to previously supported ground only after rechecking it.
        for (int blocked = 0; blocked < 3; blocked++)
        {
            m.Reset();
            probe = Floor;
            for (int i = 0; i < 20; i++) Step(m, Origin, fallen: false, grounded: true);
            if (blocked == 2) m.Reset(); // External teleport invalidates the remembered spot.
            Unstuck.GroundProbe onlyOldGround =
                (Vector3 sample, float rise, float drop, out Vector3 feet) => {
                    feet = new Vector3(0f, 0.03f, 0f);
                    return blocked != 1 && sample.sqrMagnitude < 1f;
                };
            bool recovered = false;
            for (int i = 0; i < 180; i++)
                recovered = m.Step(new Vector3(20f, 0f, 0f), true, false, false,
                    1f / 60f, Height, onlyOldGround, out target);
            Check(recovered == (blocked == 0), "Unsafe or stale fallback ground");
            if (recovered) Check(target.sqrMagnitude < 1f, "Did not use remembered safe ground");
        }

        m.Reset(); searches = 0; probe = Blocked;
        for (int i = 0; i < 180; i++) Check(!Step(m, Origin), "Recovered without safe ground");
        Check(searches == 37, "Unexpected local search coverage");
        for (int i = 0; i < 179; i++) Step(m, Origin);
        Check(searches == 37, "Failed search retried every tick");
        Step(m, Origin);
        Check(searches == 74, "Failed search did not retry after three seconds");

        // The exact Mario TOO BAD onset is now shared with Melee/Olimar.
        m.Reset();probe=Floor;
        Step(m,new Vector3(0,.03f,0),fallen:false,grounded:true,dt:.25f);
        Check(m.TryGetSafe(out var safe),"missing safe origin for early void");
        Check(!m.Lost(safe+new Vector3(0,-19.99f,0)),"early void before Mario threshold");
        Check(!m.Lost(safe+new Vector3(0,-20f,0)),"early void at inclusive threshold");
        Check(m.Lost(safe+new Vector3(0,-20.01f,0)),"early void after Mario threshold");
        Check(m.FellEarly(safe+new Vector3(0,-20.01f,0),Height,Floor,out target),"early fall recovery missing");
        Check(WorldProbe.lastReach==2000f&&target.y==safe.y,"wrong void reach or safe destination");
        WorldProbe.groundBelow=true;
        Check(!m.FellEarly(safe+new Vector3(0,-200f,0),Height,Floor,out _),"long descent onto lower ground mistaken for void");
        WorldProbe.inZone=true;
        Check(m.FellEarly(safe+new Vector3(0,-5f,0),Height,Floor,out _),"stage return zone ignored before early void");
        WorldProbe.zoneAtSafe=true;
        Check(!m.FellEarly(safe+new Vector3(0,-5f,0),Height,Floor,out _),"unsafe repeating map zone used");
        WorldProbe.inZone=WorldProbe.zoneAtSafe=WorldProbe.groundBelow=false;
        Check(!m.Lost(safe+new Vector3(0,-25f,0),30f),"Mario custom TOO BAD threshold lost");
        Check(m.Lost(safe+new Vector3(0,-31f,0),30f),"Mario custom threshold ignored");
        m.Reset();Check(!m.FellEarly(new Vector3(0,-200f,0),Height,Floor,out _),"early recovery used stale ground after teleport");

        // StageFloor gating: a drop is only "off the map" below the stage's lowest
        // ground, so a descent onto lower terrain above it is not a rescue.
        m.Reset();probe=Floor;
        Step(m,new Vector3(0,.03f,0),fallen:false,grounded:true,dt:.25f);
        Check(m.TryGetSafe(out safe),"missing safe origin for stage-floor gating");
        StageFloor.found=true;StageFloor.lowest=-50f;
        Check(!m.Lost(safe+new Vector3(0,-25f,0)),"early void above the stage floor");
        StageFloor.lowest=-10f;
        Check(m.Lost(safe+new Vector3(0,-25f,0)),"early void below the stage floor ignored");
        StageFloor.lowest=-200f;
        Check(!m.Fell(safe+new Vector3(0,-180f,0),Height,Floor,out _),"far drop above the stage floor triggered recovery");
        StageFloor.lowest=-150f;
        Check(m.Fell(safe+new Vector3(0,-180f,0),Height,Floor,out target),"far drop below the stage floor ignored");
        StageFloor.found=false;

        // No-footing rescue: never stood safely and kept falling, so the nearest
        // player spawn point to where it started is used.
        m.Reset();probe=Floor;
        RoR2.SpawnPoint.readOnlyInstancesList=new System.Collections.Generic.List<RoR2.SpawnPoint>{
            new RoR2.SpawnPoint{transform=new UnityEngine.Transform{position=new Vector3(80f,0f,0f)}},
            new RoR2.SpawnPoint{transform=new UnityEngine.Transform{position=new Vector3(12f,0f,0f)}},
            new RoR2.SpawnPoint{transform=new UnityEngine.Transform{position=new Vector3(40f,0f,0f)}},
        };
        for(int i=0;i<6*60;i++)Step(m,new Vector3(0f,-i*0.5f,0f));
        Check(m.Fell(new Vector3(0f,-50f,0f),Height,Floor,out target),"no-footing fall did not use a spawn point");
        Check(target.x>11.9f&&target.x<12.1f,"no-footing fall did not pick the nearest spawn point");
        RoR2.SpawnPoint.readOnlyInstancesList=new System.Collections.Generic.List<RoR2.SpawnPoint>();

        Wedges();

        Console.WriteLine("PASS: shared Mario 20m clear-void trigger, stage-floor gating, no-footing spawn rescue, lower terrain/zone safeguards and timeout at 30/60/120 Hz, motion, jitter, idle, crowd control, reset, " +
            "nearby escape, fallback revalidation, failed-search retry");
    }

    const float Radius = 0.5f;
    static bool Wedge(Unstuck m, Vector3 feet, bool exempt = false, float dt = 1f / 60f) =>
        m.Wedged(feet, Height, Radius, exempt, dt, Floor, out _);

    // A body grounded inside geometry (sunk into a log) that the guard jostles back and forth.
    static void Wedges()
    {
        probe = Floor;
        var jitter = new Vector3(0.3f, 0f, 0f);
        WorldProbe.wedged = true;
        // The gap: Step's stillness clock restarts on every 0.3 m jostle, so it never fires.
        var old = new Unstuck();
        for (int i = 0; i < 600; i++)
            Check(!old.Step(i % 2 == 0 ? Origin : jitter, true, true, false, 1f / 60f, Height, Floor, out _),
                "Step unexpectedly caught the jostled wedge");
        foreach (int hz in new[] { 30, 60, 120 })
        {
            var m = new Unstuck();
            int ticks = (int)Math.Round(Unstuck.WedgeDelay * hz);
            for (int i = 0; i < ticks - 1; i++)
                Check(!Wedge(m, i % 2 == 0 ? Origin : jitter, dt: 1f / hz), "Wedge freed before its delay");
            Check(m.Wedged(jitter, Height, Radius, false, 1f / hz, Floor, out var to), "Jostled wedge never freed at " + hz + " Hz");
            Check((to - jitter).sqrMagnitude > 0.01f, "Freed into the same spot");
        }
        // Blips clear of geometry shorter than half a second keep the clock; a longer one resets it.
        var w = new Unstuck();
        for (int i = 0; i < 60; i++) Wedge(w, Origin);
        WorldProbe.wedged = false;
        for (int i = 0; i < 20; i++) Check(!Wedge(w, Origin), "Clear body freed");
        WorldProbe.wedged = true;
        bool freed = false;
        for (int i = 0; i < 60 && !freed; i++) freed = Wedge(w, Origin);
        Check(freed, "A short blip clear restarted the wedge clock");
        w = new Unstuck();
        for (int i = 0; i < 60; i++) Wedge(w, Origin);
        WorldProbe.wedged = false;
        for (int i = 0; i < 30; i++) Wedge(w, Origin);
        WorldProbe.wedged = true;
        for (int i = 0; i < 119; i++) Check(!Wedge(w, Origin), "Half a second clear kept the old wedge time");
        // Walking through a prop's collider (travel past a body height) is not being wedged.
        w = new Unstuck();
        for (int i = 0; i < 600; i++)
            Check(!Wedge(w, new Vector3(i * 0.02f, 0f, 0f)), "Travel through geometry mistaken for a wedge");
        // A ledge hang or a hold rightly touches geometry; it never fires and clears the clock.
        w = new Unstuck();
        for (int i = 0; i < 600; i++) Check(!Wedge(w, Origin, exempt: true), "Exempt state freed");
        for (int i = 0; i < 100; i++) Wedge(w, Origin);
        Check(!Wedge(w, Origin, exempt: true), "Exempt state freed");
        for (int i = 0; i < 119; i++) Check(!Wedge(w, Origin), "Exempt time carried into the wedge clock");
        // A Reset (teleport, death) forgets the wedge.
        w = new Unstuck();
        for (int i = 0; i < 100; i++) Wedge(w, Origin);
        w.Reset();
        for (int i = 0; i < 119; i++) Check(!Wedge(w, Origin), "Reset kept wedge time");
        WorldProbe.wedged = false;
        Console.WriteLine("PASS: a jostled wedge (which the stillness clock never catches) is freed after 2 s at 30/60/120 Hz; blips, travel, exempt states and resets handled");

        // Through the floor into more map under it (Abandoned Aqueduct's sealed room): back to the
        // last standing spot once the whole body is under the floor, and the room never becomes it.
        var f = new Unstuck();
        WorldProbe.groundBelow = true;
        WorldProbe.floorY = 0f;
        for (int i = 0; i < 60; i++) Step(f, Origin, fallen: false, grounded: true);
        Check(f.TryGetSafe(out var standing), "No standing spot on the floor");
        var feetAt = Origin;
        bool back = false;
        Vector3 putAt = default;
        for (int i = 1; i <= 60 && !back; i++)
        {
            var next = new Vector3(0f, -i * 0.1f, 0f);
            Step(f, next, fallen: false, grounded: i > 30);
            back = f.ThroughFloor(feetAt, next, Height, probe, out putAt);
            if (!back) Check(i * 0.1f < Height + 0.05f, $"Still under the floor {i * 0.1f:F1} m down");
            feetAt = next;
        }
        Check(back && Math.Abs(putAt.y - 0.03f) < 0.01f, $"Not put back on the floor it fell through: {putAt.y}");
        Check(f.TryGetSafe(out var still) && Math.Abs(still.y - standing.y) < 0.01f, "The room under the floor became the standing spot");
        // A shallow sink lifted back above the floor (WallGuard's job) is not a fall through it.
        f = new Unstuck();
        for (int i = 0; i < 60; i++) Step(f, Origin, fallen: false, grounded: true);
        Check(!f.ThroughFloor(Origin, new Vector3(0f, -0.5f, 0f), Height, probe, out _), "A half-body sink sent back");
        Check(!f.ThroughFloor(new Vector3(0f, -0.5f, 0f), new Vector3(0f, 0.01f, 0f), Height, probe, out _), "Lifted back onto the floor and sent back");
        Check(!f.ThroughFloor(new Vector3(0f, -0.5f, 0f), new Vector3(0f, -2f, 0f), Height, probe, out _), "Lifted feet still counted under the floor");
        // A drop onto lower ground crosses no floor: never sent back.
        f = new Unstuck();
        for (int i = 0; i < 60; i++) Step(f, Origin, fallen: false, grounded: true);
        WorldProbe.floorY = 10f;
        for (int i = 1; i < 300; i++)
            Check(!f.ThroughFloor(new Vector3(0f, -(i - 1) * 0.1f, 0f), new Vector3(0f, -i * 0.1f, 0f), Height, probe, out _), "A plain drop sent back");
        WorldProbe.floorY = float.NaN;
        WorldProbe.groundBelow = false;
        Console.WriteLine("PASS: a fall through the floor into map below it goes back to the last standing spot once the body is under it; sinks and plain drops do not");
    }
}

// Small managed stand-ins for the vector/math operations used by the detector. Collision
// safety remains UnityHost.RecoveryGround's responsibility and needs an in-game check.
namespace UnityEngine
{
    public struct RaycastHit {}
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => default;
        public static Vector3 up => new Vector3(0f, 1f, 0f);
        public float sqrMagnitude => x * x + y * y + z * z;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.x * b, a.y * b, a.z * b);
    }
    public static class Mathf
    {
        public const float PI = (float)Math.PI;
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Sin(float a) => (float)Math.Sin(a);
        public static float Cos(float a) => (float)Math.Cos(a);
    }
    public class Object { public static implicit operator bool(Object obj) => obj != null; }
    public class Transform : Object { public Vector3 position; }
}

namespace RoR2
{
    public class MapZone { public string name, zoneType, triggerType; }
    public class SpawnPoint : UnityEngine.Object
    {
        public UnityEngine.Transform transform;
        public static System.Collections.Generic.List<SpawnPoint> readOnlyInstancesList = new System.Collections.Generic.List<SpawnPoint>();
    }
}

namespace Ror2Common
{
    static class WorldProbe
    {
        public static bool groundBelow,inZone,zoneAtSafe,wedged;public static float lastReach;
        public static bool Wedged(Vector3 feet, float height, float radius) => wedged;
        public static float floorY = float.NaN;
        // A floor at floorY, crossed by a step from on or above it to under it.
        public static bool CrossedFloor(Vector3 from, Vector3 to, float normalY, out Vector3 top)
        {
            top = new Vector3(to.x, floorY, to.z);
            return to.y < from.y && from.y >= floorY && to.y < floorY - 0.05f;
        }
        static readonly RoR2.MapZone testZone=new RoR2.MapZone();
        public static bool Ground(Vector3 point,float reach,out UnityEngine.RaycastHit hit){hit=default;lastReach=reach;return groundBelow;}
        public static bool InMapZone(Vector3 point, out RoR2.MapZone zone) { zone = testZone; return inZone; }
        public static bool Triggers(RoR2.MapZone zone, Vector3 point) => zoneAtSafe;
    }
    static class CommonLog { public static void Info(string message) { } }
    // Controllable stand-in for the stage's lowest ground graph node. A stage with
    // no graph has no floor (found=false), matching StageFloor.TryLowest.
    static class StageFloor
    {
        public const float Margin = 2f;
        public static bool found;
        public static float lowest;
        public static bool TryLowest(out float y) { y = lowest; return found; }
        public static bool Below(UnityEngine.Vector3 feet) => !TryLowest(out float y) || feet.y < y - Margin;
    }
}
