# MTG Battlegrounds – OG Xbox Bonus Content Converter for Windows

Unofficial fan-made converter for bringing the original Xbox-only bonus content to the Windows PC v1.4 release of *Magic: The Gathering - Battlegrounds*.

## Requirements

- Windows x64.
- A clean PC v1.4 installation for the files used by the converter.
- A replacement No-CD `MTGBattlegrounds.exe` is acceptable because the converter does not patch that executable.
- Original Xbox DLC source, either downloaded by the converter from Digiex after the user chooses that option or supplied as a local archive.

## What it converts

The converter produces the DLC spells, creatures, audio, Glacial Vale arena, textures, localization, and the Windows engine adaptations required by the original Xbox bonus content.

It also preserves the stock PC v1.4 arena ordering. Glacial Vale's registry entry is written to late-sorting `ZZ_MtGBgBonusArena` localization sidecars so adding the DLC does not cause the PC menu to mistake Mishra's Stronghold for its hidden SecretLevel slot.

The final staged conversion contains 48 files: 14 direct Xbox files and 34 generated/adapted files. Stock `MagicMenu.u` and `MagicGame.int` are not modified or installed.

## Build

Run `Build_Release.bat` with the .NET 8 SDK installed. A successful publish produces exactly one file:

`Release\MtGBgXboxBonusContentConverter.exe`

The build script prints the EXE size and SHA-256.

## Status

The core DLC conversion, both Browse dialogs, automatic Digiex acquisition, user-supplied archive acquisition, Glacial Vale selector artwork, and the late arena-registry compatibility fix have been runtime-tested on Windows.

This is an unofficial project and is not affiliated with Wizards of the Coast, Atari, Microsoft, or Digiex. No game or DLC files are bundled with the converter.
