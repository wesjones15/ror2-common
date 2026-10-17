# Melee host integration

`MeleeSelectDisplay` plays the fighter's up-B animation, effects and sound once
per selection. It continues stepping the native idle after completion; a new
selection creates a new actor. UI time drives the preview.

Set `CharacterBody.preferredPodPrefab = null` and register `MeleeArrival` as the
preferred/initial body state. The driver's `IMeleeArrival` implementation renders
its extracted Entry pose/platform using native `begin_arrival`: this enters the
same Figurine phase as falloff return, skipping the KO. Movement releases after
the source minimum arrival; otherwise the source timeout releases it. Both paths
play the original entry transformation and post-release flashing protection.
There is no initial Interact prompt. The state protects loading/placement too.
Set `Arriving=false` on errors/cleanup so a failed native startup cannot trap the
body in arrival. Map a safe standing position before disabling the vanilla motor.
Clear Arriving when native Recovering clears; the state can then go to main.
The native protection flag continues afterward through common MeleeRecovery.

Use `MeleeAim.CameraRay` for directional attack input and live projectile
convergence. It invokes RoR2 CameraRigController.ModifyAimRayIfApplicable for the
owning body. ProjectileDirection ignores the owner's colliders and never aims
backwards into the body when the camera is obstructed behind the cannon.

Facing follows the installed game's GenericCharacterMain rule for a model with
no AimAnimator: `CharacterBody.shouldAim ? aim : movement`. Keep the actual
CharacterDirection component enabled so its turnSpeed/SmoothDampAngle control
the turn; do not hardcode camera facing or implement a second turn controller.
BeginAttack uses the real SetAimTimer(3f), as Commando FirePistol2's StartAimMode.
Refresh it during held ranged actions. Native `set_host_facing`/the additive
ABI-4 `ssbm_sim_facing` consume CharacterDirection.forward independently of aim
and movement. `ssbm_sim_facing_locked` reports authored dash/back-air/spin/grab/
throw/entry/reaction locks; keep their target on the native action heading.
Normal attacks and ranged actions can follow the game's smooth turn. This does
not give Melee assets vanilla upper-body aim/strafe animation blends.

libssbm's shared 3D move-selection adaptation uses camera pitch only with LS
above 0.3: up from +20 degrees, down from -50 degrees. LB/RB force vertical
selection before LS/camera, and neutral controls remain neutral. These bands
are host policy, not Melee or vanilla RoR2 thresholds. Captured throws retain
raw Melee axes. Run tests/MeleeHostCheck and the native camera/input regressions.

For subsequent falloff recovery, add `MeleeRecovery` and implement
`IMeleeRecovery`. Call `CheckFall` with the same Unstuck instance, feet, height
and standing probe used by the driver. This uses `Unstuck.FellEarly`; keep
ordinary `Unstuck.Step` and `WallGuard` corrections outside the KO sequence.
The native fighter owns the KO, figurine/transform, movement/timeout release,
source flash and invulnerability timing. Expose its flags through Recovering
and RecoveryInvulnerable, and call Refresh immediately on a trigger/flag change.
The component balances its own HiddenInvincibility buff, preserving other owners.
Recreate source platform presentation when the KO phase returns to safe ground;
reset smoothing and wall placement there. Initial arrival and falloff share the
automatic figurine phase. Mario/Olimar keep their own initial sequences.
`Unstuck.Lost` owns Mario's early TOO BAD predicate: >20m below safe ground with
no floor within 2000m. Melee/Olimar FellEarly uses that same point plus valid stage
zones. Lower terrain prevents early distance recovery, even on drops over 150m.
Mario's configurable early-screen threshold and later teleport stay intact.
Run `tests/MeleeRecoveryCheck` and `tests/UnstuckCheck`, plus the native boundary
and rendered checks; compilation alone does not establish in-game behavior.

`EnemyCapture` owns an enemy's host identity, AI suspension and held placement;
the native fighter supplies actual grab hit windows, pummel data, throw choice and
release frame. Restore capture ownership on throw, interruption, death, timeout
or destruction. Enemy captures last at most eight seconds in this RoR2 policy;
player bodies and encounter objects with no movable body are excluded. Native
grab/throw simulation contains no RoR2 types. Full Melee escape/percent rules are
separate from this host adaptation.
