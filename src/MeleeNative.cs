using System;
using System.Runtime.InteropServices;
using Ror2Common;

namespace Ror2Common
{
    public static class MeleeNative
    {
        public const uint AbiVersion = 6;
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
        /// Mario's contextual-A lab plus the Melee clips ported onto Sunshine's rig.
        public const uint FighterMario = 8;
        const string Dll = "ssbm";
        public static bool HasOverlay {get;private set;}
        /// The view can draw a body's VR arms (ssbm_view_set_vr_arms).
        public static bool HasVrArms {get;private set;}
        public static bool HasLight {get;private set;}
        /// A hurt that carries the hit's size, so a big hit plays the fighter's hurt voice.
        public static bool HasHurtScaled {get;private set;}
        /// Recording a sim's calls and replaying them on a shadow sim (multiplayer).
        public static bool HasReplay {get;private set;}
        /// The native library can split a recorded log into its steps (ssbm_ops_steps), so a shadow replays one a frame.
        public static bool HasStepSplit {get;private set;}
        public static bool HasMaterialAlpha {get;private set;}
        /// Staged-special exports (lock-on steering, hit notification, shot reflection).
        /// Optional: only Pikachu's Quick Attack / Skull Bash / Thunder Shock use them.
        public static bool HasStagedSpecials {get;private set;}
        /// The sim's move list (ssbm_sim_moves), which the pause moveset is generated from. Optional.
        public static bool HasMoves {get;private set;}
        /// The Super Scope (Luigi's alternate neutral special): ssbm_sim_scope and the beam exports. Optional.
        public static bool HasScope {get;private set;}
        /// Link's animated held bow. Optional for older ABI-5 native libraries.
        public static bool HasLinkBow {get;private set;}
        /// A host terrain correction that moves a live fighter without resetting its action.
        public static bool HasNudge {get;private set;}
        /// Scales a jump's horizontal speed (Wax Quail). Optional.
        public static bool HasBoostJump {get;private set;}
        /// The H3AD-5T v2 stomp (jump in the air with no jumps left). Optional.
        public static bool HasStomp {get;private set;}
        /// RoR2 attack speed (the move-playback multiplier). Optional.
        public static bool HasAttackSpeed {get;private set;}
        /// Restoring a fighter's jumps without landing (Link's hookshot). Optional.
        public static bool HasJumpReset {get;private set;}
        /// Link's source chain-link and hook-head parts. Optional.
        public static bool HasHookshotParts {get;private set;}
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

        /// The scope's magazine and use, for a HUD (`ssbm_sim_scope_state`). Stage 0 not in use, 1 charging, 2 firing,
        /// 3 in a burst, 4 ending.
        [StructLayout(LayoutKind.Sequential)] public struct ScopeInfo
        {
            public uint ammo,capacity,held,level,stage;
        }

