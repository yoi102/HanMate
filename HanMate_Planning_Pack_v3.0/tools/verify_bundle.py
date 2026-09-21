#!/usr/bin/env python3
"""校验本规划包的链接、Schema、样例、ZIP 清单和 SQLite DDL。
这不是应用测试、生产导入器、拼音引擎、解码器或跨平台兼容验证。
运行：python tools/verify_bundle.py [--write-report]
依赖：python -m pip install -r tools/requirements.txt
"""
from __future__ import annotations
import argparse, copy, csv, hashlib, io, json, re, sqlite3, sys, unicodedata, zipfile, uuid, importlib.util
from pathlib import Path
import jsonschema
import regex
from datetime import datetime, timezone
import importlib.metadata
from build_bundle import check_generated, task_dependencies

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
    if isinstance(data,bytes):
        data=data.decode('utf-8',errors='strict')
    # json.loads accepts UTF-16 bytes and escaped unpaired surrogates otherwise.
    # Bound nesting before parsing so malformed input cannot exhaust recursion.
    depth=0;quoted=False;escaped=False
    for char in data:
        if quoted:
            if escaped:escaped=False
            elif char=='\\':escaped=True
            elif char=='"':quoted=False
        elif char=='"':quoted=True
        elif char in '[{':
            depth+=1
            if depth>32:raise ValueError('JSON depth exceeds 32')
        elif char in ']}':depth-=1
    result=json.loads(data,object_pairs_hook=pairs,parse_constant=invalid)
    def unicode_check(value):
        if isinstance(value,str):value.encode('utf-8',errors='strict')
        elif isinstance(value,dict):
            for key,item in value.items():unicode_check(key);unicode_check(item)
        elif isinstance(value,list):
            for item in value:unicode_check(item)
    unicode_check(result)
    return result

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
        ensure(bool(c['title'].strip()) and len(elements(c['title']))<=120,'title product limit')
        unique(c['id']);contentids.add(c['id']);roles=[u['role'] for u in c['textUnits']]
        if c['kind']=='word':
            ensure(roles.count('headword')==1 and all(x in ('headword','definition','example') for x in roles),'invalid word roles')
            ensure(roles.count('definition')<=20 and roles.count('example')<=20,'too many definitions/examples')
        elif c['kind'] in ('text','poem'):
            ensure(roles==['body'],'invalid reader roles')
        elif c['kind']=='grammar':
            ensure(all(x in ('grammarExplanation','example','grammarNote') for x in roles),'invalid grammar roles')
            g=c['grammar'];um={u['id']:u for u in c['textUnits']};listed=[]
            for key,role in [('explanationUnitIds','grammarExplanation'),('exampleUnitIds','example'),('noteUnitIds','grammarNote')]:
                for x in g[key]:
                    ensure(x in um and um[x]['role']==role,'grammar role/reference mismatch');listed.append(x)
            ensure(len(listed)==len(set(listed)) and set(listed)==set(um),'grammar units duplicated or omitted')
        else:raise AssertionError('unknown content kind')
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

def canonical_json(o):return json.dumps(o,ensure_ascii=False,sort_keys=True,separators=(',',':')).encode('utf-8')

def validate_resource(d, contents):
    validate('resource-descriptor.schema.json',d)
    entries=d['entries'];keys=[x['entryId'] for x in entries];ids=[x['contentId'] for x in entries]
    ensure(len(keys)==len(set(keys)) and len(ids)==len(set(ids)),'duplicate resource entry identity')
    cs={c['id']:c for c in contents}
    for e in entries:
        ensure(e['contentId'] in cs,'missing resource content')
        ensure(e['contentId']==str(uuid.uuid5(uuid.UUID(d['resourceId']),e['entryId'])),'unstable resource entry content ID')
        c=cs[e['contentId']];s=c['source']
        ensure(c['origin']=='resource','resource ownership must be explicit')
        ensure((s.get('resourceId'),s.get('resourceVersion'),s.get('entryId'))==(d['resourceId'],d['version'],e['entryId']),'resource provenance mismatch')
        if d['resourceKind']=='dictionary':validate('dictionary-entry.schema.json',c)
    return set(ids)

