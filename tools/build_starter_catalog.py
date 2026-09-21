from pathlib import Path
import json,uuid,zipfile,hashlib,io,copy
from build_pinyin_course import tone
root=Path(__file__).resolve().parents[1]; out=root/'HanMate.Infrastructure/Catalog';out.mkdir(exist_ok=True)
ns=uuid.UUID('4713cc55-197f-5f6a-ad2d-7dbca2ed7725')
VERSION='1.0.1'
LEARNING_VERSION='1.0.5'
definitions={}
for line in (root/'tools/content/starter-definitions.tsv').read_text(encoding='utf-8').splitlines():
 if line and not line.startswith('#'):
  word,text,numbered,en,ja=line.split(';');definitions[word]=(text,numbered,en,ja)

def enrich(original):
 d=copy.deepcopy(original)
 if d['kind']!='word':return d
 text,numbered,en,ja=definitions[d['title']];syllables=iter(numbered.split());tokens=[]
 old=next((u for u in d['textUnits'] if u['role']=='definition'),None)
 identity=old['id'] if old else str(uuid.uuid5(ns,d['id']+'/definition'))
 for i,ch in enumerate(text):
  hanzi='\u3400'<=ch<='\u9fff';s=next(syllables) if hanzi else None;p=None
  if s:
   base=s[:-1].replace('v','ü');t=int(s[-1]);p=dict(base=base,tone=t,erhua=False,display=tone(base,t) if t else base)
  tokens.append(dict(id=str(uuid.uuid5(ns,identity+'/token/'+str(i))),start=i,length=1,text=ch,kind='hanzi' if hanzi else 'punctuation',
   pinyin=p,annotationSource='manual' if p else 'unknown',locked=bool(p),reviewState='needsReview' if p else 'unknown'))
 assert next(syllables,None) is None,d['title']
 unit=dict(id=identity,role='definition',text=text,offsetUnit='textElement',elementBoundariesUtf16=list(range(len(text)+1)),tokens=tokens,
  segments=[dict(id=str(uuid.uuid5(ns,identity+'/segment')),start=0,length=len(text),text=text,kind='speech',boundarySource='auto',translations={},tokenIds=[t['id'] for t in tokens])],translations=dict(en=en,ja=ja))
 d['textUnits']=[u for u in d['textUnits'] if u['role']!='definition']+[unit]
 d['source']['reference']+='; tools/content/starter-definitions.tsv (AI-assisted definition draft; independent review pending)'
 for field in ['contentRevision','annotationRevision','metadataRevision']:d[field]+=1
 d['updatedAtUtc']='2026-09-19T00:00:00Z'
 return d
def make_unit(identity,role,text,numbered,en,ja):
 syllables=iter(numbered.split());tokens=[]
 for i,ch in enumerate(text):
  hanzi='\u3400'<=ch<='\u9fff';s=next(syllables) if hanzi else None;p=None
  if s:
   base=s[:-1].replace('v','ü');t=int(s[-1]);p=dict(base=base,tone=t,erhua=False,display=tone(base,t) if t else base)
  tokens.append(dict(id=str(uuid.uuid5(ns,identity+'/token/'+str(i))),start=i,length=1,text=ch,kind='hanzi' if hanzi else 'whitespace' if ch.isspace() else 'punctuation',
   pinyin=p,annotationSource='manual' if p else 'none',locked=bool(p),reviewState='needsReview' if p else 'notApplicable'))
 assert next(syllables,None) is None,text
 return dict(id=identity,role=role,text=text,offsetUnit='textElement',elementBoundariesUtf16=list(range(len(text)+1)),tokens=tokens,
  segments=[dict(id=str(uuid.uuid5(ns,identity+'/segment')),start=0,length=len(text),text=text,kind='speech',boundarySource='auto',translations={},tokenIds=[t['id'] for t in tokens])],translations=dict(en=en,ja=ja))

