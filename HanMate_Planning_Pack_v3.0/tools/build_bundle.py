#!/usr/bin/env python3
"""Generate derived planning documents and byte manifest, or check them without writes."""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SUMMARY = 'HanMate_Complete_Planning_v3.0.md'
MANIFEST = 'FILE_MANIFEST.json'


def read_rows(name):
    with (ROOT / 'tracking' / name).open(encoding='utf-8', newline='') as stream:
        return list(csv.DictReader(stream))


def task_dependencies(rows):
    """Expand W1-04..06 and M1..M4; reject unknown references and cycles."""
    ids = {row['taskId'] for row in rows}
    graph = {}
    for row in rows:
        deps = set()
        for part in row['dependencies'].split(','):
            part = part.strip()
            if part == '无':
                continue
            if part in ids:
                deps.add(part)
                continue
            task_range = re.fullmatch(r'(W\d+)-(\d+)\.\.(\d+)', part)
            stage_range = re.fullmatch(r'M(\d+)\.\.M(\d+)', part)
            if task_range:
                prefix, first, last = task_range.groups()
                if int(first) > int(last):
                    raise ValueError(f'reversed dependency range: {part}')
                deps.update(f'{prefix}-{n:02d}' for n in range(int(first), int(last) + 1))
            elif stage_range:
                first, last = map(int, stage_range.groups())
                if first > last:
                    raise ValueError(f'reversed dependency range: {part}')
                for stage in range(first, last + 1):
                    stage_ids = {key for key in ids if key.startswith(f'W{stage}-')}
                    if not stage_ids:
                        raise ValueError(f'unknown stage: M{stage}')
                    deps.update(stage_ids)
            else:
                raise ValueError(f'unknown dependency: {row["taskId"]} -> {part}')
        if not deps <= ids:
            raise ValueError(f'unknown tasks: {deps - ids}')
        graph[row['taskId']] = deps
    complete, visiting = set(), set()

    def visit(key):
        if key in visiting:
            raise ValueError(f'dependency cycle at {key}')
        if key in complete:
            return
        visiting.add(key)
        for dep in sorted(graph[key]):
            visit(dep)
        visiting.remove(key)
        complete.add(key)

    for key in graph:
        visit(key)
    return graph


def task_document(rows):
    path = ROOT / 'docs/21_Implementation_Backlog.md'
    source = path.read_text(encoding='utf-8')
    before, rest = source.split('## 1. 任务表', 1)
    _, after = rest.split('## 2. 执行说明', 1)
    table = ['## 1. 任务表', '', '<!-- Generated from tracking/backlog.csv by tools/build_bundle.py -->', '',
             '| ID | 工作包 | 前置 | 交付与验收 |', '|---|---|---|---|']
    for row in rows:
        values = [row[key].replace('|', '\\|') for key in ('taskId', 'title', 'dependencies', 'acceptance')]
        table.append('| ' + ' | '.join(values) + ' |')
    return before + '\n'.join(table) + '\n\n## 2. 执行说明' + after


def case_document(rows):
    lines = ['# 29 · 逐项验收用例', '', '> HanMate / 汉语小伴 · v3.0 · 2026-09-14', '',
             f'共 {len(rows)} 项应用用例；预期和汇总状态真源为 `tracking/acceptance-cases.csv`。',
             '本文件由 `tools/build_bundle.py` 生成。实际执行须另附平台证据；文档自检不提升应用状态。', '']
    category = None
    for row in rows:
        if row['category'] != category:
            category = row['category']
            lines.extend([f'## {category}', ''])
        lines.extend([f'### {row["testId"]} · {row["title"]}',
                      f'需求：{row["requirements"]}；优先级：{row["priority"]}；层级：{row["level"]}；平台：{row["platforms"]}；状态：**{row["status"]}**。',
                      f'前置：{row["precondition"]}', f'操作：{row["steps"]}', f'期望：{row["expected"]}', ''])
    return '\n'.join(lines)


