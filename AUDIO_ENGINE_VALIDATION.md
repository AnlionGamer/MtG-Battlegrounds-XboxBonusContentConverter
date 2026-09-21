# Audio engine validation

Reference comparison performed against the private known-good DLC conversion baseline.

Result: **19/19 rebuilt audio packages matched byte-for-byte.**

The proof reconstruction used only:

- the verified stock PC v1.4 UAX package;
- the Xbox `SoundBankMEP1.xsb` name table;
- the matching Xbox version-3 XWB wave bank;
- local Xbox ADPCM decoding;
- deterministic Unreal package rebuilding.

No converted WAV/UAX payloads are embedded in the converter source.

| Output | Size | Reference SHA-256 |
|---|---:|---|
| Sounds/IncantsAkroma.uax | 11609848 | edeb724e53c7878e776870f6e4c8f76e4d29177cdbe3e1f5618b3d41facc15c8 |
| Sounds/IncantsAlberon.uax | 10559026 | 55320b87d087d40f52de3c1fd0284ab20640e5971e02a9a2ca0dc5668a117efa |
| Sounds/IncantsAngus.uax | 10532735 | cae9fc50fa94e5950bd5f0dd542dac71de4c18d44dc10dded61d2c845eba01c2 |
| Sounds/IncantsArcanis.uax | 13155732 | 598c0c4ac0fc69cf6c2f9f966bdcda50269062a5058b9a9d671906558152709e |
| Sounds/IncantsEntorin.uax | 10378262 | 0a3dc8c357921d04cb632a7e45693bfdfbf743cae0e34af6cf18a36d8f1a9043 |
| Sounds/IncantsEvalisa.uax | 10985286 | a00723f1c085a566f0420bf75d66f56633f39e5611e75229c8ac1d5229f3afb4 |
| Sounds/IncantsIhsan.uax | 21010287 | 112441f1a899ff8d208a5541f2f67a3ca9eed8d23a796037f7c3145fa5d75070 |
| Sounds/IncantsJoe.uax | 11507741 | 29f75a6d732f4a5bae6c1ec25871c79156552e080075d753bcbbf82769816fd6 |
| Sounds/IncantsKeroc.uax | 11101721 | b3f937f2c1c21ac9ee5bc9fad191e760bf4b2a199f63981e7624a1ccd31f115e |
| Sounds/IncantsMaraxus.uax | 12353598 | 522f0bc5c2b0622644702175a31973829f40193e187c30eec74842e340e39bf4 |
| Sounds/IncantsMidia.uax | 10892294 | ac2668aa49ec0263a3f5d8e7f774a986cb273da91d57de3109db7f8c320ae1c6 |
| Sounds/IncantsMishra.uax | 19174750 | 10b544cd376fd0370d212105f019d8a6d07b5a96308ed8f730b48da41078ad32 |
| Sounds/IncantsMultani.uax | 18094149 | 106199f4aa4bce99e4c0ea68b9bb79d5a84fe9ee161a2bc88ee4033ed0d4edbe |
| Sounds/IncantsTenera.uax | 10049804 | 78c7a4a5a9cf6b3d362c1da4d492ee6efe70cae3afa7e2014110fb049b9abc9a |
| Sounds/IncantsTsabo.uax | 19231337 | 1f3671a00c6f0c6083e032a7bfab794caedd3b4bfca21ace729c10ac8d0ef32c |
| Sounds/IncantsVilanti.uax | 10185760 | 1927addb36011cee849c65ba283e5cafbf505922369f7484ea461fd724f7cc4e |
| Sounds/IncantsVolita.uax | 10967463 | ccfdda19bd10b388a43a326b8d9e984a5bc07aa7d7fb6b580c088852ff90e4c2 |
| Sounds/IncantsZayel.uax | 10769420 | 93649d61da09dbd55a92089d3575eaa204b0ff34d208031250bc45335d4d17a7 |
| Sounds/MagicSounds.uax | 85916856 | d8d0ebd95a492e2b86c12e2b9ca430db803af12c2d27ce925ad1db379bc20670 |