def learning_words(template):
 result=[]
 examples={}
 for line in (root/'tools/content/category-word-examples.tsv').read_text(encoding='utf-8').splitlines():
  if not line or line.startswith('#'):continue
  word,key,text,reading,en,ja=line.split(';')
  assert key not in [e[0] for e in examples.get(word,[])],(word,key)
  examples.setdefault(word,[]).append((key,text,reading,en,ja))
 for line in (root/'tools/content/category-words.tsv').read_text(encoding='utf-8').splitlines():
  if not line or line.startswith('#'):continue
  category,word,reading,definition,definition_reading,en,ja=line.split(';')
  d=copy.deepcopy(template);d['id']=str(uuid.uuid5(ns,'category-word/'+word));d['title']=word
  d['scenes']=['word-'+c for c in category.split(',')];d['contentRevision']=d['annotationRevision']=d['metadataRevision']=1
  d['createdAtUtc']=d['updatedAtUtc']='2026-09-21T00:00:00Z'
  d['source'].update(sourceId='hanmate-category-words',type='original',authorProvider='HanMate AI-assisted draft',
   reference='tools/content/category-words.tsv',reviewStatus='draft',licenseIdentifier='Project-original-draft',
   permissionNotes='Original AI-assisted word explanations; independent Chinese, pinyin and translation review pending.')
  d['textUnits']=[make_unit(str(uuid.uuid5(ns,d['id']+'/'+role)),role,text,pinyin,en,ja)
   for role,text,pinyin in [('headword',word,reading),('definition',definition,definition_reading)]]
  for key,text,pinyin,example_en,example_ja in examples.pop(word,[]):
   d['textUnits'].append(make_unit(str(uuid.uuid5(ns,d['id']+'/example/'+key)), 'example',text,pinyin,example_en,example_ja))
  if any(u['role']=='example' for u in d['textUnits']):d['source']['reference']+='; tools/content/category-word-examples.tsv'
  result.append(d)
 assert not examples,examples.keys()
 return result

def learning_lessons(template):
 result=[]
 for lesson in json.loads((root/'tools/content/common-lessons.json').read_text(encoding='utf-8')):
  d=copy.deepcopy(template);d['id']=str(uuid.uuid5(ns,'common-lesson/'+lesson['key']))
  d.update(kind=lesson['kind'],title=lesson['title'],scenes=[lesson['scene']],difficulty='beginner' if lesson['kind']=='text' else 'basic',schoolStage=None,grade=None,
   contentRevision=1,annotationRevision=1,metadataRevision=1,createdAtUtc='2026-09-21T00:00:00Z',updatedAtUtc='2026-09-21T00:00:00Z')
  poem=lesson['kind']=='poem';separator='\n' if poem else ''
  lines=lesson['lines'];assert len({line['key'] for line in lines})==len(lines),lesson['key']
  identity=str(uuid.uuid5(ns,d['id']+'/body'))
  unit=make_unit(identity,'body',separator.join(line['text'] for line in lines),' '.join(line['pinyin'] for line in lines),'','')
  unit['translations']={};unit['segments']=[];start=0
  for index,line in enumerate(lines):
   length=len(line['text'])
   unit['segments'].append(dict(id=str(uuid.uuid5(ns,identity+'/speech/'+line['key'])),start=start,length=length,text=line['text'],kind='speech',boundarySource='manual',
    translations=dict(en=line['en'],ja=line['ja']),tokenIds=[t['id'] for t in unit['tokens'][start:start+length]]))
   start+=length
   if separator and index+1<len(lines):
    unit['segments'].append(dict(id=str(uuid.uuid5(ns,identity+'/layout-after/'+line['key'])),start=start,length=1,text=separator,kind='layout',boundarySource='manual',translations={},tokenIds=[unit['tokens'][start]['id']]))
    start+=1
  d['textUnits']=[unit]
  d['source'].update(sourceId='hanmate-common-lessons',type='imported' if poem else 'original',authorProvider=lesson['author'],
   reference=('《'+lesson['title']+'》通行古诗文本；' if poem else '')+'tools/content/common-lessons.json',reviewStatus='draft',
   licenseIdentifier='Public-domain-text; original-translation-draft' if poem else 'Project-original-draft',
   permissionNotes=('Ancient Chinese poem in the public domain. ' if poem else 'Original short learning text. ')+
    'AI-assisted pinyin and original Japanese/English translations; independent text, pronunciation and translation review pending.')
  result.append(d)
 return result

