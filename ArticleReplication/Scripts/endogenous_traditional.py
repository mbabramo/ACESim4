"""Prepare/run a finite, resumable ten-start traditional-CFR experiment.

Each child process has one logical processor. Results, attempts and seed archives
are immutable. Completion is distinct from Nash/economic acceptance. No numerical
failure is retried automatically, and no existing population experiment is edited.
"""
import argparse
from collections import Counter
from datetime import datetime, timezone
import gzip
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import time

SCHEMA='endogenous-traditional-batch-v1'
SEED_BASE=1729
SEED_STRIDE=989
THREAD_ENV={'DOTNET_PROCESSOR_COUNT':'1','OMP_NUM_THREADS':'1','OPENBLAS_NUM_THREADS':'1','MKL_NUM_THREADS':'1','NUMEXPR_NUM_THREADS':'1','DOTNET_CLI_TELEMETRY_OPTOUT':'1'}


def utc():return datetime.now(timezone.utc).isoformat()
def read(p):return json.loads(Path(p).read_text(encoding='utf-8-sig'))
def sha(p):
    h=hashlib.sha256()
    with Path(p).open('rb') as f:
        for b in iter(lambda:f.read(1024*1024),b''):h.update(b)
    return h.hexdigest()


def write(p,value,replace=False):
    p=Path(p);p.parent.mkdir(parents=True,exist_ok=True)
    text=json.dumps(value,indent=2,allow_nan=False)
    if not replace:
        with p.open('x',encoding='utf-8') as f:f.write(text)
        return
    temporary=p.with_name(p.name+'.tmp')
    temporary.write_text(text,encoding='utf-8')
    for i in range(40):
        try:os.replace(temporary,p);return
        except (PermissionError,FileNotFoundError):
            if i==39:raise
            time.sleep(.05)


def identity(pid):
    """PID plus creation identity; a reused PID is never treated as our worker."""
    if os.name=='nt':
        import ctypes
        from ctypes import wintypes
        k=ctypes.WinDLL('kernel32',use_last_error=True)
        k.OpenProcess.argtypes=[wintypes.DWORD,wintypes.BOOL,wintypes.DWORD];k.OpenProcess.restype=wintypes.HANDLE
        k.GetProcessTimes.argtypes=[wintypes.HANDLE,*([ctypes.POINTER(wintypes.FILETIME)]*4)]
        k.GetExitCodeProcess.argtypes=[wintypes.HANDLE,ctypes.POINTER(wintypes.DWORD)]
        k.CloseHandle.argtypes=[wintypes.HANDLE]
        h=k.OpenProcess(0x1000,False,pid)
        if not h:return None
        try:
            code=wintypes.DWORD()
            if not k.GetExitCodeProcess(h,ctypes.byref(code)) or code.value!=259:return None
            values=[wintypes.FILETIME() for _ in range(4)]
            if not k.GetProcessTimes(h,*(ctypes.byref(v) for v in values)):return None
            return dict(Pid=pid,Created=values[0].dwLowDateTime+(values[0].dwHighDateTime<<32))
        finally:k.CloseHandle(h)
    try:
        os.kill(pid,0)
        return dict(Pid=pid,Created=Path(f'/proc/{pid}/stat').read_text().split()[21])
    except (OSError,FileNotFoundError):return None


