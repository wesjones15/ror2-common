using RoR2;
using UnityEngine;

namespace Ror2Common
{
    // The checks compile single shared sources without the network layer: this stands in for MeleeNet.
    internal static class MeleeNet
    {
        public static bool Acts(CharacterBody body) => body != null;
        public static void SendCapture(CharacterBody owner, byte op, CharacterBody victim, Vector3 forward, float dt, Vector3? anchor) { }
        public static int ProtectOn, ProtectOff;
        public static void SendProtect(CharacterBody owner, bool on) { if (on) ProtectOn++; else ProtectOff++; }
    }
}
