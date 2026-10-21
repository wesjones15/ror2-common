using System;
using System.Runtime.InteropServices;
using Ror2Common;

namespace Ror2Common
{
    public static class MeleeNative
    {
        public const uint AbiVersion = 5;
        /// Fighter kinds shared with the native `FighterKind`. A survivor's
        /// driver/view passes its own kind so the shared FFI resolves its assets.
        public const uint FighterSamus = 0;
        public const uint FighterFox = 1;
        public const uint FighterLuigi = 2;
        public const uint FighterPikachu = 3;
        public const uint FighterCaptain = 4;
        public const uint FighterLink = 5;
        public const uint FighterYoshi = 6;
        public const uint FighterDonkey = 7;
        const string Dll = "ssbm";
        public static bool HasOverlay {get;private set;}
        public static bool HasMaterialAlpha {get;private set;}
        /// Staged-special exports (lock-on steering, hit notification, shot reflection).
        /// Optional: only Pikachu's Quick Attack / Skull Bash / Thunder Shock use them.
        public static bool HasStagedSpecials {get;private set;}
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_begin_rebirth(IntPtr sim,byte[] dir,Vec3 destination);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_rebirth_flags(IntPtr sim);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_object_camera(IntPtr view,Vec3 camera);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern float ssbm_sim_charge_fraction(IntPtr sim);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_entry_control(IntPtr sim,uint kind);
        [StructLayout(LayoutKind.Sequential)] public struct Vec3
        {
            public float x, y, z;
            public Vec3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
        }
        [StructLayout(LayoutKind.Sequential)] public struct Pad { public Vec3 movement, aim; public uint buttons, allowed; }
        [StructLayout(LayoutKind.Sequential)] public struct State
        {
            public Vec3 position, velocity, facing;
            public uint grounded, phase, move_id, jumps_used, shot_charge, smash_charge;
            public ulong frame;
        }
        [StructLayout(LayoutKind.Sequential)] public struct Collision { public Vec3 position; public uint grounded, ceiling; }
        [StructLayout(LayoutKind.Sequential)] public struct Event { public uint kind, move_id; public float strength; public Vec3 position, direction; }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void CollideFn(IntPtr user, in Vec3 from, in Vec3 desired, out Collision collision);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void EventFn(IntPtr user, in Event ev);
        [StructLayout(LayoutKind.Sequential)] public struct Host { public IntPtr user, collide, evt; }
        [StructLayout(LayoutKind.Sequential)] public struct Tuning { public float speed_scale, jump_scale; public uint extra_jumps, hold_charge, smash_max_frames; }
        [StructLayout(LayoutKind.Sequential)] public struct HitboxSample
        {
            public ulong serial;
            public uint generation,group,id;
            public float damage,radius;
            public Vec3 local_position,origin,facing;
            public uint angle,growth,fixed_knockback,base_knockback,element,flags;
        }
        [StructLayout(LayoutKind.Sequential)] public struct ProjectileState
        {
            public Vec3 position,velocity;
            public float frame,animation_frame,damage,radius;
            public uint kind,state,flags,element;
        }

