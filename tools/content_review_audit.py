"""Offline review inventory for the actual shipped payloads, with hash-bound human decisions.

--write regenerates derived inventories, never review-decisions.json or reviewer evidence.
--check checks derived files. --release exits 2 while any subject remains unreviewed/blocked.
No network calls, generated signatures, or inferred listening approvals.
"""
import argparse
import csv
import hashlib
import html
import io
import json
import os
from pathlib import Path
import re
import unicodedata
import wave
import zipfile
from datetime import date
from dictionary_review_batches import scan as scan_dictionary

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'HanMate_Planning_Pack_v3.0/tracking/review'


def digest(value):
    return hashlib.sha256(json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':')).encode()).hexdigest()


def numbered(display):
    value = unicodedata.normalize('NFD', display.lower().replace('v', 'ü'))
    tone = 0
    letters = []
    for ch in value:
        if ch in '\u0304\u0301\u030c\u0300':
            tone = '\u0304\u0301\u030c\u0300'.index(ch) + 1
        else:
            letters.append(ch)
    return unicodedata.normalize('NFC', ''.join(letters)), tone


def apply_decisions(subjects, records, evidence_root):
    """An old PASS cannot approve changed text, different audio, or a different review dimension."""
    lookup = {s['subjectId']: s for s in subjects}
    applied = set()
    issues = []
    if not isinstance(records, list):
        return [dict(code='INVALID_DECISIONS_DOCUMENT')]
    for record in records:
        if not isinstance(record, dict) or any(not isinstance(record.get(k), str) for k in
                ('subjectId', 'dimension', 'sha256', 'reviewer', 'evidence', 'date', 'method', 'status')):
            issues.append(dict(code='INVALID_DECISION_RECORD'))
            continue
        sid, dimension = record.get('subjectId'), record.get('dimension')
        subject = lookup.get(sid)
        key = (sid, dimension)
        problem = None
        if subject is None or dimension not in subject['reviews']:
            problem = 'UNKNOWN_SUBJECT_OR_DIMENSION'
        elif key in applied:
            problem = 'DUPLICATE_DECISION'
        elif record.get('sha256') != subject['sha256']:
            problem = 'STALE_DECISION'
        elif subject.get('category') == 'dictionary-batch' and (
                record.get('allEntriesReviewed') is not True or
                type(record.get('reviewedEntries')) is not int or
                record['reviewedEntries'] != subject['entries']):
            problem = 'INCOMPLETE_BATCH_REVIEW'
        else:
            reviewer = record.get('reviewer', '').strip()
            try:
                evidence = (evidence_root / record['evidence']).resolve()
                valid_evidence = evidence.is_relative_to(evidence_root.resolve()) and evidence.is_file()
            except (ValueError, OSError):
                valid_evidence = False
            try:
                valid_date = date.fromisoformat(record.get('date', '')) <= date.today()
            except (ValueError, TypeError):
                valid_date = False
            if (record.get('method') != 'human' or not reviewer or reviewer.lower() in {'ai', 'codex', 'chatgpt'}
                    or not valid_date or not valid_evidence
                    or record.get('status') not in {'PASS', 'FAIL'}):
                problem = 'INCOMPLETE_REVIEW_EVIDENCE'
            else:
                subject['reviews'][dimension] = record['status']
        applied.add(key)
        if problem:
            issues.append(dict(subjectId=sid, dimension=dimension, code=problem))
            if subject is not None and dimension in subject['reviews']:
                subject['reviews'][dimension] = 'BLOCKED'
    return issues


def demo_payload(item, course):
    """Teaching approval covers its exact examples and every referenced recording."""
    contents = {d['id']: d for d in course['contents']}
    assets = {a['key']: a for a in course['assets']}
    keys = {item.get('demoAudioKey')}
    for example in item['examples']:
        keys.update(example.get(k) for k in ('audioKey', 'wordAudioKey'))
    return dict(item=item, contents=[contents[e['contentId']] for e in item['examples'] if e.get('contentId')],
                assets=[assets.get(k, dict(key=k, missing=True)) for k in sorted(k for k in keys if k)])


