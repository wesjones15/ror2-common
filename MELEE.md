# Melee host integration

`MeleeSelectDisplay` plays the fighter's up-B animation, effects and sound once
per selection. It continues stepping the native idle after completion; a new
selection creates a new actor. UI time drives the preview.

Set `CharacterBody.preferredPodPrefab = null` and register `MeleeArrival` as the
preferred/initial body state. The driver's `IMeleeArrival` implementation renders
its extracted Entry pose/platform, waits for Interact, then plays the original
entry transformation. The state supplies arrival invincibility until completion.
Set `Arriving=false` on errors/cleanup so a failed native startup cannot trap the
body in arrival. Map a safe standing position before disabling the vanilla motor.

For subsequent falloff recovery, add `MeleeRecovery` and implement
`IMeleeRecovery`. Call `CheckFall` with the same Unstuck instance, feet, height
and standing probe used by the driver. This reuses `Unstuck.Fell` exactly; keep
ordinary `Unstuck.Step` and `WallGuard` corrections outside the KO sequence.
The native fighter owns the KO, figurine/transform, movement/timeout release,
source flash and invulnerability timing. Expose its flags through Recovering
and RecoveryInvulnerable, and call Refresh immediately on a trigger/flag change.
The component balances its own HiddenInvincibility buff, preserving other owners.
Recreate source platform presentation when the KO phase returns to safe ground;
reset smoothing and wall placement there. Initial arrival retains its Start
prompt; fall recovery needs no interaction. Mario/Olimar use their own sequences.
Run `tests/MeleeRecoveryCheck` and `tests/UnstuckCheck`, plus the native boundary
and rendered checks; compilation alone does not establish in-game behavior.

`EnemyCapture` owns an enemy's host identity, AI suspension and held placement;
the native fighter supplies actual grab hit windows, pummel data, throw choice and
release frame. Restore capture ownership on throw, interruption, death, timeout
or destruction. Enemy captures last at most eight seconds in this RoR2 policy;
player bodies and encounter objects with no movable body are excluded. Native
grab/throw simulation contains no RoR2 types. Full Melee escape/percent rules are
separate from this host adaptation.