def validate_configuration(config):
    if config['Schema']!=SCHEMA:raise ValueError('Unknown batch schema')
    if any(type(config[k]) is not int for k in ('Workers','Repetitions','Iterations','CheckEvery')):raise ValueError('Integer execution dimensions required')
    if not 1<=config['Workers']<=10:raise ValueError('The experiment is capped at ten workers')
    if not 1<=config['Repetitions']<=10:raise ValueError('Expected one to ten independent repetitions')
    if config['Iterations']<3 or config['CheckEvery']<1 or config['Iterations']%config['CheckEvery']:raise ValueError('Iterations must end on a checked checkpoint')
    if len(config['Cases'])!=len(set(config['Cases'])):raise ValueError('Duplicate case')
    if len(config['Seeds'])!=config['Repetitions'] or len(set(config['Seeds']))!=config['Repetitions']:raise ValueError('Duplicate/missing seed')
    if any(type(s) is not int or not 0<=s<=2147483647 for s in config['Seeds']):raise ValueError('Seeds must fit the explicit .NET seed range')
    for field in ('NormalizedNashTarget','EconomicScreenFractionOfBenefit'):
        if not math.isfinite(config[field]) or config[field]<=0:raise ValueError('Invalid check tolerance')
    if config['Initialization'] not in ('uniform','dirichlet-one'):raise ValueError('Unknown initialization')
    if config['Flavor'] not in ('Fast','Regular','Unrolled'):raise ValueError('Unknown CFR implementation')


def prepare(args):
    out=Path(args.output).resolve();input_path=Path(args.input).resolve();assembly=Path(args.assembly).resolve()
    if out.exists():raise FileExistsError('A new batch needs a fresh directory')
    if out==input_path or out in input_path.parents or input_path in out.parents:raise ValueError('Outputs must be separate from inputs')
    catalog=read(input_path/'catalog.json');available={c['Id'] for c in catalog['Cases']}
    cases=[c['Id'] for c in catalog['Cases']] if args.cases=='all' else args.cases.split(',')
    if not set(cases)<=available:raise ValueError('Unknown case')
    runtime={p.name:sha(p) for p in sorted(assembly.parent.iterdir()) if p.is_file() and (p.suffix=='.dll' or p.name.endswith(('.deps.json','.runtimeconfig.json')))}
    repository=Path(__file__).resolve().parents[2]
    revision=subprocess.check_output(['git','rev-parse','HEAD'],cwd=repository,text=True).strip()
    dirty=subprocess.check_output(['git','status','--porcelain'],cwd=repository,text=True).strip()
    config=dict(Schema=SCHEMA,CreatedUtc=utc(),InputDirectory=str(input_path),CatalogSha256=sha(input_path/'catalog.json'),
        ArticleCsvSha256=catalog['ArticleCsvSha256'],Assembly=str(assembly),RuntimeSha256=runtime,SourceRevision=revision,
        SourceWorkingTreeDirty=bool(dirty),Runner=str(out/'runner.py'),SourceRunner=str(Path(__file__).resolve()),RunnerSha256=sha(__file__),
        OutputDirectory=str(out),Workers=args.workers,Repetitions=args.repetitions,Iterations=args.iterations,CheckEvery=args.check_every,
        Seeds=[args.seed_base+i*SEED_STRIDE for i in range(args.repetitions)],Cases=cases,
        NormalizedNashTarget=args.nash_target,EconomicScreenFractionOfBenefit=args.economic_screen,
        Initialization=args.initialization,Flavor=args.flavor,ThreadEnvironment=THREAD_ENV,
        Method='Original generalized regret-matching CFR; independent fresh starts; no Gini/Liu objective and no pooling.',
        PrimaryProfile='Current behavior at the existing best-average-exploitability checkpoint. Average behavior is saved and audited separately, never silently substituted.',
        NumericalBudget='Fixed iteration budget; nonconvergence is retained and classified, never automatically retried or mislabeled as an equilibrium.',
        ReuseRoots=[str(Path(x).resolve()) for x in args.reuse_root])
    validate_configuration(config)
    out.mkdir(parents=True);shutil.copy2(__file__,out/'runner.py');write(out/'batch.json',config)
    print(json.dumps(dict(Batch=str(out/'batch.json'),Jobs=len(cases)*args.repetitions,Workers=args.workers,SourceWorkingTreeDirty=bool(dirty))))


def check_frozen(config):
    validate_configuration(config)
    if sha(__file__)!=config['RunnerSha256']:raise ValueError('Frozen runner changed')
    if sha(Path(config['InputDirectory'])/'catalog.json')!=config['CatalogSha256']:raise ValueError('Input catalog changed')
    for name,digest in config['RuntimeSha256'].items():
        if sha(Path(config['Assembly']).parent/name)!=digest:raise ValueError('Frozen runtime changed: '+name)


