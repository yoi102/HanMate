<p align="center"><img src="docs/assets/hanmate-icon.svg" width="112" height="112" alt="HanMate icon" /></p>

# HanMate · 汉语小伴

[简体中文](README.md) · **English** · [日本語](README.ja.md)

**[Download the 0.1.3 preview](https://github.com/yoi102/HanMate/releases/tag/v0.1.3)** · [Release notes](docs/releases/0.1.3.md) · [Privacy policy](PRIVACY.md)

Free, with no in-app purchases. Windows / Android preview builds are available on GitHub; Microsoft Store and Google Play releases are not yet available.

HanMate is an offline Chinese learning app built with **.NET 10 / .NET MAUI**. It brings together pinyin, vocabulary, reading passages, grammar, poetry, a dictionary and read-aloud playback. The interface supports Simplified Chinese, Japanese and English; Chinese learning text and pinyin remain unchanged when switching languages.

**Windows and Android** are the current development and validation targets. The project retains iOS and Mac Catalyst targets, but neither is a validated deliverable.

## Features

| Tab | What you can do |
| --- | --- |
| Pinyin | Practise initials, finals, whole syllables and tones with example characters, words and matching recordings. |
| Learn | Browse vocabulary categories and read passages, grammar and poetry with pinyin and available translations. |
| Search | Search a local Chinese dictionary, view definitions and available examples, use handwriting input and bookmark entries. |
| Favorites | Organize content into folders and open the same detail pages used in Learn. |
| Settings | Choose interface language, reading options and voices; manage resources, imports, exports and backups. |

### Learning and reading

- Vocabulary categories include everyday words, forms of address, greetings, numbers, weight, classifiers, length, seasonings, kitchen utensils and household items. Numbers follow a learning-oriented order.
- Tap a word to play it; tap again to stop. Long-press to view its existing definition and any included usage examples.
- Passage and poem titles, author information and body text support pinyin and tap-to-read playback. Full-text playback is a separate action.
- Grammar pages bring explanations, patterns and examples together.
- Returning to an unchanged favorites list preserves its position. Repeated taps or a long press on a bottom tab return to that tab's first page.

Personal content editing, pinyin corrections, recording and content sharing are supported. Content resources and favorites are managed separately; removing a favorite does not delete its text or audio.

For local-network sharing, use **More → Share** on a vocabulary, passage, grammar or poem detail page, select multiple items from a module's list menu, or select multiple vocabulary categories. The sender enters a two-digit room number (for example, 07) and opens the room. Receivers use **Receive over Wi-Fi** on the Learning home page, enter the same number and start searching; the app keeps searching and joins automatically when it finds the room. If broadcast discovery fails, enter the local IP shown on the sender and search again; if several rooms match, choose the sender. The sender selects receivers and sees each device's receive and import status. Devices need the same local network; packages are limited to 32 MiB. Vocabulary carries its source categories, and receivers choose where to place it. You may include your own recordings and preferred-track settings. Voice-pack files and synthesized audio are not sent; importing does not change the receiver's speech-engine settings. The two-digit number is only a discovery label, so verify the device before sending. Built-in content restricted from sharing cannot be exported.

Each learning list puts Add content and Select items to share in the top-right More menu. Selection mode keeps up to 100 checked items across pages; the floating share icon opens a room for those items. Sharing from a detail page still sends only that item.

### Voices

Choose a voice in **Settings → Reading voice**:

- **MeloTTS:** a bundled Chinese female voice, synthesized locally.
- **Kokoro:** bundled Chinese male and female voices, synthesized locally.
- **System speech:** voices installed on your device; availability depends on the operating system.
- **Existing recordings:** matching recordings take priority in applicable teaching content.

Offline AI speech treats decorative symbols as pause separators without changing stored or displayed text. Model loading can delay the first utterance; quality and speed depend on the device, text and selected voice.

## Get the source

Models and compressed dictionaries use **Git LFS**. Install [Git](https://git-scm.com/downloads) and [Git LFS](https://git-lfs.com/) first:

```powershell
git lfs install
git clone https://github.com/yoi102/HanMate.git
cd HanMate
git lfs pull
git lfs ls-files
```

The initial checkout downloads several hundred MB of resources. Prefer Git clone: a source ZIP downloaded from the website may contain LFS pointers instead of resource files. If a model is only a tiny text file, run `git lfs pull` before building.

## Development environment

- A Windows development machine and [.NET SDK 10.0.401](https://dotnet.microsoft.com/download/dotnet/10.0); see [global.json](global.json) for the SDK policy.
- The .NET MAUI workload, using a compatible Visual Studio installation or the .NET CLI.
- Windows SDK for Windows builds; Android SDK and JDK for Android builds. Visual Studio's MAUI development setup can install these components.
- Python is only needed for selected content-generation and audit tools, not for ordinary app builds.

```powershell
dotnet --version
dotnet workload install maui
```

NuGet versions are centralized in [Directory.Packages.props](Directory.Packages.props), with per-project lock files. Initial dependency restore requires network access. Bundled models come from Git LFS; ordinary builds do not download models separately.

## Build and test

Run from the repository root. Restore each target separately to avoid building unvalidated platforms.

### Windows

```powershell
dotnet restore HanMate.App/HanMate.App.csproj --locked-mode -p:TargetFrameworks=net10.0-windows10.0.19041.0
dotnet build HanMate.App/HanMate.App.csproj -c Release -f net10.0-windows10.0.19041.0 -p:TargetFrameworks=net10.0-windows10.0.19041.0 --no-restore
```

The Windows app is unpackaged. To debug, select `HanMate.App` and the Windows target in Visual Studio.

### Android

```powershell
dotnet restore HanMate.App/HanMate.App.csproj --locked-mode -p:TargetFrameworks=net10.0-android
dotnet build HanMate.App/HanMate.App.csproj -c Debug -f net10.0-android -p:TargetFrameworks=net10.0-android -p:EmbedAssembliesIntoApk=true --no-restore
```

The APK is generated under `HanMate.App/bin/Debug/net10.0-android/`. Bundled speech models make the package large, so allow time for the first build and installation. Debug APKs use a development signing key, not a store release key.

### Automated tests

```powershell
dotnet test HanMate.Core.Tests/HanMate.Core.Tests.csproj -p:RestoreLockedMode=true
dotnet test HanMate.Infrastructure.Tests/HanMate.Infrastructure.Tests.csproj -p:RestoreLockedMode=true
```

Tests cover core rules, pinyin, speech input processing, favorites, resources and data recovery. They do not replace device UI, microphone, pronunciation listening or accessibility checks.

## Repository layout

| Path | Purpose |
| --- | --- |
| [HanMate.App](HanMate.App) | MAUI pages, interactions, platform integration and audio services. |
| [HanMate.Core](HanMate.Core) | Content models, pinyin, reading, audio and domain rules. |
| [HanMate.Infrastructure](HanMate.Infrastructure) | SQLite, dictionaries, resources, favorites, import/export and recovery. |
| [HanMate.Core.Tests](HanMate.Core.Tests) | Core logic tests. |
| [HanMate.Infrastructure.Tests](HanMate.Infrastructure.Tests) | Persistence, resources and recovery tests. |
| [tools](tools/README.md) | Content generation, resource auditing and development verification. |
| [Planning pack](HanMate_Planning_Pack_v3.0/README.md) | Product specifications, architecture, status, evidence and delivery documentation, primarily in Chinese. |

## Data and privacy

Text, favorites, recordings and settings stay on the device. Local AI synthesis does not upload text. Optional voice downloads contact designated model sources only after a user action. System speech behavior depends on the device and its speech service.

Shared files and backups can contain personal text or recordings. Do not commit personal databases, backups, recordings, credentials or signing keys. Build output and local verification reports belong in ignored directories such as `artifacts/`.

## Status and documentation

As of 2026-09-26, the main Windows and Android features are implemented, and [v0.1.3](https://github.com/yoi102/HanMate/releases/tag/v0.1.3) is a public GitHub preview. The internal work ledger remains at **45/48**: reference-device performance and accessibility, complete P0 regression, and cross-platform resource/sharing/migration checks remain open. A suitable Melo Chinese male voice, final teaching and audio review, third-party redistribution rights, production signing, and Store releases also remain open. iOS is deferred. A GitHub preview is not full product acceptance or a Store release.

- [Development status](HanMate_Planning_Pack_v3.0/tracking/STATUS.md)
- [Handoff](HanMate_Planning_Pack_v3.0/tracking/HANDOFF.md)
- [Remaining work](HanMate_Planning_Pack_v3.0/tracking/REMAINING_WORK.md)
- [Internal delivery](HanMate_Planning_Pack_v3.0/delivery/README.md)
- [User guide and FAQ](HanMate_Planning_Pack_v3.0/docs/24_User_Guide_and_FAQ.md)

These documents include historical records: check dates and validation scope. Teaching-content review, pronunciation listening, redistribution rights, production signing and store publication still have separate outstanding requirements.

## Licenses and third-party resources

HanMate's original source code and documentation are licensed under the [MIT License](LICENSE). Third-party code, dictionaries, recordings, models, fonts and derived data retain their respective licenses and attribution requirements; the MIT license does not grant redistribution rights to assets with unresolved provenance. See the [third-party license review](docs/THIRD_PARTY_LICENSES.md).

- [Speech models and runtime libraries](HanMate.App/Resources/Raw/Voices/NOTICE.txt)
- [Pinyin recordings](HanMate.App/Resources/Raw/Pinyin/NOTICE.txt)
- [Word recordings](HanMate.App/Resources/Raw/WordAudio/NOTICE.txt)
- [Dictionary sources](HanMate.Infrastructure/Dictionary/NOTICE.json)
- [Default dictionary and review status](HanMate.Infrastructure/Dictionary/XINHUA-NOTICE.json)
- [Handwriting resources](HanMate.App/Resources/Raw/Handwriting/NOTICE.json)

Upstream licenses are kept with the resources. Integration and automated checks do not establish final content, pronunciation or redistribution approval.
