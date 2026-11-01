namespace UnityEngine.Networking { public static class NetworkServer { public static bool active = true; } }
namespace RoR2
{
    public enum TeamIndex { None, Player, Monster }
    public enum DamageType { Generic = 0, AOE = 1, DoT = 2, Freeze2s = 4 }
    public struct DamageTypeCombo
    {
        int value;
        public static implicit operator DamageTypeCombo(DamageType value) => new DamageTypeCombo { value = (int)value };
        public static int operator &(DamageTypeCombo value, DamageType flag) => value.value & (int)flag;
    }
    public class BlastAttack
    {
        public enum FalloffModel { None, Linear, SweetSpot, HalfLinear, QuarterLinear, HalfSweetSpot, ThreeQuarterSweetSpot }
        public struct Result { public int hitCount; }
        public TeamIndex teamIndex = TeamIndex.Monster;
        public DamageTypeCombo damageType;
    }
}
