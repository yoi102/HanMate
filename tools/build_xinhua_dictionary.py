"""Build the user-selected local chinese-xinhua dictionary; never opens user data.

Build-only dependencies: pypinyin==0.55.0, opencc-python-reimplemented==0.1.7,
jieba==0.42.1 (word frequencies only; no runtime segmenter).
Source files must match the pinned upstream commit. Automatic pinyin is displayed
as unreviewed and is deliberately excluded from the confirmed-reading search index.
"""
import argparse
import gzip
import hashlib
import json
import re
import sqlite3
import uuid
from collections import Counter
from importlib.metadata import distribution
from pathlib import Path

from build_default_dictionary import pinyin_key

REVISION = 'fe6d6c2e8baa82187f4c96bbe042e43f96c05666'
HASHES = {
    'word': '8ae3453eacc5b0f3fdfba47eac8bb686cd73914d278e8b851ae6ef81082f80e7',
    'ci': '739e086d61dc9b95cd1df92095720e6391f952a41c476d3281ef95ebed869a09',
    'idiom': '1d4b4f454ce1c416d6a1ab2369d6e66c0ff99e04390172eef70790499e21ce19',
}
NAMESPACE = uuid.UUID('428dbdc4-7039-4ec2-93bd-45a6e5b37eee')


def normalize_pinyin(value):
    return re.sub(r'\s+', ' ', value.lower().replace('ɡ', 'g').replace('ɑ', 'a').replace('\u3000', ' ')).strip()