def pack(name,docs,template):
 version=LEARNING_VERSION if name=='learning' else VERSION
 categories=dict(line.split(';') for line in (root/'tools/content/word-categories.tsv').read_text(encoding='utf-8').splitlines() if line and not line.startswith('#'))
 rid=str(uuid.uuid5(ns,name));entries=[]; mapped=[]
 for original in docs:
  d=copy.deepcopy(original) if original['source']['sourceId']=='hanmate-category-words' else enrich(original)
  if name=='learning' and d['kind']=='word' and d['title'] in categories:
   d['scenes']=list(dict.fromkeys(d['scenes']+['word-'+c for c in categories[d['title']].split(',')]))
   d['metadataRevision']+=1;d['updatedAtUtc']='2026-09-21T00:00:00Z'
  entry='sample-'+d['id'];cid=str(uuid.uuid5(uuid.UUID(rid),entry));ids={d['id']:cid}
  for u in d['textUnits']:
   for obj in [u]+u['tokens']+u['segments']:ids[obj['id']]=str(uuid.uuid5(uuid.UUID(rid),obj['id']))
  def remap(v):
   if isinstance(v,dict):return {k:remap(x) for k,x in v.items()}
   if isinstance(v,list):return [remap(x) for x in v]
   return ids.get(v,v) if isinstance(v,str) else v
  d=remap(d);d['origin']='resource';d['source'].update(resourceId=rid,resourceVersion=version,entryId=entry)
  mapped.append(d);entries.append({'entryId':entry,'contentId':cid})
 descriptor=dict(template);descriptor.update(resourceId=rid,resourceKind='dictionary' if name=='dictionary' else 'learning',version=version,entries=entries,names={'zh-Hans':'HanMate 随附'+('字典' if name=='dictionary' else '学习')+'草稿','en':'HanMate bundled '+name+' draft','ja':'HanMate 同梱'+('辞書' if name=='dictionary' else '学習')+'草稿'})
 raw={'resource.json':descriptor,'contents.json':{'schemaVersion':2,'contents':mapped},'audio/index.json':{'schemaVersion':1,'assets':[],'bindings':[],'preferences':[]}}
 files={k:json.dumps(v,ensure_ascii=False,separators=(',',':')).encode() for k,v in raw.items()}
 manifest={'schemaVersion':2,'packageType':'resource','packageId':str(uuid.uuid5(uuid.UUID(rid),'package-'+version)),'createdAtUtc':'2026-09-21T00:00:00Z' if name=='learning' else '2026-09-19T00:00:00Z','producerAppVersion':'1.0.0','contentCatalogVersion':'draft-'+version,'counts':{'contents':len(docs),'folders':0,'favorites':0,'audioAssets':0,'audioBindings':0,'resources':1},'files':[{'path':k,'byteLength':len(v),'sha256':hashlib.sha256(v).hexdigest()} for k,v in files.items()],'warnings':[{'code':'DRAFT_CONTENT','message':'Development examples; not reviewed teaching material.'}]}
 files['manifest.json']=json.dumps(manifest,ensure_ascii=False).encode()
 with zipfile.ZipFile(out/(name+'.zip'),'w',compression=zipfile.ZIP_DEFLATED) as z:
  for k,v in files.items():
   info=zipfile.ZipInfo(k,(2026,9,17,0,0,0));info.compress_type=zipfile.ZIP_DEFLATED;z.writestr(info,v)
 print(name,rid,len(docs),hashlib.sha256((out/(name+'.zip')).read_bytes()).hexdigest())
all=json.loads((root/'HanMate_Planning_Pack_v3.0/examples/contents.json').read_text(encoding='utf-8'))['contents']
for name,docs in [('learning',all[:20]+learning_words(all[0])+learning_lessons(all[0])),('dictionary',all[21:24])]:
 with zipfile.ZipFile(root/'HanMate_Planning_Pack_v3.0/examples'/('sample-dictionary.handict' if name=='dictionary' else 'sample-learning.hanresource')) as z:template=json.loads(z.read('resource.json'))
 pack(name,docs,template)
