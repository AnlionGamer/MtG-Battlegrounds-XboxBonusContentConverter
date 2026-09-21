# v1.0.0

**MTG Battlegrounds – OG Xbox Bonus Content Converter for Windows**

This build converts the original Xbox bonus content for use with the Windows PC v1.4 release of *Magic: The Gathering - Battlegrounds*.

## Included conversion work

- Ten Xbox DLC spells and their associated gameplay content.
- DLC creatures, models, animations, textures, effects, offspring/token assets, and audio.
- All DLC spell sound effects and wizard incantations.
- Glacial Vale arena, map assets, and corrected Windows arena-selection artwork.
- Original DLC localization files for English, German, Spanish, French, and Italian.
- Windows engine adaptations required by the DLC, including the `Emitter.bFrozen` behavior used by Time Stretch.
- A Windows-specific late arena-registry sidecar that preserves the PC v1.4 arena order so Mishra's Stronghold remains available after the DLC is installed.

## Installation model

- Requires the Windows PC v1.4 baseline files used by the converter to be unmodified.
- A replacement No-CD `MTGBattlegrounds.exe` is acceptable; the converter does not patch that executable.
- Xbox DLC source can be downloaded from Digiex on user request or supplied as a local archive.
- Xbox source files are verified before conversion.
- No original game or DLC assets are bundled in the converter executable.
- No backup, rollback, or uninstall layer is provided.

## Arena-order fix

Earlier test approaches that modified `MagicMenu.u` or `MagicGame.int` were rejected after runtime crashes. The accepted solution leaves those stock PC files untouched and relocates only Glacial Vale's `LevelSummary` registry entry into late-sorting `ZZ_MtGBgBonusArena` localization sidecars. This method was validated in-game.