def build(source, output):
    import pypinyin
    from opencc import OpenCC
    if pypinyin.__version__ != '0.55.0':
        raise ValueError('Use the pinned pypinyin version')
    simplify = OpenCC('t2s')
    traditional = OpenCC('s2t')
    frequency_path = distribution('jieba').locate_file('jieba/dict.txt')
    frequency_bytes = frequency_path.read_bytes()
    frequency_hash = hashlib.sha256(frequency_bytes).hexdigest()
    if frequency_hash != '7197c3211ddd98962b036cdf40324d1ea2bfaa12bd028e68faa70111a88e12a8':
        raise ValueError('Unexpected frequency dictionary')
    frequencies = {parts[0]: int(parts[1]) for line in frequency_bytes.decode('utf-8').splitlines()
                   if len(parts := line.split()) >= 2}
    output.mkdir(parents=True, exist_ok=True)
    db = output / 'xinhua.build.sqlite'
    if db.exists():
        raise FileExistsError(db)
    groups = {}
    counts = Counter()
    with sqlite3.connect(db) as connection:
        connection.executescript('''
        PRAGMA page_size=4096;
        CREATE TABLE entry(id TEXT PRIMARY KEY,title TEXT NOT NULL,simple TEXT NOT NULL,
          pinyin TEXT NOT NULL,joined TEXT NOT NULL,separated TEXT NOT NULL,tones TEXT NOT NULL,
          sense_order INTEGER NOT NULL,raw_json TEXT NOT NULL);
        CREATE TABLE character_index(character TEXT NOT NULL,id TEXT NOT NULL,PRIMARY KEY(character,id)) WITHOUT ROWID;
        CREATE TABLE source_record(kind TEXT NOT NULL,ordinal INTEGER NOT NULL,raw_json TEXT NOT NULL,
          PRIMARY KEY(kind,ordinal)) WITHOUT ROWID;
        ''')
        for kind, digest in HASHES.items():
            payload = (source / (kind + '.json')).read_bytes()
            if hashlib.sha256(payload).hexdigest() != digest:
                raise ValueError('Source hash mismatch: ' + kind)
            for ordinal, row in enumerate(json.loads(payload)):
                counts['source_' + kind] += 1
                connection.execute('INSERT INTO source_record VALUES(?,?,?)',
                                   (kind, ordinal, json.dumps(row, ensure_ascii=False, separators=(',', ':'))))
                title = simplify.convert(row.get('word', row.get('ci', '')).strip())
                definition = simplify.convert(row.get('explanation', '').strip())
                if not title or len(title) > 120 or definition in ('', '无', '暂无', '暂无解释'):
                    counts['excluded_empty_or_invalid'] += 1
                    continue
                item = groups.setdefault(title, dict(Title=title, Pinyin='', PinyinIsAutomatic=False,
                    Definitions=[], Examples=[], Notes=[], SourceRecords=[]))
                if definition not in item['Definitions']:
                    item['Definitions'].append(definition)
                item['SourceRecords'].append([kind, ordinal])
                reading = normalize_pinyin(row.get('pinyin', ''))
                if not item['Pinyin'] and len(reading.split()) == len(title) and pinyin_key(reading)[0]:
                    item['Pinyin'] = reading
                for field, label in [('radicals', '部首'), ('strokes', '笔画'), ('derivation', '出处')]:
                    value = simplify.convert(row.get(field, '').strip())
                    if value and value != '无' and label + '：' + value not in item['Notes']:
                        item['Notes'].append(label + '：' + value)
                example = simplify.convert(row.get('example', '').strip())
                if example and example != '无' and example not in item['Examples']:
                    item['Examples'].append(example)
        digest = hashlib.sha256()
        for title, item in sorted(groups.items()):
            if not item['Pinyin']:
                reading = normalize_pinyin(' '.join(pypinyin.lazy_pinyin(title, style=pypinyin.Style.TONE,
                    neutral_tone_with_five=False, errors=lambda chars: ['?'] * len(chars))))
                if len(reading.split()) == len(title) and pinyin_key(reading)[0]:
                    item['Pinyin'] = reading
                    item['PinyinIsAutomatic'] = True
            counts['automatic_pinyin' if item['PinyinIsAutomatic'] else 'source_pinyin' if item['Pinyin'] else 'missing_pinyin'] += 1
            joined, separated, tones = ('', '', '') if item['PinyinIsAutomatic'] else pinyin_key(item['Pinyin'])
            identity = str(uuid.uuid5(NAMESPACE, title))
            raw = json.dumps(item, ensure_ascii=False, separators=(',', ':'))
            digest.update((raw + '\n').encode())
            alias = traditional.convert(title)
            connection.execute('INSERT INTO entry VALUES(?,?,?,?,?,?,?,?,?)',
                (identity, title, alias, item['Pinyin'], joined, separated, tones,
                 1_000_000 - min(frequencies.get(title, 0), 999_999), raw))
            connection.executemany('INSERT INTO character_index VALUES(?,?)',
                                   ((ch, identity) for ch in sorted(set(title + alias))))
            counts['maximum_document_characters'] = max(counts['maximum_document_characters'],
                len(title) + sum(len(x) for field in ['Definitions', 'Examples', 'Notes'] for x in item[field]))
        connection.executescript('CREATE INDEX entry_title ON entry(title); CREATE INDEX entry_pinyin ON entry(joined);')
        connection.commit()
        connection.execute('VACUUM')
        if connection.execute('PRAGMA integrity_check').fetchone()[0] != 'ok':
            raise ValueError('SQLite integrity failed')
    connection.close()
    if counts['maximum_document_characters'] > 19000:
        raise ValueError('Merged entry exceeds reading document limit')
    payload = db.read_bytes()
    compressed = gzip.compress(payload, mtime=0)
    (output / 'xinhua.sqlite.gz').write_bytes(compressed)
    info = dict(name='简体汉语字典（chinese-xinhua）', version=REVISION[:12], entries=len(groups),
        source='https://github.com/pwxcoo/chinese-xinhua', revision=REVISION, sourceHashes=HASHES,
        license='NOASSERTION', usage='User-selected local use; upstream content rights are not verified.',
        transformations='Simplified display; merged duplicate headwords; normalized IPA-shaped g/a; original records preserved.',
        pinyin='Original readings indexed; pypinyin 0.55.0 automatic supplements are unreviewed and not indexed.',
        ranking='jieba 0.42.1 word frequency; exact/prefix match tiers take precedence',
        frequencySha256=frequency_hash,
        statistics=dict(counts), contentSha256=digest.hexdigest(),
        sqliteSha256=hashlib.sha256(payload).hexdigest(), gzipSha256=hashlib.sha256(compressed).hexdigest())
    (output / 'XINHUA-NOTICE.json').write_text(json.dumps(info, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    (output / 'XINHUA-README.txt').write_bytes((source / 'README.md').read_bytes())
    (output / 'JIEBA-LICENSE.txt').write_bytes((source / 'JIEBA-LICENSE.txt').read_bytes())
    license_files = distribution('pypinyin').files
    license_path = next(p for p in license_files if str(p).endswith('/LICENSE.txt'))
    (output / 'PYPINYIN-LICENSE.txt').write_text(license_path.locate().read_text(encoding='utf-8'), encoding='utf-8')
    db.unlink()  # Exact disposable build artifact only.
    print(json.dumps(info, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    build(args.source, args.output)