def collect(root=ROOT, decisions=None, evidence_root=OUT):
    raw = root / 'HanMate.App/Resources/Raw'
    course = json.loads((raw / 'Pinyin/course.json').read_text(encoding='utf-8'))
    subjects, units, findings = [], [], []

    def add(sid, category, title, payload, dimensions, **extra):
        subject = dict(subjectId=sid, category=category, title=title, sha256=digest(payload),
                       reviews={key: 'NOT RUN' for key in dimensions}, **extra)
        subjects.append(subject)
        return subject

    library, dictionary_batches, _ = scan_dictionary(root)
    add('dictionary-library:xinhua', 'dictionary-library', library['notice']['name'], library, ['rights'],
        version=library['notice']['version'], entries=library['notice']['entries'])
    if library['notice']['license'] == 'NOASSERTION':
        findings.append(dict(subjectId='dictionary-library:xinhua', code='DICTIONARY_RIGHTS_UNRESOLVED',
                             detail='Upstream content redistribution rights have not been established.'))
    for batch in dictionary_batches:
        add(batch['subjectId'], 'dictionary-batch', batch['first'] + ' … ' + batch['last'],
            dict(library=library, batch=batch), ['chinese', 'pinyin'],
            entries=batch['entries'], first=batch['first'], last=batch['last'])

    readings = {}
    for line in (root / 'HanMate.Infrastructure/Pinyin/Unihan17.tsv').read_text(encoding='utf-8').splitlines():
        ch, value = line.split('\t')
        readings.setdefault(ch, set()).add(numbered(value))

    documents = []
    for name in ['learning', 'dictionary']:
        path = root / f'HanMate.Infrastructure/Catalog/{name}.zip'
        with zipfile.ZipFile(path) as archive:
            manifest = json.loads(archive.read('manifest.json'))
            for entry in manifest['files']:
                data = archive.read(entry['path'])
                if hashlib.sha256(data).hexdigest() != entry['sha256'] or len(data) != entry['byteLength']:
                    raise ValueError('Bundled package hash mismatch: ' + entry['path'])
            descriptor = json.loads(archive.read('resource.json'))
            add('resource:' + descriptor['resourceId'], 'resource', name, descriptor, ['rights'], version=descriptor['version'])
            documents += [(name, d) for d in json.loads(archive.read('contents.json'))['contents']]
    documents += [('pinyin', d) for d in course['contents']]
    display_sources = {name: hashlib.sha256((root / name).read_bytes()).hexdigest() for name in (
        'HanMate.Infrastructure/Pinyin/DictionaryDisplay.tsv',
        'HanMate.Infrastructure/Pinyin/DictionaryDisplayOverrides.tsv',
        'HanMate.Infrastructure/Pinyin/DictionaryDisplayPinyin.cs')}
    add('dictionary-annotation:display', 'dictionary-annotation', 'Automatic dictionary display readings',
        display_sources, ['pinyin', 'rights'])
    for line in (root / 'HanMate.Infrastructure/Dictionary/learning-examples.tsv').read_text(encoding='utf-8').splitlines():
        if not line or line.startswith('#'): continue
        headword, sentence = line.split('\t')
        if headword not in sentence: raise ValueError('Example must contain its headword')
        add('dictionary-example:' + digest([headword, sentence]), 'dictionary-example', headword,
            dict(headword=headword, sentence=sentence, displaySources=display_sources), ['chinese', 'pinyin', 'rights'])
    for collection, doc in documents:
        sid = 'content:' + doc['id']
        add(sid, 'content', doc['title'], doc, ['chinese', 'pinyin', 'english', 'japanese', 'rights'],
            collection=collection, kind=doc['kind'], sourceReview=doc['source']['reviewStatus'])
        if doc['source']['reviewStatus'] != 'approved':
            findings.append(dict(subjectId=sid, code='CONTENT_DRAFT', detail='Independent content/source review remains pending.'))
        for unit in doc['textUnits']:
            missing, candidates = [], []
            for token in unit['tokens']:
                if token['kind'] != 'hanzi':
                    continue
                pinyin = token.get('pinyin')
                if not pinyin:
                    missing.append(token['text'])
                elif len(token['text']) == 1 and token['text'] in readings:
                    target = (pinyin['base'].replace('v', 'ü'), pinyin['tone'])
                    known = readings[token['text']]
                    if target not in known and not (target[1] == 0 and any(p[0] == target[0] for p in known)):
                        candidates.append(token['text'] + '=' + pinyin['display'])
            if missing:
                findings.append(dict(subjectId=sid, code='ANNOTATION_MISSING', detail=unit['id'] + ': ' + ''.join(missing)))
            if candidates:
                findings.append(dict(subjectId=sid, code='READING_RECHECK', detail=unit['id'] + ': ' + ', '.join(candidates)))
            if unit['role'] == 'definition' and re.sub(r'\W', '', unit['text']) == re.sub(r'\W', '', doc['title']):
                findings.append(dict(subjectId=sid, code='TAUTOLOGICAL_DEFINITION', detail=doc['title']))
            units.append(dict(subjectId=sid, documentSha256=digest(doc), collection=collection, kind=doc['kind'], title=doc['title'],
                unitId=unit['id'], role=unit['role'], text=unit['text'],
                pinyin=' '.join(t['pinyin']['display'] if t.get('pinyin') else ('?' if t['kind'] == 'hanzi' else t['text']) for t in unit['tokens']),
                english=unit.get('translations', {}).get('en', ''), japanese=unit.get('translations', {}).get('ja', ''),
                missingHanzi=len(missing), readingRecheck='; '.join(candidates)))
    audio = []
    for asset in course['assets']:
        path = (raw / asset['file']).resolve()
        if not path.is_relative_to(raw.resolve()):
            raise ValueError('Unsafe audio path')
        data = path.read_bytes()
        if hashlib.sha256(data).hexdigest() != asset['sha256']:
            raise ValueError('Audio hash mismatch: ' + asset['key'])
        with wave.open(io.BytesIO(data)) as wav:
            if wav.getsampwidth() != 2 or wav.getnchannels() != 1 or wav.getcomptype() != 'NONE':
                raise ValueError('Expected mono PCM16: ' + asset['key'])
            duration = wav.getnframes() / wav.getframerate()
            if abs(duration - asset['durationSeconds']) > .002:
                raise ValueError('Duration mismatch: ' + asset['key'])
        subject = add('audio:' + asset['key'], 'audio', asset['key'], asset, ['license', 'listening', 'alignment'],
                      file=asset['file'], mediaSha256=asset['sha256'], license=asset['license'])
        if 'does not specify version' in asset['license']:
            findings.append(dict(subjectId=subject['subjectId'], code='LICENSE_VERSION_UNRESOLVED', detail=asset['licenseUrl']))
        audio.append(dict(subjectId=subject['subjectId'], sha256=subject['sha256'], file=asset['file'], mediaSha256=asset['sha256'],
                          author=asset['author'], source=asset['sourceUrl'], license=asset['license'], licenseUrl=asset['licenseUrl']))
    for item in course['items']:
        subject = add('demo:' + item['id'], 'demo', item['display'], demo_payload(item, course), ['pedagogy', 'alignment'], group=item['group'])
        if not item.get('demoAudioKey'):
            findings.append(dict(subjectId=subject['subjectId'], code='DEMO_MISSING', detail=item['display']))
    model = json.loads((root / 'HanMate.Infrastructure/Voices/catalog.json').read_text(encoding='utf-8'))
    add('model:' + model['Id'], 'model', model['Name'], model, ['license'])
    corpus = hashlib.sha256((root / 'HanMate_Planning_Pack_v3.0/tracking/review/ai-listening-corpus.csv').read_bytes()).hexdigest()
    for speaker in range(model['Speakers']):
        add(f'voice:{model["Id"]}:{speaker}', 'voice', f'{speaker + 1:03}',
            dict(model=model, speaker=speaker, corpusSha256=corpus), ['listening'])
    ids = [s['subjectId'] for s in subjects]
    if len(set(ids)) != len(ids):
        raise ValueError('Duplicate review subject')
    if decisions is None:
        path = evidence_root / 'review-decisions.json'
        decisions = json.loads(path.read_text(encoding='utf-8')) if path.exists() else []
    decision_issues = apply_decisions(subjects, decisions, evidence_root)
    pending = [s['subjectId'] for s in subjects if any(v != 'PASS' for v in s['reviews'].values())]
    reviewed_entries = sum(s['entries'] for s in subjects if s['category'] == 'dictionary-batch'
                           and all(v == 'PASS' for v in s['reviews'].values()))
    dictionary_coverage = dict(entries=library['notice']['entries'], batches=len(dictionary_batches),
        fullyReviewedEntries=reviewed_entries, notFullyReviewedEntries=library['notice']['entries'] - reviewed_entries,
        samplingDoesNotApproveBatch=True)
    summary = dict(subjects=len(subjects), contents=len(documents), units=len(units), audio=len(audio), demos=len(course['items']),
                   voices=model['Speakers'], pendingSubjects=len(pending), findingsByCode={code: sum(f['code'] == code for f in findings) for code in sorted({f['code'] for f in findings})},
                   dictionaryCoverage=dictionary_coverage,
                   decisionIssues=len(decision_issues), releaseReady=not (pending or findings or decision_issues))
    return dict(summary=summary, subjects=subjects, units=units, audio=audio, findings=findings,
                dictionaryBatches=dictionary_batches, decisionIssues=decision_issues)