def content_fingerprint(c):
    d=copy.deepcopy(c)
    for key in ('createdAtUtc','updatedAtUtc','contentRevision','annotationRevision','metadataRevision'):d.pop(key,None)
    d['scenes']=sorted(set(d['scenes']))
    for u in d['textUnits']:
        for token in u['tokens']:token.pop('modifiedAtUtc',None)
    return sha(canonical_json(d))

def resource_fingerprint(desc,contents,ai):
    ids={e['contentId'] for e in desc['entries']};cs=[c for c in contents if c['id'] in ids]
    ensure({c['id'] for c in cs}==ids,'incomplete source payload')
    targets={u['id'] for c in cs for u in c['textUnits']}|{s['id'] for c in cs for u in c['textUnits'] for s in u['segments'] if s['kind']=='speech'}
    bm={b['id']:b for b in ai['bindings']};am={a['id']:a for a in ai['assets']}
    declared=set(desc['audioBindingIds']);ensure(declared<=set(bm),'missing published audio binding')
    bindings=[bm[x] for x in sorted(declared)]
    ensure(all(b['targetId'] in targets for b in bindings),'published audio target outside resource')
    aids={b['assetId'] for b in bindings};ensure(aids<=set(am),'missing published audio asset')
    # Export paths and storage locations are not semantic audio identity.
    assets=[{k:v for k,v in am[x].items() if k not in ('path','storage')} for x in sorted(aids)]
    defaults=desc['audioDefaults'];seen=set()
    for pref in defaults:
        ensure(pref['targetId'] not in seen,'duplicate published audio default');seen.add(pref['targetId'])
        ensure(pref['bindingId'] in declared and bm[pref['bindingId']]['targetId']==pref['targetId'],'invalid published audio default')
    index={'schemaVersion':1,'assets':assets,'bindings':bindings,'preferences':sorted(defaults,key=lambda x:x['targetId'])}
    return sha(canonical_json({'descriptor':desc,'contents':[{'id':c['id'],'fingerprint':content_fingerprint(c)} for c in sorted(cs,key=lambda x:x['id'])],'audioIndex':index}))

