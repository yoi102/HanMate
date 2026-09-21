# Prepared handwriting templates

Run from the repository root:

```powershell
dotnet run --project tools/HanMate.HandwritingCompiler -c Release -- HanMate.App/Resources/Raw/Handwriting/medians.jsonl.gz HanMate.App/Resources/Raw/Handwriting/templates.bin
```

This derives a runtime asset from the pinned, licensed medians without network access. It preserves character identities and stroke geometry; the original medians and Arphic license remain shipped. Update the prepared size/hash in `NOTICE.json` when regenerating. The handwriting test suite compares the shipped asset byte-for-byte with the production compiler output.

Version HMI4 is little endian: magic Int32, character count Int32, followed for each character by Unicode scalar Int32, stroke count Int32, 16 XY float32 points per stroke, five float32 prefix bounds per stroke (min X/Y, width/height, maximum dimension), and eight normalized XY float32 points per stroke. Characters with up to 12 strokes additionally store two 16-point normalized joined templates per starting stroke (groups of 2 or 3; nonexistent groups are zero filled). The loader checks version, counts, unique Unicode scalars, finite geometry, nonnegative bounds, truncation and trailing bytes. It supports the app's little endian Android/Windows targets; it does not accept user-imported handwriting models.

The command reports repeated desktop model-load time/allocation and a reversed-stroke cross recognition sample. These are CPU measurements, not phone UI latency or real handwriting accuracy.
