#!/usr/bin/env python3
"""校验本规划包的链接、Schema、样例、ZIP 清单和 SQLite DDL。
这不是应用测试、生产导入器、拼音引擎、解码器或跨平台兼容验证。
运行：python tools/verify_bundle.py [--write-report]
依赖：python -m pip install -r tools/requirements.txt
"""
from __future__ import annotations
import argparse, copy, csv, hashlib, io, json, re, sqlite3, sys, unicodedata, zipfile
from pathlib import Path
import jsonschema
import regex

ROOT=Path(__file__).resolve().parents[1]
CHECKS=[]
def check(name, action):
    try:
        note=action()
        CHECKS.append((name,'PASS',str(note or '检查通过')))
    except Exception as exc:
        CHECKS.append((name,'FAIL',f'{type(exc).__name__}: {exc}'))

def strict_load(data):
    def pairs(xs):
        result={}
        for k,v in xs:
            if k in result: raise ValueError('duplicate JSON property: '+k)
            result[k]=v
        return result
    def invalid(c):raise ValueError('invalid JSON number '+c)
    return json.loads(data,object_pairs_hook=pairs,parse_constant=invalid)

def schemas():return {p.name:strict_load(p.read_bytes()) for p in (ROOT/'specs').glob('*.schema.json')}
SC=schemas()
def expand(value):
    if isinstance(value,dict):
        if '$ref' in value:return expand(SC[value['$ref']])
        return {k:expand(v) for k,v in value.items() if k!='$id'}
    if isinstance(value,list):return [expand(v) for v in value]
    return value

def validate(name,data):
    jsonschema.Draft202012Validator(expand(SC[name]),format_checker=jsonschema.FormatChecker()).validate(data)

def ensure(condition,message):
    if not condition:raise AssertionError(message)

def sha(b):return hashlib.sha256(b).hexdigest()
def elements(text):return regex.findall(r'\X',text)
def expected_pinyin(p):
    # 只检查本包普通元音样例，不冒充生产拼音语法验证器。
    base=p['base'];tone=p['tone']
    if tone:
        idx=base.find('a') if 'a' in base else base.find('e') if 'e' in base else base.find('o') if 'ou' in base else max((i for i,c in enumerate(base) if c in 'aiouü'),default=-1)
        ensure(idx>=0,'unsupported vowel in fixture')
        table={'a':'āáǎà','o':'ōóǒò','e':'ēéěè','i':'īíǐì','u':'ūúǔù','ü':'ǖǘǚǜ'}
        base=base[:idx]+table[base[idx]][tone-1]+base[idx+1:]
    return unicodedata.normalize('NFC',base+('r' if p['erhua'] else ''))

