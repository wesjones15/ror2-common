# SharedFiles: discs and extracted assets

All three plugins (Olimar, Mario, Melee) use one fixed folder, the `wesleyasaurus-SharedFiles` package.
It's a separate install that holds two folders and no code (`src/SharedFiles.cs`).

```
BepInEx/plugins/wesleyasaurus-SharedFiles/
  put_games_here/   the player's disc images (.iso/.gcm/.ciso, any names, plus one level of subfolders)
    README.txt      instructions, and the marker that finds a renamed folder
  assets/
    pikmin/         Pikmin (Pikmin 2 in pikmin/p2, where libpikmin reads it)
    sunshine/       Super Mario Sunshine
    melee/          Super Smash Bros. Melee: every plugin's Melee files
```

- **Per game, not per mod.** Whichever plugin needs a file extracts it into that game's folder, and every
  plugin reads it from there:
  - Mario extracts Luigi's Melee set through `sms.dll`, and `PlMr.dat` for the A attacks through the Melee
    plugin's `ssbm.dll`.
  - Olimar extracts the Ice Climbers and Pikachu.
  - Melee extracts its enabled fighters.

  `SharedFiles.Lock(game)` is a named mutex. It keeps two plugins from writing the same folder at once.
  Re-check completeness after taking it.
- **No path settings.** There are no pointer files, environment variables or config keys. The folder is
  found by its package name, then by the marker, and is created if it's missing.
- **Discs** are told apart by game ID. Melee still needs GALE01 revision 2 (`libssbm_assets::disc`).
- The package's own files are `sharedfiles/` here. `sharedfiles/Stage.ps1` stages them for a local deploy.
  r2modman names the folder `<team>-<package>`; a manual zip must contain that folder name itself.

## Unfinished content

`src/Experimental.cs` holds one switch for all three plugins:
- **Turning it on:** set `ShowUnfinished` in `BepInEx/config/Ror2Common.Experimental.cfg`, or set
  `WESLEY_DEV=1`. It's off by default.
- **Marking an item:** set `MeleeFighterDef.experimental`, or `SkinSpec.Experimental` in Mario. Delete the
  marker when the item is finished.
- **What gating does:** a marked item is not registered, offered, checked, extracted or baked.
- **Multiplayer:** the Melee hello message carries the flag and warns when two players differ.
