import asyncio, json, re, subprocess, sys
import edge_tts
SRC=sys.argv[1] if len(sys.argv)>1 else 'script_source.json'  
IDS=['s01','s02','s04','s05','s06','s08','s11']
VOICE='zh-CN-YunxiNeural'; LEAD=1.5; TAIL=1.0
d=json.load(open(SRC))['result']['script']
EDITS={'s01':('接下来十分钟','接下来几分钟')}  # film is ~5 min, not 10
ANCH={'s01':['这是','我们训练','角色是','接下来'],
 's02':['一局要爬','第一，','第二，','第三，','我们还给'],
 's04':['最早','可真游戏太慢','于是在作者','每核每秒','怎么证明'],
 's05':['第一步','第二步','第三步','模型本身'],
 's06':['来看','从随机初始化','再训到四亿步','接着是线 A','线 C','最高的','但它只比'],
 's08':['行为审计','我们试着强推','诊断发现','Adam','绳子拉不住','最后一次更糟','强制实验','要学的是'],
 's11':['最贵的教训','其中约六成','办法是','反面教材','现在的规矩']}
def anchor(sents,kw,after):
    for j,x in enumerate(sents):
        p=x['text'].find(kw)
        if p<0: continue
        t=x['start_s']+(x['end_s']-x['start_s'])*p/max(1,len(x['text']))
        if t>=after: return round(t,3),j
    raise SystemExit('anchor not found '+kw)
def dur(p): return float(subprocess.check_output(['ffprobe','-v','error','-show_entries','format=duration','-of','csv=p=0',p]).decode())
async def synth(text,out):
    c=edge_tts.Communicate(text,VOICE,rate='+0%',boundary='SentenceBoundary')
    sents=[]
    with open(out,'wb') as f:
        async for ch in c.stream():
            if ch['type']=='audio': f.write(ch['data'])
            elif ch['type']=='SentenceBoundary':
                sents.append({'text':ch['text'],'start_s':round(ch['offset']/1e7,3),'end_s':round((ch['offset']+ch['duration'])/1e7,3)})
    return sents
def step_t(s):
    m=re.match(r'\s*(\d+(?:\.\d+)?)\s*[–-]',s); return float(m.group(1)) if m else None
out={'title':d['title'],'voice':VOICE,'rate':'+0%','lead_in_s':LEAD,'tail_s':TAIL,'end_card_s':3.0,'scenes':[]}
srt=[]; t=LEAD
def ts(x):
    ms=int(round(x*1000)); return f'{ms//3600000:02d}:{ms//60000%60:02d}:{ms//1000%60:02d},{ms%1000:03d}'
for sc in d['scenes']:
    if sc['id'] not in IDS: continue
    p=f'audio/{sc["id"]}.mp3'
    if sc['id'] in EDITS: sc['narration']=sc['narration'].replace(*EDITS[sc['id']]); sc['narration_edit']=EDITS[sc['id']]
    sents=asyncio.run(synth(sc['narration'],p))
    ad=dur(p)
    beats=[]; prev=-1
    assert len(ANCH[sc['id']])==len(sc['visual']['steps'])
    for st,kw in zip(sc['visual']['steps'],ANCH[sc['id']]):
        at,j=anchor(sents,kw,prev+0.01); beats.append({'at_s':at,'sentence_index':j,'anchor':kw,'step':st}); prev=at
    out['scenes'].append({'id':sc['id'],'title':sc['title'],'audio':p,'audio_duration_s':round(ad,3),
        'scene_duration_s':round(ad+TAIL,3),'global_start_s':round(t,3),'script_duration_s':sc['duration_s'],
        'chars':len(sc['narration']),'narration':sc['narration'],'narration_edit':sc.get('narration_edit'),'sentences':sents,'beats':beats,'on_screen_text':sc['on_screen_text'],
        'visual':sc['visual'],'sources':sc['sources']})
    for s in sents: srt.append((t+s['start_s'],t+s['end_s'],s['text']))
    print(sc['id'],f'audio {ad:.2f}s scene {ad+TAIL:.2f}s chars {len(sc["narration"])} cps {len(sc["narration"])/ad:.2f} sents {len(sents)}',flush=True)
    t+=ad+TAIL
total=t+3.0
out['total_duration_s']=round(total,3)
json.dump(out,open('script.json','w'),ensure_ascii=False,indent=1)
with open('out/narration.srt','w') as f:
    for i,(a,b,x) in enumerate(srt,1): f.write(f'{i}\n{ts(a)} --> {ts(b)}\n{x}\n\n')
print('TOTAL',round(total,2),'s =',round(total/60,2),'min')