def validate_resource_state(rs,contents,ai=None):
    ai=ai or {"schemaVersion":1,"assets":[],"bindings":[],"preferences":[]}
    validate('resource-state.schema.json',rs)
    cs={c['id']:c for c in contents};seen=set();owned=set()
    for s in rs['resources']:
        d=s['descriptor'];ensure(d['resourceId'] not in seen,'duplicate resource registration');seen.add(d['resourceId'])
        ensure(sha(canonical_json(d))==s['descriptorSha256'],'resource descriptor fingerprint mismatch')
        ensure(len(s['removedEntryIds'])==len(set(s['removedEntryIds'])),'duplicate removal override')
        if s['payloadStatus']=='included':
            ensure(s['missingReason'] is None,'included resource with missing reason')
            ids=validate_resource(d,contents);ensure(not ids&owned,'resource ownership overlap');owned|=ids
            ensure(resource_fingerprint(d,contents,ai)==s['payloadFingerprint'],'resource payload fingerprint mismatch')
        else:
            ensure(bool(s['missingReason'] and s['missingReason'].strip()),'reference-only without explanation')
    retained=set(rs['retainedContentIds']);ensure(len(retained)==len(rs['retainedContentIds']),'duplicate retained content')
    ensure(retained=={c['id'] for c in contents if c['origin']=='retained'},'retained registry mismatch')
    ensure(not owned&retained,'content is owned and retained at once')
    ensure({c['id'] for c in contents if c['origin']=='resource'}==owned,'unregistered resource ownership')
    return len(seen)

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
            ensure(bool(re.fullmatch(r'(manifest\.json|contents\.json|settings\.json|collections\.json|resources\.json|resource\.json|audio/index\.json|audio/[0-9a-f]{64}\.(wav|mp3|m4a))',n)),'unsafe or unlisted path')
            ensure((i.external_attr>>16)&0o170000!=0o120000,'symbolic link')
            cap=limits['maxManifestBytes'] if n=='manifest.json' else limits['maxJsonBytes'] if n.endswith('.json') else limits['maxAudioBytes']
            parts=[];size=0
            with z.open(i) as stream:
                while block:=stream.read(65536):
                    size+=len(block);total+=len(block)
                    ensure(size<=cap and total<=limits['maxExpandedBytes'],'stream expansion limit');parts.append(block)
            raw[n]=b''.join(parts)
        # Bounded in-memory fixture check, not a production streaming installer.
        m=strict_load(raw['manifest.json']);validate('package-manifest.schema.json',m)
        fl={f['path']:f for f in m['files']};ensure(len(fl)==len(m['files']),'duplicate manifest path')
        typ=m['packageType'];required={'contents.json','audio/index.json'}
        if typ=='backup':required|={'settings.json','collections.json','resources.json'};forbidden={'resource.json'}
        elif typ=='resource':required|={'resource.json'};forbidden={'settings.json','collections.json','resources.json'}
        else:forbidden={'settings.json','collections.json','resources.json','resource.json'}
        ensure(required<=set(fl),'missing mandatory file');ensure(not forbidden&set(fl),'package contains wrong-scope management file')
        ensure(set(raw)-{'manifest.json'}==set(fl),'manifest file set mismatch')
        for name,f in fl.items():ensure(len(raw[name])==f['byteLength'] and sha(raw[name])==f['sha256'],'file hash/length mismatch')
        contents=strict_load(raw['contents.json']);validate('contents.schema.json',contents);ids,targets=validate_contents(contents)
        ai=strict_load(raw['audio/index.json']);validate('audio-index.schema.json',ai)
        assets={a['id']:a for a in ai['assets']};bindings={b['id']:b for b in ai['bindings']}
        ensure(len(assets)==len(ai['assets']) and len(bindings)==len(ai['bindings']),'duplicate audio ID')
        ensure({b['assetId'] for b in bindings.values()}==set(assets),'orphan audio asset outside content closure')
        declared_audio={a['path'] for a in assets.values() if a['storage']=='package'}
        ensure(declared_audio=={n for n in raw if n.startswith('audio/') and n!='audio/index.json'},'undeclared audio bytes outside selection')
        for a in assets.values():
            if a['storage']=='package':
                ensure(a['path']==f"audio/{a['sha256']}.{a['container']}",'audio path does not match asset identity')
                ensure(a['path'] in raw and sha(raw[a['path']])==a['sha256'] and len(raw[a['path']])==a['byteLength'],'audio payload mismatch')
            if typ=='backup' and a['origin']=='user':ensure(a['storage']=='package','user audio omitted')
            if typ=='resource':ensure(a['storage']=='package','installable resource depends on external audio')
        for b in bindings.values():ensure(b['targetId'] in targets and b['assetId'] in assets,'dangling audio binding')
        prefseen=set()
        for pr in ai['preferences']:
            ensure(pr['targetId'] not in prefseen,'duplicate preference');prefseen.add(pr['targetId'])
            ensure(pr['bindingId'] in bindings and bindings[pr['bindingId']]['targetId']==pr['targetId'],'wrong target preference')
        cols={'folders':[],'items':[]};resources=0
        if typ=='backup':
            cols=strict_load(raw['collections.json']);validate('collections.schema.json',cols)
            folders={f['id']:f for f in cols['folders']};ensure(len(folders)==len(cols['folders']),'duplicate folder')
            ensure(sum(f['systemRole']=='default' for f in folders.values())==1,'invalid default folder count')
            ensure(len({(v['folderId'],v['contentId']) for v in cols['items']})==len(cols['items']),'duplicate favorite')
            for v in cols['items']:ensure(v['folderId'] in folders and v['contentId'] in ids,'dangling favorite')
            bookmarks=cols.get('dictionaryBookmarks',[])
            ensure(len({(v['provider'],v['entryId']) for v in bookmarks})==len(bookmarks),'duplicate dictionary bookmark')
            ensure(all(uuid.UUID(v['entryId']).int != 0 and v['title'].strip() for v in bookmarks),'invalid dictionary bookmark')
            st=strict_load(raw['settings.json']);validate('settings.schema.json',st)
            ensure(st['favorites']['lastFolderId'] is None or st['favorites']['lastFolderId'] in folders,'last folder reference missing')
            resources=validate_resource_state(strict_load(raw['resources.json']),contents['contents'],ai)
        elif typ=='resource':
            d=strict_load(raw['resource.json']);owned=validate_resource(d,contents['contents']);ensure(owned==ids,'unlisted resource payload');resources=1
            ensure({b['assetId'] for b in bindings.values()}==set(assets),'resource contains orphan audio assets')
            ensure(set(d['audioBindingIds'])==set(bindings),'resource contains undeclared source bindings')
            ensure(sorted(d['audioDefaults'],key=lambda x:x['targetId'])==sorted(ai['preferences'],key=lambda x:x['targetId']),'source defaults mismatch')
            resource_fingerprint(d,contents['contents'],ai)
        else:
            ensure(all(c['origin']!='resource' for c in contents['contents']),'content sharing must not grant resource ownership')
        actual={'contents':len(contents['contents']),'folders':len(cols['folders']),'favorites':len(cols['items'])+len(cols.get('dictionaryBookmarks',[])),'audioAssets':len(assets),'audioBindings':len(bindings),'resources':resources}
        ensure(m['counts']==actual,'manifest count mismatch')
        return f"{typ}: {len(raw)} files, {len(ids)} contents, {resources} resources, {len(assets)} audio assets; fixture structure/hash/references valid"

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
    pairs['collections-dictionary-v2.json']='collections.schema.json'
    for f,s in pairs.items():validate(s,strict_load((ROOT/'examples'/f).read_bytes()))
    extra={'grammar-content.json':'content.schema.json','dictionary-entry.json':'dictionary-entry.schema.json','learning-resource.json':'resource-descriptor.schema.json','dictionary-resource.json':'resource-descriptor.schema.json','resources.json':'resource-state.schema.json','learning-manifest.json':'package-manifest.schema.json','dictionary-manifest.json':'package-manifest.schema.json'}
    for f,s in extra.items():validate(s,strict_load((ROOT/'examples'/f).read_bytes()))
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
    return f'{len(pairs)+len(extra)} JSON fixtures; {len(ids)} contents; {len(targets)} playback targets; ranges, tones, highlighter checked'

