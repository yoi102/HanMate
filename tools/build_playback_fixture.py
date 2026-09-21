"""Isolated reading-playback fixture. Synthetic tones are not teaching pronunciations."""
import argparse
import copy
import hashlib
import io
import json
import math
import struct
import uuid
import wave
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
NS = uuid.UUID('d5b3b18f-7c9a-4ee7-a878-9065d0b90c4a')


def uid(name):
    return str(uuid.uuid5(NS, name))


def encode(value):
    return json.dumps(value, ensure_ascii=False, separators=(',', ':'), sort_keys=True).encode('utf-8')


def digest(value):
    return hashlib.sha256(value).hexdigest()


def build(output):
    samples = json.loads((ROOT / 'HanMate_Planning_Pack_v3.0/examples/contents.json').read_text(encoding='utf-8'))['contents']
    data = io.BytesIO()
    with wave.open(data, 'wb') as wav:
        wav.setparams((1, 2, 8000, 0, 'NONE', 'not compressed'))
        wav.writeframes(b''.join(struct.pack('<h', int(1500 * math.sin(2 * math.pi * 440 * i / 8000))) for i in range(40000)))
    audio_bytes = data.getvalue()
    sha = digest(audio_bytes)
    asset = {'id': uid('asset'), 'origin': 'user', 'storage': 'package', 'sha256': sha, 'byteLength': len(audio_bytes),
             'durationMs': 5000, 'container': 'wav', 'codec': 'pcm_s16le', 'sampleRate': 8000, 'channels': 1,
             'path': f'audio/{sha}.wav', 'catalogRef': None}
    docs, bindings, preferences = [], [], []
    for name, kind in [('word', 'word'), ('grammar', 'grammar'), ('text', 'text'), ('poem', 'poem'), ('whole', 'text')]:
        doc = copy.deepcopy(next(d for d in samples if d['kind'] == kind))
        source = doc['source']
        for key in ['resourceId', 'resourceVersion', 'entryId']:
            source.pop(key, None)
        source.update(sourceId='playback-engineering-fixture', type='testFixture', authorProvider='HanMate engineering fixture',
                      reference='tools/build_playback_fixture.py; synthetic audio is not pronunciation', reviewStatus='draft')
        mapping = {}

        def remap(value):
            if isinstance(value, dict):
                return {k: remap(v) for k, v in value.items()}
            if isinstance(value, list):
                return [remap(v) for v in value]
            if isinstance(value, str):
                try:
                    uuid.UUID(value)
                except ValueError:
                    return value
                return mapping.setdefault(value, uid(name + ':' + value))
            return value

        doc = remap(doc)
        doc.update(origin='personal', title='Playback-' + name)
        docs.append(doc)
        for unit in doc['textUnits']:
            tokens = {t['id']: t for t in unit['tokens']}
            targets = [(unit['id'], unit['text'], unit['tokens'], 0)] if kind in ['word', 'grammar'] or name == 'whole' else [
                (s['id'], s['text'], [tokens[t] for t in s['tokenIds']], s['start']) for s in unit['segments'] if s['kind'] == 'speech']
            for target, text, selected, start in targets:
                pron = [[t['start'] - start, t['length'], t['pinyin']['base'] if t['pinyin'] else None,
                         t['pinyin']['tone'] if t['pinyin'] else None, t['pinyin']['erhua'] if t['pinyin'] else None] for t in selected]
                binding = {'id': uid('binding:' + target), 'targetId': target, 'assetId': asset['id'],
                           'boundTextHash': digest(text.encode()), 'boundPronunciationHash': digest(encode(pron)),
                           'reviewState': 'confirmed', 'sourceRole': 'user', 'label': 'Synthetic-440Hz-test-not-speech'}
                bindings.append(binding)
                preferences.append({'targetId': target, 'bindingId': binding['id']})
    files = {'contents.json': encode({'schemaVersion': 2, 'contents': docs}),
             'audio/index.json': encode({'schemaVersion': 1, 'assets': [asset], 'bindings': bindings, 'preferences': preferences}),
             asset['path']: audio_bytes}
    manifest = {'schemaVersion': 2, 'packageType': 'content', 'packageId': uid('package-v1'), 'createdAtUtc': '2026-09-18T00:00:00Z',
                'producerAppVersion': '1.0.0', 'contentCatalogVersion': 'playback-engineering-1',
                'counts': {'contents': len(docs), 'folders': 0, 'favorites': 0, 'audioAssets': 1, 'audioBindings': len(bindings), 'resources': 0},
                'files': [{'path': k, 'byteLength': len(v), 'sha256': digest(v)} for k, v in files.items()],
                'warnings': [{'code': 'TEST_FIXTURE', 'message': 'Synthetic tone verifies playback only; not teaching speech.'}]}
    files['manifest.json'] = encode(manifest)
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED) as archive:
        for name, content in files.items():
            archive.writestr(name, content)
    print(f'{output}: {len(docs)} contents, {len(bindings)} audio bindings; synthetic 5-second tone only.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('output', type=Path)
    build(parser.parse_args().output)
