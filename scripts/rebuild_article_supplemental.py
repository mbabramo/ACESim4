"""Prepare and resume the complete directed core-comparison and path workflows.

Each game calculation runs in an isolated process because model/solver settings
include process-wide state. The scheduler records input and output hashes; saved
equilibria are never treated as original-solve starting strategies.
"""
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor, wait, FIRST_COMPLETED
import argparse, csv, hashlib, itertools, json, os, re, shutil, subprocess, time

CODE=Path(__file__).resolve().parents[1]
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p):
    with Path(p).open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
def write(p,j):
    p.parent.mkdir(parents=True,exist_ok=True)
    text=json.dumps(j,indent=2,ensure_ascii=False)+'\n'
    if p.exists() and p.read_text(encoding='utf-8-sig')==text:return
    p.write_text(text,encoding='utf-8',newline='\n')
def relative(p,parent):return os.path.relpath(p,parent).replace('\\','/')
def number(x):return format(float(x),'.12g')
def fee(row):
    if float(row['Fee Shifting Multiplier'])==0:return 'american'
    return 'complete' if 'ExitFees-AllUnilateralExits' in row['OptionSetName'] else 'trial'
def risk(row):
    alpha=float(row['CARA Alpha'])
    return 'risk-neutral' if alpha==0 else 'risk-averse' if alpha==2 else 'risk-alpha-'+number(alpha)
def contrasts(cases):
    """Every ordered pair changing exactly one of fee rule and preferences."""
    return [(a,b) for a,b in itertools.permutations(cases,2)
            if a['cost']==b['cost'] and ((a['fee']!=b['fee']) != (a['alpha']!=b['alpha']))]

