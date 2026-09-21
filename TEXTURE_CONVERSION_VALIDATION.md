# Texture conversion validation — through v0.7.3

The public converter does not contain either converted icon package. Both are rebuilt from the verified Xbox DLC UTX source.

## ExpansionSpellIcons.utx

Xbox source:
- 11 DLC texture exports
- each texture: 128x128 BC3/DXT5
- 8 mips
- package size: 255,108 bytes

Windows target:
- each texture: 256x256 BC3/DXT5
- 9 mips
- a new 256x256 top mip is decoded/resized/recompressed from the Xbox 128x128 top mip
- all eight original Xbox mips become the lower PC mip chain unchanged
- generated package size: 976,191 bytes
- known-good private reference package size: 976,191 bytes

## ExpansionLandIcons.utx

Xbox large Glacial Vale preview:
- 512x256 BC3/DXT5, 10 mips

Windows large preview:
- 1024x512 BC3/DXT5, 11 mips
- new 1024x512 top mip generated from the Xbox top mip
- all ten Xbox mips retained unchanged below it

Xbox small Glacial Vale selector:
- 64x128 BC3/DXT5 surface
- visible selector artwork occupies a 50x80 alpha-bounded region at the top-left of the 64x128 surface; the remaining area is transparent padding

Windows small selector:
- 128x128 BC3/DXT5
- visible Xbox artwork is alpha-cropped from the 64x128 source
- artwork is scaled to a 76x121 selector footprint and placed at the top-left of a transparent 128x128 canvas
- transparent right/bottom padding preserves the Windows arena carousel's active-selection geometry
- all eight target mips are regenerated from the composed 128x128 PC image

Generated package size: 723,988 bytes.
Known-good private reference package size: 723,988 bytes.

## Verification policy

The prior 22 generated packages are verified byte-for-byte against the private known-good reference hashes. BC3 is not a canonical encoding: different valid encoders may select different color/alpha indices while decoding to equivalent artwork. The two icon packages therefore use strict semantic validation instead of requiring the historical compressor's exact bitstream:

- Unreal package structure parses successfully;
- export/import offsets are rebuilt consistently;
- lazy-array absolute pointers are valid;
- target dimensions and mip counts are exact;
- every mip has the exact BC3 payload size for its dimensions;
- final UTX package size matches the known-good Windows reference layout.
