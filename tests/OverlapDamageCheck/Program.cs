using System;
using RoR2;
using Ror2Common;
using UnityEngine;
using UnityEngine.Networking;

static class Program
{
    static void Check(bool ok, string text) { if (!ok) throw new Exception(text); }
    static OverlapAttack Attack() => new OverlapAttack
    {
        hitBoxGroup = new HitBoxGroup { hitBoxes = new[] { new HitBox() } }
    };
    static DamageInfo Report(OverlapAttack a) => new DamageInfo { attacker=a.attacker, inflictor=a.inflictor, damageType=a.damageType };
    static void Main()
    {
        var box = new OverlapDamage.Volume(default, new Vector3(4,2,1), Quaternion.identity, false);
        Check(box.Touches(new Vector3(2.2f,0,0), .25f), "body radius reaches box face");
        Check(!box.Touches(new Vector3(2.3f,0,0), .25f), "outside box face misses");
        Check(!box.Touches(new Vector3(2.2f,1.2f,0), .25f), "box corners use distance, not expanded AABB");
        float q = MathF.Sqrt(.5f);
        var rotated = new OverlapDamage.Volume(default, new Vector3(4,2,1), new Quaternion(0,q,0,q), false);
        Check(rotated.Touches(new Vector3(0,0,1.8f), .1f), "rotated long axis hits");
        Check(!rotated.Touches(new Vector3(1.8f,0,0), .1f), "rotated short axis misses");
        var sphere = new OverlapDamage.Volume(default, new Vector3(1,2,3), Quaternion.identity, true);
        Check(sphere.Touches(new Vector3(3.2f,0,0), .25f), "sphere matches RoR2 full-scale radius");
        Check(!sphere.Touches(new Vector3(3.3f,0,0), .25f), "sphere outside misses");
        Check(!box.Touches(new Vector3(float.NaN,0,0), 1), "bad geometry ignored");

        var attack = Attack(); var world = new object(); int hits = 0;
        Time.realtimeSinceStartup = 1f;
        bool Apply(OverlapDamage.Sample sample, OverlapDamage.Swing swing)
        {
            if (!swing.TryHit(world, 1, sample.Now, sample.Retrigger)) return false;
            hits++; return true;
        }
        Check(OverlapDamage.Fire((a,list) =>
        {
            Check(OverlapDamage.CoversReport(Report(a)), "body report covered while swing fires");
            Check(!OverlapDamage.CoversReport(Report(Attack())), "other attacker report preserved");
            return false;
        }, attack, null, Apply), "native-only hit changes Fire result to true");
        Check(!OverlapDamage.CoversReport(Report(attack)), "report scope restored");
        Time.realtimeSinceStartup = 2f;
        Check(!OverlapDamage.Fire((a,list) => false, attack, null, Apply) && hits == 1, "sustained swing hits once");
        OverlapDamage.Reset(attack);
        OverlapDamage.Fire((a,list) => false, attack, null, Apply);
        Check(hits == 2, "new swing can hit again");
        attack.retriggerTimeout = .5f; OverlapDamage.Reset(attack);
        OverlapDamage.Fire((a,list) => false, attack, null, Apply);
        Time.realtimeSinceStartup = 2.5f;
        OverlapDamage.Fire((a,list) => false, attack, null, Apply);
        Check(hits == 3, "retrigger expiry matches strict RoR2 time comparison");
        Time.realtimeSinceStartup = 2.6f;
        OverlapDamage.Fire((a,list) => false, attack, null, Apply);
        Check(hits == 4, "continuous attack retriggers after its interval");
        var memory = new OverlapDamage.Swing();
        Check(memory.TryHit(world, 1, 0, float.PositiveInfinity) && memory.TryHit(world, (1UL<<32)|1, 0, float.PositiveInfinity),
            "reused native id with a new generation remains hittable");
        Check(memory.TryHit(new object(), 1, 0, float.PositiveInfinity), "different native world has independent victims");

        foreach (var team in new[] { TeamIndex.Player, TeamIndex.None })
        {
            var friendly = Attack(); friendly.teamIndex=team;
            OverlapDamage.Fire((a,l) => false, friendly, null, (s,m) => throw new Exception("friendly swing forwarded"));
        }
        NetworkServer.active=false;
        OverlapDamage.Fire((a,l) => false, attack, null, (s,m) => throw new Exception("client dealt native damage"));
        NetworkServer.active=true;
        attack.hitBoxGroup.hitBoxes[0].enabled=false;
        OverlapDamage.Fire((a,l) => false, attack, null, (s,m) => throw new Exception("disabled hitbox forwarded"));
        attack.hitBoxGroup.hitBoxes[0].enabled=true;
        try { OverlapDamage.Fire((a,l) => throw new InvalidOperationException(), attack, null, Apply); }
        catch (InvalidOperationException) { }
        Check(!OverlapDamage.CoversReport(Report(attack)), "exception restores report scope");
        Console.WriteLine("OverlapDamageCheck: ok (oriented boxes, spheres, swing resets, retriggers, report deduplication)");
    }
}
