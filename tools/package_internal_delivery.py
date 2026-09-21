"""Create a local internal handoff from explicit build outputs, never app data.

Builds/signing remain separate. Verification establishes file integrity only.
"""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import shutil
import zipfile

ROOT = Path(__file__).resolve().parents[1]
PACK = ROOT / 'HanMate_Planning_Pack_v3.0'
EXCLUDED = {'bin', 'obj', '.git', '.vs', '__pycache__'}
PRIVATE = {'.pfx', '.p12', '.pem', '.key', '.keystore', '.jks', '.user', '.suo', '.db'}
PROJECTS = ['HanMate.App', 'HanMate.Core', 'HanMate.Infrastructure', 'HanMate.Core.Tests', 'HanMate.Infrastructure.Tests']
TOOLS = ['build_internal_delivery.ps1', 'package_internal_delivery.py', 'release_preflight.py', 'content_review_audit.py', 'dictionary_review_batches.py']


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def files_under(path):
    for p in sorted(path.rglob('*')):
        if any(part in EXCLUDED for part in p.relative_to(path).parts):
            continue
        if p.suffix.lower() in {'.user', '.suo'}:
            continue  # Per-user IDE state is never part of a reproducible handoff.
        if p.is_symlink():
            raise ValueError('Links are not delivery inputs: ' + str(p))
        if p.is_file():
            if p.suffix.lower() in PRIVATE:
                raise ValueError('Private/runtime file in delivery inputs: ' + str(p))
            yield p


def source_files():
    paths = [ROOT / name for name in ['global.json', 'Directory.Build.props', 'Directory.Packages.props', 'HanMate.slnx']]
    for project in PROJECTS:
        paths.extend(files_under(ROOT / project))
    for folder in ['specs', 'examples', 'archive/v2_baseline/specs', 'archive/v2_baseline/examples']:
        paths.extend(files_under(PACK / folder))
    paths.extend(ROOT / 'tools' / name for name in TOOLS)
    paths.extend((ROOT / 'tools').glob('*.tsv'))
    paths.extend((ROOT / 'tools').glob('*.py'))
    paths.extend(files_under(ROOT / 'tools/content'))
    # Rebuilding the handoff also needs its curated documentation and review data.
    paths.extend(files_under(PACK / 'delivery'))
    paths.extend(files_under(PACK / 'tracking/review'))
    paths.append(PACK / 'tracking/environment-lock.json')
    # release_preflight reads this file; its scope is explicitly internal in ADR-106.
    paths.append(PACK / 'tracking/backlog.csv')
    paths.append(PACK / 'tracking/deferred-release-gates.json')
    paths.append(PACK / 'tracking/internal-delivery-scope.json')
    paths.append(PACK / 'tracking/STATUS.md')
    paths.append(PACK / 'docs/26_Decisions_and_Open_Items.md')
    paths.append(PACK / 'AGENTS.md')
    return sorted(set(paths))


def inventory(paths, root):
    return [{'path': p.relative_to(root).as_posix(), 'bytes': p.stat().st_size, 'sha256': sha(p)} for p in paths]


