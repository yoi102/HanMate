"""Build offline audio-cmn headword recordings; --download fetches pinned source blobs.

Whole words require one unambiguous CC-CEDICT reading, or an existing teaching
recording binding. This is a lexicon match, NOT an independent listening review.
Never manufacture neutral tones, concatenate syllables, or guess polyphones.
"""
import argparse
import collections
import concurrent.futures
import hashlib
import json
from pathlib import Path
import re
import shutil
import urllib.parse
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
AUDIT = ROOT / 'tools/resource-audit/audio-cmn'
OUT = ROOT / 'HanMate.App/Resources/Raw/WordAudio'
REV = 'ff9ed3d0c631195bd2c06f39450f3264c7124040'
LEXICON_SHA = 'cafb6f52f35166bef07e0a9e671ba77286b8528a3cbe4979597be0e04a5c7883'
LICENSE = f'https://github.com/hugolpz/audio-cmn/blob/{REV}/README.md'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--download', action='store_true')
    args = parser.parse_args()
    OUT.mkdir(parents=True, exist_ok=True)
    lexicon = (AUDIT / 'cedict-reading-source.txt').read_bytes()
    assert hashlib.sha256(lexicon).hexdigest() == LEXICON_SHA
    readings = collections.defaultdict(set)
    for line in lexicon.decode('utf-8-sig').splitlines():
        match = re.match(r'^(\S+) (\S+) \[([^]]+)\]', line)
        if match:
            reading = match[3].lower().replace('u:', 'ü').replace('5', '0')
            for text in match[1], match[2]:
                readings[text].add(reading)
    tree = json.loads((AUDIT / 'tree.json').read_text(encoding='utf-8-sig'))
    nodes = {n['path']: n for n in tree['tree'] if n['type'] == 'blob'}
    course = json.loads((ROOT / 'HanMate.App/Resources/Raw/Pinyin/course.json').read_text(encoding='utf-8'))
    existing = {}
    for asset in course['assets']:
        if 'audio-cmn' not in asset['sourceUrl']:
            continue
        if asset.get('text') and asset.get('pinyin'):
            existing[(asset['text'], asset['pinyin'])] = asset
        elif re.fullmatch('[a-zü]+[1-4]', asset['key']):
            existing[(None, asset['key'])] = dict(asset, pinyin=asset['key'])
    # Retain the independently sourced replacements already used in the course.
    for reading in ('pa1', 'pa4', 'chi1'):
        asset = next(a for a in course['assets'] if a['key'] == reading)
        existing[(None, reading)] = dict(asset, text=None, pinyin=reading)
    tasks = []
    skipped = []
    for path, node in sorted(nodes.items()):
        if path.startswith('64k/syllabs/cmn-') and path.endswith('.mp3'):
            pinyin = Path(path).stem.removeprefix('cmn-').replace('v', 'ü')
            if not re.fullmatch('[a-zü]+[1-4]', pinyin):
                continue
            identity = (None, pinyin)
        elif path.startswith('64k/hsk/cmn-') and path.endswith('.mp3'):
            text = Path(path).stem.removeprefix('cmn-')
            if len(text) < 2 or not all('\u3400' <= c <= '\u9fff' for c in text):
                continue
            if any(bound_text == text for bound_text, _ in existing):
                continue  # One recording must not acquire a second reading from another lexicon.
            candidates = readings[text]
            if len(candidates) != 1:
                skipped.append({'text': text, 'reason': 'missing-or-ambiguous-lexicon-reading', 'readings': sorted(candidates)})
                continue
            pinyin = next(iter(candidates))
            if len(pinyin.split()) != len(text) or not re.fullmatch(r'[a-zü]+[0-4]( [a-zü]+[0-4])*', pinyin):
                skipped.append({'text': text, 'reason': 'unsupported-reading', 'readings': [pinyin]})
                continue
            identity = (text, pinyin)
        else:
            continue
        if identity not in existing:
            tasks.append((path, node, identity))

    def acquire(task):
        import soundfile
        path, node, (text, pinyin) = task
        cache = AUDIT / 'sources' / Path(path).name
        url = f'https://raw.githubusercontent.com/hugolpz/audio-cmn/{REV}/' + urllib.parse.quote(path)
        if not cache.exists():
            if not args.download:
                raise FileNotFoundError('Run --download to acquire ' + path)
            with urllib.request.urlopen(urllib.request.Request(url, headers={'User-Agent': 'HanMate-source-review'}), timeout=40) as response:
                data = response.read(2 * 1024 * 1024)
            assert hashlib.sha1(b'blob ' + str(len(data)).encode() + b'\0' + data).hexdigest() == node['sha'], path
            cache.write_bytes(data)
        data = cache.read_bytes()
        assert hashlib.sha1(b'blob ' + str(len(data)).encode() + b'\0' + data).hexdigest() == node['sha'], path
        samples, rate = soundfile.read(cache)
        duration = len(samples) / rate
        assert .1 < duration < 15 and abs(samples).max() > .001, path
        key = 'cmn-' + node['sha']
        target = OUT / (key + '.mp3')
        if not target.exists() or target.read_bytes() != data:
            shutil.copyfile(cache, target)
        return {'key': key, 'file': 'WordAudio/' + target.name, 'sha256': hashlib.sha256(data).hexdigest(),
                'originalSha256': hashlib.sha256(data).hexdigest(), 'durationSeconds': round(duration, 4),
                'sourceUrl': f'https://github.com/hugolpz/audio-cmn/blob/{REV}/' + urllib.parse.quote(path),
                'downloadUrl': url, 'author': ('Chen Wang' if text is None else 'Yue Tan; SWAC') + '; audio-cmn / Hugo Lopez',
                'license': 'CC BY-SA (audio source does not specify version)', 'licenseUrl': LICENSE,
                'description': 'Original audio-cmn recording: ' + Path(path).name,
                'changes': 'Original 64 kbps MP3 bytes; no concatenation or phonetic edits.',
                'reviewStatus': 'needsReview', 'text': text, 'pinyin': pinyin}

    assets = list(existing.values())
    print(f'Reusing {len(assets)} bindings; acquiring {len(tasks)} recordings', flush=True)
    # Fixed low concurrency; provider failures stop this run, with no retry/rate-limit bypass.
    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
        for index, asset in enumerate(pool.map(acquire, tasks), 1):
            assets.append(asset)
            if index % 250 == 0:
                print(f'{index}/{len(tasks)} verified', flush=True)
    # An exact-character entry and the syllable replacement can share one file/key.
    assets.sort(key=lambda a: (a.get('text') or '', a['pinyin']))
    referenced = {a['file'] for a in assets}
    for generated in OUT.glob('cmn-*.mp3'):
        if re.fullmatch(r'cmn-[0-9a-f]{40}\.mp3', generated.name) and 'WordAudio/' + generated.name not in referenced:
            assert generated.resolve().parent == OUT.resolve()
            generated.unlink()
    (OUT / 'catalog.json').write_text(json.dumps({'version': '2026.09.21.1', 'assets': assets}, ensure_ascii=False, indent=2), encoding='utf-8')
    pairs = {(a.get('text'), a['pinyin']) for a in assets}
    coverage = []
    with zipfile.ZipFile(ROOT / 'HanMate.Infrastructure/Catalog/learning.zip') as z:
        documents = json.loads(z.read('contents.json'))['contents']
    for document in documents:
        if document['kind'] != 'word':
            continue
        for unit in document['textUnits']:
            if unit['role'] != 'headword':
                continue
            pinyin = ' '.join(t['pinyin']['base'] + str(t['pinyin']['tone']) for t in unit['tokens'] if t.get('pinyin'))
            match = (unit['text'], pinyin) in pairs or (len(unit['text']) == 1 and (None, pinyin) in pairs)
            coverage.append({'text': unit['text'], 'pinyin': pinyin, 'recording': match})
    report = {'version': '2026.09.21.1', 'revision': REV, 'lexiconSha256': LEXICON_SHA,
              'syllableBindings': sum(a.get('text') is None for a in assets),
              'wholeWordBindings': sum(len(a.get('text') or '') > 1 for a in assets),
              'uniqueFiles': len({a['file'] for a in assets}), 'learning': coverage,
              'skipped': skipped, 'listeningReview': 'NOT RUN', 'licenseReview': 'pending'}
    (AUDIT / 'word-audio-coverage.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    notice = ['HanMate audio-cmn headword recordings',
              'Speakers: Chen Wang (syllables); Yue Tan / SWAC (words). Curation: Hugo Lopez / audio-cmn.',
              'Audio license: CC BY-SA; the pinned source does not specify a version. ' + LICENSE,
              'New MP3 assets preserve the original bytes. Reused Pinyin WAV conversions are detailed in Pinyin/NOTICE.txt.',
              'Reading index: CC-CEDICT, MDBG and contributors, CC BY-SA 4.0.',
              'https://www.mdbg.net/chinese/dictionary?page=cedict | https://creativecommons.org/licenses/by-sa/4.0/',
              'Reading source SHA256: ' + LEXICON_SHA,
              'Words with multiple lexicon readings are excluded unless already bound in the teaching course.',
              'Matching is based on lexicon metadata; independent listening/pronunciation and license review remain pending.',
              'Per-recording source, hash, speaker and changes: catalog.json. No speaker endorsement is implied.']
    (OUT / 'NOTICE.txt').write_text('\n'.join(notice) + '\n', encoding='utf-8')
    print(json.dumps({k: v for k, v in report.items() if k not in ('learning', 'skipped')}, ensure_ascii=False), flush=True)
    print(f'Learning coverage: {sum(x["recording"] for x in coverage)}/{len(coverage)}', flush=True)


if __name__ == '__main__':
    main()
