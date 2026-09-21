"""Build the offline draft pinyin course; --fetch refreshes explicit Commons audio metadata.
Python/soundfile are build-time only. No requests are made by the application.
"""
from pathlib import Path
import json,uuid,hashlib,unicodedata,urllib.request,urllib.parse,urllib.error,html,re,io,argparse,concurrent.futures,time
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'HanMate.App/Resources/Raw/Pinyin'; AUDIT=ROOT/'tools/resource-audit'
NS=uuid.UUID('cda65827-c97e-5f8c-ad6c-4d56668e1011')
def uid(s):return str(uuid.uuid5(NS,s))
def tone(base,t):
 vowels='aoeiuü';marks=['āáǎà','ōóǒò','ēéěè','īíǐì','ūúǔù','ǖǘǚǜ']
 i=next((base.index(v) for v in ['a','e'] if v in base),-1)
 if i<0:i=base.index('ou') if 'ou' in base else max((i for i,c in enumerate(base) if c in vowels),default=-1)
 assert i>=0
 return base[:i]+marks[vowels.index(base[i])][t-1]+base[i+1:]
def request(url):
 return urllib.request.urlopen(urllib.request.Request(url,headers={'User-Agent':'HanMate-resource-audit/0.1 (offline educational app; no runtime requests)'}),timeout=35).read()
