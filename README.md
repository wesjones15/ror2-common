> modern/1.5.0 branch: targets RiskOfRain2_DLC4 (Unity 2021.3.45f2). Build/API validation only; in-game validation is pending. Legacy instructions below describe the previous release.

# ror2-common

Pickup effects explicitly bind the game's pickup-definition sprite to the world
`SpriteRenderer` after the vanilla coroutine finishes its effect-data/tier setup.
An owned async request replaces the pooled `AsyncSprite` lifecycle; each activation
releases the prior request and ignores late callbacks. This covers
`ItemTakenOrbEffect` and `PickupTakenOrbEffect`. `ItemHooks.Install()` installs it
once across all three plugins. The first assignments/failures are logged.
`dotnet run --project tests/PickupIconCheck -c Release` checks original coroutine
timing, direct/cached/async results, repeated-item reuse and pool cancellation.
Built against DLC4; in-game confirmation of the empty-frame fix is pending.

`ModelView` caches unchanged material property blocks and avoids sunlight searches
for models without native vertex lighting. A rigid `IViewSource` may additionally
implement `IStaticGeometrySource` to share one mesh per model and upload its geometry
once. This promises identical positions, normals and frame UVs across all instances
for the source's entire lifetime; animated sources must leave it off. Models requiring
rotation-dependent vertex lighting automatically retain separate dynamic meshes.
Transforms, materials, tint, fade, hit flash and freeze sheen still update normally.

Run `dotnet run --project tests/ModelViewCheck -c Release` to check mesh sharing,
ownership, dynamic fallback and material-state updates with a recording host.

Generated portraits persist between launches, independently of the mod release version.
Character/skin `Portrait.Request` calls take an `assetsDir` delegate and a stable `cacheKey`
(required for skins). Results live at `assets/portraits/character-v1/<key>.bmp`.
Melee `SkillIcons` uses `assets/skill-icons/melee-v1/<skill-name>.bmp`, replacing the old
write-only review dumps. Each image is generated only when its cache is missing or invalid;
new characters/skins/skills generate only their own missing entries. A hit never invokes
the native model/camera factory. Melee character portraits use the same idle-only
character-select source and the common 45-degree left framing; original CSS photos remain
fallbacks while generation is pending or fails. The Rust viewer's `character_portrait`
example compares Samus and DK at Wait frames 0/15/30 before host integration.

Bump `PortraitCache.CharacterRevision` or `MeleeSkillRevision` when those portraits'
recipes/rendering change; an individual character request can also override its revision.
Routine gameplay releases keep the cache. These pixels are local user data, never packaged.
`tests/PortraitCacheCheck` verifies repeated-launch factory bypass, independent new keys,
revision invalidation, corrupt-cache recovery and retry after unavailable assets.
Character portraits yield between completed jobs so cache misses don't all render in one frame.
First-time generation can still hitch; this change does not defer it past the intro.

`SkinPresentation` supplies Mario/Olimar's selected skin name and skill art in character
select and its loadout panel, plus the same art in each body's HUD cooldown slots.
Registrations use the offered loadout index, not the native skin ID (an unavailable Louie
must not shift Pianta onto the wrong pictures). Menu icon substitutions restore the shared
SkillDefs after the synchronous build, including exceptions; HUDs read their own body's
skin without replacing the SkillDef or changing stock/recharge behavior. Cached variants
arriving late refresh menus. Air Pump, Onion Ride and the sprout-only passive keep shared art.
Only Mario/Olimar register skins; Melee and vanilla survivors keep their own UI behavior.
Run `tests/SkinPresentationCheck` for independent-skin, fallback and restoration checks.

`SkillCard` renders native skill scenes in Unity after disc extraction completes, one
portrait per frame. Pass the plugin's assets-directory delegate to `Request` to persist
the 256x256 result under `assets/skill-icons/approved-v3/<skill-name>.bmp`. Subsequent
starts load these pixels without building a portrait sim. Missing, malformed or old
revision caches regenerate. Change the revision when a recipe changes; deleting a
cached image also regenerates it next launch. The BMP codec is Unity-independent and
writes by atomic replacement. No generated art belongs in Git or release packages.

Camera yaw/pitch are radians; `CameraDistance` supplies an absolute distance for matching
viewer captures. `BeforeRender` receives the actual camera to orient native particles.
The scene renders directly on its chosen background so additive fire and refraction
survive capture. Run `dotnet run --project tests/SkillCardCacheCheck -c Release` for cache
round-trip, regeneration, replacement and path checks.

