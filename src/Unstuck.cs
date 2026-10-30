using UnityEngine;

namespace Ror2Common
{
    // Host recovery for a body a native sim moves through RoR2's geometry, when it is stuck
    // (the sim says it is fallen, prone or blocked) and has not moved for three seconds: search
    // for clear standing ground around it, then fall back to the last verified safe spot. Not a
    // game movement rule. The plugin says when the body is `fallen` (its sim's states) and
    // supplies the ground probe. Measure actual displacement, since a blocked sim can still
    // report a large falling/sliding velocity.
    public sealed class Unstuck
    {
        public delegate bool GroundProbe(Vector3 sample, float rise, float drop, out Vector3 feet);
        const float Delay = 3f;
        Vector3 anchor, safeFeet;
        bool watching, hasSafeFeet;
        float elapsed, safeAge, airborne, noSafe;
        Vector3 startFeet;
        bool hasStart;
        RoR2.MapZone ignored;

        // External teleports, vehicles, death and loss of authority invalidate old ground.
        public void Reset()
        {
            watching = hasSafeFeet = false;
            elapsed = safeAge = airborne = noSafe = 0f;
            wedgedFor = clearFor = 0f;
            hasStart = false;
            underFloor = false;
        }

        /// The last spot the body stood on safely, if it has one.
        public bool TryGetSafe(out Vector3 feet)
        {
            feet = safeFeet;
            return hasSafeFeet;
        }

        /// Metres below its last standing spot at which a body is rescued even outside every map
        /// zone (a stage without them; 150 m is past any drop RoR2's stages ask of a player).
        public const float AnyDrop = 150f;
        /// Mario's early TOO BAD point: a clear void, not a descent onto lower terrain.
        public const float VoidDrop = 20f;
        public const float VoidReach = 2000f;

        // Only below the stage's lowest ground counts as off the map (StageFloor): a long drop onto lower
        // ground is not "too bad", and the stage's own out-of-bounds zones above its floor do not send him back.
        public bool Lost(Vector3 feet, float drop = VoidDrop) =>
            hasSafeFeet && safeFeet.y - feet.y > drop && StageFloor.Below(feet) && !WorldProbe.Ground(feet, VoidReach, out _);

        /// A body that has never stood anywhere safe since it was placed (a crash pod that landed on a scene
        /// object and put it out under the map): after a few seconds, falling or inside an out-of-bounds map
        /// zone, it goes back to the map's nearest player spawn point to where it started. `destination` is
        /// the ground at the spawn point.
        bool NoFootingFall(Vector3 feet, float height, GroundProbe probe, out Vector3 destination)
        {
            destination = default;
            if (!hasStart || noSafe < 5f) return false;
            if (airborne < 3f && !WorldProbe.InMapZone(feet + Vector3.up * (height * 0.5f), out _)) return false;
            var spawns = RoR2.SpawnPoint.readOnlyInstancesList;
            Vector3 best = default;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < spawns.Count; i++)
            {
                if (!spawns[i]) continue;
                var at = spawns[i].transform.position;
                float distance = (at - startFeet).sqrMagnitude;
                if (distance < bestDistance) { bestDistance = distance; best = at; }
            }
            if (float.IsInfinity(bestDistance)) return false;
            destination = probe(best, height * 2f, height * 3f, out var clear) ? clear : best + Vector3.up * 0.5f;
            noSafe = 0f;
            CommonLog.Info($"No safe footing since it was placed at {startFeet}: back at the nearest spawn point {destination}");
            return true;
        }

        /// A fall that has gone on too long: airborne for `seconds` without touching anything and well
        /// below the last standing spot, whatever the map zones or ground probes say. Catches a drop
        /// where no clear void or zone ever fires (the body keeps falling through the world).
        public bool FellTooLong(Vector3 feet, float height, GroundProbe probe, out Vector3 destination,
            float seconds = 8f, float below = 40f)
        {
            destination = default;
            if (!hasSafeFeet || airborne < seconds || safeFeet.y - feet.y < below) return false;
            destination = probe(safeFeet, height * 0.25f, height * 0.5f, out var clear) ? clear : safeFeet;
            return true;
        }