        public static void Load(string path)
        {
            if (!NativeLib.Load(path, out string error)) throw new InvalidOperationException(error);
            if (ssbm_abi_version() != AbiVersion) throw new InvalidOperationException("Melee native ABI mismatch");
            // Added exports preserve ABI-4 layout compatibility. Older matching
            // ABI-4 pairs can omit this presentation-only capability safely.
            try {ssbm_sim_overlay(IntPtr.Zero,null);HasOverlay=true;}
            catch(EntryPointNotFoundException){HasOverlay=false;}
            try {ssbm_view_alpha_sources(IntPtr.Zero,out _);HasMaterialAlpha=true;}
            catch(EntryPointNotFoundException){HasMaterialAlpha=false;}
            try {ssbm_sim_lock_target(IntPtr.Zero,default,0);ssbm_sim_connect(IntPtr.Zero);ssbm_projectile_reflect(IntPtr.Zero,default);HasStagedSpecials=true;}
            catch(EntryPointNotFoundException){HasStagedSpecials=false;}
            var sizes = new uint[14]; uint count=ssbm_struct_sizes(sizes,(uint)sizes.Length);
            error=NativeLib.CompareSizes(sizes,count,new[] {
                ("Vec3",NativeLib.Size<Vec3>()),("Pad",NativeLib.Size<Pad>()),("State",NativeLib.Size<State>()),
                ("Collision",NativeLib.Size<Collision>()),("Event",NativeLib.Size<Event>()),("Host",NativeLib.Size<Host>()),
                ("Tuning",NativeLib.Size<Tuning>()),("ViewModelInfo",NativeLib.Size<ViewModelInfo>()),
                ("ViewBatch",NativeLib.Size<ViewBatch>()),("ViewTexture",NativeLib.Size<ViewTexture>()),("ViewDraw",NativeLib.Size<ViewDraw>()),
                ("HitboxSample",NativeLib.Size<HitboxSample>()),("ViewParticle",NativeLib.Size<ViewParticle>()),("ProjectileState",NativeLib.Size<ProjectileState>()) });
            if (error!=null) throw new InvalidOperationException(error);
            // These behavior exports are required by the five-skill host. Refuse
            // an older ABI-4 DLL before starting a character with partial controls.
            try {
                ssbm_sim_jump_height(IntPtr.Zero,0);ssbm_sim_anchor(IntPtr.Zero,default,0);
                ssbm_sim_defense_flags(IntPtr.Zero);ssbm_sim_shield_hit(IntPtr.Zero,0);
                ssbm_sim_shield_fraction(IntPtr.Zero);ssbm_sim_shield_scale(IntPtr.Zero);
                ssbm_sim_mobile_moves(IntPtr.Zero,0);
                ssbm_sim_play_sound(IntPtr.Zero,0);
                ssbm_sim_hold_knockdown(IntPtr.Zero);
            }catch(EntryPointNotFoundException e){throw new InvalidOperationException("Melee requires its matching ssbm.dll behavior exports.",e);}
        }
        public static string Error => NativeLib.Utf8(ssbm_last_error());
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_overlay(IntPtr sim,[Out]float[] rgba);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_charge_visual(IntPtr sim,[Out]float[] matrix);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_sim_set_grabbed(IntPtr sim,uint held);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_grabbed(IntPtr sim);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern float ssbm_sim_motion_frame(IntPtr sim);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_grapple_points(IntPtr sim,[Out]Vec3[] points,uint max);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_grapple_new(byte[] dir,uint part);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_hit_feedback(IntPtr audio,uint element,uint kind,uint severity);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_projectile_new(byte[] dir,uint fighterKind,uint kind,uint state,float charge,Vec3 direction);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_projectile_step(IntPtr projectile,double dt,Vec3 target,uint hasTarget);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_projectile_lifetime(IntPtr projectile,float frames);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_projectile_state(IntPtr projectile,out ProjectileState state);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_projectile_place(IntPtr projectile,Vec3 relative,uint grounded);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_projectile_bounce(IntPtr projectile);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_projectile_reflect(IntPtr projectile,Vec3 normal);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_sim_lock_target(IntPtr sim,Vec3 target,uint valid);
        /// World facts a fighter's staged specials read (Link's boomerang out, a carried bomb); set every tick.
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_sim_set_host_flags(IntPtr sim,uint flags);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_sim_connect(IntPtr sim);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_projectile_impact(IntPtr projectile,IntPtr audio);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_projectile_free(IntPtr projectile);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_particles_new(byte[] dir);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_particles_free(IntPtr handle);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_particles_spawn(IntPtr handle,uint id,float[] transform,ulong key);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_particles_attach(IntPtr handle,ulong key,float[] transform);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_particles_stop(IntPtr handle,ulong key);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_particles_step(IntPtr handle,double dt);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_particles_draws(IntPtr handle,[Out]ViewParticle[] output,uint max);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_particles_texture(IntPtr handle,uint id,out ViewTexture info,[Out]byte[] rgba,uint len);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_abi_version();
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_announce_sound(uint kind);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_struct_sizes([Out]uint[] output,uint max);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern IntPtr ssbm_last_error();
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_assets_complete(byte[] dir,uint kind);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_portrait(byte[] dir,uint kind,uint costume,out ViewTexture info,[Out]byte[] rgba,uint len);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_extract(byte[] disc,byte[] dir,uint kind);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_sim_new(byte[] dir,Vec3 position);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_sim_new_fighter(byte[] dir,Vec3 position,uint kind);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_sim_free(IntPtr sim);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_step(IntPtr sim,in Host host,in Pad pad,double dt);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_state(IntPtr sim,out State state);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_hitboxes(IntPtr sim,[Out]HitboxSample[] output,uint max);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_audio(IntPtr sim,[Out]float[] stereo,uint frames);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_play_sound(IntPtr sim,uint sound);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_audio_active(IntPtr sim);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_tuning(IntPtr sim,Tuning tuning);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_mobile_moves(IntPtr sim,ulong mask);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_sim_place(IntPtr sim,Vec3 vector,uint kind);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_sim_facing(IntPtr sim,Vec3 facing);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_sim_jump_height(IntPtr sim,float height);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_sim_anchor(IntPtr sim,Vec3 position,uint grounded);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_defense_flags(IntPtr sim);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_shield_hit(IntPtr sim,float healthFraction);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern float ssbm_sim_shield_fraction(IntPtr sim);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern float ssbm_sim_shield_scale(IntPtr sim);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_facing_locked(IntPtr sim);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_sim_interrupt(IntPtr sim);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_sim_hurt(IntPtr sim);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_sim_hold_knockdown(IntPtr sim);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_sim_launch_pad(IntPtr sim,Vec3 velocity,Vec3 gravity);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_view_new(byte[] dir,uint kind,uint costume);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_article_new(byte[] dir,uint fighterKind,uint articleKind,uint state);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_object_update(IntPtr view,float frame);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_article_cues(uint kind,uint state,uint previous,uint frame,uint fullCharge,[Out]uint[] output,uint max);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern int ssbm_effect_target(uint id);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_effect_flags(uint id);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern int ssbm_effect_companion(uint id);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_effect_new(byte[] dir,uint kind,uint id);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_entry_platform_new(byte[] dir);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_game_new(byte[] dir);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_entry_platform_update(IntPtr view,IntPtr sim);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_begin_entry(IntPtr sim,byte[] dir);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_begin_arrival(IntPtr sim,byte[] dir);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern float ssbm_object_duration(IntPtr view);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_gfx(IntPtr sim,[Out]Event[] output,uint max);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_bone(IntPtr sim,uint bone,[Out]float[] matrix);
        /// Like ssbm_sim_bone, but `part` is a common FtPart index (HipN 4, RThumbNb 49...).
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_common_bone(IntPtr sim,uint part,[Out]float[] matrix);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern ulong ssbm_sim_motion_serial(IntPtr sim);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_view_free(IntPtr view);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_view_pose(IntPtr view,byte[] clip,float frame);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_view_update(IntPtr view,IntPtr sim,float dt);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_view_model(IntPtr view,out ViewModelInfo info);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_view_batches(IntPtr view,[Out]ViewBatch[] output,uint max);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_view_indices(IntPtr view,[Out]int[] output,uint max);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_view_uvs(IntPtr view,[Out]float[] output,uint max);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_view_frame_uvs(IntPtr view,out uint count);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_view_texture_version(IntPtr view,uint id);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_view_positions(IntPtr view,out uint count);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_view_normals(IntPtr view,out uint count);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_view_colors(IntPtr view,out uint count);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_view_alpha_sources(IntPtr view,out uint count);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_view_texture_ids(IntPtr view,out uint count);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_view_texture(IntPtr view,uint id,out ViewTexture info,[Out]byte[] rgba,uint len);
    }
}

