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
- **Extractor versions.** The native completeness checks only see that each file exists and parses. So each
  extraction set records the version that wrote it in `assets/<game>/extracted.txt`, one `set=version` line
  each (`SharedFiles.Current` / `MarkCurrent`).
  - A set whose recorded version differs is extracted again when the disc is there. Without the disc, the old
    files are used as they are.
  - **Bump a set's constant when its extractor writes an existing file differently.** The constants:

    | Plugin | Constant | Sets |
    |---|---|---|
    | Mario | `MainExtractorVersion` | `sunshine` `main` |
    | Mario | `LuigiExtractorVersion` | `melee` `luigi` |
    | Olimar | `MainExtractorVersion` | `pikmin` `main` |
    | Olimar | `P2ExtractorVersion` | `pikmin` `p2` |
    | Olimar | `P2Enemies.ExtractorVersion` | `p2enemies` |
    | Olimar | `TreasureExtractorVersion` | `p2treasures` |
    | Olimar | `IceClimbersExtractorVersion` | `melee` `iceclimbers` |
    | every Melee extract | `MeleeNative.ExtractorVersion` | `melee` `fighter<kind>`, used by the Melee plugin and Mario's `PlMr.dat` |

  - An install with no record extracts each set once.
  - A new file in a set needs no bump, because the completeness check already misses it.
- **Overlap.** Olimar's Ice Climbers set includes Pikachu's `PlPk.dat`, `PlPkNr.dat` and `PlPkAJ.dat`. It is
  validated with libssbm's own Pikachu validator, so the Olimar and Melee plugins agree on what a good file is.

## Unfinished content

`src/Experimental.cs` holds one switch for all three plugins:
- **Turning it on:** set `ShowUnfinished` in `BepInEx/config/Ror2Common.Experimental.cfg`, or set
  `WESLEY_DEV=1`. It's off by default.
- **Marking an item:** set `MeleeFighterDef.experimental`, or `SkinSpec.Experimental` in Mario. Delete the
  marker when the item is finished.
- **What gating does:** a marked item is not registered, offered, checked, extracted or baked.
- **Multiplayer:** each plugin calls `Experimental.ShareWithLobby()` at startup. It adds one
  `wesleyasaurus.unfinished;shown|hidden` entry to RoR2's network mod list, so the game refuses a join when the
  two settings differ. Hidden survivors are never registered, so the body catalogs would not match. The Melee
  hello message also carries the flag and warns.
- **Check:** `tests/ExperimentalCheck` (`dotnet run --project tests/ExperimentalCheck -c Release`) runs the
  switch, the lobby entry and `extracted.txt`. It also reads the sibling plugin sources to confirm three things:
  - The default Melee roster is Samus and Yoshi.
  - Every fighter registration and extraction goes through `Enabled()`.
  - Bowser Jr. is gated.

  Update its `MeleeReleased` list when a fighter is finished.