def check_trace():
    with (ROOT/'tracking/acceptance-cases.csv').open(encoding='utf-8',newline='') as f:cases=list(csv.DictReader(f))
    with (ROOT/'tracking/requirements.csv').open(encoding='utf-8',newline='') as f:reqs=list(csv.DictReader(f))
    ensure(len({c['testId'] for c in cases})==len(cases),'duplicate test ID')
    ensure(all(c['status'] in {'PASS','FAIL','NOT RUN','BLOCKED'} for c in cases),'invalid application result')
    for case in cases:
        if case['status']!='NOT RUN':
            evidence=case.get('evidence','').strip()
            ensure(evidence and (ROOT/evidence).is_file(),'executed/blocked case lacks evidence: '+case['testId'])
    alltests={c['testId'] for c in cases}
    for r in reqs:
        linked=set(r['tests'].split(';'));ensure(linked<=alltests and bool(linked),'uncovered requirement')
        for d in r['documents'].split(';'):ensure((ROOT/d).exists(),'missing requirement document')
    ensure({r['requirementId'] for r in reqs}=={f'R{i:02d}' for i in range(1,23)},'original or new requirement lost')
    ensure(all(set(c['requirements'].split(';'))<={r['requirementId'] for r in reqs} for c in cases),'case refers to unknown requirement')
    counts={state:sum(c['status']==state for c in cases) for state in ('PASS','FAIL','NOT RUN','BLOCKED')}
    return f'{len(reqs)} requirements mapped; {len(cases)} defined cases; {counts}; evidence existence is not platform execution'

