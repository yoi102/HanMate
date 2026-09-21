"""Generate an explicitly labeled local reader smoke package, never a bundled teaching catalog."""
import argparse, copy, hashlib, json, uuid, zipfile
from pathlib import Path
import regex

ROOT = Path(__file__).resolve().parents[1]
NS = uuid.UUID('d45ccfc2-32af-5c92-80da-0bd80c4d0b7d')

def uid(name): return str(uuid.uuid5(NS, name))
def build(output):
    source = ROOT / 'HanMate_Planning_Pack_v3.0/examples'
    samples = json.loads((source / 'contents.json').read_text(encoding='utf-8'))['contents']
    rid = uid('resource-reader-smoke-1')
    docs, entries = [], []
    inputs = [
        ('long', '阅读器测试：两万字', '中' * 19999 + '终'),
        ('mixed', '阅读器测试：混排与诗行', '女儿，绿色。\r\n\r\n你好！「中文。」\r\nABC 3.14 / 10:30 / n\u030c / 👩‍💻 / 日本語\r\n')]
    spellings = {'中': ('zhong', 1, 'zhōng'), '终': ('zhong', 1, 'zhōng'), '女': ('nü', 3, 'nǚ'), '儿': ('er', 2, 'ér'), '绿': ('lü', 4, 'lǜ'), '色': ('se', 4, 'sè'), '你': ('ni', 3, 'nǐ'), '好': ('hao', 3, 'hǎo'), '文': ('wen', 2, 'wén')}
    for key, title, text in inputs:
        doc = copy.deepcopy(next(d for d in samples if d['kind'] == 'text'))
        cid = str(uuid.uuid5(uuid.UUID(rid), key)); unitid = uid(key + '-unit')
        doc.update(id=cid, kind='text', origin='resource', title=title)
        doc['source'].update(type='testFixture', sourceId='hanmate-reader-smoke', authorProvider='HanMate reader regression fixture', reference='tools/build_reader_fixture.py', resourceId=rid, resourceVersion='1.0.0', entryId=key, reviewStatus='draft')
        elements = regex.findall(r'\X', text)
        bounds, tokens, segments = [0], [], []
        current = []
        def flush():
            if not current: return
            begin = current[0]['start']; chunk = ''.join(t['text'] for t in current)
            segments.append({'id': uid(f'{key}-segment-{begin}'), 'start': begin, 'length': len(current), 'text': chunk,
                'kind': 'layout' if chunk.isspace() else 'speech', 'boundarySource': 'manual', 'tokenIds': [t['id'] for t in current],
                'translations': {}})
            current.clear()
        for index, element in enumerate(elements):
            bounds.append(bounds[-1] + len(element.encode('utf-16-le')) // 2)
            py = spellings.get(element)
            token = {'id': uid(f'{key}-token-{index}'), 'start': index, 'length': 1, 'text': element,
                'kind': 'hanzi' if py else 'whitespace' if element.isspace() else 'symbol',
                'pinyin': {'base': py[0], 'tone': py[1], 'erhua': False, 'display': py[2]} if py else None,
                'annotationSource': 'manual' if py else 'none', 'locked': False,
                'reviewState': 'needsReview' if py else 'notApplicable'}
            if element in ['\r\n', '\n']: flush()
            tokens.append(token); current.append(token)
            if element in ['\r\n', '\n', '，', '。', '！']: flush()
        flush()
        doc['textUnits'] = [{'id': unitid, 'role': 'body', 'text': text, 'offsetUnit': 'textElement', 'elementBoundariesUtf16': bounds,
            'tokens': tokens, 'segments': segments, 'translations': {'ja': '読み取り試験用データ。', 'en': 'Reader test data.'}}]
        docs.append(doc); entries.append({'entryId': key, 'contentId': cid})
    with zipfile.ZipFile(source / 'sample-learning.hanresource') as z: descriptor = json.loads(z.read('resource.json'))
    descriptor.update(resourceId=rid, version='1.0.0', entries=entries, names={'zh-Hans': '阅读器回归测试（非教材）', 'ja': 'リーダー試験用（教材ではありません）', 'en': 'Reader regression fixture (not teaching material)'})
    raw = {'resource.json': descriptor, 'contents.json': {'schemaVersion': 2, 'contents': docs}, 'audio/index.json': {'schemaVersion': 1, 'assets': [], 'bindings': [], 'preferences': []}}
    files = {k: json.dumps(v, ensure_ascii=False, separators=(',', ':')).encode() for k, v in raw.items()}
    manifest = {'schemaVersion': 2, 'packageType': 'resource', 'packageId': uid('package-reader-smoke-1'), 'createdAtUtc': '2026-09-17T00:00:00Z',
        'producerAppVersion': '1.0.0', 'contentCatalogVersion': 'reader-smoke-1', 'counts': {'contents': 2, 'folders': 0, 'favorites': 0, 'audioAssets': 0, 'audioBindings': 0, 'resources': 1},
        'files': [{'path': k, 'byteLength': len(v), 'sha256': hashlib.sha256(v).hexdigest()} for k, v in files.items()], 'warnings': [{'code': 'TEST_FIXTURE', 'message': 'Reader regression fixture only.'}]}
    files['manifest.json'] = json.dumps(manifest, ensure_ascii=False).encode()
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED) as z:
        for name, data in files.items(): z.writestr(name, data)
    print(f'Created {output}: {len(files["contents.json"])} content bytes, 20000-element long text and mixed text; resource {rid}')

if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('output', type=Path); build(parser.parse_args().output)