Capture visibility is scoped to the stage's hierarchy. Other plugins' renderers on
ManualRender are hidden only during the camera render and restored on failure too.
Stage objects, cameras and lights deactivate before deferred destruction, preventing
same-frame contamination between plugin runners. `tests/SkillCardRenderCheck` checks
this ownership boundary. Revision v3 automatically replaces v2's contaminated captures.

C# shared by the Risk of Rain 2 hosts: `../ror2-mario` (libsms) and `../ror2-olimar`
(libpikmin). It targets RoR2 1.2.4.4 (Unity 2019.4, BepInEx 5, no R2API).

There's no DLL here. Each plugin compiles these sources in, so two mods never fight over a
shared assembly's version:

```xml
<ItemGroup>
  <Compile Include="$(MSBuildThisFileDirectory)../../ror2-common/src/**/*.cs" Link="Common\%(RecursiveDir)%(Filename)%(Extension)" />
</ItemGroup>
```

Set `Ror2Common.CommonLog.Log = Logger;` first thing in the plugin's `Awake`.

| File | What |
|------|------|
| `Mp.cs`, `Fragments.cs`, `ByteIO.cs` | Custom UNet messages without R2API (register a type with server and client handlers, send byte payloads to the server, to everyone or to everyone but one), with fragmentation for payloads over 1100 bytes, and little-endian `ByteWriter` / `ByteReader` with no Unity types. See [MULTIPLAYER.md](MULTIPLAYER.md). |
| `GameCubeLibrary.cs` | The optional shared folder of disc images and per-mod caches (`assets/Olimar`, `assets/Mario`, `assets/Melee`) all three plugins read, without moving an existing cache. See [GAMECUBE-LIBRARY.md](GAMECUBE-LIBRARY.md). |
| `NativeLib.cs` | Load a Rust DLL by full path before `[DllImport]`s bind; compare struct sizes with the native side; UTF-8 strings. |
| `Prefabs.cs` | Clone a prefab under an inactive holder, with its own network asset id (what R2API's `PrefabAPI.InstantiateClone` does). |
| `ContentProvider.cs` | Register content through RoR2's `collectContentPackProviders` hook. |
| `WorldProbe.cs` | Ground, roof and wall queries against the world layer. |
| `ViewSource.cs` | The model-view structs both native libraries share, and `IViewSource` over them. |
| `ModelView.cs` | A native view's models in Unity. Meshes are built once; each frame takes skinned vertices and per-batch texture and colour. Uses HGStandard, plus Cloud Remap for see-through and additive parts. Also has `Shaders`. |
| `MaterialAlpha.cs` | Shared source alpha handling for future ports: texture multiply/blend/replace without erasing zero-material-alpha effects. `MaterialAlphaBuffer` adapts native RGBA plus animated alpha metadata for `IViewSource.Colors`. Preserves texture masks and intentional fades. See [material-alpha.md](docs/material-alpha.md). |
| `MeleeRecovery.cs` | Melee-only return adapter: `IMeleeRecovery`, common `Unstuck.FellEarly` at Mario's early clear-void point and owned invulnerability buffs. Native Melee shares figurine/automatic release/flashing protection between initial spawn and falloff; only falloff plays KO. See [MELEE.md](MELEE.md). |
| `GameSettings.cs` | RoR2's SFX volume, the player's camera, hiding a body's own model (or any hierarchy's renderers). |
| `CameraPivot.cs` | Puts a body's camera on a pivot of our own (a plain child of the body), for plugins that hide the body's model: the stock pivot hangs off that still-animated model and sways the camera. Its comment lists what the driver should do too (run before RoR2's camera; never leave the body ahead of the drawn position). |
| `StepSmoother.cs` | A body that a native sim moves each physics step, drawn smoothly between steps, plus `LagTicks`: how many sim ticks behind the sim that drawing is. `Smooth(alpha)` accepts a native clock, such as Pikmin's 30 Hz ticks within RoR2's 60 Hz physics. Hand the same lag to the native view so it poses the character that far back too; otherwise fast moves show as stacked "ghost" copies at high refresh rates. The view half is each native library's own (`SmsViewFrame.lag`, `PikViewFrame.lag`). |
| `InteractGuard.cs` | RoR2's interact button when it's also one of the character's buttons: a press made while RoR2 shows an interact prompt is RoR2's until let go. `Claim(down, prompt)` each frame returns the button as the character sees it; `PromptShown(driver)` is the prompt check. |
| `InteractTarget.cs` | A native object as a RoR2 interactable, with RoR2's own prompt: `Make(name, parent, at, context, canUse, onUse)`, then `Override(driver, target)` each frame while it's in reach (null takes the prompt back). No collider, so only the plugin's own reach test finds it. Host or single player only (a client's interact goes by network id). |
| `NativeHit.cs` | Damage from a native sim as RoR2 damage from a character (`Deal(attacker, victim, spec)`): crit, kill credit, `OnHitEnemy`/`OnHitAll` at the spec's proc coefficient. Server only. |
| `MinionHit.cs`, `MinionHooks.cs` | A hit by a minion with no RoR2 body: `NativeHit` from its owner, with the attacker-position check in `TakeDamage` (Focus Crystal, backstab) made from the minion. The hook needs `ROR2COMMON_HOOKS` (see below). |
| `MinionStats.cs` | An owner's stats as ratios to base (damage, attack speed, move speed, max health), plus regen fraction, damage taken after armour and crit, for a native sim's minions or the character itself. |
| `Minions.cs` | Bodied minions: a few owned things (an Onion) with a real RoR2 master and body, so RoR2's item code runs on them with their own state. `Minions.Define(pack, owner, spec)` at content load, `Minions.Spawn(kind, ownerBody, at, rot)` on the server, then `Place(at, rot)` every FixedUpdate and `Release()`. The owner's items follow (RoR2's copy filter plus the spec's); not moving is RoR2's own check on the placed transform; it reads as airborne; optional untargetable and invincible. Commando's body underneath, hidden. |
| `NativeSkills.cs` | A native move in one of RoR2's skill slots (`NativeSkillDef`, `Make`, `SetFamily`), so RoR2 keeps its charges: the slot's stock, recharge and cooldown scale are what Backup Magazine, Hardlight, Lysate Cell, Alien Head, Purity, Bandolier and Brainstalks change. Nothing executes it; `Spend(slot, def)` takes a charge when the sim makes the move (free under Brainstalks, `Unlimited`), or runs an overriding skill (Heresy) instead. `RechargeGate` can hold the recharge. |
| `SkillCooldown.cs` | A slot's cooldown items as a ratio (`Ratio`) and a boost for moves with no cooldown (`Boost`: `1 / ratio ^ exponent`, held through Brainstalks). |
| `ProcLimiter.cs` | On-hit procs at most once per interval per target for an attack that hits many times a second; the hits between land at proc coefficient 0. |
| `NativeBody.cs` | On a body moved by a native sim: RoR2's throws on the character motor (`WroteVelocity` / `TakeExternalVelocity`: jump pads, knockback, Milky Chrysalis), crowd control (`Control`: stunned, frozen or shocked, or rooted), GenericCharacterMain's sprint rule (`UpdateSprint`), the out-of-combat clock (`MarkCombat`, for Red Whip), and primary uses for Shuriken (`PulsePrimary`). `JumpPadLaunch` identifies JumpVolume's air-control hold: preserve its full velocity with `Physics.gravity` until native collision/landing, then call `Landed`. RoR2's jump and landing events, which its own movement code raises and items listen for: `Jumped()` (Faraday Spur, Earthbound Vessel, Hex of Leaping) and `Landed(velocity)` (Mycelium Cap's spores). `JumpScale` / `AccelerationScale`: the body's jump power and acceleration over their bases, for the sim. Also marks the body for `ItemHooks`. |
| `ItemHooks.cs` | `ItemHooks.Install()`, then `Healed` (every heal as asked, at full health too; lifesteal carries HealOnHit/HealOnCrit) and `AmmoPack` (Bandolier). Keeps Headstompers' motor-driven stomp off `NativeBody` bodies. Each hook is found by name and skipped if missing. Needs `ROR2COMMON_HOOKS`. |
| `SurvivorAi.cs` | An AI master for a custom survivor (cloned from a vanilla one), so Goobo Jr., Vengeance and the like can spawn it. `Drivers` swaps its skill drivers for movement alone (hold in range, chase, follow the leader); `Enemy` and `HasAi` let the plugin press its own buttons from what the AI sees. `aggressive` makes the drivers re-decide at 20 Hz. |
| `MeleeBrain.cs` | The generic decisions of a Melee fighter no player controls: aim at the AI's enemy, close, sprint/hop, alternate the B A-attack with a ready skill, hold and release charge moves, and evade a hit with a shield or a hop. `MeleeDriver.CreateBrain` (virtual) is the per-fighter override seam; the default reads `IsRangedMove` so a melee primary is not thrown at range. |
| `GlyphOverride.cs` | Makes the HUD's skill-icon glyphs name the pad buttons a native sim actually reads (Mario's LT for the pound, Olimar's A to throw) instead of RoR2's binds: per body, per slot, one button, several joined with "+", or none. Gamepad only; hooks `Glyphs.GetGlyphString` (needs `ROR2COMMON_HOOKS` and a UnityEngine.UI reference). |
| `Unstuck.cs` | Recovery for a native-sim body that is fallen or blocked and hasn't moved for three seconds: searches rings of clear standing ground around it (the plugin's probe), falling back to the last verified safe spot. The plugin says when it is `fallen` from its sim's states. |
| `WallGuard.cs` | Keeps a native-sim body out of walls its sim let it into: after each step, `Step` sweeps the step (rays and a small sphere) and tests for being inside solid geometry (`WorldProbe.InsideSolid`, which asks mesh back faces too); either returns the last good spot to put the body back at. `strict` (a hit, a knock-down, a launch) sweeps more heights and acts on the first step inside. `Placed` after any deliberate move. |
| `EnemyLock.cs` | Hold-to-lock targeting: the enemy nearest the camera's heading within `Range` (25 m by default) and in sight, kept until released, with cycling by screen direction (Pikmin's RT, Mario's LT). |
| `LockMarker.cs` | Where a lock-on marker floats (`LockMarker.Place`: on the line from the owner's eye to the middle of the target's hurt boxes, just in front of the first it enters, as libpikmin's `lock_marker` does for Olimar's cursor) and `MarkerQuad`, a picture standing there facing the eye and turning about the line to it (see-through material). |
| `ByName.cs` | Items, buffs, types and fields by name at run time: a RoR2 without one reads as none, and newer builds' items can be named before this build has them. |
| `Portrait.cs` | A survivor's portrait (`portraitIcon`) rendered from its lobby character: `Portrait.Request(bodyPrefab, displayFactory, hide)` at content load. Tried at each scene start until the native models load; one render over black and white backgrounds for the alpha, framed on the upper body. |
| `VanillaIcons.cs` | A mod skill wearing a vanilla skill's icon: `VanillaIcons.Use(skillDef, "EngiBodyPlaceSpiderMine", quarterTurnsCw)` and `UsePassive(bodyPrefab, name)` at content load. The vanilla SkillDef is found by asset name (or `skillName`) in `SkillCatalog` on `RoR2Application.onLoad`; turned icons are a CPU copy. A missing name logs a warning. |
| `DisplayModel.cs` | The character select screen: on a survivor's display prefab, draws an `IDisplayActor` (the plugin's own view, posed by its own sim) at the prefab's root and hides the prefab's model. `DisplayModel.Attach(displayPrefab, id, factory)`. |