def check_sql():
    con=sqlite3.connect(':memory:');con.executescript((ROOT/'specs/database.sql').read_text())
    ensure(con.execute('PRAGMA foreign_keys').fetchone()[0]==1,'FK disabled')
    ensure(con.execute('PRAGMA user_version').fetchone()[0]==2,'wrong schema version')
    h='0'*64
    con.execute('INSERT INTO content VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?)',('c','word','personal','词','{}',1,1,1,1,1,h,'2026-09-14T00:00:00Z','2026-09-14T00:00:00Z'))
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
    for kind,rid in [('learning','r1'),('dictionary','r2')]:
        con.execute('INSERT INTO installed_resource VALUES(?,?,?,?,?,?,?,?,?,?,?)',(rid,kind,'1.0.0','{}',h,h,'external',1,1,100,1))
    # Reject cross-owner data and non-word dictionary entries with real SQLite constraints.
    con.execute("INSERT INTO resource_entry VALUES('r2','e1','c')")
    rejected("INSERT INTO resource_entry VALUES('r1','e2','c')")
    rejected("UPDATE content SET kind='grammar' WHERE id='c'")
    con.execute('INSERT INTO content VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?)',('g','grammar','personal','语法','{}',1,1,1,1,1,h,'2026-09-14T00:00:00Z','2026-09-14T00:00:00Z'))
    rejected("INSERT INTO resource_entry VALUES('r2','e2','g')")
    rejected("UPDATE installed_resource SET resource_kind='learning' WHERE resource_id='r2'")
    rejected("UPDATE installed_resource SET is_present=0 WHERE resource_id='r2'")
    for ordinal in (0,1):
        con.execute('INSERT INTO search_index VALUES(?,?,?,?,?,?,?,?,?,?)',('c',ordinal,'词','ci','ci','ci2','ci2','[]',1,2))
    rejected("INSERT INTO search_index SELECT * FROM search_index WHERE alias_ordinal=0")
    con.execute("INSERT INTO resource_entry_override VALUES('r2','historical-removed',1,'2026-09-14T00:00:00Z')")
    # Compare-and-swap rejects a second writer with the old revision.
    con.execute("INSERT INTO user_settings(singleton,schema_version,body_json) VALUES(1,1,'{}')")
    con.execute("INSERT INTO draft(id,draft_kind,body_json,updated_at_utc) VALUES('d','content','{}','2026-09-14T00:00:00Z')")
    for table,column,where in [('content','membership_revision',"id='c'"),('user_settings','row_revision','singleton=1'),('draft','row_revision',"id='d'")]:
        sql=f'UPDATE {table} SET {column}={column}+1 WHERE {where} AND {column}=1'
        ensure(con.execute(sql).rowcount==1,'first revision update failed')
        ensure(con.execute(sql).rowcount==0,'stale revision update accepted')
        rejected(f'UPDATE {table} SET {column}=0 WHERE {where}')
    con.execute('INSERT INTO import_receipt VALUES(?,?,?,?,?)',('p',h,h,'2026-09-14T00:00:00Z','{}'))
    for operation in ('op1','op2'):
        con.execute('INSERT INTO import_operation VALUES(?,?,?,?,?,?)',(operation,'p','replace',1,'2026-09-14T00:00:00Z','{}'))
    rejected("INSERT INTO import_operation SELECT * FROM import_operation WHERE operation_id='op1'")
    rejected("INSERT INTO import_operation VALUES('op3','missing','merge',1,'2026-09-14T00:00:00Z','{}')")
    con.execute('SAVEPOINT interrupted_import')
    con.execute("INSERT INTO import_operation VALUES('rolled-back','p','merge',1,'2026-09-14T00:00:00Z','{}')")
    con.execute('ROLLBACK TO interrupted_import');con.execute('RELEASE interrupted_import')
    ensure(con.execute("SELECT count(*) FROM import_operation").fetchone()[0]==2,'rolled-back operation left a success receipt')
    ensure(not con.execute('PRAGMA foreign_key_check').fetchall(),'resource FK violation')
    n=con.execute("SELECT count(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'").fetchone()[0]

    con.close();return f'SQLite {sqlite3.sqlite_version}; {n} tables; favorites/FKs/audio/resource/alias constraints; stale revisions rejected; repeated-package operations and receipt rollback checked (in-memory only)'

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

