"""Build an optional, clearly labeled device test dictionary. Never alter bundled annotations."""
import argparse, copy, hashlib, json, uuid, zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
NS = uuid.UUID('c19e40ac-6537-4fc0-8345-90ea07a98161')
def uid(value): return str(uuid.uuid5(NS, value))

def build(output):
    source = ROOT / 'HanMate_Planning_Pack_v3.0/examples'
    samples = json.loads((source / 'contents.json').read_text(encoding='utf-8'))['contents']
    rid = uid('dictionary-search-smoke-1')
    docs, entries = [], []
    titles = ['你好', '女儿', '西安', '先', '银行', '妈妈', '花儿'] + ['你好'] * 54
    for index, title in enumerate(titles):
        doc = copy.deepcopy(next(d for d in samples if d['kind'] == 'word' and d['title'] == title))
        entry = f'search-{index:03d}'
        cid = str(uuid.uuid5(uuid.UUID(rid), entry))
        ids = {doc['id']: cid}
        for unit in doc['textUnits']:
            for obj in [unit] + unit['tokens'] + unit['segments']:
                ids[obj['id']] = uid(entry + obj['id'])
        def remap(value):
            if isinstance(value, dict): return {k: remap(v) for k, v in value.items()}
            if isinstance(value, list): return [remap(v) for v in value]
            return ids.get(value, value) if isinstance(value, str) else value
        doc = remap(doc)
        doc.update(origin='resource')
        doc['source'].update(sourceId='hanmate-search-smoke', type='testFixture',
            authorProvider='HanMate query regression fixture', reference='tools/build_search_fixture.py',
            resourceId=rid, resourceVersion='1.0.0', entryId=entry, reviewStatus='draft',
            permissionNotes='Synthetic search regression data; annotation confirmation is a test precondition, not teaching approval.')
        for unit in doc['textUnits']:
            for token in unit['tokens']:
                if token.get('pinyin'): token['reviewState'] = 'confirmed'
        docs.append(doc); entries.append({'entryId': entry, 'contentId': cid})
    with zipfile.ZipFile(source / 'sample-dictionary.handict') as archive:
        descriptor = json.loads(archive.read('resource.json'))
    descriptor.update(resourceId=rid, version='1.0.0', entries=entries,
        names={'zh-Hans': '搜索回归测试（非教材）', 'ja': '検索試験用（教材ではありません）', 'en': 'Search regression fixture (not teaching material)'})
    raw = {'resource.json': descriptor, 'contents.json': {'schemaVersion': 2, 'contents': docs},
           'audio/index.json': {'schemaVersion': 1, 'assets': [], 'bindings': [], 'preferences': []}}
    files = {key: json.dumps(value, ensure_ascii=False, separators=(',', ':')).encode() for key, value in raw.items()}
    manifest = {'schemaVersion': 2, 'packageType': 'resource', 'packageId': uid('package-search-smoke-1'),
        'createdAtUtc': '2026-09-17T00:00:00Z', 'producerAppVersion': '1.0.0', 'contentCatalogVersion': 'search-smoke-1',
        'counts': {'contents': len(docs), 'folders': 0, 'favorites': 0, 'audioAssets': 0, 'audioBindings': 0, 'resources': 1},
        'files': [{'path': key, 'byteLength': len(value), 'sha256': hashlib.sha256(value).hexdigest()} for key, value in files.items()],
        'warnings': [{'code': 'TEST_FIXTURE', 'message': 'Synthetic query regression fixture only.'}]}
    files['manifest.json'] = json.dumps(manifest, ensure_ascii=False).encode()
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED) as archive:
        for name, value in files.items(): archive.writestr(name, value)
    print(f'{output}: {len(docs)} test words, resource {rid}')

if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('--output', required=True, type=Path)
    build(parser.parse_args().output)
