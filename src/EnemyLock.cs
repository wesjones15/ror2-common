using RoR2;
using UnityEngine;

namespace Ror2Common
{
    // Locking onto an enemy while a button is held (Pikmin's RT, Mario's LT): the one nearest
    // the camera's heading within `Range` (25 m) with a clear line, kept until released or lost;
    // `cycle` steps to the next in `direction` on screen. Enemies on screen come first, but one
    // off screen can still be locked (and stepped to) when nothing on screen is in reach. A host
    // can aim the lock with a ray of its own (`Aim`: a VR hand's pointer) in place of the camera.
    public sealed class EnemyLock
    {
        CharacterBody target;

        /// How far away an enemy can be locked, in metres.
        public float Range = 25f;

        /// An aim of the host's own in place of the camera (a VR hand's pointer, so looking about
        /// never moves the lock): the lock picks the enemy nearest this ray, "on screen" is within
        /// `AimConeDegrees` of it, the crosshair is within AimCrosshairDegrees, and a cycle steps
        /// across a frame kept upright around it (world up, so rolling the wrist never swaps left
        /// and right). Set before each Update; null (the default) is the camera, as before.
        public Ray? Aim;
        public float AimConeDegrees = 30f;
        const float AimCrosshairDegrees = 4f;

        bool Valid(CharacterBody owner, CharacterBody candidate)
        {
            if (!candidate || candidate == owner || !candidate.healthComponent || !candidate.healthComponent.alive
                || !candidate.teamComponent || candidate.teamComponent.teamIndex == TeamIndex.None
                || (owner.teamComponent && candidate.teamComponent.teamIndex == owner.teamComponent.teamIndex)) return false;
            var delta = candidate.corePosition - owner.corePosition;
            return delta.sqrMagnitude <= Range * Range && Clear(owner, candidate);
        }

        // Line of sight is the level's geometry only. The movement probes' mask includes enemy bodies,
        // and the line ends inside the target's own hull: that could count the target, or any other
        // enemy in between, as a wall, most often with the flying ones.
        static bool Clear(CharacterBody owner, CharacterBody candidate) =>
            !Physics.Linecast(owner.corePosition, candidate.corePosition, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);

        /// What an enemy off screen adds to its score: more than any on-screen enemy's, so they come
        /// first, but finite, so with none on screen the best of the rest is still taken.
        const float OffScreen = 8f;

        /// Pellets, corpses and buds are what the lock takes only when there is no enemy on screen,
        /// or when the crosshair is directly on one. This is their score penalty otherwise.
        const float PointBelowEnemy = 1000f;
        const float CrosshairRadius = .05f;

        static bool OnScreen(Camera camera, Vector3 point)
        {
            if (!camera) return true;
            var v = camera.WorldToViewportPoint(point);
            return v.z > 0f && v.x > 0f && v.x < 1f && v.y > 0f && v.y < 1f;
        }

        /// Whether the screen-centre crosshair is on this point (a little tighter than "on screen").
        static bool AtCrosshair(Camera camera, Vector3 point)
        {
            if (!camera) return false;
            var v = camera.WorldToViewportPoint(point);
            if (v.z <= 0f) return false;
            float dx = (v.x - .5f) * Mathf.Max(1f, camera.aspect), dy = v.y - .5f;
            return dx * dx + dy * dy <= CrosshairRadius * CrosshairRadius;
        }

        // "On screen" and "at the crosshair": the camera's viewport, or a cone round the host's Aim.
        struct View
        {
            public Camera camera;
            public bool ray;
            public Vector3 origin, forward;
            public float cone, centre;

            float Cos(Vector3 point) => Vector3.Dot((point - origin).normalized, forward);
            public bool Shows(Vector3 point) => ray ? Cos(point) >= cone : OnScreen(camera, point);
            public bool Centres(Vector3 point) => ray ? Cos(point) >= centre : AtCrosshair(camera, point);
        }

        float reported;

