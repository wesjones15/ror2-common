# World pickup icon binding

The supplied Hunter's Harpoon screenshot shows the notification's icon correctly
while the floating tier-coloured world frame is empty. The player confirms this
also happens clear of Mario's body. The log confirms the earlier reset-only
`PickupIconHooks` were installed, so resetting the vanilla loader did not resolve
the report.

The exact world assets are
`Assets/RoR2/Base/Common/VFX/ItemTransferOrbs/ItemTakenOrbEffect.prefab` and
`ItemTransferOrbEffect.prefab`, effect indices 325/326 in the local DLC4 bundle.
Their `PickupSprite` SpriteRenderer uses `Sprites-Default`, white colour and sort
order 3. The frame uses the same material, sort order 1.
`ItemTakenOrbEffect.DelayedUpdateSprite` decodes `EffectData.genericUInt` as an
ItemIndex; `PickupTakenOrbEffect` decodes it as a PickupIndex. Both delay a frame
for effect data, colour the tier components, then delegate to `AsyncSprite`.
These identities were inspected in the game's assembly and bundle; no alternate
sprite, material or particle asset is substituted.

The hook now preserves that coroutine, then explicitly binds the selected
PickupDef's icon (falling back to ItemDef for an item lacking a PickupDef).
`PickupIconBinding` clears/disables the previous AsyncSprite, uses the game's
`AssetOrDirectReference<Sprite>` for addressable icons, and writes the resulting
sprite directly to the world renderer. Direct icons are assigned immediately.
The binding owns its request until the effect returns to the pool, with a
generation check preventing old completions from repainting a new activation.
The game still owns the frame, tier colour, trajectory and lifetime.

The v2 process marker allows any one of the plugins to install the shared hooks.
All three plugins must be rebuilt/staged so the fix works when loaded alone.
Logs report installation plus the first eight assigned sprites and unresolved or
missing icons. This makes a future failure distinguishable from an inactive hook
or a missing asset reference.

`tests/PickupIconCheck` checks real binding writes using recording Unity/RoR2
boundaries: coroutine order/disposal, direct results, immediate cached results,
deferred results, same-item reuse, stale completions and cancellation at pool
return. All three hosts compile against DLC4. This is not in-game verification.
