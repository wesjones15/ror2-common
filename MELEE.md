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

`EnemyCapture` owns an enemy's host identity, AI suspension and held placement;
the native fighter supplies actual grab hit windows, pummel data, throw choice and
release frame. Restore capture ownership on throw, interruption, death, timeout
or destruction. Enemy captures last at most eight seconds in this RoR2 policy;
player bodies and encounter objects with no movable body are excluded. Native
grab/throw simulation contains no RoR2 types. Full Melee escape/percent rules are
separate from this host adaptation.