IL hooks are opt-in, so a plugin that doesn't want MonoMod still compiles: define
`ROR2COMMON_HOOKS` and reference BepInEx's own copies:

```xml
<DefineConstants>$(DefineConstants);ROR2COMMON_HOOKS</DefineConstants>
...
<Reference Include="Mono.Cecil"><HintPath>$(RoR2Dir)\BepInEx\core\Mono.Cecil.dll</HintPath></Reference>
<Reference Include="MonoMod.Utils"><HintPath>$(RoR2Dir)\BepInEx\core\MonoMod.Utils.dll</HintPath></Reference>
<Reference Include="MonoMod.RuntimeDetour"><HintPath>$(RoR2Dir)\BepInEx\core\MonoMod.RuntimeDetour.dll</HintPath></Reference>
```

The native views are `sms_view_*` in libsms-ffi and `pik_view_*` in libpikmin-ffi. Their
structs match field for field, so a plugin only wraps its own functions in an
`IViewSource`.

Translucent model surfaces and Mario's water share `Shaders.SurfaceTintOpacity` (0.25):
an alpha-only multiplier that keeps overlapping pale tints from washing the background
white. Smoke and additive particles keep their original opacity. `SeeThrough` also accepts
an explicit opacity multiplier; water's nearly opaque juice uses 1 instead.