def validate_contents(payload):
    seen=set();targets={};contentids=set()
    def unique(x):ensure(x not in seen,'duplicate entity UUID '+x);seen.add(x)
    for c in payload['contents']:
        unique(c['id']);contentids.add(c['id']);roles=[u['role'] for u in c['textUnits']]
        ensure((c['kind']=='word' and roles.count('headword')==1 and all(x in ('headword','example') for x in roles)) or (c['kind']!='word' and roles==['body']),'invalid TextUnit roles')
        stage=c['schoolStage'];grade=c['grade']
        ensure((stage is None and grade is None) or (stage is not None and (grade is None or 1<=grade<=(6 if stage=='primary' else 3))),'invalid grade')
        total=0
        for u in c['textUnits']:
            unique(u['id']);targets[u['id']]=u['text'];es=elements(u['text']);total+=len(es)
            bounds=[0]
            for e in es:bounds.append(bounds[-1]+len(e.encode('utf-16-le'))//2)
            ensure(bounds==u['elementBoundariesUtf16'],'UTF-16/text-element map mismatch')
            tokenmap={};pos=0
            for t in u['tokens']:
                unique(t['id']);tokenmap[t['id']]=t
                ensure(t['start']==pos and t['length']>0,'noncontiguous token')
                ensure(t['text']==''.join(es[pos:pos+t['length']]),'token text slice mismatch')
                pos+=t['length'];p=t['pinyin']
                if p:
                    ensure(t['kind']=='hanzi','non-Han forced pinyin')
                    ensure(p['display']==expected_pinyin(p),'tone display mismatch')
                    ensure(unicodedata.normalize('NFC',p['display'])==p['display'],'pinyin not NFC')
            ensure(pos==len(es),'tokens do not cover original')
            pos=0;tokenseen=[]
            for s in u['segments']:
                unique(s['id']);ensure(s['start']==pos,'noncontiguous segments')
                ensure(s['text']==''.join(es[pos:pos+s['length']]),'segment text mismatch')
                for tid in s['tokenIds']:
                    ensure(tid in tokenmap,'missing token reference')
                    t=tokenmap[tid];ensure(pos<=t['start'] and t['start']+t['length']<=pos+s['length'],'token crosses segment')
                ensure(''.join(tokenmap[x]['text'] for x in s['tokenIds'])==s['text'],'segment token order mismatch')
                tokenseen+=s['tokenIds'];pos+=s['length']
                if s['kind']=='speech':targets[s['id']]=s['text']
            ensure(pos==len(es) and tokenseen==list(tokenmap),'segment coverage mismatch')
            if u['role']=='headword':ensure(len(es)<=64,'headword exceeds product limit')
        ensure(total<=20000,'content limit exceeded')
    return contentids,targets

def verify_package(path):
    limits=strict_load((ROOT/'specs/limits.json').read_bytes())
    ensure(path.stat().st_size<=limits['maxArchiveBytes'],'archive limit')
    with zipfile.ZipFile(path) as z:
        infos=z.infolist();names=[i.filename for i in infos]
        ensure(len(names)<=limits['maxArchiveEntries'],'entry count limit')
        ensure(len(names)==len(set(n.casefold() for n in names)),'duplicate paths')
        total=0;raw={}
        for i in infos:
            n=i.filename
            ensure(bool(re.fullmatch(r'(manifest\.json|contents\.json|settings\.json|collections\.json|audio/index\.json|audio/[0-9a-f]{64}\.(wav|mp3|m4a))',n)),'unsafe or unlisted path')
            ensure((i.external_attr>>16)&0o170000!=0o120000,'symbolic link')
            cap=limits['maxManifestBytes'] if n=='manifest.json' else limits['maxJsonBytes'] if n.endswith('.json') else limits['maxAudioBytes']
            parts=[];size=0
            with z.open(i) as stream:
                while block:=stream.read(65536):
                    size+=len(block);total+=len(block)
                    ensure(size<=cap and total<=limits['maxExpandedBytes'],'stream expansion limit')
                    parts.append(block)
            raw[n]=b''.join(parts)
        # 开发样例校验保留有上限的数据于内存；正式应用需流式落暂存文件。
        m=strict_load(raw['manifest.json']);validate('package-manifest.schema.json',m)
        fl={f['path']:f for f in m['files']};ensure(len(fl)==len(m['files']),'duplicate manifest path')
        required={'contents.json','audio/index.json'} | ({'settings.json','collections.json'} if m['packageType']=='backup' else set())
        ensure(required<=set(fl),'missing mandatory file')
        if m['packageType']=='content':ensure(not {'settings.json','collections.json'}&set(fl),'content package includes private settings')
        ensure(set(raw)-{'manifest.json'}==set(fl),'manifest file set mismatch')
        for name,f in fl.items():ensure(len(raw[name])==f['byteLength'] and sha(raw[name])==f['sha256'],'file hash/length mismatch')
        contents=strict_load(raw['contents.json']);validate('contents.schema.json',contents);ids,targets=validate_contents(contents)
        ai=strict_load(raw['audio/index.json']);validate('audio-index.schema.json',ai)
        assets={a['id']:a for a in ai['assets']};bindings={b['id']:b for b in ai['bindings']}
        ensure(len(assets)==len(ai['assets']) and len(bindings)==len(ai['bindings']),'duplicate audio ID')
        for a in assets.values():
            if a['storage']=='package':ensure(a['path'] in raw and sha(raw[a['path']])==a['sha256'] and len(raw[a['path']])==a['byteLength'],'audio payload mismatch')
            if m['packageType']=='backup' and a['origin']=='user':ensure(a['storage']=='package','user audio omitted')
        for b in bindings.values():ensure(b['targetId'] in targets and b['assetId'] in assets,'dangling audio binding')
        prefseen=set()
        for p in ai['preferences']:
            ensure(p['targetId'] not in prefseen,'duplicate preference');prefseen.add(p['targetId'])
            ensure(p['bindingId'] in bindings and bindings[p['bindingId']]['targetId']==p['targetId'],'wrong target preference')
        cols={'folders':[],'items':[]}
        if m['packageType']=='backup':
            cols=strict_load(raw['collections.json']);validate('collections.schema.json',cols)
            folders={f['id']:f for f in cols['folders']};ensure(len(folders)==len(cols['folders']),'duplicate folder')
            ensure(sum(f['systemRole']=='default' for f in folders.values())==1,'invalid default folder count')
            ensure(len({(v['folderId'],v['contentId']) for v in cols['items']})==len(cols['items']),'duplicate favorite')
            for v in cols['items']:ensure(v['folderId'] in folders and v['contentId'] in ids,'dangling favorite')
            st=strict_load(raw['settings.json']);validate('settings.schema.json',st)
            ensure(st['favorites']['lastFolderId'] is None or st['favorites']['lastFolderId'] in folders,'last folder reference missing')
        actual={'contents':len(contents['contents']),'folders':len(cols['folders']),'favorites':len(cols['items']),'audioAssets':len(assets),'audioBindings':len(bindings)}
        ensure(m['counts']==actual,'manifest count mismatch')
        return f"{len(raw)} files; {len(ids)} contents; hashes and fixture references valid; audio assets={len(assets)}"

def check_links():
    total=0
    for p in ROOT.rglob('*.md'):
        if 'archive' in p.parts:continue
        text=p.read_text(encoding='utf-8');ensure(text.count('```')%2==0,f'unbalanced code fence: {p}')
        for dest in re.findall(r'\]\(([^)]+)\)',text):
            if dest.startswith(('http:','https:','#','mailto:')):continue
            f=dest.split('#')[0]
            if not f:continue
            ensure((p.parent/f).exists(),f'broken link: {p.relative_to(ROOT)} -> {f}');total+=1
    return f'{total} local Markdown links exist; fences balanced (excluding archive)'

def check_schemas():
    for s in SC.values():jsonschema.Draft202012Validator.check_schema(s)
    return f'{len(SC)} Draft 2020-12 schemas structurally valid'

def check_fixture_json():
    pairs={'contents.json':'contents.schema.json','collections.json':'collections.schema.json','settings.json':'settings.schema.json','audio-index.json':'audio-index.schema.json','pinyin-catalog.json':'pinyin-catalog.schema.json','content-manifest.json':'package-manifest.schema.json','backup-manifest.json':'package-manifest.schema.json'}
    for f,s in pairs.items():validate(s,strict_load((ROOT/'examples'/f).read_bytes()))
    data=strict_load((ROOT/'examples/contents.json').read_bytes());ids,targets=validate_contents(data)
    # 高亮必须指向实际存在的拼音文本元素。
    cat=strict_load((ROOT/'examples/pinyin-catalog.json').read_bytes())
    cs={c['id']:c for c in data['contents']}
    for item in cat['items']:
        ensure([x['tone'] for x in item['examples']]==[1,2,3,4],'tone example order')
        for ex in item['examples']:
            if ex['status']!='available':continue
            h=ex['highlight'];c=cs[h['contentId']];u=next(u for u in c['textUnits'] if u['id']==h['unitId']);t=next(t for t in u['tokens'] if t['id']==h['tokenId'])
            ensure(ex['contentId']==c['id'] and t['pinyin']['tone']==ex['tone'],'tone/example mismatch')
            parts=elements(t['pinyin']['display']);a=h['pinyinElementStart'];b=a+h['pinyinElementLength']
            ensure(0<=a<b<=len(parts) and ''.join(parts[a:b])=='m','fixture highlight mismatch')
    for x in strict_load((ROOT/'examples/segmentation-cases.json').read_bytes())['cases']:ensure(''.join(x['segments'])==x['text'],'segmentation fixture not conservative')
    return f'{len(pairs)} JSON fixtures; {len(ids)} contents; {len(targets)} playback targets; ranges, tones, highlighter checked'

def check_trace():
    with (ROOT/'tracking/acceptance-cases.csv').open(encoding='utf-8',newline='') as f:cases=list(csv.DictReader(f))
    with (ROOT/'tracking/requirements.csv').open(encoding='utf-8',newline='') as f:reqs=list(csv.DictReader(f))
    ensure(len({c['testId'] for c in cases})==len(cases),'duplicate test ID')
    ensure(all(c['status']=='NOT RUN' for c in cases),'unexecuted app cases falsely marked')
    alltests={c['testId'] for c in cases}
    for r in reqs:
        linked=set(r['tests'].split(';'));ensure(linked<=alltests and bool(linked),'uncovered requirement')
        for d in r['documents'].split(';'):ensure((ROOT/d).exists(),'missing requirement document')
    ensure({r['requirementId'] for r in reqs}=={f'R{i:02d}' for i in range(1,17)},'original requirement lost')
    return f'{len(reqs)} requirements mapped; {len(cases)} defined cases, all NOT RUN'

def check_sql():
    con=sqlite3.connect(':memory:');con.executescript((ROOT/'specs/database.sql').read_text())
    ensure(con.execute('PRAGMA foreign_keys').fetchone()[0]==1,'FK disabled')
    ensure(con.execute('PRAGMA user_version').fetchone()[0]==1,'wrong schema version')
    h='0'*64
    con.execute('INSERT INTO content VALUES(?,?,?,?,?,?,?,?,?,?,?,?)',('c','word','personal','词','{}',1,1,1,1,h,'2026-09-14T00:00:00Z','2026-09-14T00:00:00Z'))
    con.execute("INSERT INTO favorite_folder VALUES('f','默认','default','',0,'default')")
    con.execute("INSERT INTO favorite_item VALUES('f','c',0,'2026-09-14T00:00:00Z')")
    def rejected(sql,params=()):
        try:con.execute(sql,params)
        except sqlite3.IntegrityError:return
        raise AssertionError('constraint allowed invalid data: '+sql)
    rejected("INSERT INTO favorite_item VALUES('f','c',1,'2026-09-14T00:00:00Z')")
    rejected("INSERT INTO favorite_folder VALUES('f2','默认2','default2','',1,'default')")
    rejected("INSERT INTO favorite_item VALUES('f','absent',0,'2026-09-14T00:00:00Z')")
    for t in ('t1','t2'):con.execute('INSERT INTO playback_target VALUES(?,?,?,?,?,?)',(t,'c',None,'unit',h,h))
    con.execute('INSERT INTO audio_asset VALUES(?,?,?,?,?,?,?,?,?,?)',('a',h,'audio/'+h+'.wav',10,100,'wav','pcm_s16le',16000,1,'user'))
    con.execute('INSERT INTO audio_binding VALUES(?,?,?,?,?,?,?,?)',('b','t1','a',h,h,'confirmed','user','test'))
    rejected("INSERT INTO audio_preference VALUES('t2','b')")
    con.execute("INSERT INTO audio_preference VALUES('t1','b')")
    con.execute("DELETE FROM favorite_folder WHERE id='f'")
    ensure(con.execute('SELECT count(*) FROM content').fetchone()[0]==1,'folder deletion deleted content')
    ensure(not con.execute('PRAGMA foreign_key_check').fetchall(),'foreign key violation')
    ensure(con.execute('PRAGMA integrity_check').fetchone()[0]=='ok','integrity failed')
    n=con.execute("SELECT count(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'").fetchone()[0]
    con.close();return f'SQLite {sqlite3.sqlite_version}; {n} tables; duplicate favorites/default role/dangling refs/wrong-target audio rejected'

def check_rejections():
    import tempfile
    try:strict_load('{"a":1,"a":2}')
    except ValueError:pass
    else:raise AssertionError('duplicate property allowed')
    original=ROOT/'examples/sample-backup.hanbackup'
    with zipfile.ZipFile(original) as z:payload={i.filename:z.read(i) for i in z.infolist()}
    n=0
    with tempfile.TemporaryDirectory() as tmp:
        for kind in ['hash','path','count','missing','version']:
            changed=copy.deepcopy(payload)
            if kind=='hash':changed['contents.json']+=b' '
            elif kind=='path':changed['../escape.txt']=b'x'
            else:
                m=strict_load(changed['manifest.json'])
                if kind=='count':m['counts']['contents']+=1
                elif kind=='missing':del changed['audio/index.json']
                elif kind=='version':m['schemaVersion']=999
                changed['manifest.json']=json.dumps(m,ensure_ascii=False).encode()
            p=Path(tmp)/(kind+'.zip')
            with zipfile.ZipFile(p,'w') as z:
                for k,v in changed.items():z.writestr(k,v)
            try:verify_package(p)
            except (AssertionError,ValueError,KeyError,jsonschema.ValidationError):n+=1
            else:raise AssertionError('negative package accepted: '+kind)
    return f'{n} malformed packages and duplicate JSON properties rejected (fixture-level checks only)'

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--write-report',action='store_true');args=parser.parse_args()
    check('本地链接与代码围栏',check_links)
    check('JSON Schema 结构',check_schemas)
    check('样例 JSON 与字音引用',check_fixture_json)
    check('内容包清单/哈希',lambda:verify_package(ROOT/'examples/sample-content.hanpack'))
    check('备份包清单/哈希',lambda:verify_package(ROOT/'examples/sample-backup.hanbackup'))
    check('需求与用例追踪',check_trace)
    check('SQLite DDL 与约束',check_sql)
    check('破坏性样例拒绝',check_rejections)
    for name,status,note in CHECKS:print(f'{status}: {name}: {note}')
    fail=any(r[1]=='FAIL' for r in CHECKS)
    if args.write_report:
        text='# 文档包校验报告\n\n校验日期：2026-09-14。此报告仅覆盖开发文档、契约与工程样例，**不是 MAUI 应用测试报告**。\n\n'
        text+='执行命令：`python tools/verify_bundle.py --write-report`\n\n| 项目 | 结果 | 实际检查 |\n|---|---|---|\n'
        text+='\n'.join('| '+' | '.join(x.replace('|','/') for x in r)+' |' for r in CHECKS)
        text+='\n\n## 没有验证的部分\n\n尚无应用实现，本次未构建 MAUI，未运行 iOS/Android/Windows 真机；未验证实际录音、系统 TTS、字体排版、文件交互、性能、生产级导入安全和崩溃恢复。示例包没有音频字节；不能以其通过推断音频跨设备迁移完成。\n\nPython regex 文本元素校验针对当前工程样例，不证明与每个平台 StringInfo 的所有 Unicode 情况一致。工具按固定白名单校验当前协议样例，不是可以直接上线的通用导入器。应用用例全部保留 NOT RUN。\n'
        (ROOT/'VALIDATION_REPORT.md').write_text(text,encoding='utf-8')
    return 1 if fail else 0
if __name__=='__main__':sys.exit(main())