def csv_bytes(rows):
    stream = io.StringIO(newline='')
    writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
    writer.writeheader(); writer.writerows(rows)
    return stream.getvalue().encode('utf-8')


def review_html(report):
    esc = html.escape
    body = []
    for row in report['units']:
        title = f"{row['collection']} · {row['kind']} · {row['title']} · {row['role']}"
        body.append('<article><h3>' + esc(title) + '</h3><p>' + esc(row['pinyin']) + '</p><p class="hanzi">' + esc(row['text']) +
                    '</p><p>EN: ' + esc(row['english'] or '待补充 / pending') + '</p><p>JA: ' + esc(row['japanese'] or '待补充 / pending') +
                    '</p><small>' + esc(row['subjectId']) + '<br>SHA256 ' + row['documentSha256'] + '</small></article>')
    for row in report['audio']:
        path = os.path.relpath(ROOT / 'HanMate.App/Resources/Raw' / row['file'], OUT).replace('\\', '/')
        body.append('<article><h3>' + esc(row['subjectId']) + '</h3><audio controls preload="none" src="' + esc(path, quote=True) +
                    '"></audio><p>' + esc(row['author'] + ' · ' + row['license']) + '</p><small>' + row['mediaSha256'] + '</small></article>')
    return ('''<!doctype html><html lang="zh-Hans"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>HanMate 内容与音频待审清单</title><style>body{font:16px system-ui;max-width:980px;margin:32px auto;padding:0 20px;background:#f7f8fa;color:#17212b}header{position:sticky;top:0;background:#f7f8fa;padding:12px 0}input{padding:12px;width:90%;font:inherit}article{background:white;padding:20px;margin:12px 0;border:1px solid #ddd;border-radius:10px}p{line-height:1.7}.hanzi{font-size:24px}small{overflow-wrap:anywhere;color:#52616d}audio{width:100%}</style>
<header><h1>内容与音频待审清单</h1><p>这是审核材料，不是审核通过。问号表示缺少注音。音频只在点击播放时读取本地文件。</p><label>筛选标题、类别或文字 <input id="filter" type="search"></label></header>''' + ''.join(body) +
        '''<script>document.getElementById('filter').addEventListener('input',e=>{let q=e.target.value.toLowerCase();document.querySelectorAll('article').forEach(x=>x.hidden=!x.textContent.toLowerCase().includes(q));});</script></html>''').encode('utf-8')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--write', action='store_true'); parser.add_argument('--check', action='store_true'); parser.add_argument('--release', action='store_true')
    args = parser.parse_args()
    report = collect()
    outputs = {'content-audit.json': (json.dumps(report, ensure_ascii=False, indent=2) + '\n').encode('utf-8'),
               'dictionary-batch-inventory.csv': csv_bytes(report['dictionaryBatches']),
               'content-unit-inventory.csv': csv_bytes(report['units']), 'content-review.html': review_html(report)}
    if args.write:
        OUT.mkdir(parents=True, exist_ok=True)
        for name, data in outputs.items(): (OUT / name).write_bytes(data)
        if not (OUT / 'review-decisions.json').exists(): (OUT / 'review-decisions.json').write_text('[]\n', encoding='utf-8')
    if args.check:
        for name, data in outputs.items():
            if not (OUT / name).exists() or (OUT / name).read_bytes() != data: raise ValueError('Stale derived review file: ' + name)
    print(json.dumps(report['summary'], ensure_ascii=False, indent=2))
    return 2 if args.release and not report['summary']['releaseReady'] else 0


if __name__ == '__main__':
    raise SystemExit(main())
