"""Isolated backup smoke data. Synthetic audio and example content are not teaching material."""
import argparse
import hashlib
import json
import tempfile
import uuid
import zipfile
from pathlib import Path
from build_playback_fixture import build

NS = uuid.UUID('fa660462-3c84-4c21-934c-bf998211cebe')


def encode(value):
    return json.dumps(value, ensure_ascii=False, separators=(',', ':')).encode('utf-8')


def rewrite(value):
    if isinstance(value, dict):
        return {key: rewrite(item) for key, item in value.items()}
    if isinstance(value, list):
        return [rewrite(item) for item in value]
    if isinstance(value, str):
        try:
            return str(uuid.uuid5(NS, str(uuid.UUID(value))))
        except ValueError:
            return value.replace('Playback-', 'Backup-')
    return value


def generate(output):
    with tempfile.TemporaryDirectory(prefix='hanmate-backup-fixture-') as temporary:
        content = Path(temporary) / 'playback.zip'
        build(content)
        with zipfile.ZipFile(content) as archive:
            files = {name: archive.read(name) for name in archive.namelist() if name != 'manifest.json'}
    documents = rewrite(json.loads(files['contents.json']))
    audio = rewrite(json.loads(files['audio/index.json']))
    default = str(uuid.uuid5(NS, 'default'))
    folder = str(uuid.uuid5(NS, 'folder'))
    files['contents.json'] = encode(documents)
    files['audio/index.json'] = encode(audio)
    files['collections.json'] = encode({'schemaVersion': 1, 'folders': [
        {'id': default, 'name': 'default', 'description': '', 'sortOrder': 0, 'systemRole': 'default'},
        {'id': folder, 'name': 'Backup-smoke', 'description': 'Engineering fixture; not teaching material', 'sortOrder': 1, 'systemRole': None}],
        'items': [{'folderId': folder, 'contentId': d['id'], 'sortOrder': i, 'addedAtUtc': '2026-09-18T00:00:00Z'} for i, d in enumerate(documents['contents'])]})
    files['settings.json'] = encode({'schemaVersion': 1, 'uiLanguage': 'ja', 'reading': {'showPinyin': True, 'fontSize': 30, 'scalePercent': 150},
        'audio': {'policy': 'localOnly', 'preferredLocale': 'zh-CN'}, 'favorites': {'lastFolderId': folder}})
    files['resources.json'] = encode({'schemaVersion': 1, 'resources': [], 'retainedContentIds': []})
    manifest = {'schemaVersion': 2, 'packageType': 'backup', 'packageId': str(uuid.uuid5(NS, 'package')),
        'createdAtUtc': '2026-09-18T00:00:00Z', 'producerAppVersion': '1.0', 'contentCatalogVersion': None,
        'counts': {'contents': len(documents['contents']), 'folders': 2, 'favorites': 5, 'audioAssets': len(audio['assets']), 'audioBindings': len(audio['bindings']), 'resources': 0},
        'files': [{'path': path, 'byteLength': len(data), 'sha256': hashlib.sha256(data).hexdigest()} for path, data in files.items()],
        'warnings': [{'code': 'ENGINEERING_ONLY', 'message': 'Synthetic sound is not a pronunciation example.'}]}
    output = Path(output)
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED) as archive:
        for path, data in files.items():
            archive.writestr(path, data)
        archive.writestr('manifest.json', encode(manifest))
    print(output)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('output', type=Path)
    generate(parser.parse_args().output)
