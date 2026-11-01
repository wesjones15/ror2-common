using System.Collections.Generic;
using UnityEngine;
namespace RoR2
{
    public class HitBox : Object
    {
        public bool enabled = true;
        public GameObject gameObject = new GameObject();
        public Transform transform = new Transform();
    }
    public class HitBoxGroup : Object { public HitBox[] hitBoxes; }
    public class HurtBox : Object { }
    public class CharacterBody : Object { public float critMultiplier = 2f; public bool isChampion, isElite; }
    public class DamageInfo { public GameObject attacker, inflictor; public DamageTypeCombo damageType; }
    public class OverlapAttack
    {
        public GameObject attacker = new GameObject(), inflictor = new GameObject();
        public TeamIndex teamIndex = TeamIndex.Monster;
        public float damage = 12f, procCoefficient = 1f, retriggerTimeout = float.PositiveInfinity;
        public bool isCrit, isSphereOverlap;
        public DamageTypeCombo damageType;
        public HitBoxGroup hitBoxGroup;
        public bool Fire(List<HurtBox> list) => false;
        public void Reset() { }
        public void ResetIgnoredHealthComponents() { }
    }
}