def prepare(results,output,exe,log_roots):
    request=read(results/'welfare-exhibits.json')
    report_map={}
    for batch in request['Inputs']:
        summary=results/batch['NumericalResultsCsv'];directory=results/batch['IndividualDirectory']
        with summary.open(encoding='utf-8-sig',newline='') as f:
            for row in csv.DictReader(f):
                if row['Filter']=='All' and row['Equilibrium Type']=='Only Eq':
                    report_map[row['OptionSetName']]=(directory,batch['ReportPrefix'])
    with (results/'Aggregated Data/Sources/welfare-outcomes.csv').open(encoding='utf-8-sig',newline='') as f:
        rows=[r for r in csv.DictReader(f) if r['Comparison Family']=='baseline']
    if not rows:raise ValueError('No core baseline rows in the verified welfare collection')
    changes=output/'Equilibrium strategy changes';paths=output/'Equilibrium solution paths'
    profiles=changes/'Sources/Profiles';profiles.mkdir(parents=True,exist_ok=True)
    cases=[]
    for row in rows:
        c={'id':risk(row)+'-'+fee(row)+'-cost-'+number(row['Costs Multiplier']),
           'option':row['OptionSetName'],'cost':number(row['Costs Multiplier']),
           'alpha':number(row['CARA Alpha']),'risk':risk(row),'fee':fee(row)}
        directory,prefix=report_map[c['option']]
        for key,suffix in [('equilibrium','-equ.csv'),('actions','-InformationSetActions.csv')]:
            origin=directory/(prefix+' '+c['option']+' '+suffix)
            target=profiles/(c['id']+suffix)
            if target.exists() and sha(target)!=sha(origin):raise ValueError('Changed retained input: '+str(target))
            if not target.exists():shutil.copy2(origin,target)
            c[key]=str(target)
        cases.append(c)
    cases.sort(key=lambda c:(float(c['cost']),float(c['alpha']),c['fee']))
    for cost in {c['cost'] for c in cases}:
        group=[c for c in cases if c['cost']==cost]
        risks={c['alpha'] for c in group}
        if len({(c['alpha'],c['fee']) for c in group})!=len(group):raise ValueError('Duplicate core scenario')
        if any({c['fee'] for c in group if c['alpha']==r}!={'american','trial','complete'} for r in risks):
            raise ValueError('Incomplete three-rule core at cost '+cost)
    jobs=[]
    def job(id,phase,command,dependencies,inputs,expected,outputs):
        jobs.append({'id':id,'phase':phase,'command':[str(x) for x in command],
                     'dependencies':dependencies,'inputs':[str(x) for x in inputs],
                     'expected':[str(x) for x in expected],'outputs':outputs})
    def source(c,parent,profile=None):
        s={'Id':c['id'],'OptionSetName':c['option'],'EquilibriumFile':relative(c['equilibrium'],parent),
           'ActionReportFile':relative(c['actions'],parent),'EquilibriumNumber':1}
        if profile:s['ProfileFile']=relative(profile,parent)
        return s
    settings={'MaxSweeps':6,'MaxCutsPerBlock':60,'GainLimit':1e-9,'ValidationTolerance':1e-7,
              'TieTolerance':1e-10,'ImprovementTolerance':1e-7,'SourceReachThreshold':1e-12,'SupportThreshold':1e-6}
    for c in cases:
        for representation in ['Mixed','Mixed tighter check']:
            directory=changes/'Calculations/Mixing'/representation/c['id']
            req=directory/'equilibrium-mixing.request.json'
            control={**settings}
            if representation!='Mixed':control.update(GainLimit=1e-10,TieTolerance=1e-11)
            write(req,{'OutputDirectory':'.','Sources':[source(c,directory)],'Settings':control,
                       'Orders':['forward','reverse'] if representation=='Mixed' else ['forward']})
            job('mix-'+representation+'-'+c['id'],'mixing',[exe,'equilibrium-mixing','--request',req],[],
                [req,c['equilibrium'],c['actions']],[directory/(c['id']+'-profile.json')],[(str(directory),'*.json')])
    pairs=contrasts(cases)
    for a,b in pairs:
        id=(a['fee']+'-to-'+b['fee']+'-'+a['risk'] if a['fee']!=b['fee'] else a['risk']+'-to-'+b['risk']+'-'+a['fee'])+'-cost-'+a['cost']
        calculated={}
        for representation in ['Original','Mixed','Mixed tighter check']:
            directory=changes/'Calculations'/representation/('cost-'+a['cost'])/id
            req=directory/'equilibrium-changes.request.json';sources=[];inputs=[req];dependencies=[]
            for c in [a,b]:
                profile=None
                if representation!='Original':
                    profile=changes/'Calculations/Mixing'/representation/c['id']/(c['id']+'-profile.json')
                    inputs.append(profile);dependencies.append('mix-'+representation+'-'+c['id'])
                inputs.extend([c['equilibrium'],c['actions']]);sources.append(source(c,directory,profile))
            write(req,{'OutputDirectory':'.','Sources':sources,'Contrasts':[
                {'Id':id,'Label':id,'Source':a['id'],'Target':b['id']}],
                'CheckOffPathCompletions':True,'CheckTieSensitivity':True})
            jid='change-'+representation+'-'+id;calculated[representation]=(req,jid,directory)
            job(jid,'original' if representation=='Original' else 'mixed',[exe,'equilibrium-changes','--request',req,'--calculate-only'],
                dependencies,inputs,[directory/'equilibrium-changes-manifest.json'],[(str(directory),'*.json')])
        pub=changes/'Published source tables'
        args=[exe,'equilibrium-publication','--original',calculated['Original'][0],'--mixed',calculated['Mixed'][0],
              '--check',calculated['Mixed tighter check'][0],'--output',pub]
        job('publication-'+id,'publication',args,[v[1] for v in calculated.values()],
            [p for req,_,d in calculated.values() for p in [req,d/'equilibrium-changes-manifest.json',d/(id+'.json')]],
            [pub/(id+'.pdf'),pub/(id+'.png'),pub/'Sources'/(id+'.json')],[(str(pub),id+'.*'),(str(pub/'Sources'),id+'.*')])
    ordinary=[c for c in cases if c['cost']=='1']
    log_candidates=[]
    for root in [*(Path(r) for r in log_roots),results/'Run records']:
        if root.exists():
            for p in root.rglob('*'):
                if p.is_file() and (p.suffix in ['.txt','.log']):
                    text=p.read_text(encoding='utf-8-sig',errors='replace')
                    if 'Prior 1 of 1' in text and 'Using exact arithmetic for initial prior' in text and len(re.findall(r'Complete after (\d+) pivoting steps',text))==1:
                        log_candidates.append((p,text))
    traces=[];path_jobs=[]
    for c in ordinary:
        candidates=[p for p,text in log_candidates if re.search('Option set '+re.escape(c['option'])+r'(?![A-Za-z0-9_-])',text)]
        if not candidates:raise ValueError('Missing original single-prior exact solve log: '+c['option'])
        log=paths/'Sources/Original solve logs'/(c['id']+'.log');log.parent.mkdir(parents=True,exist_ok=True)
        if log.exists() and sha(log)!=sha(candidates[0]):raise ValueError('Conflicting original log')
        if not log.exists():shutil.copy2(candidates[0],log)
        directory=paths/'Sources/Requests';trace=paths/'Sources/Traces'/c['id']
        src=directory/(c['id']+'-sources.json');req=directory/(c['id']+'.request.json')
        write(src,{'OutputDirectory':relative(trace,directory),'Sources':[source(c,directory)],'Contrasts':[]})
        write(req,{'SourceRequest':src.name,'OutputDirectory':relative(trace,directory),
                   'Equilibria':[{'Source':c['id'],'OriginalLog':relative(log,directory),'Seed':0}],'MaxPivots':0})
        jid='path-'+c['id'];path_jobs.append(jid);traces.append(trace/(c['id']+'.json'))
        job(jid,'paths',[exe,'equilibrium-paths','--request',req,'--calculate-only'],[],
            [req,src,c['equilibrium'],c['actions'],log],[trace/'equilibrium-paths-manifest.json'],[(str(trace),'*.json'),(str(trace),'*.jsonl')])
    collection=paths/'equilibrium-paths.collection.json'
    write(collection,{'OutputDirectory':'.','Results':[relative(p,paths) for p in traces]})
    job('path-collection','paths',[exe,'equilibrium-paths','--combine',collection],path_jobs,[collection,*traces],
        [paths/'all-equilibrium-solution-paths.html'],[(str(paths),'*.html'),(str(paths),'equilibrium-paths-collection-manifest.json')])
    plan={'Schema':1,'Cases':cases,'DirectedContrasts':len(pairs),'PathCases':len(ordinary),'Jobs':jobs}
    write(output/'supplemental-plan.json',plan)
    print(f'Prepared {len(cases)} core profiles, {len(pairs)} directed contrasts x three representations, {2*len(cases)} mixing checks, {len(ordinary)} paths.',flush=True)
    return plan