def check_new_negatives():
    source=strict_load((ROOT/'examples/contents.json').read_bytes());n=0
    g=next(c for c in source['contents'] if c['kind']=='grammar')
    bad=copy.deepcopy(source);gg=next(c for c in bad['contents'] if c['kind']=='grammar');gg['grammar']['exampleUnitIds'][0]=gg['grammar']['explanationUnitIds'][0]
    try:validate_contents(bad)
    except AssertionError:n+=1
    else:raise AssertionError('wrong grammar role accepted')
    bad=copy.deepcopy(g);bad['kind']='text'
    try:validate('content.schema.json',bad)
    except jsonschema.ValidationError:n+=1
    else:raise AssertionError('grammar attached to text accepted')
    d=strict_load((ROOT/'examples/learning-resource.json').read_bytes());d['resourceKind']='dictionary'
    try:validate_resource(d,source['contents'])
    except jsonschema.ValidationError:n+=1
    else:raise AssertionError('dictionary grammar accepted')
    rs=strict_load((ROOT/'examples/resources.json').read_bytes());rs['resources'][0]['descriptorSha256']='0'*64
    try:validate_resource_state(rs,source['contents'])
    except AssertionError:n+=1
    else:raise AssertionError('resource fingerprint tampering accepted')
    import tempfile
    with zipfile.ZipFile(ROOT/'examples/sample-content.hanpack') as z:raw={x.filename:z.read(x) for x in z.infolist()}
    m=strict_load(raw['manifest.json']);raw['settings.json']=b'{}';m['files'].append({'path':'settings.json','byteLength':2,'sha256':sha(b'{}')});raw['manifest.json']=json.dumps(m).encode()
    with tempfile.TemporaryDirectory() as td:
        p=Path(td)/'private-leak.hanpack'
        with zipfile.ZipFile(p,'w') as z:
            for k,v in raw.items():z.writestr(k,v)
        try:verify_package(p)
        except AssertionError:n+=1
        else:raise AssertionError('content package with private settings accepted')
    return f'{n} additional grammar/resource/privacy malformed fixtures rejected'

def check_resource_overlay_isolation():
    contents=strict_load((ROOT/'examples/contents.json').read_bytes())['contents']
    desc=strict_load((ROOT/'examples/learning-resource.json').read_bytes())
    base={'schemaVersion':1,'assets':[],'bindings':[],'preferences':[]}
    expected=resource_fingerprint(desc,contents,base)
    c=next(c for c in contents if c['id']==desc['entries'][0]['contentId'])
    # Synthetic in-memory metadata only; no claim of a real recording or audio decode.
    target=c['textUnits'][0]['id'];binding=str(uuid.uuid5(uuid.NAMESPACE_URL,'fixture:user-binding'));asset=str(uuid.uuid5(uuid.NAMESPACE_URL,'fixture:user-asset'))
    overlay={'schemaVersion':1,'assets':[{'id':asset}], 'bindings':[{'id':binding,'targetId':target,'assetId':asset,'sourceRole':'user'}], 'preferences':[{'targetId':target,'bindingId':binding}]}
    ensure(resource_fingerprint(desc,contents,overlay)==expected,'user track altered publisher payload fingerprint')
    bad=copy.deepcopy(desc);bad['audioBindingIds']=[binding]
    try:resource_fingerprint(bad,contents,base)
    except AssertionError:pass
    else:raise AssertionError('missing published track accepted')
    return 'published identity excludes synthetic user audio/default overlays; missing declared source binding rejected; metadata-only test'

