# MagicExpansionPack1.u conversion validation

The v0.4 expansion-package transform was independently executed against the verified Xbox DLC source package and compared with the private known-good Windows conversion.

## Verified source

- `content/$c/4947000e00000002/MagicExpansionPack1.u`
- Size: `122490`
- SHA-256: `2016b4afb78b56c96968df7282a3c357851db270b89df9ff36f88c045ea503cd`

## Verified output

- `SYSTEM/MagicExpansionPack1.u`
- Size: `122771`
- SHA-256: `9c2422d7a1485add553b7c1d3f5dfea6816d84d32acfc0374f898b446981e7b1`
- Result: byte-for-byte identical to the private reference conversion.

## Exact transformation

The Xbox package contains 747 names, 430 exports, and 295 imports. The Windows reference retains all 430 exports and 295 imports and performs only these logical adaptations:

1. Rename name-table/import package `MagicSoundsX` to `MagicSounds` so the DLC creature/spell sound imports resolve against the augmented normal PC `MagicSounds.uax`.
2. Append the name-table entries `DisableFogging` and `FooName` using the package's ordinary name flags. `DisableFogging` is retained solely because the historical reference package's editor ScriptText contains that placeholder text; compiled TimeWarp bytecode still uses `bFrozen`.
3. Clear `DownloadableContentExtension="X"` on 11 classes:
   - `SummonLivingHiveToken`
   - `TimeWarp`
   - `PlagueWind`
   - `SummonTidalKraken`
   - `SummonTephraderm`
   - `SummonSerraAvatar`
   - `SummonReiverDemon`
   - `SummonLivingHive`
   - `Biorhythm`
   - `Insurrection`
   - `BlessedWind`
4. Add the PC `FooName` string default to the 10 player-facing spells:
   - `TimeWarp` -> `TimeStretch`
   - `PlagueWind` -> `PlagueWind`
   - `SummonTidalKraken` -> `TidalKraken`
   - `SummonTephraderm` -> `Tephraderm`
   - `SummonSerraAvatar` -> `SerraAvatar`
   - `SummonReiverDemon` -> `ReiverDemon`
   - `SummonLivingHive` -> `LivingHive`
   - `Biorhythm` -> `Biorhythm`
   - `Insurrection` -> `Insurrection`
   - `BlessedWind` -> `BlessedWind`
5. Reproduce the known-good TimeWarp `ScriptText` metadata edits. This changes only the editor/source-text object. No compiled function export is changed.
6. Rebuild the name table, serialized object offsets, import offset, export offset, export sizes/offsets, and generation name count.

## Time Stretch behavior

The compiled Xbox TimeWarp bytecode is preserved unchanged and continues to reference `Engine.Emitter.bFrozen`. Runtime freezing is therefore provided by the separate deterministic `Engine.u` + `Engine.dll` patch from v0.3. The `DisableFogging` wording in `ScriptText` is historical source/editor metadata and is not the code executed by the game.