`ModelView` came from ror2-olimar's `ModelView.cs` without its particles: Pikmin's whistle
and ghosts still live in ror2-olimar.

`Minion.Place(at, rotation, moving, grounded)` supplies explicit conditions for externally
controlled entities. Hovering hosts pass `moving: true, grounded: false`; planted entities
pass `false, true`. `MinionMotion` hooks RoR2's stationary query for only those entities and
starts the normal one-second stationary delay on landing. Existing two-argument callers
retain RoR2's transform-based check. This overload requires `ROR2COMMON_HOOKS` for the
stationary override; both survivor plugins enable it.

SpawnMusic provides local stings from build-generated stereo PCM16 WAVs: spawn.wav once a run, when the character first emerges on the run's first stage (later stages and respawns stay quiet), and an optional death.wav; an optional gameover.wav takes the death's place at PlayDeath (death.wav is then PlayMiss's, for a lesser loss), and bluecoin.wav is PlayCue("bluecoin") (SpawnMusic.Cues). Initialize it with the plugin config and folder, then call PlayOnce(body) at emergence and PlayDeath(body) when the body dies. The death sting plays on its own object, so it outlasts the body and a game over. They follow master/music volume, have their own enable switches (SpawnMusic.Enabled, SpawnMusic.DeathSting) and volume (SpawnMusic.Volume; SpawnMusic.DeathVolume, default 1.15, adds gain to the death sting), fade the excerpt edges and pause with the game. A missing clip logs a warning and leaves gameplay usable.

RunAudio resets native presentation and arrival stings at game over (before run destruction), direct quit, shutdown, and new-run startup. A game over lets a death sting finish; leaving the run stops it. Its Ended gate prevents recreation on the results screen. Run the headless event-sequence regression with dotnet run --project tests/RunAudioCheck -c Release. Native device silence still needs an in-game check.

Arrival PCM uses Windows waveOut rather than Unity AudioClip: this RoR2 build rejects AudioClip.SetData. Gain is sampled at arrival; disable, pause, body destruction and run cleanup stop or pause the owned output. The optional --device argument to RunAudioCheck exercises silent buffers on the real output device, including early stop and reopen.

## VR (opt-in)

`src/Vr/` supports Blowntobytes' VR mod (Resurrected_VRMod 2.26.54, plugin id
`com.DrBibop.VRMod`; Quest 3 is the expected headset). It compiles only into a plugin that
defines `ROR2COMMON_VR` and references `UnityEngine.XRModule` and `UnityEngine.VRModule`
(MarioTest does; OlimarTest and Melee don't yet). Nothing links against the VR mod. Its hands
are read by reflection and its controllers through Unity's XR input, so a plugin loads and plays
the same without it, and every call is a no-op with no headset.

- `VrCharacter` + `VrSettings`: what every character shares, and what a character mod extends. It
  covers first person, hiding the body for its own player, the forearm and hand draws on the
  controllers (`ArmKey` + side, `DrawArms`, `ArmAt`, the VR mod's stand-in hands hidden while they
  show), the eye following the head's height (`FollowHead` with the character's `Pose`), the opt-in flip
  view, the crosshair and the wrist HUD. `VrSettings` binds the shared `[VR]` keys (HideBody, Crosshair,
  HeadFollow, ArmPitch/Roll/Forward, WristHud, FlipCamera) in the plugin's own config. A character
  keeps only its own parts: its controls, which moves are flips, and its menus (Mario: `MarioVr`).