def run(plan,output,jobs,phases):
    records=output/'Run records';records.mkdir(parents=True,exist_ok=True)
    statefile=records/'supplemental-state.json';state=read(statefile) if statefile.exists() else {}
    pending={j['id']:j for j in plan['Jobs'] if not phases or j['phase'] in phases}
    def signature(job):
        binary=Path(job['command'][0])
        return {'command':job['command'],'inputs':{p:sha(p) for p in job['inputs']},
                'binaries':{str(p):sha(p) for p in [binary,binary.with_suffix('.dll'),binary.parent/'ACESimBase.dll']}}
    def current(job):
        prior=state.get(job['id'],{})
        try:return prior.get('status')=='complete' and prior['signature']==signature(job) and all(sha(p)==h for p,h in prior['outputs'].items())
        except (OSError,KeyError):return False
    complete={j['id'] for j in plan['Jobs'] if current(j)}
    for id in complete:pending.pop(id,None)
    def execute(job):
        started=time.time();sig=signature(job);stem=re.sub(r'[^a-zA-Z0-9.-]','-',job['id'])
        with (records/(stem+'.log')).open('w',encoding='utf-8') as log:
            p=subprocess.Popen(job['command'],cwd=CODE,stdout=log,stderr=subprocess.STDOUT,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
            write(records/(stem+'-process.json'),{'pid':p.pid,'started':started,'command':job['command']})
            code=p.wait()
        if code:raise RuntimeError(f'{job["id"]}: exit {code}; see {stem}.log')
        if any(not Path(p).exists() for p in job['expected']):raise RuntimeError('Missing expected output: '+job['id'])
        files={str(p):sha(p) for directory,pattern in job['outputs'] for p in Path(directory).glob(pattern) if p.is_file()}
        return {'status':'complete','seconds':time.time()-started,'signature':sig,'outputs':files}
    failures=[]
    with ThreadPoolExecutor(max_workers=jobs) as pool:
        running={}
        while pending or running:
            ready=[j for j in pending.values() if all(d in complete for d in j['dependencies'])]
            ready.sort(key=lambda j:{'paths':0,'original':1,'mixing':2,'mixed':3,'publication':4}[j['phase']])
            for j in ready[:jobs-len(running)]:
                pending.pop(j['id']);running[pool.submit(execute,j)]=j
                print('Start '+j['id'],flush=True)
            if not running:
                if pending:failures.append('Blocked dependencies: '+', '.join(pending))
                break
            done,_=wait(running,timeout=30,return_when=FIRST_COMPLETED)
            for f in done:
                j=running.pop(f)
                try:
                    state[j['id']]=f.result();complete.add(j['id']);print('Complete '+j['id'],flush=True)
                except Exception as e:
                    state[j['id']]={'status':'failed','error':str(e)};failures.append(str(e));print('FAILED '+str(e),flush=True)
                write(statefile,state)
    if failures:raise RuntimeError('\n'.join(failures))
    print(f'Completed requested phases; {len(complete)} current jobs verified.',flush=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--results',type=Path,default=CODE/'ReportResults')
    parser.add_argument('--output',type=Path,default=CODE/'SupplementalResults')
    parser.add_argument('--exe',type=Path,default=CODE/'LitigCharts/bin/Release/net9.0/LitigCharts.exe')
    parser.add_argument('--original-logs',type=Path,action='append',default=[])
    parser.add_argument('--jobs',type=int,default=os.cpu_count())
    parser.add_argument('--prepare-only',action='store_true')
    parser.add_argument('--phase',action='append',choices=['original','mixing','mixed','publication','paths'])
    args=parser.parse_args()
    if args.jobs<1:parser.error('--jobs must be positive')
    plan=prepare(args.results.resolve(),args.output.resolve(),args.exe.resolve(),args.original_logs)
    if not args.prepare_only:run(plan,args.output.resolve(),args.jobs,args.phase)
