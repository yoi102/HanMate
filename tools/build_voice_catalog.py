"""Explicit developer acquisition of pinned AI voice model metadata. App downloads only on user request."""
import hashlib, json, os, urllib.request
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
REV='57345c004e13ed640e408c4c29ab56187b18f065'
REPO='csukuangfj/icefall-tts-aishell3-vits-low-2024-04-06'
CACHE=Path(os.environ['TEMP'])/'HanMate-voice-research'/'files'
CACHE.mkdir(parents=True,exist_ok=True)
files=[]
for name,remote in [('model.onnx','exp/vits-epoch-960.onnx')]+[(n,'data/'+n) for n in ['lexicon.txt','tokens.txt','date.fst','number.fst','phone.fst','speakers.txt']]+[('MODEL-README.md','README.md')]:
 url=f'https://huggingface.co/{REPO}/resolve/{REV}/{remote}'
 path=CACHE/name
 if not path.exists():
  with urllib.request.urlopen(url,timeout=120) as r: path.write_bytes(r.read(64*1024*1024))
 data=path.read_bytes()
 files.append(dict(Name=name,Url=url,Size=len(data),Sha256=hashlib.sha256(data).hexdigest()))
pack=dict(Id='aishell3-vits-20240406',Name='AISHELL-3',Version=REV,Speakers=174,DefaultSpeaker=66,
 Source=f'https://huggingface.co/{REPO}/tree/{REV}',License='Apache-2.0',Files=files)
out=ROOT/'HanMate.Infrastructure/Voices'; out.mkdir(exist_ok=True)
(out/'catalog.json').write_text(json.dumps(pack,ensure_ascii=False,indent=2),encoding='utf8')
print(f"Pinned {len(files)} files, {sum(f['Size'] for f in files)} bytes; source {pack['Source']}")