def check_legacy():
    file=ROOT/'archive/v2_baseline/tools/verify_bundle.py'
    spec=importlib.util.spec_from_file_location('hanmate_legacy_fixture_verifier',file)
    old=importlib.util.module_from_spec(spec);spec.loader.exec_module(old)
    migrated=0
    for name in ['sample-content.hanpack','sample-backup.hanbackup']:
        p=ROOT/'archive/v2_baseline/examples'/name;old.verify_package(p)
        with zipfile.ZipFile(p) as z:before=strict_load(z.read('contents.json'))
        after=copy.deepcopy(before);after['schemaVersion']=2
        for c in after['contents']:c['schemaVersion']=2
        validate('contents.schema.json',after);validate_contents(after)
        revert=copy.deepcopy(after);revert['schemaVersion']=1
        for c in revert['contents']:c['schemaVersion']=1
        ensure(revert==before,'legacy data changed beyond schema tag');migrated+=len(after['contents'])
    return f'2 original v1 packages verified; {migrated} content instances converted in memory with IDs/text/annotation unchanged; no database or MAUI migration executed'

def check_task_registry():
    with (ROOT/'tracking/backlog.csv').open(encoding='utf-8',newline='') as f:rows=list(csv.DictReader(f))
    ensure(len(rows)==48 and len({r['taskId'] for r in rows})==48,'wrong or duplicate task count')
    ensure(all(r['status'] in {'TODO','IN_PROGRESS','REVIEW','DONE','BLOCKED'} for r in rows),'invalid task state')
    graph=task_dependencies(rows)
    byid={r['taskId']:r for r in rows}
    for row in rows:
        if row['status'] in {'REVIEW','DONE','BLOCKED'}:
            evidence=row['evidence'].strip()
            ensure(evidence and (ROOT/evidence).is_file(),'task lacks evidence: '+row['taskId'])
        if row['status']=='DONE':
            ensure(all(byid[d]['status']=='DONE' for d in graph[row['taskId']]),'DONE task has incomplete dependency')
    with (ROOT/'examples/ui-copy.csv').open(encoding='utf-8',newline='') as f:ui=list(csv.DictReader(f))
    ensure(len({r['key'] for r in ui})==len(ui),'duplicate UI key')
    ensure(all(r['zh-Hans'] and r['ja'] and r['en'] for r in ui),'missing UI translation')
    return f'{len(rows)} tasks with valid acyclic dependencies and status/evidence checks; {len(ui)} three-language draft copy entries'

