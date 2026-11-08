# ror2-common

Shared C# integration for the **GameCube characters in Risk of Rain 2** project. This repository connects native Rust character simulations to RoR2: movement and collision, combat and items, models and effects, input, audio, UI and multiplayer.

It is used by [Mario](https://github.com/wesjones15/ror2-mario), [Olimar](https://github.com/wesjones15/ror2-olimar) and the [Melee fighters](https://github.com/wesjones15/ror2-samus). Keeping these systems together lets a fix to item handling, rendering or world collision benefit every character that uses it.

**This is a source dependency for developers.** Each character plugin compiles the shared sources into its own assembly; there is no separate `ror2-common.dll` to install. Players should start with the [character releases](https://github.com/wesjones15/ror2-characters).

## Where it fits

The project separates the original games' behavior from their RoR2 integration:

| Layer | Repositories | Responsibility |
| --- | --- | --- |
| Game simulations and presentation | [libsms](https://github.com/wesjones15/libsms), [libpikmin](https://github.com/wesjones15/libpikmin), [libssbm](https://github.com/wesjones15/libssbm) | Game-specific mechanics, asset extraction, animation and native rendering data, exposed through C APIs. |
| Shared host integration | **ror2-common** | Translate between those native systems and RoR2/Unity. |
| Character plugins | [ror2-mario](https://github.com/wesjones15/ror2-mario), [ror2-olimar](https://github.com/wesjones15/ror2-olimar), [ror2-samus](https://github.com/wesjones15/ror2-samus) | Register survivors, configure their rules and connect each simulation to the shared host systems. `ror2-samus` is the historical name for the whole Melee roster. |
| Packaging and releases | [ror2-characters](https://github.com/wesjones15/ror2-characters) | Assemble the character packages and release documentation. |

[`libcommon`](https://github.com/wesjones15/libcommon) is a different repository: it supplies shared **Rust** primitives. `ror2-common` supplies shared **C# RoR2** integration.

The `modern/1.5.0` branch targets the project's RoR2 DLC4 build, Unity 2021.3 and BepInEx 5. The `legacy/1.2.4.4` branch is frozen. These hosts register content and exchange network messages without R2API.

## What is shared

Optional-content policy is split between `ModPresence` (catalog registration),
local renderer fallback, and `OptionalContentSession` (shared gameplay availability).
The latter advertises versioned capability bits, intersects known lobby peers,
locks at run launch and refuses incompatible late joins after an explanation.
`SessionFeaturePolicy` is tested without Unity, including unknown peers and run reset.
`PrimitiveShapes` and `SurfaceQuads` own reusable, non-colliding generated visuals;
no original models, textures or sound are bundled.

| System | What it provides | Starting points |
| --- | --- | --- |
| Movement and the world | Collision probes, external forces and crowd control, jump/landing events, stage-start placement, wall protection and recovery from blocked terrain or falls. | [`NativeBody`](src/NativeBody.cs), [`WorldProbe`](src/WorldProbe.cs), [`WallGuard`](src/WallGuard.cs), [`Unstuck`](src/Unstuck.cs) |
| Combat, skills and items | Turn native hits into RoR2 damage; preserve crits, kill credit and item hooks; track skill stock/recharge; limit procs for rapid-hit attacks; support minions and AI-controlled survivors. | [`NativeHit`](src/NativeHit.cs), [`NativeSkills`](src/NativeSkills.cs), [`ItemHooks`](src/ItemHooks.cs), [`ProcLimiter`](src/ProcLimiter.cs) |
| Models, effects and UI | Draw native meshes and particles with RoR2 materials; smooth fixed-step motion; manage cameras, lobby displays, cached portraits, skill art, skins and control sheets. | [`ModelView`](src/ModelView.cs), [`ParticleView`](src/ParticleView.cs), [`StepSmoother`](src/StepSmoother.cs), [`Portrait`](src/Portrait.cs), [`SkinPresentation`](src/SkinPresentation.cs) |
| Native loading and multiplayer | Load the correct native library, check boundary layouts, send and fragment messages, and handle client spawn placement. Character plugins supply their own simulation replay and gameplay messages. | [`NativeLib`](src/NativeLib.cs), [`Mp`](src/Mp.cs), [`ClientSpawn`](src/ClientSpawn.cs) |
| Melee host framework | Common fighter driver, input, skills, defense, items, effects, AI, multiplayer, arrival and fall recovery for the Melee roster. | [`MeleeDriver`](src/MeleeDriver.cs), [Melee integration notes](MELEE.md) |
| Audio and optional VR | PCM playback, volume/pause handling and run cues; optional controller, pointer, camera and wrist-HUD integration with the VR mod. | [Audio notes](AUDIO.md), [`Vr/`](src/Vr/) |
| Local files and content availability | Find each player's discs, share extraction caches safely across plugins, and coordinate the unfinished-content setting. | [SharedFiles guide](SHARED-FILES.md) |

The native libraries own game-specific simulation. Shared host code handles the RoR2 side; individual plugins choose character-specific damage, movement and item adaptations.

## SharedFiles: the player-facing package

This repository also contains the files for **wesleyasaurus-SharedFiles**, the common disc/cache folder used by all three character packages. SharedFiles contains instructions and folders, with no plugin DLL:

```text
BepInEx/plugins/wesleyasaurus-SharedFiles/
  put_games_here/        Player's own ISO, GCM or CISO disc images
  assets/
    sunshine/           Sunshine extraction and generated caches
    pikmin/             Pikmin extraction, including optional Pikmin 2 data
    melee/              Melee extraction shared by the plugins
```

Plugins identify discs by game ID and extract the files they need locally. Per-game locks prevent simultaneous writes; extraction-version records allow caches to be refreshed when extractors change. Original game assets and generated images/audio are not distributed with the source or releases.

Current builds use this shared folder automatically. Old path config keys, environment variables and `assets_dir.txt` pointers are no longer the host setup flow. See [SHARED-FILES.md](SHARED-FILES.md) for supported discs, staging and the shared `ShowUnfinished` setting.

## Using the sources

Keep `ror2-common` beside the character repository. From a plugin project one directory below its repo root, include:

```xml
<ItemGroup>
  <Compile Include="$(MSBuildThisFileDirectory)../../ror2-common/src/**/*.cs"
           Link="Common\%(RecursiveDir)%(Filename)%(Extension)" />
</ItemGroup>
```

Set `Ror2Common.CommonLog.Log = Logger;` at the start of the plugin's `Awake`. Build through the character's project/build script, which supplies the installed game's Unity and RoR2 references.

Build scripts use [`build/BuildPaths.ps1`](build/BuildPaths.ps1) to require an explicit `-RoR2Dir` reference directory and resolve staging under workspace `target/` independently. Projects import [`build/RoR2References.targets`](build/RoR2References.targets) for the same reference requirement in direct MSBuild invocations. [`tests/BuildPathsCheck.ps1`](tests/BuildPathsCheck.ps1) checks reference selection and staging containment with synthetic files.

IL hooks are opt-in through `ROR2COMMON_HOOKS` and require BepInEx's `Mono.Cecil`, `MonoMod.Utils` and `MonoMod.RuntimeDetour` references. Optional VR sources use `ROR2COMMON_VR` and the relevant Unity XR references. The [Mario project](https://github.com/wesjones15/ror2-mario/blob/modern/1.5.0/MarioTest/MarioTest.csproj) shows a complete consumer setup.

Because these sources are compiled into several plugins, public API changes need coordinated updates to their consumers. Native view layouts must continue to agree with the corresponding Rust C ABI.

## Checks and further reading

Small console checks exercise shared behavior without launching RoR2. For example, from this repository:

```powershell
dotnet run --project tests/ModelViewCheck -c Release
dotnet run --project tests/PortraitCacheCheck -c Release
dotnet run --project tests/UnstuckCheck -c Release
```

See [`tests/`](tests/) for the remaining checks. Some use recording hosts or Unity stubs; others need local game assemblies or sibling repositories. A passing check establishes the behavior it covers, not in-game appearance or playability. Validate consuming plugin builds and manually test the affected gameplay when changing integration code.

- [Multiplayer architecture and test plan](MULTIPLAYER.md) — includes historical branch/version notes.
- [Melee host integration](MELEE.md).
- [Audio integration](AUDIO.md).
- [Material alpha handling](docs/material-alpha.md).
- [Pickup icon handling](docs/pickup-icons.md).

## License and credits

Original contributions by **wesleyasaurus** are dedicated to the public domain under
[CC0 1.0 Universal](LICENSE), to the extent wesleyasaurus owns the relevant rights.
Attribution for those contributions is appreciated but not required. Third-party software
and original game content retain their respective rights and terms.
See [CREDITS.md](CREDITS.md) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
