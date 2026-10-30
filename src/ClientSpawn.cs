using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace Ror2Common
{
    // Where a client's own body really spawned, and putting it back there before a native sim takes it.
    //
    // A client's body is created by UNet at the server's spawn point (ClientScene.OnObjectSpawn
    // instantiates the prefab at the spawn message's position). Its authority arrives in a later
    // message; until then RoR2's CharacterNetworkTransform treats it as someone else's body and, with
    // no snapshot of its own yet, blends it from a zeroed snapshot at the world origin toward the
    // server's position (ApplyCurrentSnapshot). When authority arrives that blending stops wherever it
    // was, part way along the line from (0, 0, 0) to the spawn: mid-air or under the map. RoR2's own
    // motor then falls from there too, but a vanilla body's motor is only stepped once SpawnTeleporterState
    // has played out, by which time nothing is left of it; a native sim starts at once and falls.
    // Stage 1 never shows it: the pod holds the body and moves it itself.
    //
    // Use: Remember(transform) in Awake; Settle(body, motor) once a step before the sim starts
    // (before GroundGate.Ready), on any machine (it does nothing on the server or after the first start).
    public sealed class ClientSpawn
    {
        /// Further than this from the spawn when authority arrives is the blend, not the body's own move.
        const float Tolerance = 0.5f;

        Vector3 at;
        bool remembered, settled;

        /// The body was just created: this is the server's spawn point.
        public void Remember(Transform body)
        {
            if (!body) return;
            at = body.position;
            remembered = true;
            settled = false;
        }

        /// Call each step until the sim starts. On a client that owns `body`, the first call puts it back at
        /// its spawn point if the network blend left it elsewhere. A pod stage (the pod places the body),
        /// a body that was in a vehicle, or a spawn point with no ground under it is left as it is.
        public void Settle(CharacterBody body, CharacterMotor motor)
        {
            if (settled || !remembered || !body) return;
            if (NetworkServer.active || !body.hasEffectiveAuthority) return;
            settled = true;
            if (body.currentVehicle || Stage.instance && Stage.instance.usePod) return;
            var now = body.transform.position;
            if ((now - at).sqrMagnitude <= Tolerance * Tolerance) return;
            var feet = at - (now - body.footPosition);
            if (!WorldProbe.Ground(feet + Vector3.up * 0.6f, 4f, out _))
            {
                CommonLog.Info($"Client spawn: {body.name} at {now}, its spawn point {at} has no ground under it; left as it is");
                return;
            }
            if (motor && motor.Motor && motor.Motor.enabled) motor.Motor.SetPosition(at, true);
            else body.transform.position = at;
            if (motor) motor.velocity = Vector3.zero;
            Physics.SyncTransforms();
            CommonLog.Info($"Client spawn: {body.name} was at {now} when it became this machine's; put back at its spawn point {at}");
        }
    }
}