def telemetry(path,value):
    try:write(path,value,replace=True)
    except (PermissionError,FileNotFoundError) as e:
        # Optional telemetry must never detach an active worker from supervision.
        print(json.dumps(dict(Event='ProgressWriteDeferred',Error=type(e).__name__,UpdatedUtc=utc())),flush=True)


def job_plan(config,cid,rep,output):
    return dict(Schema='endogenous-traditional-job-v1',InputDirectory=config['InputDirectory'],CatalogSha256=config['CatalogSha256'],CaseId=cid,
        OutputDirectory=str(output),Repetition=rep,Seed=config['Seeds'][rep-1],Iterations=config['Iterations'],CheckEvery=config['CheckEvery'],
        NormalizedNashTarget=config['NormalizedNashTarget'],EconomicScreenFractionOfBenefit=config['EconomicScreenFractionOfBenefit'],
        Initialization=config['Initialization'],Flavor=config['Flavor'],RuntimeSha256=config['RuntimeSha256'])


def compatible(plan,expected):
    return {k:v for k,v in plan.items() if k!='OutputDirectory'}=={k:v for k,v in expected.items() if k!='OutputDirectory'}


def validate_result(path,expected):
    path=Path(path);done=read(path/'completed.json');plan=read(path/'plan.json')
    if not done['Passed'] or not compatible(plan,expected):raise ValueError('Completed candidate identity differs')
    for name,digest in done['Files'].items():
        p=path/name
        if p.parent!=path or sha(p)!=digest:raise ValueError('Completed evidence changed: '+name)
    if not {'result.json','audit.json','game.json','settings.json','initialization.json','plan.json'}<=set(done['Files']):raise ValueError('Incomplete evidence ledger')
    result=read(path/'result.json');audit=read(path/'audit.json');game=read(path/'game.json');initial=read(path/'initialization.json')
    if result['CaseId']!=plan['CaseId'] or result['Seed']!=plan['Seed'] or result['Repetition']!=plan['Repetition']:raise ValueError('Result identity mismatch')
    if not audit['Passed'] or initial['SavedEquilibriumUsed'] or initial['GiniOrLiuUsed']:raise ValueError('Independent-start contract failed')
    if result['Iterations']!=plan['Iterations']:raise ValueError('Unexpected iteration count')
    row=result['Current']
    if row!=audit['Primary']:raise ValueError('Audit/result values differ')
    scales=game['UtilityScale'];raw=row['RawNashGains'];normalized=row['NormalizedNashGains']
    if any(not math.isfinite(x) for x in raw+normalized+row['ExpectedUtilities']):raise ValueError('Nonfinite numerical result')
    if any(abs(max(0,g)/s-n)>1e-13 for g,s,n in zip(raw,scales,normalized)):raise ValueError('Normalized/raw gains disagree')
    if row['NashAtTarget']!=(max(normalized)<=plan['NormalizedNashTarget']):raise ValueError('Invalid Nash grade')
    if max(row['IndependentOracleDifferences'])>1e-8:raise ValueError('Independent oracle mismatch')
    mass=result['Outcomes']['TotalProbability'];measures=result['Outcomes']['Measures']
    if abs(mass-1)>1e-9 or abs(measures['SocialWelfareLoss']-sum(measures[k] for k in ('ForegoneActivityCost','PrecautionCost','HarmCost','LitigationExpenditures')))>1e-10:raise ValueError('Outcome accounting mismatch')
    return dict(CaseId=plan['CaseId'],Repetition=plan['Repetition'],Seed=plan['Seed'],Run=str(path.resolve()),
        CompletionSha256=sha(path/'completed.json'),ResultSha256=sha(path/'result.json'),NashAtTarget=row['NashAtTarget'],
        NashAt1eMinus7=row['NashAt1eMinus7'],NashAt1eMinus5=row['NashAt1eMinus5'],EconomicScreenPassed=row['EconomicScreenPassed'],
        MaximumNormalizedNashGain=max(normalized),MaximumCeGainFraction=max((x for x in row['CeGainFractionsOfActivityBenefit'] if x is not None),default=None),
        SocialCost=measures['SocialWelfareLoss'],TotalSeconds=result['Timing']['TotalSeconds'],InitialSemanticSha256=initial['SemanticSha256'])


