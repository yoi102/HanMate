"""Pinned CC BY-SA audio-cmn teaching recordings. No runtime network or syllable concatenation.
Called by build_pinyin_course.py; --teaching-audio explicitly downloads missing source files.
"""
from pathlib import Path
import copy, hashlib, json, re, subprocess, time, urllib.parse, urllib.request, uuid, wave
from pinyin_tone_coverage import rows, INITIAL, highlight, share_examples, coverage

ROOT = Path(__file__).resolve().parents[1]
AUDIT = ROOT / 'tools/resource-audit/audio-cmn'
OUT = ROOT / 'HanMate.App/Resources/Raw/Pinyin'
REV = 'ff9ed3d0c631195bd2c06f39450f3264c7124040'
LICENSE = f'https://github.com/hugolpz/audio-cmn/blob/{REV}/README.md'
NS = uuid.UUID('cda65827-c97e-5f8c-ad6c-4d56668e1011')
def uid(s): return str(uuid.uuid5(NS, s))

# item, text, numbered pinyin (0 = neutral), highlighted token, original Chinese explanation, EN, JA
WORDS = [
 ('b','波浪','bo1 lang4',0,'水面起伏形成的波。','wave','波'),
 ('b','播放','bo1 fang4',0,'通过设备放出声音或影像。','play audio or video','再生する'),
 ('b','爸爸','ba4 ba0',0,'对父亲的称呼。','dad','お父さん'),
 ('p','婆婆','po2 po0',0,'丈夫的母亲；也可称年长的妇女。','mother-in-law; elderly woman','義母・年配の女性'),
 ('p','破坏','po4 huai4',0,'使事物损坏或受到损害。','damage; destroy','壊す'),
 ('p','苹果','ping2 guo3',0,'一种常见水果，通常呈圆形。','apple','りんご'),
 ('p','害怕','hai4 pa4',1,'因危险或困难而感到恐惧。','be afraid','怖がる'),
 ('m','妈妈','ma1 ma0',0,'对母亲的称呼。','mother','お母さん'),
 ('m','麻木','ma2 mu4',0,'身体某部分失去感觉；也指反应迟钝。','numb','感覚がない'),
 ('m','马车','ma3 che1',0,'由马拉着行驶的车辆。','horse-drawn carriage','馬車'),
 ('m','蘑菇','mo2 gu0',0,'一类常有菌盖和菌柄的真菌。','mushroom','きのこ'),
 ('m','抹布','ma1 bu4',0,'用来擦拭器物的布。','cleaning cloth','ふきん'),
 ('f','佛教','fo2 jiao4',0,'起源于古印度、由释迦牟尼创立的宗教。','Buddhism','仏教'),
 ('f','办法','ban4 fa3',1,'处理事情或解决问题的方法。','method; way','方法'),
 ('f','罚款','fa2 kuan3',0,'因违反规定而被要求缴纳的钱。','fine; monetary penalty','罰金'),
 ('f','头发','tou2 fa0',1,'长在人头部的毛。','hair','髪'),
 ('d','地图','di4 tu2',0,'按一定比例表示地理事物的图。','map','地図'),
 ('d','冬天','dong1 tian1',0,'一年四季中通常最寒冷的季节。','winter','冬'),
 ('l','老师','lao3 shi1',0,'教学生知识和技能的人。','teacher','先生'),
 ('h','花朵','hua1 duo3',0,'植物的花。','flower','花'),
 ('t','天空','tian1 kong1',0,'地面以上的广阔空间。','sky','空'),
 ('t','兔子','tu4 zi0',0,'耳朵长、善于跳跃的小动物。','rabbit','ウサギ'),
 ('t','太阳','tai4 yang0',0,'给地球带来光和热的恒星。','sun','太陽'),
 ('n','奶奶','nai3 nai0',0,'父亲的母亲。','paternal grandmother','父方の祖母'),
 ('n','牛奶','niu2 nai3',0,'牛产的奶。','milk','牛乳'),
 ('g','哥哥','ge1 ge0',0,'同辈中比自己年长的男子。','older brother','兄'),
 ('k','快乐','kuai4 le4',0,'感到高兴和愉快。','happy','楽しい'),
 ('j','姐姐','jie3 jie0',0,'同辈中比自己年长的女子。','older sister','姉'),
 ('q','气球','qi4 qiu2',0,'充入气体后鼓起来的球。','balloon','風船'),
 ('x','西瓜','xi1 gua1',0,'一种夏天常见、果肉多汁的水果。','watermelon','スイカ'),
 ('zh','蜘蛛','zhi1 zhu1',0,'有八条腿、常能结网的小动物。','spider','クモ'),
 ('ch','春天','chun1 tian1',0,'冬天之后、夏天之前的季节。','spring','春'),
 ('sh','狮子','shi1 zi0',0,'一种体型大、以肉为食的猫科动物。','lion','ライオン'),
 ('r','人民','ren2 min2',0,'社会成员的总称。','people','人民'),
 ('r','容易','rong2 yi4',0,'做起来不困难。','easy','易しい'),
 ('c','草地','cao3 di4',0,'长着草的地面。','grassland','草地'),
 ('s','森林','sen1 lin2',0,'大片生长着树木的地方。','forest','森林'),
 ('y','衣服','yi1 fu0',0,'穿在身上的衣物。','clothes','服'),
 ('w','我们','wo3 men0',0,'说话人和与自己有关的一些人。','we; us','私たち'),
 ('y','月亮','yue4 liang0',0,'围绕地球运行的天然卫星。','moon','月'),
 ('er','耳朵','er3 duo0',0,'听声音的器官。','ear','耳'),
 ('f','飞机','fei1 ji1',0,'依靠动力在空中飞行的交通工具。','airplane','飛行機'),
 ('p','朋友','peng2 you0',0,'彼此有交情的人。','friend','友達'),
 ('l','礼物','li3 wu4',0,'为了表达心意而送给别人的东西。','gift','贈り物'),
]

