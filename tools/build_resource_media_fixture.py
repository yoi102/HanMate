"""Independent resource/teaching UI fixture. 440 Hz is a synthetic test signal, never pronunciation material."""
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

NS = uuid.UUID('98dd9460-7bb6-42ef-8130-de1a4c3cc5de')


def encode(n):
    return json.dumps(n, ensure_ascii=False, separators=(',', ':'), sort_keys=True).encode()


def generate(output):
    output.mkdir(parents=True, exist_ok=True)
    sample = Path(__file__).resolve().parents[1] / 'HanMate_Planning_Pack_v3.0/examples/sample-learning.hanresource'
    with zipfile.ZipFile(sample) as z:
        descriptor = json.loads(z.read('resource.json'))
        document = json.loads(z.read('contents.json'))['contents'][0]
    entry = 'media-smoke'
    mapping = {document['id']: str(uuid.uuid5(NS, entry))}

    def collect(n):
        if isinstance(n, dict):
            if 'id' in n and n['id'] not in mapping:
                mapping[n['id']] = str(uuid.uuid5(NS, n['id']))
            for value in n.values():
                collect(value)
        elif isinstance(n, list):
            for value in n:
                collect(value)

    collect(document)

    def rewrite(n):
        if isinstance(n, dict):
            return {k: rewrite(v) for k, v in n.items()}
        if isinstance(n, list):
            return [rewrite(v) for v in n]
        return mapping.get(n, n) if isinstance(n, str) else n

    document = rewrite(document)
    document['source'].update(resourceId=str(NS), entryId=entry, resourceVersion='1.0.0')
    descriptor.update(resourceId=str(NS), entries=[{'entryId': entry, 'contentId': document['id']}])
    descriptor['names'] = {k: 'Media-lifecycle-smoke' for k in descriptor['names']}
    unit = document['textUnits'][0]
    binding, asset = str(uuid.uuid5(NS, 'binding')), str(uuid.uuid5(NS, 'asset'))
    data = io.BytesIO()
    with wave.open(data, 'wb') as wav:
        wav.setnchannels(1)
        wav.setsampwidth(2)
        wav.setframerate(8000)
        wav.writeframes(b''.join(struct.pack('<h', round(1800*math.sin(2*math.pi*440*i/8000))) for i in range(8000)))
    audio_bytes = data.getvalue()
    digest = hashlib.sha256(audio_bytes).hexdigest()
    audio_path = f'audio/{digest}.wav'
    pronunciation = [[t['start'], t['length'], (t.get('pinyin') or {}).get('base'), (t.get('pinyin') or {}).get('tone'), (t.get('pinyin') or {}).get('erhua')] for t in unit['tokens']]
    audio = {'schemaVersion': 1, 'assets': [{'id': asset, 'origin': 'catalog', 'storage': 'package', 'sha256': digest,
             'byteLength': len(audio_bytes), 'durationMs': 1000, 'container': 'wav', 'codec': 'pcm_s16le', 'sampleRate': 8000, 'channels': 1, 'path': audio_path, 'catalogRef': None}],
             'bindings': [{'id': binding, 'targetId': unit['id'], 'assetId': asset, 'boundTextHash': hashlib.sha256(unit['text'].encode()).hexdigest(),
                           'boundPronunciationHash': hashlib.sha256(encode(pronunciation)).hexdigest(), 'reviewState': 'confirmed', 'sourceRole': 'standard', 'label': 'Synthetic 440 Hz test signal'}],
             'preferences': [{'targetId': unit['id'], 'bindingId': binding}]}
    descriptor.update(audioBindingIds=[binding], audioDefaults=copy.deepcopy(audio['preferences']))
    for version in ('1.0.0', '2.0.0'):
        doc = copy.deepcopy(document)
        doc['source']['resourceVersion'] = version
        doc['title'] = 'Media-smoke-v' + version[0]
        descriptor['version'] = version
        files = {'contents.json': encode({'schemaVersion': 2, 'contents': [doc]}), 'resource.json': encode(descriptor), 'audio/index.json': encode(audio), audio_path: audio_bytes}
        manifest = {'schemaVersion': 2, 'packageType': 'resource', 'packageId': str(uuid.uuid5(NS, version)), 'createdAtUtc': '2026-09-18T00:00:00Z',
                    'producerAppVersion': '1.0', 'contentCatalogVersion': None, 'counts': {'contents': 1, 'folders': 0, 'favorites': 0, 'resources': 1, 'audioAssets': 1, 'audioBindings': 1},
                    'files': [{'path': p, 'byteLength': len(b), 'sha256': hashlib.sha256(b).hexdigest()} for p, b in files.items()],
                    'warnings': [{'code': 'ENGINEERING_ONLY', 'message': 'Synthetic test signal. Not teaching audio.'}]}
        with zipfile.ZipFile(output / ('media-v' + version[0] + '.zip'), 'w', zipfile.ZIP_DEFLATED) as z:
            for path, body in files.items():
                z.writestr(path, body)
            z.writestr('manifest.json', encode(manifest))
    token = next(t for t in unit['tokens'] if t.get('pinyin'))
    examples = [{'tone': tone, 'status': 'available' if tone == token['pinyin']['tone'] else 'unavailable', 'note': 'Engineering example',
                 'contentId': document['id'] if tone == token['pinyin']['tone'] else None,
                 'highlight': {'contentId': document['id'], 'unitId': unit['id'], 'tokenId': token['id'], 'pinyinElementStart': 0, 'pinyinElementLength': len(token['pinyin']['base'])} if tone == token['pinyin']['tone'] else None} for tone in range(1, 5)]
    (output / 'teaching.json').write_bytes(encode({'schemaVersion': 1, 'catalogId': str(NS), 'catalogVersion': 'fixture', 'items': [
        {'id': str(uuid.uuid5(NS, 'teaching')), 'groupCode': 'wholeSyllable', 'display': token['pinyin']['base'], 'sortOrder': 0, 'demoAudioAssetId': asset, 'examples': examples, 'reviewStatus': 'draft'}]}))
    print(output)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('output', type=Path)
    generate(parser.parse_args().output)
