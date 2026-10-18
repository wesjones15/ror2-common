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
        Console.WriteLine("EnemyLockCheck: world visibility, on-screen priority, off-screen fallback, cycling and filters pass.");
    }
}
