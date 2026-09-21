"""Export pypinyin 0.55.0 readings for the offline dictionary display only.
Never overwrites the user's annotation engine, corrections or dictionary source.
"""
from pathlib import Path
import hashlib
import json
import pypinyin
from pypinyin.constants import PINYIN_DICT, PHRASES_DICT
from pypinyin.contrib.tone_convert import to_tone3

ROOT = Path(__file__).resolve().parents[1]


def build():
    if pypinyin.__version__ != '0.55.0':
        raise ValueError('Use pypinyin 0.55.0')
    rows = []
    for text, syllables in sorted([(chr(code), [readings.split(',')[0]]) for code, readings in PINYIN_DICT.items()]
                                 + [(text, [readings[0] for readings in syllables]) for text, syllables in PHRASES_DICT.items()]):
        values = [to_tone3(s, neutral_tone_with_five=True).replace('5', '0') for s in syllables]
        rows.append(text + '\t' + ' '.join(values))
    payload = ('\n'.join(rows) + '\n').encode('utf-8')
    directory = ROOT / 'HanMate.Infrastructure/Pinyin'
    (directory / 'DictionaryDisplay.tsv').write_bytes(payload)
    (directory / 'DictionaryDisplay.source.json').write_text(json.dumps(dict(
        source='https://github.com/mozillazg/python-pinyin', version=pypinyin.__version__,
        license='MIT; see Dictionary/PYPINYIN-LICENSE.txt', entries=len(rows),
        sha256=hashlib.sha256(payload).hexdigest(),
        policy='First reading and longest phrase; automatic display only, needs review. Unsupported readings remain unannotated.'),
        ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'Exported {len(rows)} offline display readings')


if __name__ == '__main__':
    build()
