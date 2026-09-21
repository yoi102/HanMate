# Local builds and release handoff

This repository is a development workspace. The current application ID and Windows
publisher are placeholders. Existing installed data must not be lost by silently
changing the application identity. Native iOS, store signing and publication need
the owner's actual environment and credentials; they are not provided here.

## Internal handoff (ADR-106, 2026-09-20)

The user authorized internal development delivery, deferring application tests,
formal content review, distribution signing and store publication. Run
`tools/build_internal_delivery.ps1 -OutputDirectory <new-directory> -Python <python-path>`
for serial Windows/Android builds and complete archive/hash verification. See
`HanMate_Planning_Pack_v3.0/delivery/README.md`. This does not install or run the app.
The seven records in `tracking/deferred-release-gates.json` remain independent
release conditions; internal work-package completion does not approve them.
The full checks below are for a later acceptance/release round, not evidence that
they ran during D8. iOS is excluded from the current scope under ADR-105.

## Reproduce the local checks

Use Windows, .NET SDK 10.0.401 and the installed .NET 10 MAUI workloads. The current
reference environment reports MAUI Windows 10.0.20, Android 36.1.69 and iOS
26.5.10315. NuGet dependencies, including their content hashes and transitive
dependencies, are recorded in each project's `packages.lock.json`.

```powershell
dotnet restore HanMate.slnx --locked-mode
dotnet test HanMate.Core.Tests/HanMate.Core.Tests.csproj -c Release --no-restore
dotnet test HanMate.Infrastructure.Tests/HanMate.Infrastructure.Tests.csproj -c Release --no-restore
dotnet build HanMate.App/HanMate.App.csproj -f net10.0-windows10.0.19041.0 -c Release --no-restore
dotnet build HanMate.App/HanMate.App.csproj -f net10.0-android -c Release -t:Rebuild --no-restore
dotnet build HanMate.App/HanMate.App.csproj -f net10.0-ios -c Release --no-restore
```

Run builds serially. Close only the app process started for testing before rebuilding
Windows. The iOS command on Windows checks managed compilation only; it does not
produce a validated iOS release. Android's development-signed APK is not a store
signing assertion. A lock file does not lock the OS, Xcode, workloads or signing key.

Inspect the final APK with the read-only audit (Python 3 and Android SDK `aapt`):

```powershell
python tools/release_preflight.py --apk HanMate.App/bin/Release/net10.0-android/com.companyname.hanmate.app-Signed.apk --aapt 'C:/Program Files (x86)/Android/android-sdk/build-tools/36.0.0/aapt.exe' --output "$env:TEMP/hanmate-release-preflight.json"
```

Exit **2** means release readiness is blocked. The report distinguishes successful
source/compiled-manifest checks from unresolved work packages and manual release
evidence. Do not convert these blockers to PASS to obtain a green exit code.

## On-device acceptance

- Preserve the existing app and database; never clear storage to make an upgrade pass.
- Backup: preview, generate, save `.hanbackup`, cancel saving, and save `.zip` fallback.
  The ZIP fallback changes only the name, not the archive contents or validation.
  Read the saved file back with strict package validation. Saving to a cloud provider
  delegates synchronization to that provider and is not proof of a completed upload.
- Content export: explicitly choose content/audio, review rights, then save `.hanpack`
  and exercise the ZIP fallback. Recorded WAV can be saved separately from its track.
- Sharing: inspect the system chooser and cancel unless delivery is authorized.
  The Android provider must expose only `cache/sharing/`, never app data or cache root.
- Speech: Settings → system voice check → explicit check → select installed Mandarin
  candidate → preview the fixed sample. Verify missing voice, completion, Stop, leaving,
  background and recording-busy paths. Airplane-mode testing and human pronunciation
  review are separate; discovery is not an offline guarantee. Reading defaults to local
  audio. Settings can enable explicit reading fallback; the reader still asks before
  sending missing targets' Chinese text to the selected system voice. Manual pinyin
  does not control that voice. No speech engine is initialized at application startup.
- Audio import: PCM16 WAV, conservative MPEG Layer III MP3 and single-track AAC-LC M4A
  are validated by bytes, then compressed inputs are decoded by the OS into PCM16 WAV.
  Input and output are each bounded at 100 MiB; encrypted, fragmented, externally
  referenced and unsupported AAC variants are rejected. Native decoder coverage is
  platform evidence, not a guarantee that every file bearing those extensions works.
- Privacy: verify all three translations against manifests and actual flows. Android
  auto-backup/cloud/device transfer are excluded. Other platforms' OS backup policies
  remain applicable. File-provider destinations and system TTS may use their own network
  policies. Android Internet permission is now explicitly used for user-requested
  voice model downloads from the pinned source; synthesis does not upload text.
- AI voices: download once, select a voice, preview supported Chinese/numbers, stop,
  leave/re-enter during synthesis, disable/re-enable and remove/re-download. Validate
  saved personal text with airplane mode enabled. Unsupported English, emoji or
  out-of-lexicon characters must fail with an explanation before native inference;
  they must not be silently skipped or written to native diagnostic logs. A damaged
  model remains removable. Models and voice choice do not migrate in app backups.

## Recovery and retained data

Use the app's explicit logical backup to preserve saved content, corrections,
favorites, settings and allowed audio/resources. It does not include unsaved drafts
or trash; the preview reports those exclusions. Backups are unencrypted.

Before complete replacement, the app creates and verifies a private SQLite/audio
safety copy, then requests a second confirmation. Recovery receipt state comes from
the database operation record, not the presence of `ready.json`. Do not delete the
`recovery` tree, old media or `.part` files as a space-saving workaround. Physical
media cleanup and the user-facing safety-copy recovery wizard were implemented in
stage B. They conservatively protect drafts, leases and safety copies; recovery
requires a readable current database and a complete fresh safety copy. On-device
destructive acceptance is still open. Use disposable databases for recovery tests. Never replace a real
database using an unreviewed shell copy procedure.

Generating a new share copy cleans recognized files older than seven days; this is
not a background seven-day deletion guarantee. External saved/shared files are outside
the application's deletion control. Logs in the UI catch paths record exception type
only, excluding original text, filenames and exception messages.

## Remaining release decisions

The owner must supply release identity, signing ownership, publisher/contact details,
regions and store age classification. Formal learning/audio/translation/license review,
native device acceptance and all 176 complete acceptance cases remain required. The
work-package count is not a certification of store or teaching readiness.

## Current scope clarification (2026-09-20)

Use `HanMate_Planning_Pack_v3.0/tracking/REMAINING_WORK.md` for the active remaining
work. The current course has 204 example entries and 224 recordings. Explicit
pinyin synthesis is connected to teaching playback and parseable dictionary
headwords; ordinary full-text AI and system TTS still synthesize Chinese text.
The offline AI path can synthesize ong, while the original recording path still
lacks its standalone recording. Signal validation is not pronunciation approval.

The current 767 content-audit subjects do not provide per-entry coverage of the
292,114-entry standalone chinese-xinhua dictionary. Its separate artifact/rights
gate and traceable review batches must be added before asserting complete shipped
content review. Local noncommercial selection does not establish redistribution
rights. Current release preflight remains blocked; do not interpret the five
blocked check groups as only five remaining actions.
