"""Hash-bound, exhaustive review batches for the shipped default dictionary. No approvals inferred."""
import gzip
import hashlib
import json
from pathlib import Path
import shutil
import sqlite3
import tempfile

BATCH_SIZE = 1000


def batches(rows, size=BATCH_SIZE):
    """Input ordering is fixed by frequency, title, then UUID; every row occurs exactly once."""
    if size < 1:
        raise ValueError('Batch size must be positive')
    batch = []
    for identity, title, raw in rows:
        batch.append(dict(id=identity, title=title, raw=raw,
                          sha256=hashlib.sha256(raw.encode('utf-8')).hexdigest()))
        if len(batch) == size:
            yield batch
            batch = []
    if batch:
        yield batch


def scan(root, export_batch=None):
    directory = Path(root) / 'HanMate.Infrastructure/Dictionary'
    notice_bytes = (directory / 'XINHUA-NOTICE.json').read_bytes()
    notice = json.loads(notice_bytes)
    archive = directory / 'xinhua.sqlite.gz'
    with archive.open('rb') as file:
        compressed_hash = hashlib.file_digest(file, 'sha256').hexdigest()
    if compressed_hash != notice['gzipSha256']:
        raise ValueError('Default dictionary gzip hash mismatch')
    library = dict(notice=notice, noticeSha256=hashlib.sha256(notice_bytes).hexdigest(),
                   gzipSha256=compressed_hash, batchSize=BATCH_SIZE,
                   order='sense_order,title,id', formatVersion=1)
    manifests, exported = [], None
    with tempfile.TemporaryDirectory(prefix='hanmate-dictionary-review-') as temp:
        database = Path(temp) / 'dictionary.sqlite'
        with gzip.open(archive, 'rb') as source, database.open('wb') as target:
            shutil.copyfileobj(source, target)
        with database.open('rb') as file:
            if hashlib.file_digest(file, 'sha256').hexdigest() != notice['sqliteSha256']:
                raise ValueError('Default dictionary SQLite hash mismatch')
        connection = sqlite3.connect(database.as_uri() + '?mode=ro', uri=True)
        try:
            rows = connection.execute('SELECT id,title,raw_json FROM entry ORDER BY sense_order,title,id')
            for number, batch in enumerate(batches(rows), 1):
                batch_id = f'dictionary-batch:xinhua:{number:04}'
                manifests.append(dict(subjectId=batch_id, entries=len(batch), first=batch[0]['title'],
                    last=batch[-1]['title'], membersSha256=hashlib.sha256(
                        json.dumps([(r['id'], r['sha256']) for r in batch], separators=(',', ':')).encode()).hexdigest()))
                if batch_id == export_batch:
                    exported = [dict(id=r['id'], title=r['title'], sha256=r['sha256'], **{
                        'sourceRecord': json.loads(r['raw'])}) for r in batch]
        finally:
            connection.close()
    if sum(b['entries'] for b in manifests) != notice['entries']:
        raise ValueError('Default dictionary entry count mismatch')
    if export_batch is not None and exported is None:
        raise ValueError('Unknown dictionary review batch')
    return library, manifests, exported


if __name__ == '__main__':
    import argparse
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--batch', required=True, help='e.g. dictionary-batch:xinhua:0001')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    library, manifests, rows = scan(Path(__file__).resolve().parents[1], args.batch)
    payload = dict(library=library, batch=next(b for b in manifests if b['subjectId'] == args.batch),
                   scope='All entries in this batch; not a sample approval.', entries=rows)
    payload['reviewSubjectSha256'] = hashlib.sha256(json.dumps(
        dict(library=library, batch=payload['batch']), ensure_ascii=False, sort_keys=True,
        separators=(',', ':')).encode()).hexdigest()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'Exported {len(rows)} entries; no review decision was created.')