        // Why a held lock found nothing, at most every few seconds: for telling a range or line of
        // sight problem from a camera one.
        void Report(CharacterBody owner)
        {
            if (Time.unscaledTime < reported) return;
            reported = Time.unscaledTime + 4f;
            int enemies = 0, far = 0, blocked = 0;
            string by = "";
            foreach (var b in CharacterBody.readOnlyInstancesList)
            {
                if (!b || b == owner || !b.healthComponent || !b.healthComponent.alive || !b.teamComponent
                    || b.teamComponent.teamIndex == TeamIndex.None
                    || (owner.teamComponent && b.teamComponent.teamIndex == owner.teamComponent.teamIndex)) continue;
                enemies++;
                if ((b.corePosition - owner.corePosition).sqrMagnitude > Range * Range) { far++; continue; }
                if (!Clear(owner, b))
                {
                    blocked++;
                    if (by.Length == 0 && Physics.Linecast(owner.corePosition, b.corePosition, out var hit,
                        LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                        by = $" (first behind {hit.collider.name})";
                }
            }
            if (enemies == 0) return;
            CommonLog.Info($"Lock-on found nothing: {enemies} enemies, {far} beyond {Range:0} m, {blocked} out of line of sight{by}");
        }

        /// A thing other than an enemy a host lets the player lock onto (Olimar's pellets, corpses and ship parts).
        public struct LockPoint
        {
            public uint Id;
            public Vector3 Position;
        }

        /// Fills the list with what can be locked besides enemies; asked on every update. Null (the default)
        /// leaves the lock for enemies only, exactly as it was.
        public System.Action<System.Collections.Generic.List<LockPoint>> Points;

        readonly System.Collections.Generic.List<LockPoint> points = new System.Collections.Generic.List<LockPoint>();
        bool hasPoint;
        uint pointId;
        Vector3 pointPosition;

        struct Candidate
        {
            public CharacterBody Body;
            public LockPoint Point;
            public Vector3 Position;
        }

        readonly System.Collections.Generic.List<Candidate> candidates = new System.Collections.Generic.List<Candidate>();

        bool PointValid(CharacterBody owner, Vector3 at) =>
            (at - owner.corePosition).sqrMagnitude <= Range * Range
            && !Physics.Linecast(owner.corePosition, at, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);

        public CharacterBody Update(CharacterBody owner, bool held, bool cycle, Vector2 direction) =>
            Update(owner, held, cycle, direction, out _, out _);

        /// As above, and with `Points` set the lock can sit on one of those instead: then the result is null and
        /// `onPoint` is set, with its id.
        public CharacterBody Update(CharacterBody owner, bool held, bool cycle, Vector2 direction, out bool onPoint, out uint pointOut)
        {
            onPoint = false;
            pointOut = 0;
            if (!held) { target = null; hasPoint = false; return null; }
            if (!Valid(owner, target)) target = null;
            points.Clear();
            if (Points != null) Points(points);
            if (hasPoint)
            {
                bool found = false;
                foreach (var p in points)
                    if (p.Id == pointId) { found = PointValid(owner, p.Position); pointPosition = p.Position; break; }
                if (!found) hasPoint = false;
            }
            if ((target || hasPoint) && !cycle) { onPoint = hasPoint; pointOut = pointId; return target; }
            var camera = Aim.HasValue ? null : GameSettings.PlayerCamera();
            var forward = camera ? camera.forward : owner.transform.forward;
            var right = camera ? camera.right : owner.transform.right;
            var up = camera ? camera.up : Vector3.up;
            var view = new View { camera = camera ? camera.GetComponent<Camera>() : null };
            var eye = owner.corePosition;
            if (Aim.HasValue)
            {
                var ray = Aim.Value;
                eye = ray.origin;
                forward = ray.direction.normalized;
                var flat = Vector3.Cross(Vector3.up, forward);
                if (flat.sqrMagnitude > 1e-4f) right = flat.normalized;
                up = Vector3.Cross(forward, right).normalized;
                view.ray = true;
                view.origin = eye;
                view.forward = forward;
                view.cone = (float)System.Math.Cos(AimConeDegrees * System.Math.PI / 180.0);
                view.centre = (float)System.Math.Cos(AimCrosshairDegrees * System.Math.PI / 180.0);
            }
            if (direction.sqrMagnitude < .2f) direction = Vector2.right;
            direction.Normalize();
            candidates.Clear();
            foreach (var b in CharacterBody.readOnlyInstancesList)
                if (b != target && Valid(owner, b)) candidates.Add(new Candidate { Body = b, Position = b.corePosition });
            foreach (var p in points)
                if (!(hasPoint && p.Id == pointId) && PointValid(owner, p.Position)) candidates.Add(new Candidate { Point = p, Position = p.Position });
            bool have = target || hasPoint;
            bool enemyOnScreen = false;
            foreach (var c in candidates)
                if (c.Body && view.Shows(c.Position)) { enemyOnScreen = true; break; }
            var current = target ? target.corePosition : pointPosition;
            int best = -1, wrap = -1;
            float score = float.PositiveInfinity, wrapScore = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                var at = candidates[i].Position;
                var delta = at - eye;
                float below = enemyOnScreen && !candidates[i].Body && !view.Centres(at) ? PointBelowEnemy : 0f;
                if (!have)
                {
                    float s = 1f - Vector3.Dot(delta.normalized, forward) + delta.magnitude * .01f
                        + (view.Shows(at) ? 0f : OffScreen) + below;
                    if (s < score) { best = i; score = s; }
                    continue;
                }
                var from = at - current;
                var screen = new Vector2(Vector3.Dot(from, right), Vector3.Dot(from, up));
                float along = Vector2.Dot(screen, direction);
                if (along < wrapScore) { wrap = i; wrapScore = along; }
                float alignment = Vector2.Dot(screen.normalized, direction);
                if (alignment <= .1f) continue;
                float rank = screen.magnitude / alignment + (view.Shows(at) ? 0f : OffScreen * 1000f) + below;
                if (rank < score) { best = i; score = rank; }
            }
            int pick = best >= 0 ? best : wrap;
            if (pick >= 0)
            {
                var c = candidates[pick];
                if (c.Body) { target = c.Body; hasPoint = false; }
                else { target = null; hasPoint = true; pointId = c.Point.Id; pointPosition = c.Point.Position; }
            }
            if (!target && !hasPoint) Report(owner);
            onPoint = hasPoint;
            pointOut = pointId;
            return target;
        }
    }
}