        /// Start recovery at the shared early-void point, or at a valid stage zone.
        /// Ground far below prevents the distance fallback, just as for Mario.
        public bool FellEarly(Vector3 feet, float height, GroundProbe probe, out Vector3 destination)
        {
            if (Lost(feet))
            {
                destination = probe(safeFeet, height * 0.25f, height * 0.5f, out var clear) ? clear : safeFeet;
                return true;
            }
            return Fell(feet, height, probe, out destination, float.PositiveInfinity);
        }

        /// Fall-off-the-map recovery, like RoR2's own for its characters, for a body a native sim
        /// moves (its motor is off, so the stage's zones don't bring it back reliably). Call every
        /// tick after `Step`, which keeps the last standing spot. True when the body's centre is
        /// where a map zone would send a player back (`WorldProbe.InMapZone`, the same moment
        /// vanilla characters are returned), or it is `anyDrop` below that spot; `destination` is
        /// that spot, as clear as it is now, no damage dealt. The caller teleports it and calls
        /// `Reset`.
        public bool Fell(Vector3 feet, float height, GroundProbe probe, out Vector3 destination,
            float anyDrop = AnyDrop)
        {
            destination = default;
            if (!hasSafeFeet) return NoFootingFall(feet, height, probe, out destination);
            if (safeFeet.y - feet.y >= anyDrop)
            {
                // A far drop is only off the map below the stage's lowest ground (StageFloor). A stage's own
                // out-of-bounds zones, which also catch a body put under the map, are not held to this.
                if (!StageFloor.Below(feet)) return false;
            }
            else
            {
                if (!WorldProbe.InMapZone(feet + Vector3.up * (height * 0.5f), out var zone)) return false;
                // A zone that fires at the last standing spot too is not telling the body it fell:
                // sending it back there would only fire it again, every tick.
                if (WorldProbe.Triggers(zone, safeFeet + Vector3.up * (height * 0.5f)))
                {
                    if (ignored != zone)
                    {
                        ignored = zone;
                        CommonLog.Info($"Map zone '{zone.name}' ({zone.zoneType}, {zone.triggerType}) fires where the body last stood safely; not used for fall recovery");
                    }
                    return false;
                }
            }
            destination = probe(safeFeet, height * 0.25f, height * 0.5f, out var clear) ? clear : safeFeet;
            return true;
        }

        bool underFloor;
        Vector3 floorTop;

        /// A body its sim let fall through a floor (a seam its ground ray missed): the step went down
        /// through the floor's top (`WorldProbe.CrossedFloor`), and once the whole body is under it,
        /// `destination` is the last standing spot. Nothing else catches this when there is more
        /// map under the floor (a stage's sealed rooms, the inside of its terrain): `Fell` takes that
        /// for lower ground, and the body landing in there would make it the new standing spot, so
        /// none is kept while the body is under a floor it fell through. A body back above that
        /// floor (`WallGuard` lifts one that sank a little) is fine again. Call every tick after
        /// `Step` with the feet before and after the sim's step; the caller teleports it and calls
        /// `Reset`.
        public bool ThroughFloor(Vector3 from, Vector3 feet, float height, GroundProbe probe, out Vector3 destination)
        {
            destination = default;
            if (WorldProbe.CrossedFloor(from, feet, 0.7f, out var top) && (!underFloor || top.y > floorTop.y))
            {
                if (!underFloor) CommonLog.Info($"Fell through the floor at {top}");
                underFloor = true;
                floorTop = top;
            }
            if (!underFloor) return false;
            if (feet.y > floorTop.y - 0.05f)
            {
                underFloor = false;
                return false;
            }
            if (floorTop.y - feet.y < height || !hasSafeFeet) return false;
            underFloor = false;
            destination = probe(safeFeet, height * 0.25f, height * 0.5f, out var clear) ? clear : safeFeet;
            return true;
        }

