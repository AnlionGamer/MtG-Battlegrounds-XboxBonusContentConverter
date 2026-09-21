# Installer design — v1.0.0

**Product name:** MTG Battlegrounds – OG Xbox Bonus Content Converter for Windows  
**Public executable:** `MtGBgXboxBonusContentConverter.exe`

The release intentionally uses a compact, old-style one-shot patcher workflow.

## User flow

1. Launch the converter. It requests administrator rights so protected game installations can be modified.
2. Auto-detect the game from Atari's Battlegrounds uninstall product code used by the official PC v1.4 patcher. If that fails, check only a small known-location set; never crawl drives.
3. If detection fails or is wrong, the user selects the game directory with **Browse**.
4. The UI states that PC v1.4 is required. Converter input files must match clean v1.4; a replacement No-CD game executable is acceptable because it is not patched or validated.
5. Choose the Xbox DLC source. **Download automatically from Digiex** is the default. **Use my own DLC installer/archive** exposes a normal file picker.
6. User clicks **Install** and confirms.
7. Verify only the 22 PC v1.4 files actually used by the conversion.
8. Download the original Xbox DLC installer into a unique temporary workspace, or use the selected local archive. If the Digiex download fails, offer the local archive picker immediately.
9. Extract and SHA-256 verify the 63 required Xbox source files.
10. Generate the 25 adapted Windows files and stage the 14 direct-copy/rename files.
11. Validate the complete 48-file staged result.
12. Copy/overwrite those files into the selected game installation.
13. Delete the temporary workspace and show **Installation complete.**

## Deliberate omissions

There is no backup, rollback, restore point, uninstall manifest, profile system, component selector, installation-wide validation, or drive crawl. Restoring a stock game is handled by reinstalling/repairing the game and reapplying PC v1.4.

## Public packaging

The intended public binary distribution is exactly one file:

`MtGBgXboxBonusContentConverter.exe`

The JSON conversion manifests and custom application icon are embedded resources. Managed dependencies are single-file bundled, and native .NET desktop dependencies are included for runtime self-extraction by the .NET host. Users should not need companion DLLs, manifests, artwork, or other files beside the executable.


## Arena registry compatibility

The release leaves stock PC v1.4 `MagicMenu.u` and `MagicGame.int` untouched. The five Xbox `MagicExpansionPack1` localization files are adapted only by removing the Glacial Vale `LevelSummary` registration. That registration is emitted into five late-sorting `ZZ_MtGBgBonusArena` localization sidecars. This RC5-derived method was validated in-game and prevents Mishra's Stronghold from being mistaken for the stock hidden arena slot.
