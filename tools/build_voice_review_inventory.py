"""Offline, deterministic review inventory. Never marks listening/licensing review complete."""
import csv
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
RAW = ROOT / 'HanMate.App/Resources/Raw'
OUT = ROOT / 'HanMate_Planning_Pack_v3.0/tracking/review'


def write(name, rows):
    with (OUT / name).open('w', encoding='utf-8', newline='') as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)


def main():
    course = json.loads((RAW / 'Pinyin/course.json').read_text(encoding='utf-8'))
    assets = {a['key']: a for a in course['assets']}
    contents = {c['id']: c for c in course['contents']}
    OUT.mkdir(exist_ok=True)
    rows = []
    for item in course['items']:
        key = item.get('demoAudioKey')
        asset = assets.get(key)
        examples = []
        for example in item['examples']:
            unit = next(u for u in contents[example['contentId']]['textUnits'] if u['id'] == example['unitId'])
            pinyin = ' '.join(t['pinyin']['display'] for t in unit['tokens'] if t.get('pinyin'))
            examples.append(f"{unit['text']} ({pinyin})")
        rows.append(dict(itemId=item['id'], group=item['group'], display=item['display'],
                         demoAudioKey=key or '', demoFile=asset['file'] if asset else '',
                         demoSha256=asset['sha256'] if asset else '', examples='; '.join(examples),
                         sourceUrl=asset['sourceUrl'] if asset else '', license=asset['license'] if asset else '',
                         availability='PRESENT' if asset else 'MISSING', reviewStatus='NOT RUN'))
    write('pinyin-demo-inventory.csv', rows)
    rows = []
    for asset in course['assets']:
        path = RAW / asset['file']
        if not path.resolve().is_relative_to(RAW.resolve()):
            raise ValueError('Asset escapes raw directory')
        actual = hashlib.sha256(path.read_bytes()).hexdigest()
        if actual != asset['sha256']:
            raise ValueError(f"Audio hash mismatch: {asset['key']}")
        rows.append(dict(audioKey=asset['key'], file=asset['file'], sha256=actual,
                         durationSeconds=asset['durationSeconds'], author=asset['author'], sourceUrl=asset['sourceUrl'],
                         license=asset['license'], licenseUrl=asset['licenseUrl'], changes=asset['changes'],
                         licenseReview='BLOCKED' if 'does not specify version' in asset['license'] else 'NOT RUN',
                         listeningReview='NOT RUN'))
    write('pinyin-audio-inventory.csv', rows)
    print(f"Inventory: {len(course['items'])} teaching items, {len(rows)} verified file hashes; human review remains pending.")


if __name__ == '__main__':
    main()
