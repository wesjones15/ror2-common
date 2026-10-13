# ror2-common

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
| `NativeLib.cs` | Load a Rust DLL by full path before `[DllImport]`s bind; compare struct sizes with the native side; UTF-8 strings. |
| `Prefabs.cs` | Clone a prefab under an inactive holder, with its own network asset id (what R2API's `PrefabAPI.InstantiateClone` does). |
| `ContentProvider.cs` | Register content through RoR2's `collectContentPackProviders` hook. |
| `WorldProbe.cs` | Ground, roof and wall queries against the world layer. |
| `ViewSource.cs` | The model-view structs both native libraries share, and `IViewSource` over them. |
| `ModelView.cs` | A native view's models in Unity. Meshes are built once; each frame takes skinned vertices and per-batch texture and colour. Uses HGStandard, plus Cloud Remap for see-through and additive parts. Also has `Shaders`. |
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
| `NativeBody.cs` | On a body moved by a native sim: RoR2's throws on the character motor (`WroteVelocity` / `TakeExternalVelocity`: jump pads, knockback, Milky Chrysalis), crowd control (`Control`: stunned, frozen or shocked, or rooted), GenericCharacterMain's sprint rule (`UpdateSprint`), the out-of-combat clock (`MarkCombat`, for Red Whip), and primary uses for Shuriken (`PulsePrimary`). Also marks the body for `ItemHooks`. |
| `ItemHooks.cs` | `ItemHooks.Install()`, then `Healed` (every heal as asked, at full health too; lifesteal carries HealOnHit/HealOnCrit) and `AmmoPack` (Bandolier). Keeps Headstompers' motor-driven stomp off `NativeBody` bodies. Each hook is found by name and skipped if missing. Needs `ROR2COMMON_HOOKS`. |
| `SurvivorAi.cs` | An AI master for a custom survivor (cloned from a vanilla one), so Goobo Jr., Vengeance and the like can spawn it. |
| `ByName.cs` | Items, buffs, types and fields by name at run time: a RoR2 without one reads as none, and newer builds' items can be named before this build has them. |
| `Portrait.cs` | A survivor's portrait (`portraitIcon`) rendered from its lobby character: `Portrait.Request(bodyPrefab, displayFactory, hide)` at content load. Tried at each scene start until the native models load; one render over black and white backgrounds for the alpha, framed on the upper body. |
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

SpawnMusic provides local stings from build-generated stereo PCM16 WAVs: spawn.wav once a run, when the character first emerges on the run's first stage (later stages and respawns stay quiet), and an optional death.wav. Initialize it with the plugin config and folder, then call PlayOnce(body) at emergence and PlayDeath(body) when the body dies. The death sting plays on its own object, so it outlasts the body and a game over. They follow master/music volume, have their own enable switches (SpawnMusic.Enabled, SpawnMusic.DeathSting) and volume, fade the excerpt edges and pause with the game. A missing clip logs a warning and leaves gameplay usable.

RunAudio resets native presentation and arrival stings at game over (before run destruction), direct quit, shutdown, and new-run startup. A game over lets a death sting finish; leaving the run stops it. Its Ended gate prevents recreation on the results screen. Run the headless event-sequence regression with dotnet run --project tests/RunAudioCheck -c Release. Native device silence still needs an in-game check.

Arrival PCM uses Windows waveOut rather than Unity AudioClip: this RoR2 build rejects AudioClip.SetData. Gain is sampled at arrival; disable, pause, body destruction and run cleanup stop or pause the owned output. The optional --device argument to RunAudioCheck exercises silent buffers on the real output device, including early stop and reopen.
