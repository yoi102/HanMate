"""Check evidence bookkeeping only; never promote aggregate platform acceptance automatically."""
import csv
from datetime import datetime
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def rows(path):
    with path.open(encoding='utf-8-sig', newline='') as stream:
        return list(csv.DictReader(stream))


def main():
    cases = {r['testId']: r for name in ('acceptance-cases.csv', 'acceptance-supplement.csv')
             for r in rows(ROOT / 'tracking' / name)}
    executions = rows(ROOT / 'tracking/execution-ledger.csv')
    ids = set()
    for row in executions:
        identity = row['executionId']
        if not identity or identity in ids:
            raise ValueError('Duplicate or missing execution ID')
        ids.add(identity)
        if row['caseId'] not in cases:
            raise ValueError(f'{identity}: unknown case')
        if row['platform'] not in {'Shared', 'Windows', 'Android', 'iOS'}:
            raise ValueError(f'{identity}: invalid platform')
        if row['status'] not in {'PASS', 'FAIL', 'NOT RUN', 'BLOCKED'}:
            raise ValueError(f'{identity}: invalid result')
        if row['coverage'] not in {'full', 'partial', 'none'}:
            raise ValueError(f'{identity}: invalid coverage')
        if row['layer'] not in {'unit', 'integration', 'native', 'platform'}:
            raise ValueError(f'{identity}: invalid execution layer')
        if not row['scope'] or not row['device']:
            raise ValueError(f'{identity}: missing scope/device')
        datetime.fromisoformat(row['executedAt'])
        if row['status'] in {'PASS', 'FAIL'}:
            if row['coverage'] == 'none' or not re.fullmatch('[a-f0-9]{64}', row['buildSha256']):
                raise ValueError(f'{identity}: executed result requires coverage and exact build hash')
            evidence = (ROOT / row['evidence']).resolve()
            if not evidence.is_relative_to(ROOT) or not evidence.is_file():
                raise ValueError(f'{identity}: missing local evidence')
        if row['platform'] == 'Shared' and row['layer'] in {'native', 'platform'}:
            raise ValueError(f'{identity}: platform execution cannot be Shared')
        if row['status'] == 'PASS' and row['platform'] not in cases[row['caseId']]['platforms'].split(';'):
            raise ValueError(f'{identity}: platform outside case scope')
    print(f'PASS: {len(executions)} scoped execution rows; original 176 aggregate statuses were not changed.')


if __name__ == '__main__':
    main()
