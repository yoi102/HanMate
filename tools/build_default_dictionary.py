"""Build an offline, read-only MOE dictionary. Source text is preserved verbatim.

Build dependency: opencc-python-reimplemented==0.1.7 (indexes only).
Input: official dict_revised_2015_20260625.zip and reviseddict_10312.odt.
No network requests. Rebuild into a temporary database; never open a user's DB.
"""
import argparse, gzip, hashlib, io, json, re, sqlite3, unicodedata, uuid, zipfile
from importlib.metadata import distribution
from pathlib import Path
import xml.etree.ElementTree as ET

VERSION = '2015_20260625'
SOURCE = 'https://language.moe.gov.tw/001/Upload/Files/site_content/M0001/respub/download/dict_revised_' + VERSION + '.zip'
NAMESPACE = uuid.UUID('8cc767df-e47a-4ccd-a381-7a26c4286ebc')
NS = {'m': 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'}

def rows(archive):
    with zipfile.ZipFile(archive) as z:
        name = 'dict_revised_' + VERSION + '.xlsx'
        with zipfile.ZipFile(io.BytesIO(z.read(name))) as x:
            # SpreadsheetML escapes are part of the container encoding, not dictionary text.
            def decode(value):
                return re.sub(r'_x([0-9A-Fa-f]{4})_', lambda m: chr(int(m[1], 16)), value)
            strings = [decode(''.join(n.itertext())) for n in ET.fromstring(x.read('xl/sharedStrings.xml'))]
            with x.open('xl/worksheets/sheet1.xml') as sheet:
                for _, row in ET.iterparse(sheet, events=['end']):
                    if row.tag != '{'+NS['m']+'}row': continue
                    fields = [''] * 18
                    for cell in row:
                        col = re.match(r'[A-Z]+', cell.get('r'))[0]
                        if len(col) != 1 or col > 'R': raise ValueError('Unexpected source column')
                        v = cell.find('m:v', NS)
                        text = '' if v is None else (v.text or '')
                        fields[ord(col)-65] = strings[int(text)] if cell.get('t') == 's' else text
                    yield fields
                    row.clear()

def pinyin_key(text):
    bases, tones = [], []
    for syllable in text.strip().lower().split():
        base, tone = '', 0
        for ch in unicodedata.normalize('NFD', syllable):
            if ch in '\u0304\u0301\u030c\u0300': tone = '\u0304\u0301\u030c\u0300'.index(ch)+1
            elif ch == '\u0308' and base.endswith('u'): base = base[:-1]+'v'
            elif 'a' <= ch <= 'z': base += ch
            else: return '', '', ''
        if not base: return '', '', ''
        bases.append(base); tones.append(str(tone))
    return ''.join(bases), ' '.join(bases), ' '.join(tones)

def build(source, instructions, output):
    from opencc import OpenCC
    if hashlib.sha256(source.read_bytes()).hexdigest() != '64003a98fcc7097940e5a536c999bc08ba7c07e2c1be66448f01bf1ae10a53fc':
        raise ValueError('Source archive differs from the verified official release')
    if hashlib.sha256(instructions.read_bytes()).hexdigest() != 'ea1086d297ac5a7a795246f088cc49fd950f816ea508ed903fee936c4f8718ef':
        raise ValueError('Usage instructions differ from the verified original')
    output.mkdir(parents=True, exist_ok=True)
    db = output/'moe.build.sqlite'
    if db.exists(): raise FileExistsError('Remove only this disposable build file before retrying: '+str(db))
    convert = OpenCC('t2s')
    with sqlite3.connect(db) as c:
        c.executescript('''
          PRAGMA page_size=4096;
          CREATE TABLE entry(id TEXT PRIMARY KEY, title TEXT NOT NULL, simple TEXT NOT NULL,
            pinyin TEXT NOT NULL, joined TEXT NOT NULL, separated TEXT NOT NULL, tones TEXT NOT NULL,
            raw_json TEXT NOT NULL);
          CREATE TABLE character_index(character TEXT NOT NULL,id TEXT NOT NULL,PRIMARY KEY(character,id)) WITHOUT ROWID;
          CREATE TABLE metadata(key TEXT PRIMARY KEY,value TEXT NOT NULL);
        ''')
        iterator=iter(rows(source)); headers=next(iterator); count=0; max_length=0; missing=0
        digest=hashlib.sha256()
        for fields in iterator:
            if not fields[0]: continue
            identity=str(uuid.uuid5(NAMESPACE, fields[3]+'|'+fields[7]+'|'+fields[0]))
            raw=json.dumps(fields,ensure_ascii=False,separators=(',',':'))
            digest.update((raw+'\n').encode()); simple=convert.convert(fields[0])
            joined,separated,tones=pinyin_key(fields[11]); missing += not bool(joined)
            c.execute('INSERT INTO entry VALUES(?,?,?,?,?,?,?,?)',(identity,fields[0],simple,fields[11],joined,separated,tones,raw))
            c.executemany('INSERT INTO character_index VALUES(?,?)',((ch,identity) for ch in sorted(set(fields[0]+simple))))
            max_length=max(max_length,sum(map(len,fields)));count+=1
        info={'name':'教育部《重編國語辭典修訂本》','version':VERSION,'entries':count,
              'source':SOURCE,'sourceSha256':hashlib.sha256(source.read_bytes()).hexdigest(),
              'fields':headers,'contentSha256':digest.hexdigest(),'unindexedPinyinRows':missing,
              'maximumEntryCharacters':max_length,'license':'CC-BY-ND-3.0-TW',
              'attribution':'中華民國教育部（Ministry of Education, R.O.C.）。《重編國語辭典修訂本》（版本編號：'+VERSION+'） https://dict.revised.moe.edu.tw/',
              'formatChanges':'SpreadsheetML to SQLite; all 18 original fields retained. Simplified forms and normalized pinyin are search indexes only; displayed source text is unchanged.',
              'indexTool':'opencc-python-reimplemented 0.1.7; Apache-2.0; https://github.com/yichen0831/opencc-python'}
        c.executemany('INSERT INTO metadata VALUES(?,?)', [('version',VERSION),('entries',str(count)),('notice',json.dumps(info,ensure_ascii=False))])
        c.executescript('CREATE INDEX entry_simple ON entry(simple); CREATE INDEX entry_pinyin ON entry(joined);')
        c.commit(); c.execute('VACUUM')
    c.close()
    payload=db.read_bytes(); (output/'moe.sqlite.gz').write_bytes(gzip.compress(payload,mtime=0))
    info['sqliteSha256']=hashlib.sha256(payload).hexdigest();info['gzipSha256']=hashlib.sha256((output/'moe.sqlite.gz').read_bytes()).hexdigest()
    with zipfile.ZipFile(instructions) as z:
        document=ET.fromstring(z.read('content.xml'))
        paragraphs=document.findall('.//{urn:oasis:names:tc:opendocument:xmlns:text:1.0}p')
        license_text='\n'.join(''.join(p.itertext()) for p in paragraphs)
    (output/'MOE-USAGE.txt').write_text(license_text+'\n',encoding='utf-8')
    (output/'MOE-USAGE.odt').write_bytes(instructions.read_bytes())
    (output/'OPENCC-LICENSE.txt').write_text(distribution('opencc-python-reimplemented').read_text('LICENSE.txt'),encoding='utf-8')
    (output/'NOTICE.json').write_text(json.dumps(info,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    db.unlink()  # Only the exact temporary file created above, never any installed database.
    print(json.dumps(info,ensure_ascii=False,indent=2))

if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source',type=Path);parser.add_argument('instructions',type=Path);parser.add_argument('output',type=Path)
    args=parser.parse_args();build(args.source,args.instructions,args.output)
