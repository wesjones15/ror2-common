using System;
using System.IO;
using System.Runtime.InteropServices;
using Ror2Common;
using Sms = MarioTest.Native;
static class Program
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static readonly Sms.GroundFn smsGround = (IntPtr u, ref Sms.Vec3 p, out Sms.GroundHit h) => { h = new Sms.GroundHit { normal = new Sms.Vec3(0, 1, 0) }; return 1; };
    static readonly MeleeNative.CollideFn meleeGround = (IntPtr u, in MeleeNative.Vec3 from, in MeleeNative.Vec3 to, out MeleeNative.Collision hit) =>
    { hit = new MeleeNative.Collision { position = new MeleeNative.Vec3(to.x, Math.Max(0, to.y), to.z), grounded = to.y <= 0 ? 1u : 0u }; };
    static void Main(string[] args)
    {
        Check(args.Length == 2, "usage: NoAssetsCheck sms|ssbm|pikmin <native DLL>");
        string dll = Path.GetFullPath(args[1]);
        if (args[0] == "sms") Mario(dll);
        else if (args[0] == "ssbm") Melee(dll);
        else if (args[0] == "pikmin") Pikmin(dll);
        else throw new Exception("unknown native kind");
        Console.WriteLine("PASS: " + args[0] + " no-assets boundary (empty path; no discovery, models or audio required)");
    }
    static void Mario(string dll)
    {
        Check(Sms.Load(dll, out var error), error);
        var feet = new Sms.Vec3();
        var host = new Sms.Host { ground = Marshal.GetFunctionPointerForDelegate(smsGround) };
        var pad = new Sms.Pad { r = 1, r_analog = 255 };
        for (int repeat = 0; repeat < 3; repeat++)
        {
            IntPtr sim = Sms.sms_sim_new(NativeLib.CString(""), ref feet, 0);
            Check(sim != IntPtr.Zero, "Mario constructor without assets");
            try {
                for (int frame = 0; frame < 180; frame++) Check(Sms.sms_sim_step(sim, ref host, ref pad, 0, 1f/60f, IntPtr.Zero) > 0, "Mario step");
                Check(Sms.sms_sim_state(sim, out var state) != 0 && state.water < 1f, "spray consumes water");
                var particles = new Sms.WaterParticle[512];
                Check(Sms.sms_sim_water_particles(sim, particles, (uint)particles.Length) > 0, "live drawing state without a view");
                Check(float.IsFinite(particles[0].pos.x) && particles[0].radius > 0, "water layout/finite data");
                var spans = new Sms.WaterSegment[512];
                Check(Sms.sms_sim_water_segments(sim, 1f, spans, (uint)spans.Length) > 0, "textured-water spans exist without a view");
                Check(float.IsFinite(spans[0].tail.z) && spans[0].radius > 0, "water-segment layout/finite data");
                Check(Sms.sms_sim_emitter(sim,0,out var muzzle,out var direction)!=0 && float.IsFinite(muzzle.y)
                    && float.IsFinite(direction.z),"stand-in nozzle uses a valid gameplay emitter");
                var wet=new Sms.WaterVertex[16384];
                Check(Sms.sms_sim_wet_vertices(sim,wet,(uint)wet.Length)>0 && float.IsFinite(wet[0].pos.y),"wet terrain exists without a view");
            } finally { Sms.sms_sim_free(sim); }
        }
    }
    static void Melee(string dll)
    {
        MeleeNative.Load(dll);
        var host = new MeleeNative.Host { collide = Marshal.GetFunctionPointerForDelegate(meleeGround) };
        var samples = new MeleeNative.HitboxSample[64];
        for (uint kind = 0; kind < 8; kind++)
        {
            IntPtr sim = MeleeNative.ssbm_sim_new_fighter(NativeLib.CString(""), default, kind);
            Check(sim != IntPtr.Zero, "fighter " + kind + " constructor");
            // The host holds MeleeArrival as the arrival state exactly for the fighters whose built-in
            // sim runs the shared arrival (MeleeNative.AuthoredArrival); the two must agree.
            IntPtr arrival = MeleeNative.ssbm_sim_new_fighter(NativeLib.CString(""), default, kind);
            Check(arrival != IntPtr.Zero && MeleeNative.ssbm_sim_begin_arrival(arrival, NativeLib.CString("")) == 1, "built-in arrival call");
            Check((MeleeNative.ssbm_sim_rebirth_flags(arrival) != 0) == MeleeNative.AuthoredArrival(kind),
                "fighter " + kind + " built-in arrival matches MeleeNative.AuthoredArrival");
            MeleeNative.ssbm_sim_free(arrival);
            IntPtr view = MeleeNative.ssbm_view_new(NativeLib.CString(""), kind, 0);
            try {
                Check(view != IntPtr.Zero && MeleeNative.ssbm_view_model(view, out var model) == 1 && model.vertex_count > 0, "generated fighter geometry");
                bool damaged = false;
                for (int frame = 0; frame < 120; frame++) {
                    var pad = new MeleeNative.Pad { aim = new MeleeNative.Vec3(0,0,1), buttons = frame < 10 ? 2u : 0u, allowed = uint.MaxValue };
                    Check(MeleeNative.ssbm_sim_step(sim, in host, in pad, 1.0/60) > 0, "fighter step");
                    damaged |= MeleeNative.ssbm_sim_hitboxes(sim, samples, (uint)samples.Length) > 0;
                    Check(MeleeNative.ssbm_view_update(view, sim, 1f/60f) == 1, "procedural view update");
                }
                Check(damaged, "fighter " + kind + " has working hit volumes");
                if(kind==MeleeNative.FighterLuigi){
                    Check(MeleeNative.ssbm_sim_scope(sim,1,0)==1,"Luigi can equip the scope without ItCo.dat");
                    var beam=MeleeNative.ssbm_scope_beam_new(NativeLib.CString(""),0,new MeleeNative.Vec3(0,0,1));
                    Check(beam!=IntPtr.Zero,"built-in scope beam");
                    try{MeleeNative.ssbm_projectile_step(beam,1.0/60,default,0);
                        Check(MeleeNative.ssbm_projectile_state(beam,out var beamState)==1 && beamState.position.z>0,"built-in scope beam travels");}
                    finally{MeleeNative.ssbm_projectile_free(beam);}
                }
            } finally { if(view != IntPtr.Zero) MeleeNative.ssbm_view_free(view); MeleeNative.ssbm_sim_free(sim); }
            var projectile = MeleeNative.ssbm_projectile_new(NativeLib.CString(""), kind, 0, 0, 0, new MeleeNative.Vec3(0,0,1));
            // Melee fighters with public article 0 must retain a simulated projectile.
            if (kind == 0 || kind == 1 || kind == 2 || kind == 3 || kind == 5 || kind == 6) {
                Check(projectile != IntPtr.Zero, "generic projectile " + kind);
                MeleeNative.ssbm_projectile_step(projectile, 1.0/60, default, 0);
                // Samus's article 0 is the bomb: placed and hopped, not flown (as on the disc), so it rises instead.
                Check(MeleeNative.ssbm_projectile_state(projectile, out var shot) == 1
                    && (kind == MeleeNative.FighterSamus ? shot.position.y > 0 : shot.position.z > 0), "projectile moves");
            }
            if (projectile != IntPtr.Zero) MeleeNative.ssbm_projectile_free(projectile);
        }
    }
    [DllImport("pikmin", EntryPoint="pik_sim_new", CallingConvention=CallingConvention.Cdecl)] static extern IntPtr PikNew(byte[] dir, ulong seed);
    [DllImport("pikmin", EntryPoint="pik_sim_free", CallingConvention=CallingConvention.Cdecl)] static extern void PikFree(IntPtr sim);
    [DllImport("pikmin", EntryPoint="pik_sim_ship_part_weight", CallingConvention=CallingConvention.Cdecl)] static extern uint PartWeight(IntPtr sim, uint id);
    static void Pikmin(string dll)
    {
        Check(NativeLib.Load(dll, out var error), error);
        var sim = PikNew(NativeLib.CString(""), 1);
        Check(sim != IntPtr.Zero, "Olimar constructor without assets");
        try { for(uint id=0; id<30; id++) Check(PartWeight(sim,id)>0, "generic ship part " + id); }
        finally { PikFree(sim); }
    }
}
