# Engine patch validation — prototype v0.3

Validated against the actual project PC v1.4 baseline, Xbox title-update source, and private known-good Windows conversion.

## Engine.u

PC baseline:
- size: `1,554,811` bytes
- exports: `7,324`
- imports: `81`

Generated reference:
- size: `1,554,858` bytes
- exports: `7,325`
- SHA-256: `33c1c5b19fa90697961dfe03cd41bd8aa14ffbae389df2dd6054896294f5ab49`

Structural result:
- one new export: `Emitter.bFrozen` (`BoolProperty`)
- `SavedParticleScale` remains the previous Emitter field but its `UField.Next` is rewritten to the new export
- replacement `SavedParticleScale` serialized record: 15 bytes
- new `bFrozen` serialized record: 14 bytes
- inserted serialized data: 29 bytes
- export-table growth: 18 bytes total (one existing compact serial-offset encoding grows by one byte; the new export record is 17 bytes)
- total package growth: 47 bytes

Independent reconstruction result: **byte-for-byte identical** to the private reference `Engine.u`.

## Engine.dll

Input SHA-256:
`7e8927ace957f8bf77bfc40fbe097bbdfa50a470a353bac17ca5c812df666eb2`

Output SHA-256:
`85e3f34184bcdfbf3529ddbb82bf1ee9859f487e6266a894cfdb49afb6396e6d`

File size remains `2,269,184` bytes.

The patch changes 38 bytes across four regions:

- PE `SizeOfCode` adjustment at file offset `0x200`
- redirect at file offset `0xA12D2`
- 28-byte code-cave body beginning at file offset `0x15DAF0`

Disassembled behavior of the injected code:

```text
test byte ptr [ebx+0x2B0], 1   ; Emitter.bFrozen
jne  existing_early_out

test byte ptr [ebx+0x28], 0x10 ; original test displaced by redirect
jne  existing_early_out

jmp  original_code_after_test
```

Independent application of the four manifest operations produced a DLL **byte-for-byte identical** to the private reference output.
