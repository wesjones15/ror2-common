using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using EntityStates;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace Ror2Common
{
    // Generic native-sim driver for Melee survivors. Owns the fixed-step sim,
    // collision, facing, hitbox sampling, arrival/recovery/defense wiring and
    // falloff/unstuck rescue. A per-fighter subclass supplies presentation and
    // per-character tuning through the abstract members below, so a new survivor
    // does not duplicate the simulation glue.
    [DefaultExecutionOrder(-200)]
    public abstract class MeleeDriver : MonoBehaviour, IMeleeArrival, IMeleeRecovery, IMeleeDefense
    {
        static readonly MeleeNative.CollideFn collideFn = Collide;
        static readonly MeleeNative.EventFn eventFn = Event;

        protected CharacterBody body; protected CharacterMotor motor; protected InputBankTest input; protected NativeBody native; protected CharacterDirection direction;
        protected IntPtr sim; protected MeleeNative.Host host; protected GCHandle self; protected MeleeNative.State state;
        protected MeleeRecovery recovery; protected uint recoveryFlags;
        protected Vector3 feetOffset, safe; protected StepSmoother smoother; protected CameraPivot cameraPivot;
        protected readonly GroundGate ground = new GroundGate();
        protected readonly Unstuck unstuck = new Unstuck();
        protected readonly WallGuard wallGuard = new WallGuard();
        protected static readonly float[] sweepHeights = { 0.1f, 0.6f, 1.2f, 1.7f };
        protected readonly List<Vector3> walls = new List<Vector3>();
        protected bool active, failed, permitDamage, deathStarted;
        protected float scale, lastHealth;
        protected MeleeNative.HitboxSample[] hitboxes = new MeleeNative.HitboxSample[64];
        protected readonly HashSet<string> presentationErrors = new HashSet<string>();
        protected readonly MeleePad pad = new MeleePad();

        public bool MeleeInvulnerable => active && !Recovering && (MeleeNative.ssbm_sim_defense_flags(sim) & (2 | 64 | 128)) != 0;
        public bool BlockMeleeDamage(float amount) { if (!active || Recovering) return false; uint flags = MeleeNative.ssbm_sim_defense_flags(sim); return (flags & 1) != 0 && MeleeNative.ssbm_sim_shield_hit(sim, amount) != 0; }
        public bool Recovering => (recoveryFlags & 1) != 0;
        public bool RecoveryInvulnerable => (recoveryFlags & 2) != 0;
        public bool Arriving { get; set; }

        protected bool FacingLocked => Arriving || Recovering || MeleeNative.ssbm_sim_facing_locked(sim) != 0;

        // --- Per-fighter seam -------------------------------------------------
        protected abstract string FighterName { get; }
        protected abstract string AssetsDir { get; }
        protected abstract float MetresPerUnit { get; }
        protected abstract float JumpScale { get; }
        protected abstract bool HoldCharge { get; }
        protected abstract bool AssetsReady { get; }
        protected abstract bool AssetsBusy { get; }
        protected abstract uint AllowedButtons { get; }
        protected abstract bool ShieldSelected { get; }
        protected abstract MeleeControlLayout Layout { get; }
        protected abstract bool GrabAttached { get; }
        protected abstract bool GrabHolding { get; }
        protected abstract bool IsRangedMove(uint moveId);
        protected abstract bool IsChargeMove(uint moveId);
        protected abstract void UpdateAudio();
        protected abstract void CreatePresentation();
        protected abstract void DisposePresentation();
        protected abstract void ResetPresentation();
        protected abstract void ResetEntryPlatform();
        protected abstract void StepPresentation(float dt);
        protected abstract void DrawPresentation(Vector3 at, Quaternion rotation);
        protected abstract void DrawHud();
        protected abstract void BeginDeathPresentation(Vector3 feet, Quaternion rotation);
        protected abstract void GrabDetach();
        protected abstract void GrabReleaseCapture();
        protected abstract void GrabThrow(MeleeNative.Event e);
        protected abstract void ApplyHitboxes(MeleeNative.HitboxSample[] samples, int count, float scale);
        protected abstract void OnEventStarted(MeleeNative.Event e);
        protected abstract void OnEventHit(MeleeNative.Event e);
        protected abstract void FallbackSkills();
        protected virtual void OnSimStarted() { }

        // --- Generic core -----------------------------------------------------
        protected void FaceTarget()
        {
            if (IsRangedMove(state.move_id)) MeleeAim.BeginAttack(body, direction, MeleeAim.CameraRay(body, input).direction);
            MeleeAim.FacingTarget(body, direction, input ? input.moveVector : Vector3.zero, MeleeAim.CameraRay(body, input).direction, FacingLocked, new Vector3(state.facing.x, 0, state.facing.z));
        }
        protected void Awake() { body = GetComponent<CharacterBody>(); motor = GetComponent<CharacterMotor>(); input = GetComponent<InputBankTest>(); native = GetComponent<NativeBody>(); direction = GetComponent<CharacterDirection>(); }
        protected void Update()
        {
            if (failed || !body) return;
            pad.Read(body, input, GrabHolding, MeleeAim.CameraRay(body, input).direction, ShieldSelected, Layout);
            UpdateAudio();
            if (body.healthComponent && !body.healthComponent.alive) { BeginDeath(); return; }
            if (active) { smoother.Smooth(); cameraPivot.Use(true); FaceTarget(); }
        }
        protected void FixedUpdate()
        {
            if (failed || !body || !body.healthComponent || !body.healthComponent.alive) return;
            if (!body.hasEffectiveAuthority) return;
            if (body.currentVehicle) { if (active) Cleanup(); return; }
            if (!NetworkServer.active) { Fallback("Remote-player native simulation is not supported by this offline prototype."); return; }
            if (!active) {
                if (AssetsBusy) return;
                if (!AssetsReady) { Fallback("Assets missing. Set the fighter's disc config."); return; }
                if (!ground.Ready(body.footPosition, Time.fixedDeltaTime, FighterName)) return;
                try { StartSim(); } catch (Exception e) { Fallback(e.ToString()); return; }
            }
            try {
                if (smoother.MovedElsewhere) { var moved = transform.position - feetOffset; MeleeNative.ssbm_sim_place(sim, ToSim(moved), 0); smoother.Reset(); native.ForgetVelocity(); unstuck.Reset(); wallGuard.Placed(moved); }
                bool hitImpulse = false;
                if (native.TakeExternalVelocity(out var external) && !Recovering) {
                    hitImpulse = !native.JumpPadLaunch && external.sqrMagnitude > 0.01f;
                    var velocity = new MeleeNative.Vec3(external.x / (scale * 60), external.y / (scale * 60), external.z / (scale * 60));
                    if (native.JumpPadLaunch) { var g = Physics.gravity / (scale * 3600); MeleeNative.ssbm_sim_launch_pad(sim, velocity, new MeleeNative.Vec3(g.x, g.y, g.z)); }
                    else MeleeNative.ssbm_sim_place(sim, velocity, 1);
                }
                float health = body.healthComponent.combinedHealth;
                if (health < lastHealth - 0.001f && (hitImpulse || native.Control() == NativeBody.Hold.All)) MeleeNative.ssbm_sim_hurt(sim);
                lastHealth = health;
                var control = native.Control();
                var ps = pad.Take(AllowedButtons, control == NativeBody.Hold.All);
                var p = new MeleeNative.Pad { movement = new MeleeNative.Vec3(ps.movement.x, ps.movement.y, ps.movement.z), aim = new MeleeNative.Vec3(ps.aim.x, ps.aim.y, ps.aim.z), buttons = ps.buttons, allowed = ps.allowed };
                if (control == NativeBody.Hold.All) MeleeNative.ssbm_sim_hold_knockdown(sim);
                if (GrabAttached && ((p.buttons & 1) != 0 || control != NativeBody.Hold.None)) { GrabDetach(); MeleeNative.ssbm_sim_interrupt(sim); }
                if (GrabHolding && ((p.buttons & 1) != 0 || control != NativeBody.Hold.None)) GrabReleaseCapture();
                if (control == NativeBody.Hold.All) { p.buttons = 0; p.movement = default; p.allowed = 0; MeleeNative.ssbm_sim_interrupt(sim); }
                else if (control == NativeBody.Hold.Move) p.movement = default;
                bool sprint = native.UpdateSprint((p.buttons & 32) != 0, new Vector3(p.movement.x, 0, p.movement.z).sqrMagnitude > 0.01f, control == NativeBody.Hold.None);
                if (sprint) p.buttons |= 32;
                if (!IsChargeMove(state.move_id) && (p.buttons & 256) != 0 && sprint) { native.UpdateSprint(false, false); p.buttons &= ~32u; }
                uint extra = (uint)Math.Max(0, body.maxJumpCount - 2);
                float speedScale = body.moveSpeed / Mathf.Max(0.1f, body.baseMoveSpeed);
                if (body.isSprinting) speedScale /= Mathf.Max(0.1f, body.sprintingSpeedMultiplier);
                MeleeNative.ssbm_sim_tuning(sim, new MeleeNative.Tuning { speed_scale = speedScale, jump_scale = JumpScale * NativeBody.JumpScale(body), extra_jumps = extra, hold_charge = HoldCharge ? 1u : 0u, smash_max_frames = 60 });
                MeleeNative.ssbm_sim_jump_height(sim, MeleeControls.JumpHeight(body.jumpPower, Physics.gravity.y, motor.jumpPowerPenaltyCoefficient) / scale);
                FaceTarget();
                if (direction) { var facing = direction.forward; MeleeNative.ssbm_sim_facing(sim, new MeleeNative.Vec3(facing.x, 0, facing.z)); }
                MeleeNative.ssbm_sim_step(sim, in host, in p, Time.fixedDeltaTime);
                uint previousRecovery = recoveryFlags; recoveryFlags = MeleeNative.ssbm_sim_rebirth_flags(sim);
                recovery?.Refresh();
                if (Arriving && !Recovering) Arriving = false;
                StepPresentation(Time.fixedDeltaTime);
                uint count = MeleeNative.ssbm_sim_hitboxes(sim, null, 0);
                if (count > 1024) throw new InvalidOperationException("Native hitbox sample budget exceeded");
                if (count > hitboxes.Length) hitboxes = new MeleeNative.HitboxSample[count];
                if (count > 0) {
                    if (MeleeNative.ssbm_sim_hitboxes(sim, hitboxes, (uint)hitboxes.Length) != count) throw new InvalidOperationException(MeleeNative.Error);
                    if (permitDamage) ApplyHitboxes(hitboxes, (int)count, scale);
                }
                if (MeleeNative.ssbm_sim_state(sim, out state) != 1) { Fallback(MeleeNative.Error); return; }
                var feet = ToWorld(state.position);
                if ((previousRecovery & 4) != 0 && (recoveryFlags & 4) == 0) {
                    transform.position = feet + feetOffset; smoother.Reset(); native.ForgetVelocity(); wallGuard.Placed(feet);
                    unstuck.Step(feet, false, true, false, 0.25f, 1.8f, RecoveryGround, out _);
                    ResetEntryPlatform();
                }
                bool supported = RecoveryGround(feet, 0.45f, 0.45f, out var support) && Mathf.Abs(support.y - feet.y) < 0.15f;
                bool embedded = WorldProbe.InsideSolid(feet + Vector3.up * 0.9f);
                bool blocked = (new Vector3(p.movement.x, 0, p.movement.z).sqrMagnitude > 0.05f && !supported) || embedded;
                bool fallen = state.grounded == 0 && state.velocity.y <= 0;
                if (!Recovering && recovery != null && recovery.CheckFall(unstuck, feet, 1.8f, RecoveryGround)) {
                    CommonLog.Info(FighterName + " falloff: common threshold, Melee KO and figurine recovery");
                } else if (!Recovering && unstuck.Step(feet, fallen || blocked, state.grounded != 0 && supported, control != NativeBody.Hold.None || GrabAttached, Time.fixedDeltaTime, 1.8f, RecoveryGround, out var recovered)) {
                    Recover(recovered); feet = ToWorld(state.position); CommonLog.Info(FighterName + " recovered using common Unstuck");
                } else if (!Recovering && wallGuard.Step(feet, 1.8f, sweepHeights, 0.6f, control != NativeBody.Hold.None || GrabAttached, false, out var back, out var why)) {
                    Recover(back); feet = ToWorld(state.position); CommonLog.Info(FighterName + " wall recovery: " + why);
                }
                smoother.Stepped(feet + feetOffset); native.WroteVelocity(ToWorld(state.velocity) * 60f);
                motor.Motor.GroundingStatus.IsStableOnGround = state.grounded != 0;
                motor.Motor.GroundingStatus.FoundAnyGround = state.grounded != 0;
                FaceTarget();
            } catch (Exception e) { Fallback(e.ToString()); }
        }
        void StartSim()
        {
            scale = MetresPerUnit; feetOffset = transform.position - body.footPosition; safe = body.footPosition;
            recovery = GetComponent<MeleeRecovery>();
            lastHealth = body.healthComponent.combinedHealth;
            if (RecoveryGround(safe, 0.6f, 0.8f, out var standing)) { safe = standing; transform.position = standing + feetOffset; }
            sim = MeleeNative.ssbm_sim_new(NativeLib.CString(AssetsDir), ToSim(safe)); if (sim == IntPtr.Zero) throw new InvalidOperationException(MeleeNative.Error);
            CreatePresentation();
            self = GCHandle.Alloc(this);
            if (MeleeNative.ssbm_sim_begin_arrival(sim, NativeLib.CString(AssetsDir)) != 1) throw new InvalidOperationException(MeleeNative.Error);
            recoveryFlags = MeleeNative.ssbm_sim_rebirth_flags(sim); recovery?.Refresh();
            host = new MeleeNative.Host { user = GCHandle.ToIntPtr(self), collide = Marshal.GetFunctionPointerForDelegate(collideFn), evt = Marshal.GetFunctionPointerForDelegate(eventFn) };
            WorldProbe.UseBodyCollision(motor, gameObject); motor.Motor.enabled = false;
            smoother = new StepSmoother(transform); cameraPivot = new CameraPivot(body); cameraPivot.Use(true); active = true;
            unstuck.Reset(); unstuck.Step(safe, false, true, false, 0.25f, 1.8f, RecoveryGround, out _); wallGuard.Placed(safe);
            CommonLog.Info(FighterName + " sim started with costume " + body.skinIndex);
            OnSimStarted();
        }
        protected void LateUpdate()
        {
            if (!active || failed) return;
            try {
                GameSettings.HideRenderers(body.modelLocator.modelTransform, true);
                var animator = body.modelLocator.modelTransform.GetComponent<Animator>(); if (animator) animator.enabled = false;
                var at = transform.position - feetOffset;
                var facing = new Vector3(state.facing.x, 0, state.facing.z);
                var rotation = facing.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(facing) : Quaternion.identity;
                DrawPresentation(at, rotation);
            } catch (Exception e) { Fallback(e.ToString()); }
        }
        protected void OnGUI()
        {
            if (!active || failed || !body || !body.isPlayerControlled || !body.hasEffectiveAuthority || !body.healthComponent.alive) return;
            DrawHud();
        }
        protected MeleeNative.Vec3 ToSim(Vector3 v) => new MeleeNative.Vec3(v.x / scale, v.y / scale, v.z / scale);
        protected Vector3 ToWorld(MeleeNative.Vec3 v) => new Vector3(v.x, v.y, v.z) * scale;
        protected void Present(Action draw)
        {
            try { draw(); } catch (Exception e) { if (presentationErrors.Add(e.Message)) CommonLog.Error(FighterName + " presentation failed; character simulation remains active: " + e); }
        }
        protected bool RecoveryGround(Vector3 sample, float rise, float drop, out Vector3 feet) => WorldProbe.StandingGround(sample, rise, drop, 1.8f, 0.3f, out feet);
        public void BeginFallRecovery(Vector3 safeFeet)
        {
            if (!active || Recovering) return;
            ResetPresentation();
            permitDamage = false;
            if (MeleeNative.ssbm_sim_begin_rebirth(sim, NativeLib.CString(AssetsDir), ToSim(safeFeet)) != 1) throw new InvalidOperationException(MeleeNative.Error);
            recoveryFlags = MeleeNative.ssbm_sim_rebirth_flags(sim);
        }
        protected void Recover(Vector3 feet)
        {
            MeleeNative.ssbm_sim_place(sim, ToSim(feet), 2); MeleeNative.ssbm_sim_state(sim, out state);
            smoother.Reset(); native.ForgetVelocity(); unstuck.Reset(); wallGuard.Placed(feet);
        }
        static void Collide(IntPtr user, in MeleeNative.Vec3 from, in MeleeNative.Vec3 desired, out MeleeNative.Collision result)
        {
            result = new MeleeNative.Collision { position = from };
            try { var driver = (MeleeDriver)GCHandle.FromIntPtr(user).Target; result = driver.Collision(from, desired); }
            catch (Exception e) { CommonLog.Error("Melee collision callback: " + e.Message); }
        }
        protected MeleeNative.Collision Collision(MeleeNative.Vec3 from, MeleeNative.Vec3 desired)
        {
            Vector3 start = ToWorld(from), end = ToWorld(desired); var delta = end - start; bool roof = false, land = false;
            float radius = 0.3f, height = 1.8f;
            if (delta.sqrMagnitude > 0.000001f && Physics.CapsuleCast(start + Vector3.up * radius, start + Vector3.up * (height - radius), radius, delta.normalized, out var hit, delta.magnitude, WorldProbe.Mask, QueryTriggerInteraction.Ignore)) {
                end = start + delta.normalized * Mathf.Max(0, hit.distance - 0.01f);
                if (hit.normal.y > 0.6f && delta.y <= 0) land = true; if (hit.normal.y < -0.6f) roof = true;
                var remain = Vector3.ProjectOnPlane(delta - (end - start), hit.normal);
                end += remain;
            }
            end = WorldProbe.PushOutOfWalls(end + Vector3.up * 0.6f, radius, 0.6f, walls, 8) - Vector3.up * 0.6f;
            float above = Mathf.Max(start.y, end.y) + 0.35f;
            if (delta.y <= 0 && WorldProbe.Ground(new Vector3(end.x, above, end.z), above - end.y + 0.15f, out var floor) && floor.normal.y > 0.6f && floor.point.y >= end.y - 0.12f) {
                end.y = floor.point.y; land = true;
            }
            if (delta.y > 0 && WorldProbe.Roof(start + Vector3.up * height, delta.y + 0.05f, out var ceiling)) { end.y = Mathf.Min(end.y, ceiling.point.y - height - 0.01f); roof = true; }
            return new MeleeNative.Collision { position = ToSim(end), grounded = land ? 1u : 0u, ceiling = roof ? 1u : 0u };
        }
        static void Event(IntPtr user, in MeleeNative.Event e)
        {
            try { ((MeleeDriver)GCHandle.FromIntPtr(user).Target).Receive(e); } catch (Exception error) { CommonLog.Error("Melee event callback: " + error.Message); }
        }
        void Receive(MeleeNative.Event e)
        {
            if (e.kind == 1) { OnEventStarted(e); NativeBody.MarkCombat(body); MeleeAim.BeginAttack(body, direction, MeleeAim.CameraRay(body, input).direction); }
            else if (e.kind == 2) { if (permitDamage) OnEventHit(e); }
            else if (e.kind == 6) GrabThrow(e);
            else if (e.kind == 3) native.Jumped();
            else if (e.kind == 4) native.Landed(Vector3.down * e.strength * scale * 60f);
        }
        void Fallback(string reason)
        {
            if (failed) return; failed = true; CommonLog.Error(FighterName + " falls back to Commando: " + reason); Cleanup();
            FallbackSkills();
            var machine = EntityStateMachine.FindByCustomName(gameObject, "Body"); if (machine) { machine.mainStateType = new SerializableEntityStateType(typeof(GenericCharacterMain)); machine.SetNextStateToMain(); }
        }
        void BeginDeath()
        {
            if (deathStarted || !active) return; deathStarted = true;
            try {
                var feet = transform.position - feetOffset;
                var facing = new Vector3(state.facing.x, 0, state.facing.z);
                var rotation = facing.sqrMagnitude > 0.001f ? Quaternion.LookRotation(facing) : transform.rotation;
                BeginDeathPresentation(feet, rotation);
            } catch (Exception e) { CommonLog.Error(FighterName + " death presentation failed: " + e); }
            Cleanup();
        }
        void Cleanup()
        {
            pad.ReleaseInteraction();
            DisposePresentation();
            Arriving = false; recoveryFlags = 0; recovery?.Refresh();
            active = false; cameraPivot?.Use(false);
            if (sim != IntPtr.Zero) { MeleeNative.ssbm_sim_free(sim); sim = IntPtr.Zero; } if (self.IsAllocated) self.Free();
            if (motor && motor.Motor) motor.Motor.enabled = true;
            if (body && body.modelLocator && body.modelLocator.modelTransform) { if (!deathStarted) GameSettings.HideRenderers(body.modelLocator.modelTransform, false); var a = body.modelLocator.modelTransform.GetComponent<Animator>(); if (a) a.enabled = !deathStarted; }
        }
        protected void OnDestroy() { if (active && body && body.healthComponent && !body.healthComponent.alive) BeginDeath(); Cleanup(); }
    }
}
