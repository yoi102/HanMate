"""Derive the offline candidate lexicon from a pinned official Unicode archive.

No network access at build or application runtime. Re-run with the audited files
in tools/resource-audit/unicode17; retain all distinct readings (no frequency guess).
"""
from pathlib import Path
import hashlib
import json
import zipfile

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'tools/resource-audit/unicode17'
OUT = ROOT / 'HanMate.Infrastructure/Pinyin'
EXPECTED = 'f7a48b2b545acfaa77b2d607ae28747404ce02baefee16396c5d2d7a8ef34b5e'
archive = (SOURCE / 'Unihan.zip').read_bytes()
assert hashlib.sha256(archive).hexdigest() == EXPECTED
readings = zipfile.ZipFile(SOURCE / 'Unihan.zip').read('Unihan_Readings.txt')
assert b'Unicode Version 17.0.0' in readings[:500]
rows = set()
for line in readings.decode('utf-8').splitlines():
    if not line or line.startswith('#'):
        continue
    code, field, value = line.split('\t')
    if field == 'kMandarin':
        candidates = value.split()
    elif field == 'kHanyuPinyin':
        candidates = [p for group in value.split() for p in group.split(':', 1)[1].split(',')]
    else:
        continue
    for candidate in candidates:
        rows.add((int(code[2:], 16), candidate))
OUT.mkdir(parents=True, exist_ok=True)
payload = ''.join(f'{chr(code)}\t{reading}\n' for code, reading in sorted(rows)).encode('utf-8')
(OUT / 'Unihan17.tsv').write_bytes(payload)
(OUT / 'UNICODE-LICENSE.txt').write_bytes((SOURCE / 'LICENSE.txt').read_bytes())
manifest = dict(version='Unicode-17.0.0-kMandarin-kHanyuPinyin-v1',
               source='https://www.unicode.org/Public/17.0.0/ucd/Unihan.zip',
               copyright='Unihan_Readings.txt: Copyright 2025 Unicode, Inc.',
               license='Unicode-3.0', archiveSha256=EXPECTED,
               readingsSha256=hashlib.sha256(readings).hexdigest(),
               outputSha256=hashlib.sha256(payload).hexdigest(),
               readings=len(rows), characters=len({c for c, _ in rows}),
               policy='All readings are unreviewed candidates. Unsupported syllables are counted by the runtime loader; never substituted.')
(OUT / 'Unihan17.source.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps(manifest, ensure_ascii=False))