def write_json(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def compiled_inputs(entries):
    # Administrative handoff/status documents may be finalized after building.
    # Code, dependencies, embedded resources and linked schema/fixtures may not.
    roots = set(PROJECTS) | {'global.json', 'Directory.Build.props', 'Directory.Packages.props', 'HanMate.slnx'}
    prefixes = ('HanMate_Planning_Pack_v3.0/specs/', 'HanMate_Planning_Pack_v3.0/examples/',
                'HanMate_Planning_Pack_v3.0/archive/v2_baseline/specs/', 'HanMate_Planning_Pack_v3.0/archive/v2_baseline/examples/')
    return [e for e in entries if PurePosixPath(e['path']).parts[0] in roots or e['path'].startswith(prefixes)]


def zip_files(output, paths, root):
    with zipfile.ZipFile(output, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=1) as archive:
        for p in paths:
            archive.write(p, p.relative_to(root).as_posix())


def verify_zip(path, entries):
    with zipfile.ZipFile(path) as archive:
        expected = {x['path']: x for x in entries}
        names = archive.namelist()
        if len(names) != len(set(names)) or set(names) != set(expected):
            raise ValueError('Archive file set mismatch: ' + path.name)
        for name in names:
            part = PurePosixPath(name)
            if part.is_absolute() or '..' in part.parts or '\\' in name or ':' in name:
                raise ValueError('Unsafe archive path')
            with archive.open(name) as stream:
                digest = hashlib.file_digest(stream, 'sha256').hexdigest()
            if digest != expected[name]['sha256'] or archive.getinfo(name).file_size != expected[name]['bytes']:
                raise ValueError('Archive content mismatch: ' + name)


def assemble(args):
    source = source_files()
    before = json.loads(args.source_before.read_text(encoding='utf-8'))
    current = inventory(source, ROOT)
    if compiled_inputs(before) != compiled_inputs(current):
        raise ValueError('Compiled inputs changed during the build; rebuild from a stable snapshot.')
    windows = args.windows.resolve(strict=True)
    for name in ['HanMate.App.exe', 'HanMate.App.dll', 'AppxManifest.xml']:
        if not (windows / name).is_file():
            raise ValueError('Missing published file: ' + name)
    if sha(windows / 'AppxManifest.xml') != sha(ROOT / 'HanMate.App/Platforms/Windows/Package.appxmanifest'):
        raise ValueError('Published microphone capability manifest differs from source.')
    preflight = json.loads(args.preflight.read_text(encoding='utf-8'))
    if preflight['artifact']['sha256'] != sha(args.apk):
        raise ValueError('Preflight does not match the selected APK.')
    required = {'apk_permissions', 'apk_backup_disabled', 'apk_share_scope'}
    if {x['check'] for x in preflight['checks'] if x['status'] == 'PASS'} & required != required:
        raise ValueError('Compiled APK privacy checks did not pass.')
    if args.output.exists():
        raise ValueError('Output already exists; delivery packages are immutable.')
    args.output.mkdir(parents=True)
    window_files = list(files_under(windows))
    window_inventory = inventory(window_files, windows)
    zip_files(args.output / 'HanMate-Windows-x64-internal.zip', window_files, windows)
    zip_files(args.output / 'HanMate-source-internal.zip', source, ROOT)
    shutil.copyfile(args.apk, args.output / 'HanMate-Android-development-signed.apk')
    shutil.copyfile(args.preflight, args.output / 'release-preflight.json')
    logs = args.output / 'build-logs'
    logs.mkdir()
    for name in ['restore-locked.log', 'windows-publish.log', 'android-build.log', 'android-signature.log']:
        log = args.preflight.parent / name
        if not log.is_file():
            raise ValueError('Missing build evidence: ' + name)
        shutil.copyfile(log, logs / name)
    shutil.copytree(PACK / 'delivery', args.output / 'guide')
    # Content audit contains source-derived text/hash data, no user recordings or database.
    shutil.copytree(PACK / 'tracking/review', args.output / 'content-review')
    # Make the review HTML usable without unpacking the source archive.
    review = json.loads((args.output / 'content-review/content-audit.json').read_text(encoding='utf-8'))
    audio_root = args.output / 'content-review/audio'
    audio_root.mkdir()
    html_path = args.output / 'content-review/content-review.html'
    html = html_path.read_text(encoding='utf-8')
    import os
    for entry in review['audio']:
        source_audio = ROOT / 'HanMate.App/Resources/Raw' / entry['file']
        old_link = os.path.relpath(source_audio, PACK / 'tracking/review').replace('\\', '/')
        target_audio = audio_root / source_audio.name
        if target_audio.exists() and sha(target_audio) != sha(source_audio):
            raise ValueError('Audio review filename collision')
        shutil.copyfile(source_audio, target_audio)
        html = html.replace(old_link, 'audio/' + source_audio.name)
    html_path.write_text(html, encoding='utf-8')
    shutil.copyfile(PACK / 'tracking/environment-lock.json', args.output / 'environment-lock.json')
    shutil.copyfile(PACK / 'tracking/deferred-release-gates.json', args.output / 'deferred-release-gates.json')
    write_json(args.output / 'source-files.json', current)
    write_json(args.output / 'windows-files.json', window_inventory)
    write_json(args.output / 'delivery-manifest.json', {
        'schemaVersion': 1, 'scope': 'INTERNAL_DEVELOPMENT_ONLY', 'publicReleaseReady': False,
        'applicationTests': 'DEFERRED_BY_USER', 'installedOnDevice': False,
        'androidSigning': 'development key; not a distribution identity',
        'windowsSigning': 'unpackaged; no distribution signing claim',
        'files': inventory(list(files_under(args.output)), args.output),
    })
    print('Assembled internal build and source archives; no formal review was approved.')


def verify(output):
    manifest = json.loads((output / 'delivery-manifest.json').read_text(encoding='utf-8'))
    expected = manifest['files']
    actual = inventory([p for p in files_under(output) if p.name != 'delivery-manifest.json'], output)
    if actual != expected:
        raise ValueError('Delivery file set or hashes differ.')
    verify_zip(output / 'HanMate-Windows-x64-internal.zip', json.loads((output / 'windows-files.json').read_text(encoding='utf-8')))
    verify_zip(output / 'HanMate-source-internal.zip', json.loads((output / 'source-files.json').read_text(encoding='utf-8')))
    print(f'PASS: {len(actual)} handoff files and both complete archive inventories match; application tests were not run.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['snapshot', 'assemble', 'verify'])
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--windows', type=Path)
    parser.add_argument('--apk', type=Path)
    parser.add_argument('--source-before', type=Path)
    parser.add_argument('--preflight', type=Path)
    args = parser.parse_args()
    if args.action == 'snapshot':
        write_json(args.output, inventory(source_files(), ROOT))
        print('Recorded build/source inputs.')
    elif args.action == 'assemble':
        if any(getattr(args, name) is None for name in ['windows', 'apk', 'source_before', 'preflight']):
            parser.error('assemble requires --windows --apk --source-before --preflight')
        assemble(args)
    else:
        verify(args.output)


if __name__ == '__main__':
    main()
