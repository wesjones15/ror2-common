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
| `DisplayModel.cs` | The character select screen: on a survivor's display prefab, draws an `IDisplayActor` (the plugin's own view, posed by its own sim) at the prefab's root and hides the prefab's model. `DisplayModel.Attach(displayPrefab, id, factory)`. |

The native views are `sms_view_*` in libsms-ffi and `pik_view_*` in libpikmin-ffi. Their
structs match field for field, so a plugin only wraps its own functions in an
`IViewSource`.

`ModelView` came from ror2-olimar's `ModelView.cs` without its particles: Pikmin's whistle
and ghosts still live in ror2-olimar.
