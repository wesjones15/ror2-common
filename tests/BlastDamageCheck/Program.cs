using System;
using RoR2;
using Ror2Common;
using UnityEngine.Networking;

static class Program
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Main()
    {
        foreach (int victims in new[] { 0, 1, 4 })
        {
            int forwarded = 0;
            var result = BlastDamage.Fire(blast =>
            {
                for (int i = 0; i < victims; i++)
                    Check(BlastDamage.CoversReport(DamageType.AOE), "per-victim splash must not be forwarded twice");
                Check(!BlastDamage.CoversReport(DamageType.Generic), "nested direct hits remain independent");
                return new BlastAttack.Result { hitCount = victims };
            }, new BlastAttack(), _ => forwarded++);
            Check(result.hitCount == victims && forwarded == 1, "one native blast even when it hits only terrain");
            Check(!BlastDamage.CoversReport(DamageType.AOE), "later hits are not suppressed");
        }
        int nested = 0;
        BlastDamage.Fire(_ =>
        {
            BlastDamage.Fire(b => default, new BlastAttack(), b => nested++);
            Check(BlastDamage.CoversReport(DamageType.AOE), "nested explosion preserves outer report scope");
            return default;
        }, new BlastAttack(), _ => nested++);
        Check(nested == 2, "nested explosions each forward exactly once");
        foreach (var blast in new[] {
            new BlastAttack { teamIndex = TeamIndex.Player },
            new BlastAttack { teamIndex = TeamIndex.None },
            new BlastAttack { damageType = DamageType.DoT } })
            BlastDamage.Fire(_ => default, blast, _ => throw new Exception("friendly or DoT explosion forwarded"));
        NetworkServer.active = false;
        BlastDamage.Fire(_ => default, new BlastAttack(), _ => throw new Exception("client forwarded damage"));
        NetworkServer.active = true;
        try { BlastDamage.Fire(_ => throw new InvalidOperationException(), new BlastAttack(), _ => { }); }
        catch (InvalidOperationException) { }
        Check(!BlastDamage.CoversReport(DamageType.AOE), "throwing original restores report scope");
        float[] edge = { 1f, 0f, .25f, .5f, .25f, .5f, .75f };
        for (int i = 0; i < edge.Length; i++)
        {
            var model = (BlastAttack.FalloffModel)i;
            Check(BlastDamage.Fraction(model, 0f, 10f) == 1f, "blast centre gets full damage");
            Check(BlastDamage.Fraction(model, 10f, 10f) == edge[i], "falloff matches RoR2 HandleHits");
            Check(BlastDamage.Fraction(model, 11f, 10f) == 0f, "outside blast is safe");
        }
        Check(BlastDamage.Fraction(BlastAttack.FalloffModel.Linear, 2.5f, 10f) == .75f, "linear interior");
        Check(BlastDamage.Fraction(BlastAttack.FalloffModel.None, float.NaN, 10f) == 0f, "invalid geometry ignored");
        Console.WriteLine("BlastDamageCheck: ok (terrain impacts, victim deduplication, nesting, teams, falloff)");
    }
}