def aggregate(documents):
    lines = ['# HanMate · 汉语小伴 · 完整规划与开发细节 v3.0', '',
             '> 2026-09-14 · .NET MAUI · iOS / Android / Windows', '',
             '由 tools/build_bundle.py 自动生成，专题正文以 docs 为准，任务/用例表以 tracking CSV 为准。',
             '当前相邻应用为 MAUI 模板，业务功能未实现。配套 Schema、SQL 和样例使用本规划目录。', '',
             '[复审结论](REVIEW_NOTES.md) · [用户需求](USER_REQUIREMENTS.md) · [开发规则](AGENTS.md) · [实际校验报告](VALIDATION_REPORT.md)', '',
             '## 阅读目录', '', '| 章节 | 主题 |', '|---|---|']
    for name, content in documents.items():
        title = content.splitlines()[0].removeprefix('# ')
        lines.append(f'| {name[:2]} | [{title}](#doc-{name[:2]}) |')
    for name, content in documents.items():
        # Relative links in a docs file must resolve from the package root here.
        def rewrite(match):
            target = match.group(1)
            if target.startswith(('https:', 'http:', 'mailto:', '#')):
                return match.group(0)
            return '](' + 'docs/' + target + ')'
        body = re.sub(r'\]\(([^)]+)\)', rewrite, content)
        lines.extend(['', '---', '', f'<a id="doc-{name[:2]}"></a>', '', body.rstrip()])
    return '\n'.join(lines).rstrip() + '\n'


def outputs():
    tasks = read_rows('backlog.csv')
    task_dependencies(tasks)
    generated = {'docs/21_Implementation_Backlog.md': task_document(tasks),
                 'docs/29_Acceptance_Cases.md': case_document(read_rows('acceptance-cases.csv'))}
    documents = {p.name: generated.get('docs/' + p.name, p.read_text(encoding='utf-8'))
                 for p in sorted((ROOT / 'docs').glob('*.md'))}
    generated[SUMMARY] = aggregate(documents)
    return generated


def excluded(path):
    return path.name == MANIFEST or '__pycache__' in path.parts or path.suffix == '.pyc'


def manifest():
    files = []
    for path in sorted(ROOT.rglob('*')):
        if not path.is_file() or excluded(path):
            continue
        data = path.read_bytes()
        files.append({'path': path.relative_to(ROOT).as_posix(), 'byteLength': len(data),
                      'sha256': hashlib.sha256(data).hexdigest()})
    return {'documentVersion': '3.0', 'productName': 'HanMate', 'productNameZh': '汉语小伴',
            'date': '2026-09-14',
            'scope': 'Document/contract/example integrity; not a digital signature or application validation',
            'excludes': [MANIFEST, '**/__pycache__/**', '**/*.pyc'], 'fileCount': len(files), 'files': files}


def check_generated():
    for relative, expected in outputs().items():
        if (ROOT / relative).read_text(encoding='utf-8') != expected:
            raise ValueError(f'stale generated document: {relative}; run tools/build_bundle.py --write')
    return 'task table, acceptance-case document and topic aggregate match their sources'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument('--write', action='store_true')
    group.add_argument('--check', action='store_true')
    args = parser.parse_args()
    if args.write:
        for relative, content in outputs().items():
            (ROOT / relative).write_text(content, encoding='utf-8', newline='\n')
        (ROOT / MANIFEST).write_text(json.dumps(manifest(), ensure_ascii=False, indent=2) + '\n', encoding='utf-8', newline='\n')
        print('Generated task/case documents, aggregate and byte manifest; no application status changed.')
    else:
        print(check_generated())
        actual = json.loads((ROOT / MANIFEST).read_text(encoding='utf-8'))
        expected = manifest()
        if actual != expected:
            raise ValueError('FILE_MANIFEST.json is stale or file bytes changed; review changes before regeneration')
        print(f'PASS: {expected["fileCount"]} file hashes and complete file set match.')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