        public bool Step(Vector3 feet, bool fallen, bool grounded, bool held, float dt,
            float height, GroundProbe probe, out Vector3 destination)
        {
            destination = default;
            float tolerance = Mathf.Max(0.05f, height * 0.08f);
            safeAge += dt;
            airborne = grounded ? 0f : airborne + dt;
            if (!hasStart) { startFeet = feet; hasStart = true; }
            if (!hasSafeFeet) noSafe += dt;
            if (!fallen && grounded && !held && !underFloor && safeAge >= 0.25f)
            {
                safeAge = 0f;
                if (probe(feet, height * 0.25f, height * 0.5f, out var safe)
                    && (safe - feet).sqrMagnitude <= tolerance * tolerance)
                {
                    safeFeet = safe;
                    hasSafeFeet = true;
                }
            }
            if (!fallen || held)
            {
                watching = false;
                elapsed = 0f;
                return false;
            }
            if (!watching || (feet - anchor).sqrMagnitude > tolerance * tolerance)
            {
                anchor = feet;
                elapsed = 0f;
                watching = true;
            }
            elapsed += dt;
            // Float accumulation must not delay a 180-step / 60 Hz timeout by a tick.
            if (elapsed + 0.0001f < Delay) return false;
            elapsed = 0f; // Retry at most once every three seconds if no clear floor exists.
            return Search(feet, height, tolerance, probe, out destination);
        }

        /// Seconds a body must stay wedged (`WorldProbe.Wedged`) before it is moved out.
        public const float WedgeDelay = 2f;
        /// Seconds clear of geometry that end a wedge; shorter blips keep the clock running.
        const float WedgeClear = 0.5f;
        float wedgedFor, clearFor;
        Vector3 wedgeStart;

        /// A body standing or walking inside geometry (sunk into a log, jammed between rocks),
        /// which `Step` never sees: its sim says it is fine and grounded, and the wall guard
        /// snapping it back and forth moves it just enough to restart `Step`'s stillness clock
        /// every time. Here the clock runs on being wedged, not on standing still: jitter and
        /// short blips out keep it running, real travel (a body's height) or half a second clear
        /// restarts it. After `WedgeDelay` the same clear-ground search as `Step` puts it out.
        /// Call every tick after `Step`; `exempt` for states that rightly touch geometry (a
        /// ledge hang or climb) and while something else holds the body.
        public bool Wedged(Vector3 feet, float height, float radius, bool exempt, float dt,
            GroundProbe probe, out Vector3 destination)
        {
            destination = default;
            if (exempt || !WorldProbe.Wedged(feet, height, radius))
            {
                clearFor = exempt ? WedgeClear : clearFor + dt;
                if (clearFor >= WedgeClear) wedgedFor = 0f;
                return false;
            }
            clearFor = 0f;
            if (wedgedFor <= 0f || (feet - wedgeStart).sqrMagnitude > height * height)
            {
                wedgeStart = feet;
                wedgedFor = 0f;
            }
            wedgedFor += dt;
            if (wedgedFor + 0.0001f < WedgeDelay) return false;
            wedgedFor = 0f; // Retry every WedgeDelay if nowhere clear is found.
            float tolerance = Mathf.Max(0.05f, height * 0.08f);
            if (Search(feet, height, tolerance, probe, out destination)) return true;
            CommonLog.Info($"Wedged at {feet} with no clear ground near; retrying");
            return false;
        }

        bool Search(Vector3 feet, float height, float tolerance, GroundProbe probe, out Vector3 destination)
        {
            destination = default;
            // Start above the obstruction, then widen the search around it. The probe checks
            // floor support and standing clearance, including the fallen pod door.
            for (int ring = 0; ring <= 3; ring++)
            {
                int count = ring == 0 ? 1 : 12;
                for (int i = 0; i < count; i++)
                {
                    float angle = i * (2f * Mathf.PI / count);
                    var sample = feet + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * (ring * height);
                    if (probe(sample, height * 2f, height * 3f, out destination)
                        && (destination - feet).sqrMagnitude > tolerance * tolerance)
                        return true;
                }
            }
            // A previously clear spot can become obstructed by a moving prop: recheck it.
            return hasSafeFeet && probe(safeFeet, height * 0.25f, height * 0.5f, out destination)
                && (destination - feet).sqrMagnitude > tolerance * tolerance;
        }
    }
}
