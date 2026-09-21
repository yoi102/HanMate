"""Exact-text-bound, AI-assisted definition readings; never infer formal review approval."""
from pathlib import Path
import re

SOURCE = Path(__file__).resolve().parent / 'content/pinyin-definition-readings.tsv'


def load_readings(path=SOURCE):
    records = {}
    for line in path.read_text(encoding='utf-8').splitlines():
        if not line or line.startswith('#'):
            continue
        key, text, reading = line.split(';')
        syllables = reading.split()
        if key in records or not syllables or any(not re.fullmatch(r'[a-züv]+[0-4]', s) for s in syllables):
            raise ValueError('Invalid or duplicate definition reading: ' + key)
        records[key] = (text, syllables)
    return records


def annotate(course, records=None):
    from build_pinyin_course import tone
    records = load_readings() if records is None else records
    used = set()
    for doc in course['contents']:
        head = doc['textUnits'][0]
        p = head['tokens'][0]['pinyin']
        key = 'word:' + head['text'] if len(head['tokens']) > 1 else p['base'] + str(p['tone'])
        definition = next(u for u in doc['textUnits'] if u['role'] == 'definition')
        text, syllables = records[key]
        tokens = [t for t in definition['tokens'] if t['kind'] == 'hanzi']
        if key in used or text != definition['text'] or len(tokens) != len(syllables):
            raise ValueError('Definition changed; revise its reading draft: ' + key)
        used.add(key)
        for token, syllable in zip(tokens, syllables):
            base, number = syllable[:-1].replace('v', 'ü'), int(syllable[-1])
            token.update(pinyin=dict(base=base, tone=number, erhua=False, display=tone(base, number) if number else base),
                         annotationSource='manual', locked=True, reviewState='needsReview')
        doc['annotationRevision'] += 1
        doc['metadataRevision'] += 1
        doc['updatedAtUtc'] = '2026-09-19T00:00:00Z'
        doc['source']['reference'] += '; tools/content/pinyin-definition-readings.tsv (AI-assisted reading draft; independent review pending)'
    if used != records.keys():
        raise ValueError('Unused definition reading records: ' + ', '.join(sorted(records.keys() - used)))
    course['version'] = 'draft-2026.09.19.3'
    return course
