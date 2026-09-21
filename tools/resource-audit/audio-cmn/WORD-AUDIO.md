# Headword recording index

`tools/build_word_audio.py` builds `HanMate.App/Resources/Raw/WordAudio/` independently of the pinyin course and learning catalogs. Run normally to rebuild from the checked source cache; use `--download` only to acquire missing pinned public MP3s. Python requires `soundfile` for decoding validation. No application runtime network access is added.

- Audio: `hugolpz/audio-cmn`, commit `ff9ed3d0c631195bd2c06f39450f3264c7124040`; paths and original Git blob SHA1 values in `tree.json`. New recordings preserve original MP3 bytes. Existing teaching WAVs are referenced without duplication.
- Reading metadata snapshot: `cedict-reading-source.txt`, obtained 2026-09-21 from `https://www.mdbg.net/chinese/export/cedict/cedict_1_0_ts_utf-8_mdbg.txt.gz` (decompressed SHA256 `cafb6f52f35166bef07e0a9e671ba77286b8528a3cbe4979597be0e04a5c7883`). CC-CEDICT contributors / MDBG, CC BY-SA 4.0 as declared in that snapshot. Changing the snapshot requires an explicit hash change and renewed coverage validation.
- Whole-word bindings require one unique lexicon reading with one syllable per Hanzi. Existing pinyin-course bindings take precedence. Ambiguous/unsupported words are excluded; see `word-audio-coverage.json`. Do not assign several readings to the same word recording.
- Single-character readings use the exact numbered syllable. Neutral-tone syllables are not synthesized from first-tone recordings. The existing `pa1`, `pa4`, `chi1` recording replacements remain in use.
- Source bytes are verified against the pinned Git blob, then decoded to verify duration and non-silent content. Packaged bytes are SHA256-bound. These checks do not establish correct pronunciation or listening quality.
- Audio licensing remains the source's unversioned CC BY-SA declaration; independent license and listening review remain pending. Speaker, source URL, attribution, transformations and `needsReview` status are carried in the shipped catalog and notices.

Playback order: an applicable user/imported recording, then an exact audio-cmn binding, then the user's selected existing speech provider. This applies to learning cards, dictionary headwords and saved word readers. Definitions and passages continue through their existing speech path. Complete words are never assembled from syllable recordings.