def check_review_regressions():
    rejected=0
    for data in [b'{"x":"\\ud800"}', '{"x":1}'.encode('utf-16'), '['*33+'0'+']'*33]:
        try:strict_load(data)
        except (ValueError,UnicodeError):rejected+=1
        else:raise AssertionError('malformed Unicode/deep JSON accepted')
    ensure(strict_load('{"text":"[\\\"{]"}')=={'text':'["{]'},'brackets inside strings counted as depth')
    for rows in [
        [{'taskId':'W0-01','dependencies':'W0-02'},{'taskId':'W0-02','dependencies':'W0-01'}],
        [{'taskId':'W0-01','dependencies':'W9-99'}],
    ]:
        try:task_dependencies(rows)
        except ValueError:rejected+=1
        else:raise AssertionError('bad dependency graph accepted')
    # A validly hashed but unselected file must not leak through a sharing package.
    import tempfile
    with zipfile.ZipFile(ROOT/'examples/sample-content.hanpack') as z:
        raw={x.filename:z.read(x) for x in z.infolist()}
    payload=b'synthetic privacy sentinel; not audio'
    name='audio/'+sha(payload)+'.wav';raw[name]=payload
    m=strict_load(raw['manifest.json']);m['files'].append({'path':name,'byteLength':len(payload),'sha256':sha(payload)})
    raw['manifest.json']=json.dumps(m).encode('utf-8')
    with tempfile.TemporaryDirectory() as td:
        path=Path(td)/'unselected.hanpack'
        with zipfile.ZipFile(path,'w') as z:
            for name,data in raw.items():z.writestr(name,data)
        try:verify_package(path)
        except AssertionError as exc:
            ensure('undeclared audio bytes' in str(exc),'unexpected rejection reason');rejected+=1
        else:raise AssertionError('unselected audio bytes accepted')
    return f'{rejected} Unicode/depth/dependency/privacy regressions rejected; synthetic metadata only, no audio decode'

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--write-report',action='store_true');args=parser.parse_args()
    check('本地链接与代码围栏',check_links)
    check('JSON Schema 结构',check_schemas)
    check('样例 JSON 与字音引用',check_fixture_json)
    check('内容包清单/哈希',lambda:verify_package(ROOT/'examples/sample-content.hanpack'))
    check('备份包清单/哈希',lambda:verify_package(ROOT/'examples/sample-backup.hanbackup'))
    check('学习资源包清单/引用',lambda:verify_package(ROOT/'examples/sample-learning.hanresource'))
    check('字典包清单/引用',lambda:verify_package(ROOT/'examples/sample-dictionary.handict'))
    check('需求与用例追踪',check_trace)
    check('任务与三语言文案',check_task_registry)
    check('派生文档与真源一致',check_generated)
    check('SQLite DDL 与约束',check_sql)
    check('破坏性样例拒绝',check_rejections)
    check('新增语法/资源/隐私拒绝',check_new_negatives)
    check('资源音轨与用户音轨隔离',check_resource_overlay_isolation)
    check('旧v1包显式内容转换',check_legacy)
    check('复审回归：严格解析/依赖/分享闭包',check_review_regressions)
    for name,status,note in CHECKS:print(f'{status}: {name}: {note}')
    fail=any(r[1]=='FAIL' for r in CHECKS)
    if args.write_report:
        checked_at=datetime.now(timezone.utc).isoformat()
        environment={'checkedAtUtc':checked_at,'scope':'document/fixture validation only','python':sys.version.split()[0],
                     'sqlite':sqlite3.sqlite_version,'packages':{name:importlib.metadata.version(name) for name in ('jsonschema','regex')},
                     'mauiBuilt':False,'platformTestsExecuted':False}
        (ROOT/'tools/validated-environment.json').write_text(json.dumps(environment,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
        text=f'# HanMate v3.0 文档包校验报告\n\n校验时间（UTC）：{checked_at}。此报告仅覆盖开发文档、契约与工程样例，**不是 MAUI 应用测试报告**。\n\n'
        text+='执行命令：`python tools/verify_bundle.py --write-report`\n\n| 项目 | 结果 | 实际检查 |\n|---|---|---|\n'
        text+='\n'.join('| '+' | '.join(x.replace('|','/') for x in r)+' |' for r in CHECKS)
        text+='\n\n## 没有验证的部分\n\n本次未构建 MAUI，未运行 iOS/Android/Windows 真机；未验证实际录音、系统 TTS、字体排版、文件交互、性能、生产级导入安全和崩溃恢复。示例包没有音频字节；不能以其通过推断音频跨设备迁移完成。\n\nPython regex 文本元素校验针对当前工程样例，不证明与每个平台 StringInfo 的所有 Unicode 情况一致。工具按固定白名单校验当前协议样例，不是可以直接上线的通用导入器。应用用例状态仅从 CSV 读取，本工具从不修改它们；证据文件存在不证明实际执行通过。\n\n更新报告后执行 `python tools/build_bundle.py --write` 刷新文件清单，再执行 `python tools/build_bundle.py --check` 独立核对；清单不嵌入本报告，避免循环哈希。\n'
        (ROOT/'VALIDATION_REPORT.md').write_text(text,encoding='utf-8',newline='\n')
    return 1 if fail else 0
if __name__=='__main__':sys.exit(main())
