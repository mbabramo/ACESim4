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

def describe(plan,output,include_paths=True):
    """Generate reader-facing navigation and the shared methodology."""
    cases=plan['Cases'];pairs=contrasts(cases)
    costs=sorted({c['cost'] for c in cases},key=float)
    labels={'american':'American','trial':'Trial Fee-Shifting','complete':'Complete Fee-Shifting'}
    def risk_label(c):return 'RN' if c['alpha']=='0' else 'RA' if c['alpha']=='2' else 'CARA '+c['alpha']
    groups={}
    for a,b in pairs:
        stem=(a['fee']+'-to-'+b['fee']+'-'+a['risk'] if a['fee']!=b['fee'] else a['risk']+'-to-'+b['risk']+'-'+a['fee'])
        label=labels[a['fee']]+' to '+labels[b['fee']]+'; '+risk_label(a) if a['fee']!=b['fee'] else risk_label(a)+' to '+risk_label(b)+'; '+labels[a['fee']]
        groups.setdefault((stem,label),{})[a['cost']]=stem+'-cost-'+a['cost']+'.pdf'
    table=['| Directed comparison | '+' | '.join('Cost '+c for c in costs)+' |', '|---|'+'---|'*len(costs)]
    for (_,label),files in sorted(groups.items()):
        table.append('| '+label+' | '+' | '.join('[PDF](<Tables/'+files[c]+'>)' if c in files else 'Unavailable' for c in costs)+' |')
    text=f'''# Equilibrium strategy changes

These tables examine how equilibrium litigation strategies change when the fee rule or the parties' risk preferences change. They separate the direct effect of the intervention from contributions associated with the opponent's entry, offers and exit decisions.

The collection contains {len(pairs)} directed comparisons across {len(cases)} equilibrium profiles. It covers American, Trial Fee-Shifting and Complete Fee-Shifting at cost multipliers {', '.join(costs)}. RN means risk neutral; RA means both parties have CARA risk aversion with coefficient 2. Other available coefficients are labeled explicitly. Each fee change is evaluated in both directions within each risk level, and each risk change in both directions within each fee rule. Costs are held fixed; a risk comparison changes both parties' preferences together.

Start with the [table index](#table-index) below. Read [Methodology and Explanation](<Methodology and Explanation.md>) for the column definitions, row-selection criteria and limitations. The tables use the same saved equilibria as the strategy figures and outcome reports. Their contributions are not uniquely identified causal effects or observed paths of adjustment.

## Folder guide

| Folder | Contents |
|---|---|
| [Tables](Tables) | One PDF and PNG per directed comparison and cost. These are a collection of candidate rows and panels for manuscript selection. |
| [Sources/Tex](Sources/Tex) | C#-generated editable table layouts. |
| [Sources/Json](Sources/Json) | Selected table values, scenario identifiers and input fingerprints. Filenames match the TeX, PDF and PNG files. |
| [Data](Data) | Complete numerical results arranged by cost and directed contrast, including counterfactual best responses, tie/completion checks and residuals, with requests and provenance records. |
| [Sources/Profiles](Sources/Profiles) | Frozen equilibrium and action-report inputs used by the comparisons and solution-path verification. These preserve the exact input bytes independently of later report regeneration. |
| [Sources/Process Logs](<Sources/Process Logs>) | Execution logs and process records for these calculations and tables. |

Table 3 in the article's top-level Tables folder draws selected rows from this collection. Inclusion here does not imply inclusion in the article or online appendix. The table PDFs have no captions embedded in the artwork; their filenames and this index identify the comparison and cost. The shared methodology supplies the explanation for all tables.

## Table index

{chr(10).join(table)}

## Reproduction and verification

The ACESim4 code repository's scripts/Rebuild-ArticleSupplemental.ps1 generates the separate analyses; scripts/Article-supplemental.md documents its options. Python prepares requests and this index; C# calculates the comparisons, selects table rows, and generates TeX and JSON. The article repository's scripts/assemble_manuscript_exhibits.py assembles the manuscript selection.

Sources/profile-provenance.json identifies the retained inputs. Sources/comparison-verification.json records the comparison audit. The supplemental-plan.json and supplemental-state.json files one directory above this folder record the jobs, completion states, commands and file hashes. An index entry specifies coverage; the completion records establish which calculations have finished. Solution-path and multiple-equilibrium studies have their own folders and verification records.
'''
    path_text=f'''# Equilibrium solution paths

The requested collection replays {plan['PathCases']} ordinary-cost original exact solves: every core fee rule crossed with every available risk level. These are numerical solver paths from the uniform prior, not transitions between fee regimes or models of how litigants learn an equilibrium.

Each replay must match its original log's pivot count and every saved final action probability (tolerance 1e-10), with final exploitability at most 1e-7. Sources/Original solve logs preserves original single-prior logs; cached-equilibrium validation logs cannot substitute for them. Sources/Requests and Sources/Traces contain reproducible requests, frame streams and fingerprinted verification metadata. Sources/Run records holds replay process logs.

After all replays verify, all-equilibrium-solution-paths.html combines them and each scenario also receives an individual HTML viewer. equilibrium-paths-collection-manifest.json records completed collection inputs. The shared supplemental-plan.json lists requested jobs; it does not assert that pending traces have completed.

The generalized supplemental rebuild performs these replays and builds the collection automatically. Its path cache reuses completed traces only after checking their request, input, frame and assembly hashes. A separate multiple-start study is in Multiple equilibria.
'''
    documents=[('Equilibrium strategy changes',text)]
    if include_paths:documents.append(('Equilibrium solution paths',path_text))
    for folder,content in documents:
        p=output/folder/'README.md';p.parent.mkdir(parents=True,exist_ok=True);p.write_text(content,encoding='utf-8',newline='\n')
    methodology=CODE/'scripts/templates/equilibrium-strategy-methodology.md'
    (output/'Equilibrium strategy changes/Methodology and Explanation.md').write_text(
        methodology.read_text(encoding='utf-8'),encoding='utf-8',newline='\n')

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
    write(changes/'Sources/profile-provenance.json',{'Profiles':[
        {'Id':c['id'],'OptionSet':c['option'],'Equilibrium':{'Path':c['equilibrium'],'Sha256':sha(c['equilibrium'])},
         'Actions':{'Path':c['actions'],'Sha256':sha(c['actions'])}} for c in cases]})
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
    def source(c,parent):
        s={'Id':c['id'],'OptionSetName':c['option'],'EquilibriumFile':relative(c['equilibrium'],parent),
           'ActionReportFile':relative(c['actions'],parent),'EquilibriumNumber':1}
        return s
    pairs=contrasts(cases)
    for a,b in pairs:
        id=(a['fee']+'-to-'+b['fee']+'-'+a['risk'] if a['fee']!=b['fee'] else a['risk']+'-to-'+b['risk']+'-'+a['fee'])+'-cost-'+a['cost']
        directory=changes/'Data'/('cost-'+a['cost'])/id
        req=directory/'equilibrium-changes.request.json';inputs=[req]
        for c in [a,b]:inputs.extend([c['equilibrium'],c['actions']])
        write(req,{'OutputDirectory':'.','Sources':[source(c,directory) for c in [a,b]],'Contrasts':[
            {'Id':id,'Label':id,'Source':a['id'],'Target':b['id']}],
            'CheckOffPathCompletions':True,'CheckTieSensitivity':True})
        jid='change-'+id
        job(jid,'calculations',[exe,'equilibrium-changes','--request',req,'--calculate-only'],
            [],inputs,[directory/'equilibrium-changes-manifest.json'],[(str(directory),'*.json')])
        pub=changes/'Tables'
        args=[exe,'equilibrium-publication','--request',req,'--output',pub]
        job('table-'+id,'tables',args,[jid],
            [req,directory/'equilibrium-changes-manifest.json',directory/(id+'.json')],
            [pub/(id+'.pdf'),pub/(id+'.png'),changes/'Sources/Tex'/(id+'.tex'),changes/'Sources/Json'/(id+'.json')],
            [(str(pub),id+'.*'),(str(changes/'Sources/Tex'),id+'.tex'),(str(changes/'Sources/Json'),id+'.json')])
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
    describe(plan,output)
    print(f'Prepared {len(cases)} core profiles, {len(pairs)} directed calculations and tables, {len(ordinary)} paths.',flush=True)
    return plan

def run(plan,output,jobs,phases):
    statefile=output/'supplemental-state.json';state=read(statefile) if statefile.exists() else {}
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
        family='Equilibrium solution paths' if job['phase']=='paths' else 'Equilibrium strategy changes'
        records=output/family/('Sources/Run records' if job['phase']=='paths' else 'Sources/Process Logs')
        records.mkdir(parents=True,exist_ok=True)
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
            ready.sort(key=lambda j:{'paths':0,'calculations':1,'tables':2}[j['phase']])
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
    parser.add_argument('--phase',action='append',choices=['calculations','tables','paths'])
    args=parser.parse_args()
    if args.jobs<1:parser.error('--jobs must be positive')
    plan=prepare(args.results.resolve(),args.output.resolve(),args.exe.resolve(),args.original_logs)
    if not args.prepare_only:run(plan,args.output.resolve(),args.jobs,args.phase)
