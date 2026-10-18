MeleeArrival owns the first-spawn figurine gate for IMeleeArrival fighters.
This policy applies only to Melee fighters. Mario and Olimar keep their own
startup states and sequences.
Register it as the initial/preferred body state and clear preferredPodPrefab.
The fighter supplies its source platform, pose, transformation effects and audio.

While ArrivalWaiting is true, the local InteractionDriver receives an
InteractTarget with context "Start". RoR2 supplies its vanilla interaction UI
and the current keyboard/controller glyph; the fighter must not draw its own
start prompt. Only the owning Interactor can release the arrival. Removing the
state destroys its target and clears the override only if that target still owns
it. Autonomous bodies release after their source waiting pose is ready.