def clean(x):return html.unescape(re.sub('<[^>]+>','',x))
def main():
 args=argparse.ArgumentParser();args.add_argument('--fetch',action='store_true');args.add_argument('--download',action='store_true')
 args.add_argument('--download-limit',type=int,default=12,help='Maximum new audio requests in this invocation (0-50). Cached audio is always processed.')
 args.add_argument('--teaching-audio',action='store_true',help='Acquire the pinned audio-cmn teaching recordings; never downloads at app runtime.')
 opt=args.parse_args()
 if not 0<=opt.download_limit<=50:args.error('--download-limit must be 0-50')
 OUT.mkdir(parents=True,exist_ok=True);AUDIT.mkdir(parents=True,exist_ok=True)
 rows={}; contents=[];examples={}
 template=json.loads((ROOT/'HanMate_Planning_Pack_v3.0/examples/dictionary-entry.json').read_text(encoding='utf-8'))
 for line in (ROOT/'tools/pinyin-examples.tsv').read_text(encoding='utf-8-sig').splitlines():
  if not line or line.startswith('#'):continue
  base,chars,en,ja=line.split(';');rows[base]=chars.split()
  for t,(ch,e,j) in enumerate(zip(chars.split(),en.split('|'),ja.split('|')),1):
   if ch=='-':continue
   key=f'{base}{t}'; display=tone(base,t);cid=uid('content/'+key);unid=uid('unit/'+key);tid=uid('token/'+key)
   doc=json.loads(json.dumps(template));doc.update(id=cid,origin='builtin',title=ch,scenes=[],difficulty='beginner',createdAtUtc='2026-09-17T00:00:00Z',updatedAtUtc='2026-09-17T00:00:00Z')
   doc['source'].update(sourceId='hanmate-pinyin-draft',type='original',authorProvider='HanMate development examples',reference='tools/pinyin-examples.tsv',licenseIdentifier='LicenseRef-HanMate-Draft',permissionNotes='Development teaching and translation draft; not independently reviewed.',reviewStatus='draft')
   for k in ['resourceId','resourceVersion','entryId']:doc['source'].pop(k,None)
   unit=doc['textUnits'][0];unit.update(id=unid,text=ch,elementBoundariesUtf16=[0,1],translations={'en':e,'ja':j})
   token=unit['tokens'][0];token.update(id=tid,start=0,length=1,text=ch,pinyin={'base':base,'tone':t,'erhua':False,'display':display})
   unit['tokens']=[token];seg=unit['segments'][0];seg.update(id=uid('segment/'+key),start=0,length=1,text=ch,tokenIds=[tid]);unit['segments']=[seg];doc['textUnits']=[unit]
   contents.append(doc);examples[key]={'tone':t,'contentId':cid,'unitId':unid,'tokenId':tid,'audioKey':key,'pinyinStart':0,'pinyinLength':len(display)}
 if opt.fetch:
  metadata={}
  keys=list(examples)
  for start in range(0,len(keys),35):
   batch=keys[start:start+35];titles={f'File:Zh-{tone(k[:-1],int(k[-1]))}.ogg':k for k in batch}
   query=urllib.parse.urlencode({'action':'query','format':'json','titles':'|'.join(titles),'prop':'imageinfo','iiprop':'url|extmetadata|sha1'})
   result=json.loads(request('https://commons.wikimedia.org/w/api.php?'+query))
   for page in result['query']['pages'].values():metadata[titles[page['title']]]=page
  (AUDIT/'commons-metadata.json').write_text(json.dumps(metadata,ensure_ascii=False,indent=2),encoding='utf-8')
 metadata=json.loads((AUDIT/'commons-metadata.json').read_text(encoding='utf-8'))
 assets={};missing=[];attempts=0
 import soundfile as sf
 def download(k):
  nonlocal attempts
  page=metadata[k]
  if 'imageinfo' not in page:return k,None,'No exact pronunciation file'
  info=page['imageinfo'][0];m=info['extmetadata'];license=m.get('LicenseShortName',{}).get('value','');licenseurl=m.get('LicenseUrl',{}).get('value','')
  if not license.startswith(('CC BY ','CC BY-SA ')) or 'creativecommons.org/licenses/' not in licenseurl:return k,None,'Unaccepted license: '+license
  # Exact title selects the phonetic reading, including tone. Homophones are not claimed to be the original recorded word.
  target=OUT/(k+'.wav');original=AUDIT/(k+'.ogg')
  if not original.exists():
   if not opt.download:return k,None,'Audio not downloaded'
   cooldown=AUDIT/'download-cooldown.json'
   if cooldown.exists() and time.time()<json.loads(cooldown.read_text())['retryAfterUtcEpoch']:
    return k,None,'Download deferred by provider Retry-After'
   if attempts>=opt.download_limit:return k,None,'Download deferred by explicit batch limit'
   attempts+=1
   time.sleep(2)
   try:original.write_bytes(request(info['url']))
   except urllib.error.HTTPError as error:
    if error.code==429:
     cooldown.write_text(json.dumps({'retryAfterUtcEpoch':time.time()+int(error.headers.get('Retry-After','600'))}))
    return k,None,f'Download deferred: HTTP {error.code}'
  if hashlib.sha1(original.read_bytes()).hexdigest()!=info['sha1']:
   return k,None,'Original audio hash differs from Commons metadata'
  samples,rate=sf.read(original,always_2d=True); samples=samples.mean(axis=1)
  assert 0.15<len(samples)/rate<15 and abs(samples).max()>0.005,(k,'invalid audio')
  sf.write(target,samples,rate,subtype='PCM_16')
  return k,{'key':k,'file':'Pinyin/'+target.name,'sha256':hashlib.sha256(target.read_bytes()).hexdigest(),'originalSha256':hashlib.sha256(original.read_bytes()).hexdigest(),'durationSeconds':round(len(samples)/rate,4),'sourceUrl':info['descriptionurl'],'downloadUrl':info['url'].split('?')[0],'author':clean(m.get('Artist',{}).get('value','')),'license':license,'licenseUrl':licenseurl,'description':clean(m.get('ImageDescription',{}).get('value','')),'changes':'Converted Ogg to mono PCM16 WAV; original sampling rate retained.','reviewStatus':'needsReview'},None
 with concurrent.futures.ThreadPoolExecutor(max_workers=1) as pool:
  for k,asset,reason in pool.map(download,examples):
   if asset:assets[k]=asset
   else:missing.append({'key':k,'reason':reason})
 items=[]
 groups=[('initial',[(s,b,s) for s,b in zip('b p m f d t n l g k h j q x zh ch sh r z c s'.split(),'ba pa ma fa da ta na la ge ke he ji qi xi zhi chi shi ri zi ci si'.split())]),('spelling',[('y','yi','y'),('w','wu','w')]),('simple',[('a','a','a'),('o','o','o'),('e','e','e'),('i','yi','i'),('u','wu','u'),('ü','yu','u')]),('compound',[(s,b,h) for s,b,h in [('ai','ai','ai'),('ei','bei','ei'),('ui','gui','ui'),('ao','bao','ao'),('ou','gou','ou'),('iu','liu','iu'),('ie','jie','ie'),('üe','yue','ue'),('er','er','er')]]),('nasal',[(s,b,h) for s,b,h in [('an','an','an'),('en','ben','en'),('in','yin','in'),('un','lun','un'),('ün','yun','un'),('ang','bang','ang'),('eng','deng','eng'),('ing','ding','ing'),('ong','dong','ong')]]),('whole',[(s,s,s) for s in 'zhi chi shi ri zi ci si yi wu yu ye yue yuan yin yun ying'.split()])]
 for group,entries in groups:
  for label,base,highlight in entries:
   ex=[]
   for t in range(1,5):
    key=f'{base}{t}'
    if key not in examples:continue
    a=dict(examples[key]);a.update(pinyinStart=base.index(highlight),pinyinLength=len(highlight));ex.append(a)
   items.append({'id':uid('item/'+group+'/'+label),'group':group,'display':label,'examples':ex,'spellingNote':label in ['ü','üe','ün']})
 assert len(items)==63
 course={'version':'draft-2026.09.17','items':items,'contents':contents,'assets':list(assets.values())}
 from expand_pinyin_course import expand
 course=expand(course,opt.teaching_audio)
 assets={a['key']:a for a in course['assets']}
 missing=[m for m in missing if m['key'] not in assets]
 content_by_id={d['id']:d for d in course['contents']}
 unrecorded_words={e['unitId'] for item in course['items'] for e in item['examples']
                   if len(content_by_id[e['contentId']]['textUnits'][0]['tokens'])>1 and not e.get('wordAudioKey')}
 (OUT/'course.json').write_text(json.dumps(course,ensure_ascii=False,indent=2),encoding='utf-8')
 (AUDIT/'missing-audio.json').write_text(json.dumps(missing,ensure_ascii=False,indent=2),encoding='utf-8')
 notice=['HanMate pinyin audio source notices','Cached licensed source recordings. Draft teaching selection, pronunciation matching pending independent review.','Each converted recording remains under its original license. No endorsement by the speakers is implied.','']
 for a in assets.values():notice+= [a['file'],a['author'],a['sourceUrl'],a['license']+' — '+a['licenseUrl'],a['changes'],'']
 (OUT/'NOTICE.txt').write_text('\n'.join(notice),encoding='utf-8')
 print(f'Built {len(items)} pinyin items, {len(contents)} draft examples, {len(assets)} attributed recordings; {len(missing)} syllable audio gaps; {len(unrecorded_words)} words without a whole-word recording')
if __name__=='__main__':main()
