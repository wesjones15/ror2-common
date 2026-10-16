using RoR2;
using UnityEngine;

namespace Ror2Common
{
    // Locking onto an enemy while a button is held (Pikmin's RT, Mario's LT): the one nearest
    // the camera's heading within `Range` (25 m) with a clear line, kept until released or lost;
    // `cycle` steps to the next in `direction` on screen.
    public sealed class EnemyLock
    {
        CharacterBody target;

        /// How far away an enemy can be locked, in metres.
        public float Range = 25f;

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

        public CharacterBody Update(CharacterBody owner, bool held, bool cycle, Vector2 direction)
        {
            if (!held) { target = null; return null; }
            if (!Valid(owner, target)) target = null;
            if (target && !cycle) return target;
            var camera = GameSettings.PlayerCamera();
            var forward = camera ? camera.forward : owner.transform.forward;
            var right = camera ? camera.right : owner.transform.right;
            var up = camera ? camera.up : Vector3.up;
            if (direction.sqrMagnitude < .2f) direction = Vector2.right;
            direction.Normalize();
            CharacterBody best = null, wrap = null;
            float score = float.PositiveInfinity, wrapScore = 0f;
            foreach (var b in CharacterBody.readOnlyInstancesList)
            {
                if (b == target || !Valid(owner, b)) continue;
                var delta = b.corePosition - owner.corePosition;
                if (!target)
                {
                    float s = 1f - Vector3.Dot(delta.normalized, forward) + delta.magnitude * .01f;
                    if (s < score) { best = b; score = s; }
                    continue;
                }
                var from = b.corePosition - target.corePosition;
                var screen = new Vector2(Vector3.Dot(from, right), Vector3.Dot(from, up));
                float along = Vector2.Dot(screen, direction);
                if (along < wrapScore) { wrap = b; wrapScore = along; }
                float alignment = Vector2.Dot(screen.normalized, direction);
                if (alignment <= .1f) continue;
                float rank = screen.magnitude / alignment;
                if (rank < score) { best = b; score = rank; }
            }
            if (best) target = best;
            else if (wrap) target = wrap;
            if (!target) Report(owner);
            return target;
        }
    }
}
