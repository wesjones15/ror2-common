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
        uint startedMove; ulong lastBatchSerial = ulong.MaxValue;
        EnemyCapture capture;
        MeleeNative.Event? pendingThrow;
        int lastHitboxCount;
        readonly HashSet<(ulong, uint)> pummeled = new HashSet<(ulong, uint)>();
        readonly Collider[] grabCandidates = new Collider[32];
        protected float scale, lastHealth;
        protected MeleeNative.HitboxSample[] hitboxes = new MeleeNative.HitboxSample[64];
        protected readonly HashSet<string> presentationErrors = new HashSet<string>();
        protected readonly MeleePad pad = new MeleePad();
        // --- Multiplayer (MeleeNet) -------------------------------------------------
        // The machine that owns the body runs the real sim; every other machine runs a view-only
        // shadow of it (`puppet`) on the owner's recorded calls. A shadow draws and sounds the
        // fighter but makes no attacks, reads no pad and moves no body.
        protected bool puppet;
        MeleeNet.Stream stream; int puppetWait, recordedSteps; bool recording, puppetBroken;
        readonly byte[] opsBuffer = new byte[1 << 15];
        /// This machine owns the body, so it runs the real sim and makes the fighter's attacks.
        public bool IsOwner => body && body.hasEffectiveAuthority;

        public bool MeleeInvulnerable => active && !Recovering && (MeleeNative.ssbm_sim_defense_flags(sim) & (2 | 64 | 128)) != 0;
        public virtual bool BlockMeleeDamage(float amount) { if (!active || Recovering) return false; uint flags = MeleeNative.ssbm_sim_defense_flags(sim); return (flags & 1) != 0 && MeleeNative.ssbm_sim_shield_hit(sim, amount) != 0; }
        public bool Recovering => (recoveryFlags & 1) != 0;
        public bool RecoveryInvulnerable => (recoveryFlags & 2) != 0;
        public bool Arriving { get; set; }

        protected bool FacingLocked => Arriving || Recovering || MeleeNative.ssbm_sim_facing_locked(sim) != 0;

        // --- Per-fighter seam -------------------------------------------------
        protected abstract string FighterName { get; }
        protected abstract string AssetsDir { get; }
        /// The fighter's native `FighterKind`, used to construct its sim.
        protected abstract uint FighterKind { get; }
        MeleeMoveInfo[] moveList;
        /// The moves this fighter's native sim has (null before the sim exists or without the move-list export);
        /// read once, for the pause sheet's moveset.
        public MeleeMoveInfo[] MoveList => moveList ?? (sim == IntPtr.Zero ? null : moveList = MeleeMoveset.Read(sim));
        protected abstract float MetresPerUnit { get; }
        protected abstract float JumpScale { get; }
        /// RoR2 calibration multiplier on walk/run speed; the Samus survivors
        /// override this with their `SpeedScale` config so the slowest fighter
        /// stays competitive with Commando.
        protected virtual float SpeedScale => 1.37f;
        protected abstract bool HoldCharge { get; }
        protected abstract bool AssetsReady { get; }
        protected abstract bool AssetsBusy { get; }
        protected abstract uint AllowedButtons { get; }
        /// Last chance to drop a button before the sim sees it. A fighter whose own state
        /// owns a slot (Yoshi's full belly masks Egg Lay until he lays the egg) drops it here.
        protected virtual uint FilterButtons(uint buttons) => buttons;
        protected abstract bool ShieldSelected { get; }
        protected abstract MeleeControlLayout Layout { get; }
        // --- Grab: every fighter captures enemies with its disc grab hitboxes ---
        // (Samus overrides all of this with her grapple.) A grab box that touches an
        // enemy holds it in front of the fighter (EnemyCapture); the sim then runs
        // the authored pummel and throws, and the throw's damage and launch land here.
        protected virtual bool GenericGrab => true;
        protected virtual bool GrabAttached => false;
        protected virtual bool GrabHolding => GenericGrab && capture != null && capture.Target;
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
        protected virtual void GrabDetach() { }
        protected virtual void GrabReleaseCapture()
        {
            if (!GenericGrab) return;
            capture?.Dispose();
            if (sim != IntPtr.Zero) MeleeNative.ssbm_sim_set_grabbed(sim, 0);
        }
        protected virtual void GrabThrow(MeleeNative.Event e)
        {
            if (GenericGrab && capture != null && capture.Target) pendingThrow = e;
        }
        protected abstract void ApplyHitboxes(MeleeNative.HitboxSample[] samples, int count, float scale);
        /// The fighter's skill layout (FoxSkills.Built...): the shared `BeginMove` finds the slot a started move
        /// belongs to through it, spends its charge and decides `permitDamage`.
        protected abstract MeleeSkillSet SkillSet { get; }
        /// A move started, after the shared `BeginMove` has set `permitDamage`. Most fighters need nothing here.
        protected virtual void OnEventStarted(MeleeNative.Event e) { }
        /// Whether starting `moveId` spends its skill's charge. A fighter whose move only
        /// pays off when it lands returns false here and spends from its own success
        /// (`NativeSkills.SpendDeferred`): Yoshi's Egg Lay spends when the tongue swallows,
        /// so a miss costs no cooldown.
        protected virtual bool SpendsOnStart(uint moveId) => true;
        protected abstract void OnEventHit(MeleeNative.Event e);
        protected abstract void FallbackSkills();
        protected virtual void OnSimStarted() { }
        /// Runs each fixed tick after input is read and before the sim steps: a fighter
        /// feeds the sim host-side context here (Pikachu's lock-on target).
        protected virtual void BeforeStep(in MeleeNative.Pad pad) { }
        /// The effects this fighter spawns hit sparks with (null: none).
        protected virtual MeleeEffects EffectsForSparks => null;
        /// A hit of this fighter landed at `world`: the spark Melee draws for the hitbox's element
        /// (ftColl_8007A06C picks the effect id per element; a hard normal hit gets the bigger burst).
        /// Safe to call from the hit samplers; does nothing before the sim is running.
        public void HitSpark(uint element, float damage, Vector3 world)
        {
            var effects = EffectsForSparks;
            if (effects == null || !active || failed || scale <= 0f) return;
            uint cue;
            switch (element)
            {
                case 1: cue = 1002; break;   // fire
                case 2: cue = 1001; break;   // electric
                case 3: cue = 1004; break;   // slash
                case 4: cue = 1145; break;   // coin
                case 5: cue = 1005; break;   // ice
                case 13: cue = 1046; break;  // dark
                case 6: case 7: case 8: case 11: case 12: return; // nap, sleep, catch, inert, disable
                default: cue = damage >= 12f ? 1011u : 1000u; break; // normal, ground, cape
            }
            try
            {
                var facing = new Vector3(state.facing.x, 0, state.facing.z);
                var rotation = facing.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(facing) : Quaternion.identity;
                effects.Spawn(cue, Matrix4x4.TRS(world / scale, rotation, new Vector3(-1, 1, 1)));
            }
            catch (Exception e) { CommonLog.Error("Hit spark: " + e.Message); }
        }
        static GameObject featherEffect;
        /// The sim jumped; `jumpsUsed` counts the ground jump and the mid-air ones. A third jump
        /// or later is a Hopoo Feather's, which in RoR2 makes its feather burst and "hooo"; the
        /// sim's jump never runs that code, so it is spawned here. The fighter's own jump voice
        /// is played by the sim, so a feather jump has both.
        protected virtual void OnJump(float jumpsUsed)
        {
            if (jumpsUsed < 3f || !NetworkServer.active) return;
            try {
                if (!featherEffect) featherEffect = LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/FeatherEffect");
                if (featherEffect) EffectManager.SpawnEffect(featherEffect, new EffectData { origin = body.footPosition }, true);
            } catch (Exception) { }
        }

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
            if (puppet || !IsOwner)
            {
                // Another player's fighter: no pad, no camera. Its sound fades with distance.
                if (puppet) { MeleeAudio.SetGain(sim, PuppetGain()); UpdateAudio(); }
                if (body.healthComponent && !body.healthComponent.alive) BeginDeath();
                return;
            }
            pad.Read(body, input, GrabHolding, MeleeAim.CameraRay(body, input).direction, ShieldSelected, Layout);
            UpdateAudio();
            if (body.healthComponent && !body.healthComponent.alive) { BeginDeath(); return; }
            if (active) { smoother.Smooth(); cameraPivot.Use(true); FaceTarget(); }
        }
        protected void FixedUpdate()
        {
            if (failed || !body || !body.healthComponent || !body.healthComponent.alive) return;
            if (!body.hasEffectiveAuthority) { StepPuppet(); return; }
            if (puppet) EndPuppet();
            if (body.currentVehicle) { if (active) Cleanup(); return; }
            if (!active) {
                if (AssetsBusy) return;
                if (!AssetsReady) { Fallback("Assets missing. Set the fighter's disc config."); return; }
                if (!ground.Ready(body.footPosition, Time.fixedDeltaTime, FighterName)) return;
                try { StartSim(); } catch (Exception e) { Fallback(e.ToString()); return; }
            }
            try {
                if (smoother.MovedElsewhere) { var moved = transform.position - feetOffset; MeleeNative.ssbm_sim_place(sim, ToSim(moved), 0); smoother.Reset(); native.ForgetVelocity(); unstuck.Reset(); wallGuard.Placed(moved); }
                bool hitImpulse = false;
                // A fluttering jump's armor (Yoshi): hits do not move him.
                bool armored = (MeleeNative.ssbm_sim_defense_flags(sim) & 256) != 0;
                if (native.TakeExternalVelocity(out var external) && !Recovering && !armored) {
                    hitImpulse = !native.JumpPadLaunch && external.sqrMagnitude > 0.01f;
                    var velocity = new MeleeNative.Vec3(external.x / (scale * 60), external.y / (scale * 60), external.z / (scale * 60));
                    if (native.JumpPadLaunch) { var g = Physics.gravity / (scale * 3600); MeleeNative.ssbm_sim_launch_pad(sim, velocity, new MeleeNative.Vec3(g.x, g.y, g.z)); }
                    else MeleeNative.ssbm_sim_place(sim, velocity, 1);
                }
                float health = body.healthComponent.combinedHealth;
                if (health < lastHealth - 0.001f && !armored && (hitImpulse || native.Control() == NativeBody.Hold.All))
                {
                    // The size of the hit as a fraction of health picks the hurt voice (the
                    // source thresholds knockback); an older native build only gets the hurt.
                    float severity = (lastHealth - health) / Mathf.Max(1f, body.healthComponent.fullCombinedHealth);
                    if (MeleeNative.HasHurtScaled) MeleeNative.ssbm_sim_hurt_scaled(sim, severity);
                    else MeleeNative.ssbm_sim_hurt(sim);
                }
                lastHealth = health;
                var control = native.Control();
                var ps = pad.Take(AllowedButtons, control == NativeBody.Hold.All);
                ps.buttons = FilterButtons(ps.buttons);
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
                float speedScale = body.moveSpeed / Mathf.Max(0.1f, body.baseMoveSpeed) * SpeedScale;
                if (body.isSprinting) speedScale /= Mathf.Max(0.1f, body.sprintingSpeedMultiplier);
                MeleeNative.ssbm_sim_tuning(sim, new MeleeNative.Tuning { speed_scale = speedScale, jump_scale = JumpScale * NativeBody.JumpScale(body), extra_jumps = extra, hold_charge = HoldCharge ? 1u : 0u, smash_max_frames = 60 });
                MeleeNative.ssbm_sim_jump_height(sim, MeleeControls.JumpHeight(body.jumpPower, Physics.gravity.y, motor.jumpPowerPenaltyCoefficient) / scale);
                FaceTarget();
                if (direction) { var facing = direction.forward; MeleeNative.ssbm_sim_facing(sim, new MeleeNative.Vec3(facing.x, 0, facing.z)); }
                BeforeStep(in p);
                MeleeNative.ssbm_sim_step(sim, in host, in p, Time.fixedDeltaTime);
                FlushOps();
                uint previousRecovery = recoveryFlags; recoveryFlags = MeleeNative.ssbm_sim_rebirth_flags(sim);
                recovery?.Refresh();
                if (Arriving && !Recovering) Arriving = false;
                StepPresentation(Time.fixedDeltaTime);
                uint count = MeleeNative.ssbm_sim_hitboxes(sim, null, 0);
                if (count > 1024) throw new InvalidOperationException("Native hitbox sample budget exceeded");
                if (count > hitboxes.Length) hitboxes = new MeleeNative.HitboxSample[count];
                lastHitboxCount = 0;
                if (count > 0) {
                    if (MeleeNative.ssbm_sim_hitboxes(sim, hitboxes, (uint)hitboxes.Length) != count) throw new InvalidOperationException(MeleeNative.Error);
                    if (permitDamage) { ApplyHitboxes(hitboxes, (int)count, scale); lastHitboxCount = (int)count; }
                }
                if (MeleeNative.ssbm_sim_state(sim, out state) != 1) { Fallback(MeleeNative.Error); return; }
                if (MeleeDiag.On && count > 0) MeleeDiag.Batch(FighterName, state.move_id, startedMove, hitboxes, (int)count, permitDamage, ref lastBatchSerial);
                StepGrab(Time.fixedDeltaTime);
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
        // Native moves 25..27 are the three grabs, 28 the pummel, 29..32 the throws.
        void StepGrab(float dt)
        {
            if (!GenericGrab || !IsOwner || puppet) return;
            if (capture == null) capture = new EnemyCapture(body);
            if (pendingThrow.HasValue)
            {
                var cue = pendingThrow.Value; pendingThrow = null;
                var target = capture.Target; capture.Dispose();
                if (target && target.alive)
                {
                    var launch = new Vector3(cue.direction.x, cue.direction.y, cue.direction.z);
                    NativeHit.Deal(body, target, new NativeHitSpec {
                        coefficient = cue.strength / 3f, position = target.body.corePosition, force = launch * 1800f,
                        procCoefficient = 1f, damageType = DamageType.Generic, color = DamageColorIndex.Default,
                    });
                }
            }
            bool catching = state.move_id >= 25 && state.move_id <= 27;
            if (!capture.Target && catching) ScanGrab();
            if (capture.Target)
            {
                if (MeleeNative.ssbm_sim_grabbed(sim) == 0) { capture.Dispose(); return; }
                if (state.move_id == 28) Pummel(); else pummeled.Clear();
                if (!capture.Step(new Vector3(state.facing.x, 0, state.facing.z), dt)) MeleeNative.ssbm_sim_set_grabbed(sim, 0);
            }
        }
        // The disc's grab hitboxes (0% damage) mark where a grab reaches; the first live
        // enemy inside one is captured.
        void ScanGrab()
        {
            for (int i = 0; i < lastHitboxCount; i++)
            {
                var hit = hitboxes[i];
                if (hit.damage > 0f) continue;
                var forward = new Vector3(hit.facing.x, 0, hit.facing.z);
                var heading = forward.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(forward) : Quaternion.identity;
                var local = new Vector3(-hit.local_position.x, hit.local_position.y, hit.local_position.z);
                var point = new Vector3(hit.origin.x, hit.origin.y, hit.origin.z) * scale + heading * local * scale;
                int n = Physics.OverlapSphereNonAlloc(point, Mathf.Max(hit.radius * scale * 1.8f, 0.6f), grabCandidates, LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide);
                for (int j = 0; j < n; j++)
                {
                    var hurt = grabCandidates[j] ? grabCandidates[j].GetComponent<HurtBox>() : null;
                    if (!hurt || !hurt.healthComponent || !hurt.healthComponent.alive) continue;
                    var other = hurt.healthComponent.body;
                    if (!other || !other.teamComponent || other.teamComponent.teamIndex == body.teamComponent.teamIndex) continue;
                    if (!capture.TryCapture(hurt.healthComponent)) continue;
                    MeleeNative.ssbm_sim_set_grabbed(sim, 1); pummeled.Clear();
                    return;
                }
            }
        }
        void Pummel()
        {
            for (int i = 0; i < lastHitboxCount; i++)
            {
                var hit = hitboxes[i];
                if (hit.damage <= 0f || !pummeled.Add((hit.serial, hit.generation))) continue;
                NativeHit.Deal(body, capture.Target, new NativeHitSpec {
                    coefficient = hit.damage / 3f, position = capture.Target.body.corePosition,
                    procCoefficient = 1f, damageType = DamageType.Generic, color = DamageColorIndex.Default,
                });
            }
        }
        void StartSim()
        {
            scale = MetresPerUnit; feetOffset = transform.position - body.footPosition; safe = body.footPosition;
            recovery = GetComponent<MeleeRecovery>();
            lastHealth = body.healthComponent.combinedHealth;
            if (RecoveryGround(safe, 0.6f, 0.8f, out var standing)) { safe = standing; transform.position = standing + feetOffset; }
            sim = MeleeNative.ssbm_sim_new_fighter(NativeLib.CString(AssetsDir), ToSim(safe), FighterKind); if (sim == IntPtr.Zero) throw new InvalidOperationException(MeleeNative.Error);
            CreatePresentation();
            self = GCHandle.Alloc(this);
            // Other players' machines replay these calls on a shadow sim (see MeleeNet).
            if (MeleeNative.HasReplay && MeleeNative.ssbm_sim_record(sim, 1) == 1) { recording = true; recordedSteps = 0; }
            if (MeleeNative.ssbm_sim_begin_arrival(sim, NativeLib.CString(AssetsDir)) != 1) throw new InvalidOperationException(MeleeNative.Error);
            recoveryFlags = MeleeNative.ssbm_sim_rebirth_flags(sim); recovery?.Refresh();
            host = new MeleeNative.Host { user = GCHandle.ToIntPtr(self), collide = Marshal.GetFunctionPointerForDelegate(collideFn), evt = Marshal.GetFunctionPointerForDelegate(eventFn) };
            WorldProbe.UseBodyCollision(motor, gameObject); motor.Motor.enabled = false;
            smoother = new StepSmoother(transform); cameraPivot = new CameraPivot(body); cameraPivot.Use(true); active = true;
            unstuck.Reset(); unstuck.Step(safe, false, true, false, 0.25f, 1.8f, RecoveryGround, out _); wallGuard.Placed(safe);
            CommonLog.Info(FighterName + " sim started with costume " + body.skinIndex);
            if (body.isPlayerControlled && body.hasEffectiveAuthority) MeleeLocal.Set(FighterKind, Run.instance);
            OnSimStarted();
        }
        protected void LateUpdate()
        {
            if (!active || failed) return;
            try {
                GameSettings.HideRenderers(body.modelLocator.modelTransform, true);
                var animator = body.modelLocator.modelTransform.GetComponent<Animator>(); if (animator) animator.enabled = false;
                var at = puppet ? ToWorld(state.position) : transform.position - feetOffset;
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
        // The owner's calls on its sim, recorded natively, go out three steps at a time (about 20 a second) when
        // other players are in the game; alone they are dropped.
        void FlushOps()
        {
            if (!recording || ++recordedSteps < 3) return;
            recordedSteps = 0;
            uint length = MeleeNative.ssbm_sim_ops_take(sim, null, 0);
            if (length == 0) return;
            var bytes = length <= opsBuffer.Length ? opsBuffer : new byte[length];
            if (MeleeNative.ssbm_sim_ops_take(sim, bytes, (uint)bytes.Length) != length) return;
            if (!MeleeNet.Networked) return;
            var ops = new byte[length];
            Buffer.BlockCopy(bytes, 0, ops, 0, (int)length);
            MeleeNet.SendFrames(body, MeleeNative.AbiVersion, ops);
        }

        // --- Another player's fighter ----------------------------------------------------
        void StepPuppet()
        {
            if (puppetBroken || !MeleeNative.HasReplay) return;
            if (!active)
            {
                if (AssetsBusy || !AssetsReady) return;
                stream = MeleeNet.StreamFor(body.netId.Value);
                if (stream == null || stream.Batches.Count == 0) return;
                try { StartPuppet(); }
                catch (Exception e) { puppetBroken = true; CommonLog.Error(FighterName + " could not show another player's fighter: " + e.Message); Cleanup(); return; }
            }
            try
            {
                if (stream == null) stream = MeleeNet.StreamFor(body.netId.Value);
                if (stream != null)
                {
                    // Play the owner's calls a few steps behind, catching up when a burst arrives.
                    puppetWait++;
                    while (stream.Batches.Count > 4) Replay(stream.Batches.Dequeue());
                    if (puppetWait >= 3 && stream.Batches.Count > 0) { Replay(stream.Batches.Dequeue()); puppetWait = 0; }
                }
                recoveryFlags = MeleeNative.ssbm_sim_rebirth_flags(sim);
                StepPresentation(Time.fixedDeltaTime);
                if (MeleeNative.ssbm_sim_state(sim, out state) != 1) throw new InvalidOperationException(MeleeNative.Error);
            }
            catch (Exception e) { puppetBroken = true; CommonLog.Error(FighterName + " stopped showing another player's fighter: " + e.Message); Cleanup(); }
        }

        void Replay(byte[] ops)
        {
            if (MeleeNative.ssbm_sim_replay(sim, in host, ops, (uint)ops.Length) == uint.MaxValue)
                throw new InvalidOperationException("the other player's calls do not replay (different Melee builds?)");
        }

        void StartPuppet()
        {
            scale = MetresPerUnit; feetOffset = transform.position - body.footPosition;
            sim = MeleeNative.ssbm_sim_new_fighter(NativeLib.CString(AssetsDir), ToSim(body.footPosition), FighterKind);
            if (sim == IntPtr.Zero) throw new InvalidOperationException(MeleeNative.Error);
            CreatePresentation();
            self = GCHandle.Alloc(this);
            host = new MeleeNative.Host { user = GCHandle.ToIntPtr(self), collide = Marshal.GetFunctionPointerForDelegate(collideFn), evt = Marshal.GetFunctionPointerForDelegate(eventFn) };
            MeleeNative.ssbm_sim_state(sim, out state);
            puppet = true; active = true; permitDamage = false; puppetWait = 0;
            CommonLog.Info(FighterName + ": showing another player's fighter");
        }

        // The body became ours (or the shadow is no longer needed): drop it so the real sim can start.
        void EndPuppet() { Cleanup(); }

        /// How loud another player's fighter is from here, by distance to the camera (full within 12 m, silent past 60 m).
        float PuppetGain()
        {
            var camera = GameSettings.PlayerCamera();
            if (!camera) return 1f;
            float d = Vector3.Distance(camera.position, ToWorld(state.position));
            return Mathf.Clamp01(1f - (d - 12f) / 48f);
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
            // A shadow only shows the fighter: the owner makes its attacks and moves its body.
            if (puppet) { if (e.kind == 3) OnJump(e.strength); return; }
            if (e.kind == 1) { BeginMove(e); OnEventStarted(e); NativeBody.MarkCombat(body); MeleeAim.BeginAttack(body, direction, MeleeAim.CameraRay(body, input).direction); }
            else if (e.kind == 2) { if (permitDamage) OnEventHit(e); }
            else if (e.kind == 6) GrabThrow(e);
            else if (e.kind == 3) { native.Jumped(); OnJump(e.strength); }
            else if (e.kind == 4) native.Landed(Vector3.down * e.strength * scale * 60f);
        }
        // A move the sim started: its slot spends a charge (RoR2's bookkeeping: cooldown, items that
        // watch skill use), and `permitDamage` says whether it may hit. The pad already refused a press
        // with no charge, so a start that finds the slot empty (a chained jab, a rapid jab) still
        // deals its damage; permission is only that the slot holds this fighter's own skill and this
        // machine runs the body (MeleePermit). Every fighter uses this one rule.
        void BeginMove(MeleeNative.Event e)
        {
            var set = SkillSet;
            startedMove = e.move_id;
            bool acts = MeleeNet.Acts(body);
            if (set == null) { permitDamage = acts; MeleeDiag.MoveStart(FighterName, e.move_id, -1, true, -1, false, acts, permitDamage); return; }
            int slot = set.Slot(e.move_id);
            var skill = set.Get(body, slot);
            var expected = set.Expected(body, slot);
            bool holds = NativeSkills.Holds(skill, expected);
            int stock = skill ? skill.stock : -1;
            // A deferred move keeps its charge until it succeeds; the pad's own gate
            // (`MeleeSkillSet.Allowed`) still refused the press without one.
            bool defer = !MeleePermit.SpendsAtStart(holds, SpendsOnStart(e.move_id));
            bool spent = !defer && NativeSkills.Spend(skill, expected);
            permitDamage = MeleePermit.Allowed(holds, acts, spent);
            MeleeDiag.MoveStart(FighterName, e.move_id, slot, holds, stock, spent, acts, permitDamage);
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
                var feet = puppet ? ToWorld(state.position) : transform.position - feetOffset;
                var facing = new Vector3(state.facing.x, 0, state.facing.z);
                var rotation = facing.sqrMagnitude > 0.001f ? Quaternion.LookRotation(facing) : transform.rotation;
                BeginDeathPresentation(feet, rotation);
            } catch (Exception e) { CommonLog.Error(FighterName + " death presentation failed: " + e); }
            Cleanup();
        }
        void Cleanup()
        {
            capture?.Dispose(); pendingThrow = null;
            pad.ReleaseInteraction();
            DisposePresentation();
            if (body && body.netId.Value != 0 && puppet) MeleeNet.Drop(body.netId.Value);
            if (sim != IntPtr.Zero) MeleeAudio.SetGain(sim, 1f);
            recording = false; stream = null; puppet = false;
            Arriving = false; recoveryFlags = 0; recovery?.Refresh();
            active = false; cameraPivot?.Use(false);
            if (sim != IntPtr.Zero) { MeleeNative.ssbm_sim_free(sim); sim = IntPtr.Zero; } if (self.IsAllocated) self.Free();
            if (motor && motor.Motor) motor.Motor.enabled = true;
            if (body && body.modelLocator && body.modelLocator.modelTransform) { if (!deathStarted) GameSettings.HideRenderers(body.modelLocator.modelTransform, false); var a = body.modelLocator.modelTransform.GetComponent<Animator>(); if (a) a.enabled = !deathStarted; }
        }
        protected void OnDestroy() { if (active && body && body.healthComponent && !body.healthComponent.alive) BeginDeath(); Cleanup(); }
    }
}
