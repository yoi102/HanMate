"""Small ADB UI evidence helper. No database writes, installs, permission changes or data clearing.
Snapshots go to private TEMP unless --out is supplied. Targets use fresh exact resource IDs/descriptions.
"""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import time
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser()
parser.add_argument('--adb', default=r'C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe')
parser.add_argument('--serial', default='emulator-5554')
parser.add_argument('--package', default='com.companyname.hanmate.app',
                    choices=['com.companyname.hanmate.app', 'com.google.android.documentsui', 'com.android.intentresolver', 'com.android.permissioncontroller', 'android'],
                    help='Explicit foreground owner for app, document picker, or system share sheet.')
parser.add_argument('--out', type=Path, default=Path(os.environ['TEMP']) / 'HanMate-stage-a-20260919/ui')
parser.add_argument('--points', help='For stroke only: JSON pairs normalized inside the observed pad bounds.')
parser.add_argument('--within-text', help='Limit a repeated action to the nearest card containing this exact observed text.')
parser.add_argument('action', choices=['snapshot', 'tap', 'hold', 'back', 'scroll-down', 'scroll-up', 'key', 'stroke'])
parser.add_argument('target', nargs='?')
args = parser.parse_args()
package = args.package


def adb(*parts):
    return subprocess.run([args.adb, '-s', args.serial, *parts], check=True, capture_output=True, timeout=25).stdout


def observe():
    adb('shell', 'uiautomator', 'dump', '/sdcard/hanmate-stage-a-ui.xml')
    xml = adb('shell', 'cat', '/sdcard/hanmate-stage-a-ui.xml')
    tree = ET.fromstring(xml)
    nodes = list(tree.iter('node'))
    args.out.mkdir(parents=True, exist_ok=True)
    stem = args.out / str(time.time_ns())
    stem.with_suffix('.xml').write_bytes(xml)
    stem.with_suffix('.png').write_bytes(adb('exec-out', 'screencap', '-p'))
    return nodes, stem


nodes, stem = observe()
if args.action != 'snapshot':
    if not any(n.get('package') == package for n in nodes):
        raise RuntimeError('Expected package is not the observed app: ' + package)
    if args.action == 'back':
        adb('shell', 'input', 'keyevent', '4')
    elif args.action == 'key':
        keys = {'tab': 61, 'enter': 66, 'home': 122, 'end': 123, 'down': 20, 'up': 19}
        if args.target not in keys:
            raise ValueError('Allowed navigation keys: ' + ', '.join(keys))
        adb('shell', 'input', 'keyevent', str(keys[args.target]))
    elif args.action in ('scroll-down', 'scroll-up'):
        scrolls = [n for n in nodes if n.get('package') == package and n.get('scrollable') == 'true']
        if args.target:
            if not args.target.startswith(('class:', 'id:')):
                raise ValueError('Scroll target must be an exact class: or id: selector')
            field, value = args.target.split(':', 1)
            field = 'class' if field == 'class' else 'resource-id'
            scrolls = [n for n in scrolls if n.get(field) == value]
        if len(scrolls) != 1:
            raise ValueError('Expected one visible scroll container')
        x1, y1, x2, y2 = map(int, re.findall(r'\d+', scrolls[0].get('bounds')))
        if x2 <= x1 or y2 - y1 < 100:
            raise ValueError('Scroll container outside visible bounds')
        x, top, bottom = (x1 + x2) // 2, y1 + (y2 - y1) // 4, y2 - (y2 - y1) // 4
        start, end = (bottom, top) if args.action == 'scroll-down' else (top, bottom)
        adb('shell', 'input', 'swipe', str(x), str(start), str(x), str(end), '450')
    else:
        if not args.target:
            raise ValueError('Exact resource ID, desc:description or text:visible text required')
        field, value = (('content-desc', args.target[5:]) if args.target.startswith('desc:') else
                        ('text', args.target[5:]) if args.target.startswith('text:') else
                        ('resource-id', args.target if ':id/' in args.target else package + ':id/' + args.target))
        matches = [n for n in nodes if n.get(field) == value and n.get('package') == package]
        if args.within_text:
            labels = [n for n in nodes if n.get('text') == args.within_text and n.get('package') == package]
            if len(labels) != 1:
                raise ValueError('Expected one exact card label')
            parents = {child: parent for parent in nodes for child in parent}
            ancestor = labels[0]
            while ancestor is not None:
                scoped = [n for n in matches if n in list(ancestor.iter('node'))]
                if scoped:
                    matches = scoped
                    break
                ancestor = parents.get(ancestor)
            else:
                raise ValueError('Target is not in the observed card')
        if len(matches) != 1:
            raise ValueError(f'Expected unique target; found {len(matches)}')
        node = matches[0]
        if node.get('enabled') != 'true':
            raise ValueError('Target disabled')
        x1, y1, x2, y2 = map(int, re.findall(r'\d+', node.get('bounds')))
        if x1 >= x2 or y1 >= y2:
            raise ValueError('Target outside visible bounds')
        x, y = str((x1 + x2) // 2), str((y1 + y2) // 2)
        if args.action == 'stroke':
            if node.get('resource-id') != package + ':id/Handwriting.Pad':
                raise ValueError('Strokes require the observed handwriting pad')
            points = json.loads(args.points or '[]')
            if not 2 <= len(points) <= 64 or any(len(p) != 2 or any(not isinstance(v, (int, float)) or not 0.02 <= v <= 0.98 for v in p) for p in points):
                raise ValueError('Expected 2-64 interior normalized points')
            positions = [(str(round(x1 + p[0]*(x2-x1))), str(round(y1 + p[1]*(y2-y1)))) for p in points]
            try:
                adb('shell', 'input', 'motionevent', 'DOWN', *positions[0])
                for position in positions[1:]:
                    adb('shell', 'input', 'motionevent', 'MOVE', *position)
            finally:
                adb('shell', 'input', 'motionevent', 'UP', *positions[-1])
        elif args.action == 'hold':
            adb('shell', 'input', 'swipe', x, y, x, y, '650')
        else:
            adb('shell', 'input', 'tap', x, y)
    nodes, stem = observe()
summary = [{k: n.get(k) for k in ('resource-id', 'text', 'content-desc', 'enabled', 'bounds')}
           for n in nodes if n.get('text') or n.get('content-desc') or ':id/Voice.' in n.get('resource-id', '')]
print('snapshot: ' + str(stem))
for node in summary:
    print(' | '.join(node.values()))
with (args.out / 'actions.jsonl').open('a', encoding='utf-8') as log:
    log.write(json.dumps(dict(time=time.time(), action=args.action, package=package, target=args.target, points=args.points, snapshot=str(stem)), ensure_ascii=False) + '\n')
