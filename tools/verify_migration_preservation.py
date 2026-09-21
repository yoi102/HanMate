"""Compare private before/after backups without logging their contents.

Run only after production BackupImportStore has validated the returned package.
This supplemental check proves original data preservation, not package validity.
"""
import argparse
import hashlib
import json
from pathlib import Path
from zipfile import ZipFile


def indexed(rows, keys):
    result = {tuple(row[k] for k in keys): row for row in rows}
    if len(result) != len(rows):
        raise ValueError('Duplicate identity')
    return result


def subset(before, after, keys, label, ignore=()):
    previous, current = indexed(before, keys), indexed(after, keys)
    for key, value in previous.items():
        if key not in current or ({k: v for k, v in value.items() if k not in ignore}
                                  != {k: v for k, v in current[key].items() if k not in ignore}):
            raise ValueError(label + ' changed or missing')
    return len(previous)


def verify(before, after, additions):
    with ZipFile(before) as a, ZipFile(after) as b:
        load = lambda z, name: json.loads(z.read(name))
        ac, bc = load(a, 'contents.json')['contents'], load(b, 'contents.json')['contents']
        contents = subset(ac, bc, ['id'], 'Content')
        if len(bc) != len(ac) + additions:
            raise ValueError('Unexpected number of added contents')
        for name in ['settings.json', 'resources.json']:
            if load(a, name) != load(b, name):
                raise ValueError(name + ' changed')
        ca, cb = load(a, 'collections.json'), load(b, 'collections.json')
        folders = subset(ca['folders'], cb['folders'], ['id'], 'Folder')
        favorites = subset(ca['items'], cb['items'], ['folderId', 'contentId'], 'Favorite', ['sortOrder'])
        # Export assigns a global ordinal; compare relative order inside each original folder.
        for folder in ca['folders']:
            old = [r['contentId'] for r in sorted(ca['items'], key=lambda r: r['sortOrder']) if r['folderId'] == folder['id']]
            new = [r['contentId'] for r in sorted(cb['items'], key=lambda r: r['sortOrder']) if r['folderId'] == folder['id'] and r['contentId'] in old]
            if old != new:
                raise ValueError('Favorite order changed')
        aa, ab = load(a, 'audio/index.json'), load(b, 'audio/index.json')
        counts = {}
        for field, keys in [('assets', ['id']), ('bindings', ['id']), ('preferences', ['targetId'])]:
            counts[field] = subset(aa[field], ab[field], keys, 'Audio ' + field)
        for asset in aa['assets']:
            for archive in [a, b]:
                if hashlib.sha256(archive.read(asset['path'])).hexdigest() != asset['sha256']:
                    raise ValueError('Original audio hash changed')
        return dict(status='PASS', originalContents=contents, addedContents=additions, folders=folders,
                    favorites=favorites, audio=counts, settingsUnchanged=True, resourcesAndTeachingUnchanged=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('before', type=Path)
    parser.add_argument('after', type=Path)
    parser.add_argument('--added', type=int, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = verify(args.before, args.after, args.added)
    args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(result))