        public static void Load(string path)
        {
            if (!NativeLib.Load(path, out string error)) throw new InvalidOperationException(error);
            if (ssbm_abi_version() != AbiVersion) throw new InvalidOperationException("Melee native ABI mismatch");
            // Added exports preserve ABI-4 layout compatibility. Older matching
            // ABI-4 pairs can omit this presentation-only capability safely.
            try {ssbm_view_vr_arm_sides(IntPtr.Zero,null,0);HasVrArms=true;}
            catch(EntryPointNotFoundException){HasVrArms=false;}
            try {ssbm_sim_overlay(IntPtr.Zero,null);HasOverlay=true;}
            catch(EntryPointNotFoundException){HasOverlay=false;}
            try {ssbm_sim_light(IntPtr.Zero,null);HasLight=true;}
            catch(EntryPointNotFoundException){HasLight=false;}
            try {ssbm_sim_hurt_scaled(IntPtr.Zero,0f);HasHurtScaled=true;}
            catch(EntryPointNotFoundException){HasHurtScaled=false;}
            try {ssbm_sim_record(IntPtr.Zero,0);HasReplay=true;}
            catch(EntryPointNotFoundException){HasReplay=false;}
            try {ssbm_ops_steps(null,0,null,0);HasStepSplit=true;}
            catch(EntryPointNotFoundException){HasStepSplit=false;}
            try {ssbm_view_alpha_sources(IntPtr.Zero,out _);HasMaterialAlpha=true;}
            catch(EntryPointNotFoundException){HasMaterialAlpha=false;}
            try {ssbm_sim_lock_target(IntPtr.Zero,default,0);ssbm_sim_connect(IntPtr.Zero);ssbm_projectile_reflect(IntPtr.Zero,default);HasStagedSpecials=true;}
            catch(EntryPointNotFoundException){HasStagedSpecials=false;}
            try {ssbm_sim_moves(IntPtr.Zero,null,0);HasMoves=true;}
            catch(EntryPointNotFoundException){HasMoves=false;}
            try {ssbm_sim_scope(IntPtr.Zero,0,0f);HasScope=true;}
            catch(EntryPointNotFoundException){HasScope=false;}
            try {ssbm_sim_link_bow_state(IntPtr.Zero);ssbm_link_bow_update(IntPtr.Zero,IntPtr.Zero);HasLinkBow=true;}
            catch(EntryPointNotFoundException){HasLinkBow=false;}
            try {ssbm_sim_nudge(IntPtr.Zero,default,0);HasNudge=true;}
            catch(EntryPointNotFoundException){HasNudge=false;}
            try {ssbm_sim_boost_jump(IntPtr.Zero,0f);HasBoostJump=true;}
            catch(EntryPointNotFoundException){HasBoostJump=false;}
            try {ssbm_sim_set_stomp(IntPtr.Zero,0);HasStomp=true;}
            catch(EntryPointNotFoundException){HasStomp=false;}
            try {ssbm_sim_set_attack_speed(IntPtr.Zero,0f);HasAttackSpeed=true;}
            catch(EntryPointNotFoundException){HasAttackSpeed=false;}
            try {ssbm_sim_reset_jumps(IntPtr.Zero);HasJumpReset=true;}
            catch(EntryPointNotFoundException){HasJumpReset=false;}
            try {ssbm_hookshot_new(null,0);HasHookshotParts=true;}
            catch(EntryPointNotFoundException){HasHookshotParts=false;}
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
                ssbm_sim_break_roll(IntPtr.Zero);
                ssbm_sim_mobile_moves(IntPtr.Zero,0);
                ssbm_sim_play_sound(IntPtr.Zero,0);
                ssbm_sim_play_sound_from(IntPtr.Zero,0,0);
                ssbm_projectile_straight(IntPtr.Zero,0);
                ssbm_particles_tune(IntPtr.Zero,0,0,0,0);
                ssbm_sim_hold_knockdown(IntPtr.Zero);
            }catch(EntryPointNotFoundException e){throw new InvalidOperationException("Melee requires its matching ssbm.dll behavior exports.",e);}
        }
        /// The fighter's moves: `max` entries of 8 words each, the shared move id then 28 bytes of NUL-padded source action name.
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_moves(IntPtr sim,[Out]uint[] output,uint max);
        public static string Error => NativeLib.Utf8(ssbm_last_error());
        /// The oldest panic ssbm.dll caught at its boundary (see NativeLib.DrainPanics).
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_take_panic([Out]byte[] output,uint len);
        /// Logs what ssbm.dll caught since the last call; once a frame from one place.
        public static void DrainPanics(Action<string> logError) => NativeLib.DrainPanics("ssbm.dll", ssbm_take_panic, logError);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_overlay(IntPtr sim,[Out]float[] rgba);
        /// The additive light a colour program has on the fighter: eight floats (rgb, intensity, x and yz degrees, additive flag, spare).
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_light(IntPtr sim,[Out]float[] values);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_charge_visual(IntPtr sim,[Out]float[] matrix);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_sim_set_grabbed(IntPtr sim,uint held);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_grabbed(IntPtr sim);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern float ssbm_sim_motion_frame(IntPtr sim);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_grapple_points(IntPtr sim,[Out]Vec3[] points,uint max);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_grapple_new(byte[] dir,uint part);
        /// Link's hookshot chain part: 0/1 alternate links, 2 the hook head.
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_hookshot_new(byte[] dir,uint part);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_hit_feedback(IntPtr audio,uint element,uint kind,uint severity);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_projectile_new(byte[] dir,uint fighterKind,uint kind,uint state,float charge,Vec3 direction);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_projectile_step(IntPtr projectile,double dt,Vec3 target,uint hasTarget);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_projectile_lifetime(IntPtr projectile,float frames);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_projectile_straight(IntPtr projectile,float units);
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
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_particles_tune(IntPtr handle,uint id,float scale,float alpha,uint glow);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_particles_step(IntPtr handle,double dt);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_particles_draws(IntPtr handle,[Out]ViewParticle[] output,uint max);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_particles_texture(IntPtr handle,uint id,out ViewTexture info,[Out]byte[] rgba,uint len);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_abi_version();
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_view_set_vr_arms(IntPtr view,byte[] dir,uint kind,uint costume,uint on);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_view_vr_arm_sides(IntPtr view,byte[] output,uint max);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_announce_sound(uint kind);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_struct_sizes([Out]uint[] output,uint max);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern IntPtr ssbm_last_error();
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_assets_complete(byte[] dir,uint kind);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_portrait(byte[] dir,uint kind,uint costume,out ViewTexture info,[Out]byte[] rgba,uint len);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_extract(byte[] disc,byte[] dir,uint kind);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_bake_vr_arms(byte[] dir);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_sim_new(byte[] dir,Vec3 position);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_sim_new_fighter(byte[] dir,Vec3 position,uint kind);
        /// Mario's A clip posed on Sunshine's `ma_mdl1.bmd`: load once, sample per frame.
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_mario_pose_new(byte[] dir,byte[] smsBmd);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_mario_pose_sample(IntPtr pose,byte[] clip,float frame,[Out]float[] matrices,uint max);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_mario_pose_free(IntPtr pose);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_sim_free(IntPtr sim);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_step(IntPtr sim,in Host host,in Pad pad,double dt);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_state(IntPtr sim,out State state);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_hitboxes(IntPtr sim,[Out]HitboxSample[] output,uint max);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_audio(IntPtr sim,[Out]float[] stereo,uint frames);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_play_sound(IntPtr sim,uint sound);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_play_sound_from(IntPtr sim,uint fighterKind,uint sound);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_audio_active(IntPtr sim);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_tuning(IntPtr sim,Tuning tuning);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_mobile_moves(IntPtr sim,ulong mask);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_sim_place(IntPtr sim,Vec3 vector,uint kind);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_sim_nudge(IntPtr sim,Vec3 vector,uint grounded);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_sim_facing(IntPtr sim,Vec3 facing);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_sim_jump_height(IntPtr sim,float height);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_sim_anchor(IntPtr sim,Vec3 position,uint grounded);
        /// Gives the fighter its jumps back without landing; 1 when applied (Link's hookshot).
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_reset_jumps(IntPtr sim);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_defense_flags(IntPtr sim);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_shield_hit(IntPtr sim,float healthFraction);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern float ssbm_sim_shield_fraction(IntPtr sim);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern float ssbm_sim_shield_scale(IntPtr sim);
        /// Ends Yoshi's egg roll on the next tick (his host armor ran out); 1 if a roll was out.
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_break_roll(IntPtr sim);
        /// Bounces a held Samus Morph Ball off an enemy; 1 if a morph roll was out.
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_bonk_roll(IntPtr sim);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_facing_locked(IntPtr sim);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_sim_interrupt(IntPtr sim);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_sim_hurt(IntPtr sim);
        /// ssbm_sim_hurt for a hit that took `severity` (a fraction of the fighter's health); returns 0 for a null handle.
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_hurt_scaled(IntPtr sim,float severity);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_sim_hold_knockdown(IntPtr sim);
        /// Starts (1) or stops (0) recording the calls made on a sim; returns 0 for a null handle.
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_record(IntPtr sim,uint on);
        /// Moves the recorded calls into `bytes`; returns their length (the length needed when `bytes` is null or too small).
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_ops_take(IntPtr sim,[Out]byte[] bytes,uint capacity);
        /// Replays recorded calls on a shadow sim; returns how many ran, or uint.MaxValue when the bytes are not a log.
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_replay(IntPtr sim,in Host host,byte[] bytes,uint length);
        /// Where each step of a recorded log ends (byte offsets, up to `capacity`); returns how many steps, or uint.MaxValue when the bytes are not a log.
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_ops_steps(byte[] bytes,uint length,[Out]uint[] ends,uint capacity);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_sim_launch_pad(IntPtr sim,Vec3 velocity,Vec3 gravity);
        /// Scales the horizontal speed of the jump in progress by `factor` (Wax Quail); 1 does nothing.
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_sim_boost_jump(IntPtr sim,float factor);
        /// The stage's gravity as a multiple of the usual (the moon is low); pair it with the jump height for that gravity.
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_sim_set_gravity_scale(IntPtr sim,float scale);
        /// Enables (1) or disables (0) the H3AD-5T v2 stomp.
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_sim_set_stomp(IntPtr sim,uint enabled);
        /// Sets RoR2's attack-speed multiplier (1 is the source's rate).
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern void ssbm_sim_set_attack_speed(IntPtr sim,float speed);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_view_new(byte[] dir,uint kind,uint costume);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_view_new_choice(byte[] dir,uint kind,uint costume,uint choice);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_article_new(byte[] dir,uint fighterKind,uint articleKind,uint state);
        /// -1 absent, 0..2 ground draw/hold/release, 3..5 airborne.
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern int ssbm_sim_link_bow_state(IntPtr sim);
        /// Updates the selected bow view in fighter-local coordinates, including its authored hand attachment.
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_link_bow_update(IntPtr view,IntPtr sim);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_scope(IntPtr sim,uint on,float refillPerSecond);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_scope_state(IntPtr sim,out ScopeInfo info);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_scope_beams(byte[] dir,float[] rows);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_scope_beam_new(byte[] dir,uint level,Vec3 direction);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_item_new(byte[] dir,uint item,uint state);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_object_update(IntPtr view,float frame);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_article_cues(uint kind,uint state,uint previous,uint frame,uint fullCharge,[Out]uint[] output,uint max);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern int ssbm_effect_target(uint id);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_effect_flags(uint id);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern int ssbm_effect_companion(uint id);
        /// The sword trail's triangle strip: 7 floats a vertex (position, rgba). Null `output` returns the vertex count.
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_trail(IntPtr sim,[Out]float[] output,uint max);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_effect_new(byte[] dir,uint kind,uint id);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_entry_platform_new(byte[] dir);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr ssbm_game_new(byte[] dir);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_entry_platform_update(IntPtr view,IntPtr sim);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_begin_entry(IntPtr sim,byte[] dir);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_sim_begin_arrival(IntPtr sim,byte[] dir);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern float ssbm_object_duration(IntPtr view);
        /// A particle generator an effect model's animation starts (jobj.c track 0x28): at `frame`, on `joint`.
        [StructLayout(LayoutKind.Sequential)] public struct ObjectParticle { public float frame; public uint joint, bank, generator; }
        /// The view's animation-started particle generators in time order; null `output` returns the count. Added after the
        /// first ABI-5 libraries: a missing export reads as none.
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_object_particles(IntPtr view,[Out]ObjectParticle[] output,uint max);
        /// The view's posed joint matrix at its current frame (16 floats, column-major, unmirrored model axes).
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] public static extern uint ssbm_object_joint(IntPtr view,uint joint,[Out]float[] matrix);
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

