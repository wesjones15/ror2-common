using System;
namespace Ror2Common
{
    /// Native Melee input bits. Physical/RoR2 skill bindings are a host concern.
    public static class MeleeControls
    {
        public const uint Jump=1,Attack=2,Special=4,Sprint=32,Cancel=64,Grab=128,
            NeutralB=256,SideB=512,UpB=1024,DownB=2048,Defend=4096;
        public static uint Skills(MeleeControlLayout l,bool primary,bool secondary,bool utility,bool special)=>
            (primary?l.Primary:0)|(secondary?l.Secondary:0)|(utility?l.Utility:0)|(special?l.Special:0);
        public static uint Extra(MeleeControlLayout l,bool pressed,bool shield)=>pressed?(shield?l.ExtraAlt:l.Extra):0;
        // GenericCharacterMain.ApplyJumpVelocity uses body.jumpPower * motor penalty.
        public static float JumpHeight(float power,float gravity,float penalty=1f)
        {
            if(float.IsNaN(power)||float.IsInfinity(power)||power<=0||float.IsNaN(gravity)||float.IsInfinity(gravity)||gravity>=0||float.IsNaN(penalty)||float.IsInfinity(penalty))return 0;
            float velocity=power*Math.Max(0,penalty);return velocity*velocity/(-2f*gravity);
        }
    }
    /// Which native Melee bit each RoR2 skill slot drives, and what the fifth
    /// (B) button does. Each character supplies its own; the zero layout means
    /// Samus's default mapping.
    public struct MeleeControlLayout
    {
        public uint Primary,Secondary,Utility,Special,Extra,ExtraAlt;
        public static readonly MeleeControlLayout Samus=new MeleeControlLayout{
            Primary=MeleeControls.NeutralB,Secondary=MeleeControls.Attack,
            Utility=MeleeControls.SideB,Special=MeleeControls.UpB,
            Extra=MeleeControls.DownB,ExtraAlt=MeleeControls.Defend,
        };
    }
}
