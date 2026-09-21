"""Extract offline stroke medians from a pinned Make Me a Hanzi graphics.txt.
No network at build/runtime. Supply the source and unmodified Arphic license.
The derived data remains under the Arphic Public License, separately from code.
"""
import argparse
import gzip
import hashlib
import json
from pathlib import Path

REVISION = 'bddc96d41bef78427ed0e034e9f7e31d71fd1b92'
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('graphics', type=Path)
parser.add_argument('license', type=Path)
args = parser.parse_args()
if hashlib.sha256(args.graphics.read_bytes()).hexdigest() != 'a28c478b5178e98f67f510b2d52fde08a69dc664654ef43498253b9b764d46ee':
    raise ValueError('graphics.txt does not match the pinned revision')
target = Path(__file__).resolve().parents[1] / 'HanMate.App/Resources/Raw/Handwriting'
target.mkdir(parents=True, exist_ok=True)
notice = {
    'format': 1, 'source': 'https://github.com/skishore/makemeahanzi/tree/' + REVISION,
    'sourceSha256': hashlib.sha256(args.graphics.read_bytes()).hexdigest(),
    'copyright': 'Copyright (C) 1999 Arphic Technology Co., Ltd.; Make Me a Hanzi contributors.',
    'license': 'Arphic Public License; see ARPHICPL.txt. No warranty. Redistribution and modification permitted under that license.',
    'changes': '2026-09-19 HanMate: retain character and stroke medians only; invert Y to screen coordinates; omit SVG paths. No dictionary data included.',
}
lines = [json.dumps(notice, ensure_ascii=False, separators=(',', ':'))]
seen = set()
for line in args.graphics.read_text(encoding='utf-8').splitlines():
    source = json.loads(line)
    char = source['character']
    if len(char) != 1 or char in seen:
        raise ValueError('Invalid or duplicate character')
    seen.add(char)
    strokes = [[[x, 900-y] for x, y in stroke] for stroke in source['medians']]
    if not 1 <= len(strokes) <= 64 or any(not 2 <= len(s) <= 512 for s in strokes):
        raise ValueError('Stroke limits exceeded')
    lines.append(json.dumps({'character': char, 'strokes': strokes}, ensure_ascii=False, separators=(',', ':')))
data = ('\n'.join(lines)+'\n').encode('utf-8')
(target/'medians.jsonl.gz').write_bytes(gzip.compress(data, mtime=0))
(target/'ARPHICPL.txt').write_bytes(args.license.read_bytes())
notice['characters'] = len(seen)
notice['assetSha256'] = hashlib.sha256((target/'medians.jsonl.gz').read_bytes()).hexdigest()
(target/'NOTICE.json').write_text(json.dumps(notice, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
print(json.dumps({'characters':len(seen),'bytes':(target/'medians.jsonl.gz').stat().st_size, 'sha256':notice['assetSha256']}))