def find_imports(config):
    found={}
    for root in config['ReuseRoots']:
        root=Path(root)
        for receipt in root.glob('jobs/*/repetition-*/attempt-*/completed.json'):
            plan=read(receipt.parent/'plan.json');key=(plan['CaseId'],plan['Repetition'])
            if key[0] not in config['Cases'] or key[1]>config['Repetitions']:continue
            expected=job_plan(config,*key,receipt.parent)
            if not compatible(plan,expected):continue
            value=validate_result(receipt.parent,expected)
            if key in found:raise ValueError('Ambiguous duplicate imported start: '+str(key))
            found[key]=value
    return found


def run(args):
    root=Path(args.run).resolve();config=read(root/'batch.json');check_frozen(config)
    lock=root/'run.lock.json'
    if lock.exists():
        old=read(lock)
        if identity(old['Process']['Pid'])==old['Process']:raise RuntimeError('This batch already has a live verified coordinator')
        if not args.resume:raise RuntimeError('Stopped coordinator evidence exists; use --resume after diagnosis')
        lock.rename(root/('closed-lock-'+str(time.time_ns())+'.json'))
    me=identity(os.getpid());write(lock,dict(Process=me,StartedUtc=utc(),BatchSha256=sha(root/'batch.json'),Command=sys.argv))
    active={};completed=find_imports(config);failures={};jobs=[(c,r) for c in config['Cases'] for r in range(1,config['Repetitions']+1)]
    pending=[]
    try:
        for key in jobs:
            if key in completed:continue
            cid,rep=key;base=root/'jobs'/cid/f'repetition-{rep:02d}'
            attempts=sorted(base.glob('attempt-*'))
            if attempts:
                if len(attempts)!=1:raise ValueError('Multiple attempts need an explicit accepted-attempt decision: '+str(base))
                output=attempts[0];expected=job_plan(config,cid,rep,output)
                if (output/'completed.json').exists():completed[key]=validate_result(output,expected);continue
                launch=base/'launch.json'
                if launch.exists():
                    launched=read(launch);proc=launched['Process']
                    if identity(proc['Pid'])==proc:
                        active[key]=dict(Process=None,Identity=proc,Output=output,Plan=expected,Files=[]);continue
                failures[str(key)]=dict(Status='WorkerFailed',Output=str(output),Reason='Preserved incomplete attempt; no automatic retry')
            else:pending.append(key)
        if len(active)>config['Workers']:raise RuntimeError('Live owned workers exceed configured cap')
        def status(state):
            values=list(completed.values());telemetry(root/'status.json',dict(State=state,UpdatedUtc=utc(),Process=me,JobsTotal=len(jobs),CompletedCandidates=len(values),
                NashAtTarget=sum(v['NashAtTarget'] for v in values),NashAt1eMinus5=sum(v['NashAt1eMinus5'] for v in values),EconomicScreenPassed=sum(v['EconomicScreenPassed'] for v in values),
                Pending=len(pending),Active=[dict(CaseId=k[0],Repetition=k[1],**v['Identity'],Output=str(v['Output'])) for k,v in active.items()],Failures=failures))
        while pending or active:
            while pending and len(active)<config['Workers'] and len(failures)<3:
                key=pending.pop(0);cid,rep=key;base=root/'jobs'/cid/f'repetition-{rep:02d}';base.mkdir(parents=True,exist_ok=True);output=base/'attempt-001'
                plan=job_plan(config,cid,rep,output);plan_path=base/'request.json';write(plan_path,plan)
                stdout=(base/'worker.log').open('xb');stderr=(base/'worker.error.log').open('xb')
                command=['dotnet',config['Assembly'],'endogenous-traditional','--plan',str(plan_path)]
                env=dict(os.environ,**THREAD_ENV)
                p=subprocess.Popen(command,stdout=stdout,stderr=stderr,env=env,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
                proc=identity(p.pid)
                if proc is None:p.wait();raise RuntimeError('Worker exited before process identity was captured')
                write(base/'launch.json',dict(Process=proc,StartedUtc=utc(),Command=command,Environment=THREAD_ENV,PlanSha256=sha(plan_path)))
                active[key]=dict(Process=p,Identity=proc,Output=output,Plan=plan,Files=[stdout,stderr])
                print(json.dumps(dict(Event='Started',CaseId=cid,Repetition=rep,Pid=p.pid)),flush=True)
            for key,job in list(active.items()):
                p=job['Process'];running=p.poll() is None if p is not None else identity(job['Identity']['Pid'])==job['Identity']
                if running:continue
                for f in job['Files']:f.close()
                try:
                    if p is not None and p.returncode!=0:raise RuntimeError('Worker exit '+str(p.returncode))
                    value=validate_result(job['Output'],job['Plan']);completed[key]=value
                    with (root/'results.jsonl').open('a',encoding='utf-8') as f:f.write(json.dumps(value,allow_nan=False)+'\n')
                    print(json.dumps(dict(Event='Completed',CaseId=key[0],Repetition=key[1],NashAtTarget=value['NashAtTarget'],Seconds=value['TotalSeconds'])),flush=True)
                except Exception as e:failures[str(key)]=dict(Status='NeedsDiagnosis',Output=str(job['Output']),Error=str(e))
                del active[key]
            status('Running')
            if pending and not active and len(failures)>=3:break
            time.sleep(2 if active else .01)
        entries=[completed[k] for k in jobs if k in completed]
        index=root/'candidates.json'
        if not index.exists():write(index,dict(Schema='traditional-candidate-index-v1',BatchSha256=sha(root/'batch.json'),Candidates=entries,Failures=failures))
        elif read(index)['Candidates']!=entries:raise ValueError('Existing final index differs')
        status('Complete' if not failures else 'NeedsDiagnosis')
        if not failures and not (root/'completed.json').exists():write(root/'completed.json',dict(ComputationCompleted=True,Candidates=len(entries),IndexSha256=sha(index),FinishedUtc=utc(),
            Note='Candidate computation and independent checks complete; inspect Nash/economic grades before treating ten runs as accepted equilibria.'))
    finally:
        # Never kill workers on coordinator failure. Their identity-bound launch
        # records permit a later resume to adopt them instead of duplicating them.
        for job in active.values():
            for f in job['Files']:f.close()
        lock.rename(root/('closed-lock-'+str(time.time_ns())+'.json'))


def main():
    p=argparse.ArgumentParser(description=__doc__);sub=p.add_subparsers(dest='command',required=True)
    prepare_p=sub.add_parser('prepare');prepare_p.add_argument('--input',required=True);prepare_p.add_argument('--assembly',required=True);prepare_p.add_argument('--output',required=True)
    prepare_p.add_argument('--workers',type=int,default=10);prepare_p.add_argument('--repetitions',type=int,default=10);prepare_p.add_argument('--iterations',type=int,default=1000)
    prepare_p.add_argument('--check-every',type=int,default=100);prepare_p.add_argument('--cases',default='all');prepare_p.add_argument('--seed-base',type=int,default=SEED_BASE)
    prepare_p.add_argument('--nash-target',type=float,default=1e-7);prepare_p.add_argument('--economic-screen',type=float,default=.001)
    prepare_p.add_argument('--initialization',choices=['dirichlet-one','uniform'],default='dirichlet-one');prepare_p.add_argument('--flavor',choices=['Fast','Regular','Unrolled'],default='Fast');prepare_p.add_argument('--reuse-root',action='append',default=[])
    run_p=sub.add_parser('run');run_p.add_argument('--run',required=True);run_p.add_argument('--resume',action='store_true')
    args=p.parse_args();prepare(args) if args.command=='prepare' else run(args)


if __name__=='__main__':main()
