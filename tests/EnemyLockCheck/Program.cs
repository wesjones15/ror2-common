using System;
using RoR2;
using Ror2Common;
using UnityEngine;

static class Program
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static CharacterBody Body(float x, float z, TeamIndex team = TeamIndex.Monster)
    {
        var b = new CharacterBody { corePosition = new Vector3(x, 0, z),
            teamComponent = new TeamComponent { teamIndex = team }, healthComponent = new HealthComponent() };
        CharacterBody.readOnlyInstancesList.Add(b);
        return b;
    }
    static CharacterBody Reset()
    {
        CharacterBody.readOnlyInstancesList.Clear();
        Physics.blockWorld = false; Physics.entityOccludes = true; Physics.lastMask = 0;
        GameSettings.camera = new Transform { camera = new Camera() };
        return Body(0, 0, TeamIndex.Player);
    }
    static void Main()
    {
        var owner = Reset(); var target = Body(0, 10);
        var aim = new EnemyLock();
        Check(aim.Update(owner, true, false, default) == target, "target's hull and intervening enemies do not block lock");
        Check(Physics.lastMask == LayerIndex.world.mask, "visibility queries world geometry only");
        Physics.blockWorld = true;
        Check(aim.Update(owner, true, false, default) == null, "world walls still block lock and invalidate held target");

        owner = Reset(); var offscreen = Body(6, 8); var visible = Body(0, 20);
        aim = new EnemyLock();
        Check(aim.Update(owner, true, false, default) == visible, "on-screen target takes priority over closer off-screen enemy");
        visible.healthComponent.alive = false;
        Check(aim.Update(owner, true, false, default) == offscreen, "off-screen target remains available when visible target dies");
        Check(aim.Update(owner, false, false, default) == null, "release clears lock");

        owner = Reset(); target = Body(0, 10); visible = Body(2, 10); offscreen = Body(6, 10);
        aim = new EnemyLock();
        Check(aim.Update(owner, true, false, default) == target, "initial target nearest camera heading");
        Check(aim.Update(owner, true, true, Vector2.right) == visible, "cycle prefers on-screen target in requested direction");
        Check(aim.Update(owner, true, true, Vector2.right) == offscreen, "cycle can reach off-screen target when no visible candidate in direction");

        owner = Reset(); Body(0, 10, TeamIndex.Player); Body(0, 30); var dead = Body(0, 5); dead.healthComponent.alive = false;
        aim = new EnemyLock();
        Check(aim.Update(owner, true, false, default) == null, "team, life and range filters retained");
        target = Body(0, 15); GameSettings.camera = null;
        Check(aim.Update(owner, true, false, default) == target, "no-camera fallback retains targeting");
        // A host's own aim (a VR pointer): the camera looks down +z while the pointer points down +x.
        owner = Reset(); var ahead = Body(0, 10); var pointed = Body(10, 1);
        aim = new EnemyLock { Aim = new Ray(new Vector3(0, 0, 0), new Vector3(1, 0, 0)) };
        Check(aim.Update(owner, true, false, default) == pointed, "aim ray picks the pointed enemy, not the camera's");
        Check(aim.Update(owner, true, false, default) == pointed, "lock is kept while the head looks elsewhere");
        aim.Aim = new Ray(new Vector3(0, 0, 0), new Vector3(0, 0, 1));
        Check(aim.Update(owner, true, false, default) == pointed, "moving the pointer away doesn't drop the lock");
        Check(aim.Update(owner, false, false, default) == null, "release clears an aimed lock");
        Check(aim.Update(owner, true, false, default) == ahead, "after release the lock follows the pointer again");

        // Cycling round the pointer: frame kept upright, so "right" of a pointer down +z is +x.
        owner = Reset(); var middle = Body(0, 10); var left = Body(-2, 10); var right = Body(2, 10);
        aim = new EnemyLock { Aim = new Ray(new Vector3(0, 0, 0), new Vector3(0, 0, 1)) };
        Check(aim.Update(owner, true, false, default) == middle, "aimed lock starts at the pointer");
        Check(aim.Update(owner, true, true, Vector2.right) == right, "aimed cycle right steps to +x");
        Check(aim.Update(owner, true, true, new Vector2(-1, 0)) == middle, "aimed cycle left steps back");
        Check(aim.Update(owner, true, true, new Vector2(-1, 0)) == left, "aimed cycle left again reaches -x");

        // Inside the pointer's cone comes before a nearer enemy outside it.
        owner = Reset(); var near = Body(-4, 2); var inCone = Body(1, 20);
        aim = new EnemyLock { Aim = new Ray(new Vector3(0, 0, 0), new Vector3(0, 0, 1)) };
        Check(aim.Update(owner, true, false, default) == inCone, "aimed cone priority over a near enemy outside it");
        // A long range (Samus's 120 m missile): distance must not outweigh the crosshair. The nearer enemy 11 degrees off the
        // crosshair loses to the one dead centre 100 m away, but only when the distance weight is small (the default is for 25 m).
        owner = Reset(); var closeOff = Body(2, 10); var farCentre = Body(0, 100);
        Check(new EnemyLock { Range = 120f }.Update(owner, true, false, default) == closeOff, "default weighting is unchanged");
        Check(new EnemyLock { Range = 120f, DistanceWeight = .00002f }.Update(owner, true, false, default) == farCentre, "a long-range lock takes the enemy nearest the crosshair");
        // Measured from the camera (where the crosshair is), not from the owner: the camera stands 4 m to the side.
        owner = Reset(); var ownerAhead = Body(0, 100); var cameraAhead = Body(4, 100);
        GameSettings.camera.position = new Vector3(4, 0, 0);
        Check(new EnemyLock { Range = 120f, DistanceWeight = .00002f }.Update(owner, true, false, default) == ownerAhead, "from the owner by default");
        Check(new EnemyLock { Range = 120f, DistanceWeight = .00002f, FromCamera = true }.Update(owner, true, false, default) == cameraAhead, "from the camera when asked");
        Console.WriteLine("EnemyLockCheck: world visibility, on-screen priority, off-screen fallback, cycling, filters and aim-ray locking pass.");
    }
}