- `Vr`: whether the mod is installed and active, its motion controls and handedness, whether a
  body is the local one, the hands by dominance or side, the muzzles, the pointer ray, the head
  and its flat heading, `PointerTarget` (where a ray lands past the body's own colliders), and
  `ShowHandModel` (hides the VR mod's stand-in hand where a character draws its own).
- `VrPad`: both controllers' analog trigger, grip, A/B (X/Y), stick and stick click, with edges.
  The VR mod only passes buttons to RoR2 as on/off Rewired actions.
- `VrCrosshair`: a ring at the pointer's world hit, the same size to the eye at any depth,
  drawn over everything.
- `VrRig`: `EyeOffset` lifts or lowers the VR mod's "VRCamera" anchor on the body (a
  character's head dropping in a slide). `RenderTurn` turns the "VR Camera Rig" for one
  frame's render only (an opt-in flip), and `Turned` turns a hand-space pose with it.
- `VrPanel`: a character's own IMGUI HUD on a quad over the off-hand's wrist. Draw between
  `Begin()` and `End()` in OnGUI, call `Show()` each LateUpdate, and use `PointAt(ray)` to get
  the GUI point the pointer is on.
- `EnemyLock.Aim`: an aim ray (the pointer) to pick and cycle targets about, in place of the
  camera's view. The lock holds while the head looks away. Desktop behaviour is unchanged when
  it's null (`tests/EnemyLockCheck` covers both).
- `CameraHandoff.Hold()` gives nothing to a local VR body, so cutscene cameras never take the
  headset.

The VR mod hides the renderers under the body's model, but ModelView draws sit outside it, so
each character hides its own body for the local player and keeps it for everyone else.
