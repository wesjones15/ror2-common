# The GameCube library (shared discs and caches)

`src/GameCubeLibrary.cs`, compiled into the Olimar, Mario and Melee plugins. It lets all three read
one folder that holds the user's disc images and their generated caches:

```
<library>/                    disc images, any names (told apart by the header's game ID)
<library>/isos/               also scanned, with one level of subfolders (isos/Nintendo/...)
<library>/assets/Olimar/      Olimar's cache (Pikmin 1; Pikmin 2's files in p2/)
<library>/assets/Mario/       Mario's cache (Sunshine)
<library>/assets/Melee/       Melee's cache (also read by Mario's Luigi skin and A attacks and
                              Olimar's Captain Pikachu skin)
```

## Choosing the library

First match wins:

1. The plugin's own `Paths.Library` config entry.
2. A `gamecube_library.txt` holding the folder's path, in the plugin's folder or up to three
   folders above it. One file in `BepInEx/plugins/` serves all three plugins. A relative path is
   relative to the file.
3. `$GAMECUBE_LIBRARY`.

A library that doesn't exist is logged and ignored.

## Existing layouts are unchanged

A plugin picks its cache folder as before. The library's folder only replaces the old last-resort
default:

1. `Paths.AssetsDir` in the plugin's config.
2. `assets_dir.txt` beside the plugin (what `build.ps1` stages, pointing at `libpikmin/assets`,
   `libsms/assets`, ...).
3. `$PIKMIN_ASSETS` / `$SMS_ASSETS` (Melee never read an environment variable, and still doesn't).
4. An `assets/` folder beside the plugin that already has anything in it.
5. **`<library>/assets/<Mod>`**, when a library is set: where a fresh install extracts.
6. `assets/` beside the plugin (the old default).

So a cache that already exists is never moved or orphaned. To move one into the library, move the
folder there and delete the pointer that names the old place.

The cross-plugin lookups (Olimar's Pianta and Captain Pikachu skins, Mario's Luigi skin and Melee A
attacks) try every place they did before, then the library's `assets/Mario` or `assets/Melee`.

## Disc search

Each plugin still checks its configured disc, its environment variable (`$PIKMIN_DISC`,
`$SMS_DISC`, `$SSBM_DISC`) and the folders it always searched. The library's disc folders are
added to that search. The native finders (`pik_find_disc`, `pik_p2_find_disc`, `pik_ssbm_find_disc`,
`sms_find_disc`) already pick images by game ID. Melee's own name-based search (`*Melee*`) falls
back to `GameCubeLibrary.FindDisc(root, "GALE01", 2)`, which reads the header (ISO/GCM at 0, CISO at
0x8000) and accepts revision 2 only. A folder of unrelated games is fine.
