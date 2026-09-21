# Arena registration compatibility validation

## Problem

The stock Windows PC v1.4 arena menu assigns arena coordinates from the order returned by the Unreal localization registry. It treats coordinate `(3,1)` as the hidden `SecretLevel`.

The original Xbox DLC registers `MajorArenaBonus1` (Glacial Vale) in `MagicExpansionPack1.<language>` before the stock `MagicGame` arena entries are enumerated on Windows. That shifts the stock arenas by one slot and causes Mishra's Stronghold to occupy the coordinate the PC menu treats as `SecretLevel`. The map remains hidden even after Mishra is defeated because the unlock data is not the failing component; the menu is filtering the shifted registry slot.

## Rejected approaches

- Patching the compiled Windows `MagicMenu.u` to imitate the Xbox title-update behavior caused an early game crash.
- Moving the DLC registration into `MagicGame.int` caused the game's `Invalid system data. Please reinstall.` integrity path.

Neither approach is used by the converter.

## Validated solution

The tested RC5 solution leaves all stock PC v1.4 files untouched.

For each supported localization (`.int`, `.det`, `.est`, `.frt`, `.itt`):

1. Copy the verified Xbox `MagicExpansionPack1` localization while removing only its `MajorArenaBonus1` / `Engine.LevelSummary` registration line.
2. Generate `ZZ_MtGBgBonusArena.<language>` containing that same Glacial Vale registration.
3. The `ZZ_` filename sorts the DLC arena registration after the stock arena registry entries on the tested Windows build.

This restores the stock ordering used by the PC menu while retaining Glacial Vale.

## Runtime result

RC5 was tested in-game on Windows and confirmed working after the prior failure cases. Mishra's Stronghold and Glacial Vale are both available as intended, with the stock hidden arena behavior preserved.

The final converter therefore does not modify or install `MagicMenu.u` or `MagicGame.int`.
