"""Share explicitly annotated examples across matching lessons, without inventing tones."""
from pathlib import Path
import re

SOURCE = Path(__file__).resolve().parent / 'content'
INITIAL = re.compile(r'^(zh|ch|sh|[bpmfdtnlgkhjqxrzcsyw])')


def rows(name):
    return [line.split(';') for line in (SOURCE / name).read_text(encoding='utf-8').splitlines()
            if line and not line.startswith('#')]


def highlight(item, base):
    label, group = item['display'], item['group']
    match = INITIAL.match(base)
    initial = match[0] if match else ''
    if group in ('initial', 'spelling'):
        return (0, len(label)) if initial == label else None
    if group == 'whole':
        return (0, len(label)) if base == label else None
    final = base[len(initial):]
    final = {'yi': 'i', 'wu': 'u', 'yu': 'ü', 'yue': 'üe', 'yun': 'ün', 'yin': 'in',
             'ying': 'ing', 'you': 'iu', 'wei': 'ui', 'wen': 'un', 'ye': 'ie'}.get(base, final)
    final = {'ya': 'ia', 'yao': 'iao', 'yan': 'ian', 'yang': 'iang', 'yong': 'iong',
             'yuan': 'üan', 'wa': 'ua', 'wo': 'uo', 'wai': 'uai', 'wan': 'uan',
             'wang': 'uang', 'weng': 'ueng'}.get(base, final)
    if initial in ('j', 'q', 'x') and final.startswith('u'):
        final = 'ü' + final[1:]
    # zi/chi have apical vowels, not the ordinary vowel i.
    if label == 'i' and initial in ('z', 'c', 's', 'zh', 'ch', 'sh', 'r'):
        return None
    plain, target = base.replace('ü', 'u'), label.replace('ü', 'u')
    if final != label or target not in plain:
        return None
    return (plain.index(target), len(label))


def share_examples(course):
    for item in course['items']:
        present = {e['unitId'] for e in item['examples']}
        for doc in course['contents']:
            head = doc['textUnits'][0]
            if head['id'] in present:
                continue
            for token in head['tokens']:
                p = token['pinyin']
                bounds = highlight(item, p['base']) if p and p['tone'] in (1, 2, 3, 4) else None
                if bounds is None:
                    continue
                e = dict(tone=p['tone'], contentId=doc['id'], unitId=head['id'], tokenId=token['id'],
                         audioKey=p['base'] + str(p['tone']), pinyinStart=bounds[0], pinyinLength=bounds[1])
                if len(head['tokens']) > 1:
                    e['wordAudioKey'] = 'word-' + head['id'].replace('-', '')
                item['examples'].append(e)
                break
        # Keep examples compact: preserve the original choices, add only what each
        # tone lacks (at least one character and one word), then order characters first.
        original = set(present)
        contents = {d['id']: d for d in course['contents']}
        selected, covered = [], set()
        for e in item['examples']:
            kind = len(contents[e['contentId']]['textUnits'][0]['tokens']) > 1
            category = (e['tone'], kind)
            if e['unitId'] in original or category not in covered:
                selected.append(e)
                covered.add(category)
        # Pair each displayed character with a word containing that same character
        # AND reading. A same-tone word or a different reading of a polyphone is
        # not a pair. Highlight the matching token, including inside a word.
        characters = [e for e in selected if len(contents[e['contentId']]['textUnits'][0]['tokens']) == 1]
        for character in characters:
            token = contents[character['contentId']]['textUnits'][0]['tokens'][0]
            reading = token['pinyin']
            candidates = []
            for doc in course['contents']:
                head = doc['textUnits'][0]
                if len(head['tokens']) < 2:
                    continue
                for match in head['tokens']:
                    p = match['pinyin']
                    if match['text'] != token['text'] or not p or (p['base'], p['tone']) != (reading['base'], reading['tone']):
                        continue
                    bounds = highlight(item, p['base'])
                    if bounds is not None:
                        candidates.append(dict(tone=p['tone'], contentId=doc['id'], unitId=head['id'], tokenId=match['id'],
                            audioKey=p['base'] + str(p['tone']), pinyinStart=bounds[0], pinyinLength=bounds[1],
                            wordAudioKey='word-' + head['id'].replace('-', '')))
            # Reuse an already selected word first, otherwise a short curated word.
            candidates.sort(key=lambda e: (not any(s['unitId'] == e['unitId'] and s['tone'] == e['tone'] for s in selected),
                                            len(contents[e['contentId']]['textUnits'][0]['tokens'])))
            if candidates:
                match = candidates[0]
                existing = next((n for n, e in enumerate(selected) if e['unitId'] == match['unitId'] and e['tone'] == match['tone']), None)
                if existing is None:
                    selected.append(match)
                else:
                    selected[existing] = match
        item['examples'] = sorted(selected, key=lambda e: (e['tone'], len(contents[e['contentId']]['textUnits'][0]['tokens']) > 1))
    return course


def coverage(course):
    contents = {d['id']: d for d in course['contents']}
    result = []
    for item in course['items']:
        for tone in range(1, 5):
            examples = [contents[e['contentId']]['textUnits'][0]['text'] for e in item['examples'] if e['tone'] == tone]
            result.append(dict(group=item['group'], item=item['display'], tone=tone,
                               characters=[x for x in examples if len(x) == 1], words=[x for x in examples if len(x) > 1]))
    return result