def expand(course, download=False):
 from build_pinyin_course import tone
 tree = json.loads((AUDIT/'tree.json').read_text(encoding='utf-8-sig'))
 files = {n['path']: n for n in tree['tree'] if n['type']=='blob'}
 definitions = {a:b.split('|') for line in (ROOT/'tools/pinyin-definitions.tsv').read_text(encoding='utf-8').splitlines() if line and not line.startswith('#') for a,b in [line.split(';',1)]}
 assets = {a['key']:a for a in course['assets']}
 used = {}; missing = []
 # Complete example records use a separate whole-word asset. A syllable recording never reads a whole word.
 def unit(text, identity, role, syllables=None, translations=None):
  tokens=[]
  for i,ch in enumerate(text):
   s=syllables[i] if syllables else None; p=None
   if s:
    base=s[:-1]; t=int(s[-1]); p={'base':base,'tone':t,'erhua':False,'display':tone(base,t) if t else base}
   tokens.append({'id':uid(identity+'/token/'+str(i)),'start':i,'length':1,'text':ch,'kind':'hanzi' if '\u3400'<=ch<='\u9fff' else 'punctuation','pinyin':p,'annotationSource':'manual' if p else 'unknown','locked':bool(p),'reviewState':'needsReview' if p else 'unknown'})
  return {'id':uid(identity),'role':role,'text':text,'offsetUnit':'textElement','elementBoundariesUtf16':list(range(len(text)+1)),
   'tokens':tokens,'segments':[{'id':uid(identity+'/segment'),'start':0,'length':len(text),'text':text,'kind':'speech','boundarySource':'auto','translations':{},'tokenIds':[t['id'] for t in tokens]}], 'translations':translations or {}}

 for d in course['contents']:
  p=d['textUnits'][0]['tokens'][0]['pinyin']; definition=definitions[p['base']][p['tone']-1]
  d['textUnits'].append(unit(definition, 'definition/'+d['id'], 'definition'))
  d['source']['reference']='tools/pinyin-examples.tsv; tools/pinyin-definitions.tsv'

 for base,number,text,definition,en,ja in rows('pinyin-tone-characters.tsv'):
  identity='content/'+base+number
  d=copy.deepcopy(course['contents'][0]); d['id']=uid(identity); d['title']=text
  d['source']['reference']='tools/content/pinyin-tone-characters.tsv (original draft)'
  d['textUnits']=[unit(text,'unit/'+base+number,'headword',[base+number],{'en':en,'ja':ja}),
                  unit(definition,'definition/'+d['id'],'definition')]
  course['contents'].append(d)
  item=next(i for i in course['items'] if highlight(i,base) is not None)
  head=d['textUnits'][0]; start,length=highlight(item,base)
  item['examples'].append(dict(tone=int(number),contentId=d['id'],unitId=head['id'],tokenId=head['tokens'][0]['id'],
                              audioKey=base+number,pinyinStart=start,pinyinLength=length))

 extra_words=[]
 pairing_words = rows('pinyin-character-words.tsv')
 optional_audio = {row[0] for row in pairing_words}
 for text,pinyin,definition,en,ja in rows('pinyin-tone-words.tsv') + pairing_words:
  base=pinyin.split()[0][:-1]; match=INITIAL.match(base)
  extra_words.append((match[0] if match else base,text,pinyin,0,definition,en,ja))
 for initial,text,pinyin,index,definition,en,ja in WORDS + extra_words:
  syllables=pinyin.split(); t=int(syllables[index][-1])
  # Neutral syllables belong to their normal teaching tone, while their displayed neutral reading is preserved.
  if t==0: continue
  d=copy.deepcopy(course['contents'][0]); identity='word/'+text
  d['id']=uid(identity); d['title']=text
  d['source']['reference']='tools/expand_pinyin_course.py (original draft words and definitions)'
  head=unit(text,identity+'/head','headword',syllables,{'en':en,'ja':ja})
  d['textUnits']=[head,unit(definition,identity+'/definition','definition')]; course['contents'].append(d)
  item=next(i for i in course['items'] if i['display']==initial)
  item['examples'].append({'tone':t,'contentId':d['id'],'unitId':head['id'],'tokenId':head['tokens'][index]['id'],
   'audioKey':syllables[index],'pinyinStart':0,'pinyinLength':len(initial),'wordAudioKey':'word-'+head['id'].replace('-','')})

 share_examples(course)
 # The user reported 雌 sounding like ci2. Use 匆/匆忙 for the c initial
 # lesson instead; the separate ci lesson must retain its actual ci syllables.
 c_item=next(i for i in course['items'] if i['group']=='initial' and i['display']=='c')
 c_item['examples']=[e for e in c_item['examples'] if e['audioKey']!='ci1']

 def recording(key,path,author,text=None,pinyin=None):
  if path not in files: raise ValueError('Source path absent: '+path)
  node=files[path]; used[path]=node
  cached=AUDIT/'sources'/Path(path).name; cached.parent.mkdir(exist_ok=True)
  url='https://raw.githubusercontent.com/hugolpz/audio-cmn/'+REV+'/'+urllib.parse.quote(path)
  if not cached.exists():
   if not download: raise FileNotFoundError('Run --teaching-audio once to acquire '+path)
   time.sleep(.15)
   # A provider error stops the entire run; no parallel retries or rate-limit bypass.
   with urllib.request.urlopen(urllib.request.Request(url,headers={'User-Agent':'HanMate-source-review'}),timeout=30) as response:
    data=response.read(2*1024*1024)
   if hashlib.sha1(b'blob '+str(len(data)).encode()+b'\0'+data).hexdigest()!=node['sha']: raise ValueError('Git blob mismatch')
   cached.write_bytes(data)
  data=cached.read_bytes()
  if hashlib.sha1(b'blob '+str(len(data)).encode()+b'\0'+data).hexdigest()!=node['sha']: raise ValueError('Cached source mismatch')
  target=OUT/(key+'.wav')
  import imageio_ffmpeg
  subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(),'-v','error','-y','-i',str(cached),'-ac','1','-ar','24000','-c:a','pcm_s16le','-map_metadata','-1',str(target)],check=True)
  with wave.open(str(target)) as w: duration=w.getnframes()/w.getframerate()
  assert .1<duration<15
  assets[key]={'key':key,'file':'Pinyin/'+target.name,'sha256':hashlib.sha256(target.read_bytes()).hexdigest(),
   'originalSha256':hashlib.sha256(data).hexdigest(),'durationSeconds':round(duration,4),'sourceUrl':'https://github.com/hugolpz/audio-cmn/blob/'+REV+'/'+urllib.parse.quote(path),
   'downloadUrl':url,'author':author,'license':'CC BY-SA (source does not specify version)','licenseUrl':LICENSE,
   'description':'Original audio-cmn recording: '+Path(path).name,'changes':'Decoded MP3 to mono 24 kHz PCM16 WAV; no concatenation or phonetic edits.','reviewStatus':'needsReview','text':text,'pinyin':pinyin}

 # Use the same syllable speaker throughout. Independent source records, not Commons download retries.
 for e in {e['audioKey']:e for i in course['items'] for e in i['examples'] if not e.get('wordAudioKey')}.values():
  # Use independent whole-character recordings where the source has them.
  # chi4 retains its exact syllable recording; the source has no standalone 赤.
  replacement={'pa1':'趴','pa4':'怕','chi1':'吃'}.get(e['audioKey'])
  if replacement:
   recording(e['audioKey'],'64k/hsk/cmn-'+replacement+'.mp3','Yue Tan; SWAC / audio-cmn / Hugo Lopez',replacement,e['audioKey'])
  else:
   recording(e['audioKey'],'64k/syllabs/cmn-'+e['audioKey']+'.mp3','Chen Wang; audio-cmn / Hugo Lopez')
 initials=dict(zip('b p m f d t n l g k h j q x zh ch sh r z c s y w'.split(),'bo po mo fo de te ne le ge ke he ji qi xi zhi chi shi ri zi ci si yi wu'.split()))
 finals={'i':'yi','u':'wu','ü':'yu','ui':'wei','iu':'you','ie':'ye','üe':'yue','in':'yin','un':'wen','ün':'yun','ing':'ying'}
 for item in course['items']:
  display=item['display']; base=initials[display] if item['group'] in ('initial','spelling') else finals.get(display,display)
  path='64k/syllabs/cmn-'+base+'1.mp3'
  if path in files:
   key=base+'1'
   if key not in assets or 'audio-cmn' not in assets[key]['sourceUrl']: recording(key,path,'Chen Wang; audio-cmn / Hugo Lopez')
   item['demoAudioKey']=key
  else: missing.append({'item':display,'reason':'No exact standalone recording; example audio remains available'})
 for item in course['items']:
  for e in item['examples']:
   if e.get('wordAudioKey'):
    d=next(d for d in course['contents'] if d['id']==e['contentId'])
    head=d['textUnits'][0]
    if e['wordAudioKey'] not in assets:
     source='64k/hsk/cmn-'+d['title']+'.mp3'
     if d['title'] in optional_audio and (source not in files or (not download and not (AUDIT/'sources'/Path(source).name).exists())):
      # A new real word does not imply a new whole-word recording. The app may
      # use the explicitly enabled offline AI voice, otherwise reports no audio.
      del e['wordAudioKey']
      continue
     recording(e['wordAudioKey'],source,'Yue Tan; SWAC / audio-cmn / Hugo Lopez',head['text'],' '.join(t['pinyin']['base']+str(t['pinyin']['tone']) for t in head['tokens']))
 course['assets']=list(assets.values()); course['version']='draft-2026.09.19'
 from pinyin_definition_annotations import annotate
 annotate(course)
 course['version']='draft-2026.09.21.1'
 (AUDIT/'tone-coverage.json').write_text(json.dumps(coverage(course),ensure_ascii=False,indent=2),encoding='utf-8')
 (AUDIT/'used-files.json').write_text(json.dumps({'revision':REV,'files':used,'missingDemos':missing},ensure_ascii=False,indent=2),encoding='utf-8')
 return course
