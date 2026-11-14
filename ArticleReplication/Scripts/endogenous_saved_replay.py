"""Finite, non-destructive saved-equilibrium reproduction of the endogenous study.

Every original file is read-only. Native forward replay uses the original joint
weights. One worker reconstructs one game, then replays its archived profiles.
"""
import argparse
from collections import Counter
from datetime import datetime,timezone
import gzip
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

from endogenous_traditional import identity, write, sha, telemetry, THREAD_ENV


def read(path):
    path=Path(path);raw=path.read_bytes()
    return json.loads(gzip.decompress(raw) if path.suffix=='.gz' else raw)


def physical(path):
    path=Path(path)
    if path.exists():return path
    path=Path(str(path)+'.gz')
    if not path.exists():raise FileNotFoundError(path)
    return path


def utc():return datetime.now(timezone.utc).isoformat()


def prepare(args):
    workspace=Path(args.workspace).resolve();root=Path(args.output).resolve();assembly=Path(args.assembly).resolve()
    if root.exists():raise FileExistsError('Reproduction needs a fresh output root; no source is overwritten')
    if not 1<=args.workers<=20:raise ValueError('One to twenty single-threaded workers')
    source=workspace/'outputs/endogenous-story-report-20261107/data-v2.json';data=read(source)
    catalog_path=workspace/'outputs/endogenous-population-inputs/catalog.json';catalog=read(catalog_path)
    cases={c['Id']:c for c in catalog['Cases']};report_cases={c['Id']:c for c in data['Cases']}
    selected=list(cases) if args.cases=='all' else args.cases.split(',')
    if len(selected)!=len(set(selected)) or not set(selected)<=set(cases):raise ValueError('Unknown/duplicate case')
    traditional=workspace/'outputs/endogenous-traditional-resumed-20261111';index=read(traditional/'candidates.json');done=read(traditional/'completed.json')
    audit_path=workspace/'outputs/endogenous-traditional-audit-20261112/audit.json';audit=read(audit_path)
    if not audit['Passed'] or audit['Candidates']!=22680 or sha(traditional/'candidates.json')!=done['IndexSha256'] or audit['IndexSha256']!=done['IndexSha256']:
        raise ValueError('Complete original traditional evidence audit required')
    starts={(r['CaseId'],r['Repetition']):r for r in index['Candidates']}
    if len(starts)!=22680:raise ValueError('Duplicate/missing traditional starts')
    manifest_path=workspace/'outputs/endogenous-population-pooled/repetitions.json';manifest=read(manifest_path)
    if len(manifest['Runs'])!=10 or {c['Id'] for c in manifest['Cases']}!=set(cases):raise ValueError('Wrong population dimensions')
    initial={};selected_set=set(selected)
    for rep,run in enumerate(manifest['Runs'],1):
        for row in read(Path(run)/'completed.json')['Results']:
            if row['CaseId'] in selected_set:initial[rep,row['CaseId'],row['Selector']]=(Path(run),row)
    replacement_path=workspace/'outputs/endogenous-population-repairs/accepted-replacements.json';replacement_index=read(replacement_path)
    if manifest['Replacements']!=replacement_index['Replacements']:raise ValueError('Initial replacement index changed')
    for receipt in replacement_index['Receipts']:
        if sha(receipt['Path'])!=receipt['Sha256'] or not read(receipt['Path'])['Passed']:raise ValueError('Initial repair receipt changed')
    replaced=set()
    for entry in manifest['Replacements']:
        rep,run=entry['repetition'],Path(entry['run'])
        for row in read(run/'completed.json')['Results']:
            cid,selector=row['CaseId'],row['Selector'];key=(rep,cid,selector)
            if key in replaced:raise ValueError('Duplicate accepted replacement')
            replaced.add(key)
            if cid in selected_set:initial[key]=(run,row)
    pooled={c['CaseId']:c for c in data['Provenance']['FinalSources']}
    pooled_index_path=workspace/'outputs/endogenous-pooled-repairs/accepted-pooled-repairs.json'
    if sha(pooled_index_path)!=data['Provenance']['PooledReplacementIndexSha256']:raise ValueError('Pooled selection changed')
    if sha(catalog_path)!=data['Provenance']['CatalogSha256'] or sha(catalog['ArticleCsv'])!=catalog['ArticleCsvSha256']:raise ValueError('Published input changed')
    runtime={p.name:sha(p) for p in sorted(assembly.parent.iterdir()) if p.is_file() and (p.suffix=='.dll' or p.name.endswith(('.deps.json','.runtimeconfig.json')))}
    if assembly.name not in runtime:raise FileNotFoundError(assembly)
    root.mkdir();script=Path(__file__).resolve();helper=script.with_name('endogenous_traditional.py')
    shutil.copy2(script,root/'runner.py');shutil.copy2(helper,root/helper.name)
    postprocess=root/'postprocess';postprocess.mkdir()
    for module in script.parent.glob('*.py'):shutil.copy2(module,postprocess/module.name)
    if args.finalize:
        marker=Path('C:/Users/Admin/.codex/plugins/cache/openai-primary-runtime/pdf/26.1110.11258/skills/pdf/container_tools/mark_artifact_operation_started.mjs')
        shutil.copy2(marker,postprocess/marker.name)
        shutil.copytree(workspace/'sites/endogenous-equilibrium-story/dist',root/'report-snapshot')
    paths=[];archive_hashes={}
    def item(label,kind,result,result_hash,archive,rep=None,selector=None):
        actual=physical(archive)
        if str(actual) not in archive_hashes:archive_hashes[str(actual)]=sha(actual)
        return dict(Id=label,Kind=kind,SourceResult=str(physical(result)),SourceResultSha256=result_hash,
            Archive=str(actual),ArchiveSha256=archive_hashes[str(actual)],Repetition=rep,Selector=selector)
    for i,cid in enumerate(selected):
        case=cases[cid];baseline=Path(manifest['Runs'][0])/cid/'baseline.json';game=read(baseline.parent/'game.json')
        items=[item('draft-saved','draft',baseline,sha(baseline),case['SavedProfile'])]
        if items[0]['ArchiveSha256']!=case['SavedProfileSha256']:raise ValueError('Original equilibrium file changed')
        for rep in range(1,11):
            row=starts[cid,rep];run=Path(row['Run']);result=run/'result.json'
            if sha(run/'completed.json')!=row['CompletionSha256']:raise ValueError('Traditional source completion changed')
            for kind,name in [('traditional-current','population.json'),('traditional-average','average-population.json')]:
                items.append(item(f'{kind}-{rep:02d}',kind,result,row['ResultSha256'],run/name,rep))
            for selector in ('gini','liu'):
                run,row=initial[rep,cid,selector]
                if row['Status']!='FullGameCceVerified':raise ValueError('Unaccepted initial source')
                items.append(item(f'initial-{selector}-{rep:02d}','initial',run/row['DetailsFile'],row['DetailsSha256'],run/cid/selector/'population.json',rep,selector))
        final=pooled[cid];run=Path(final['FinalRun'])
        if sha(run/'completed.json')!=final['CompletedSha256']:raise ValueError('Selected pooled source changed')
        for selector in ('gini','liu'):
            items.append(item('pooled-'+selector,'pooled',run/cid/selector/'result.json',report_cases[cid]['Selectors'][selector]['ResultSha256'],run/cid/selector/'population.json',None,selector))
        if len(items)!=43 or len({r['Id'] for r in items})!=43:raise ValueError('Wrong case replay coverage')
        plan=root/'jobs'/cid/'request.json';output=plan.parent/'attempt-001'
        write(plan,dict(Schema='endogenous-saved-replay-case-v1',InputDirectory=str(catalog_path.parent),CatalogSha256=sha(catalog_path),CaseId=cid,
            GameHash=game['Fingerprint']['CompleteSha256'],OutputDirectory=str(output),Items=items,RuntimeSha256=runtime))
        paths.append(dict(CaseId=cid,Path=str(plan),Sha256=sha(plan)))
        if (i+1)%100==0:print(json.dumps({'PreparedCases':i+1,'Total':len(selected)}),flush=True)
    sources=[catalog_path,Path(catalog['ArticleCsv']),traditional/'candidates.json',traditional/'completed.json',audit_path,manifest_path,replacement_path,pooled_index_path,source,
        workspace/'outputs/endogenous-traditional-review-data-20261112.json.gz']
    if args.finalize:
        sources += [workspace/relative for relative in (
            'outputs/endogenous-story-report-20261107/reviewed-findings-v8-magnitude.json',
            'outputs/endogenous-story-report-20261107/economic-accuracy-review-20261107.json',
            'outputs/endogenous-initial-economic-review-20261108/completed.json',
            'outputs/endogenous-initial-economic-review-20261108/selection-rows.jsonl.gz',
            'outputs/endogenous-traditional-story-review-20261112/reviewed-findings-v1.json',
            'outputs/endogenous-traditional-story-review-20261112/review-diagnostics.json',
            'outputs/endogenous-traditional-story-review-20261112/conclusion-diagnostics.json')]
    revision=subprocess.check_output(['git','rev-parse','HEAD'],cwd=script.parents[2],text=True).strip()
    write(root/'batch.json',dict(Schema='endogenous-saved-replay-batch-v1',CreatedUtc=utc(),Workers=args.workers,Workspace=str(workspace),Assembly=str(assembly),
        RuntimeSha256=runtime,RunnerSha256=sha(root/'runner.py'),HelperSha256=sha(root/helper.name),CodeRevision=revision,
        PostprocessSha256={p.name:sha(p) for p in postprocess.iterdir() if p.is_file()},
        ReportSnapshotSha256={p.relative_to(root/'report-snapshot').as_posix():sha(p) for p in (root/'report-snapshot').rglob('*') if p.is_file()} if args.finalize else {},
        Cases=paths,ExpectedRows=len(selected)*43,ExpectedReportedRows=len(selected)*33,OriginalCases=2268,
        OriginalFiles={str(p):sha(p) for p in sources},ThreadEnvironment=THREAD_ENV,
        Preservation='Read source equilibria, policies and joint weights only. No optimization, normalization of joint weights, source updates, deletion or numerical reclassification.',
        Comparisons='Fresh native outcomes and detached utilities. Probability/cost tolerance 1e-12+1e-10*abs(expected); utility absolute tolerance 1e-8. Exact bitwise equality and all absolute differences are also recorded.',
        Coverage='Per case: one saved draft, ten traditional current, ten diagnostic average, twenty accepted initial Gini/Liu, two selected pooled. Diagnostics are not additional equilibria.',
        Finalize=args.finalize))
    print(json.dumps({'PreparedCases':len(paths),'Rows':len(paths)*43,'Root':str(root)}),flush=True)


def checked_case(entry):
    plan=read(entry['Path']);out=Path(plan['OutputDirectory'])
    if sha(entry['Path'])!=entry['Sha256']:raise ValueError('Frozen case plan changed')
    done=read(out/'completed.json')
    if done['PlanSha256']!=entry['Sha256'] or done['CaseId']!=entry['CaseId'] or done['Rows']!=len(plan['Items']) or sha(out/'replay.json.gz')!=done['ReplaySha256']:
        raise ValueError('Incomplete/changed forward replay evidence')
    return dict(**{k:entry[k] for k in ('CaseId','Path','Sha256')},Output=str(out),CompletionSha256=sha(out/'completed.json'),**{k:done[k] for k in ('Passed','Rows','ReplaySha256','Seconds')})


def run(args):
    root=Path(args.run).resolve();batch=read(root/'batch.json')
    if sha(__file__)!=batch['RunnerSha256'] or sha(Path(__file__).with_name('endogenous_traditional.py'))!=batch['HelperSha256']:raise ValueError('Frozen replay controller changed')
    if not 1<=batch['Workers']<=20:raise ValueError('Worker cap')
    for name,digest in batch['RuntimeSha256'].items():
        if sha(Path(batch['Assembly']).parent/name)!=digest:raise ValueError('Runtime changed')
    for name,digest in batch['PostprocessSha256'].items():
        if sha(root/'postprocess'/name)!=digest:raise ValueError('Frozen postprocessor changed')
    for path,digest in batch['OriginalFiles'].items():
        if sha(path)!=digest:raise ValueError('Source changed before replay: '+path)
    lock=root/'run.lock.json'
    if lock.exists():
        old=read(lock)
        if identity(old['Process']['Pid'])==old['Process']:raise RuntimeError('Live replay coordinator already owns this batch')
        if not args.resume:raise RuntimeError('Stopped coordinator requires explicit --resume')
        lock.rename(root/('closed-lock-'+str(time.time_ns())+'.json'))
    if (root/'completed.json').exists():raise RuntimeError('Numerical replay already completed; inspect it or invoke finalize explicitly, never repeat workers')
    me=identity(os.getpid());write(lock,dict(Process=me,StartedUtc=utc(),BatchSha256=sha(root/'batch.json')))
    active={};completed={};failures={};pending=[]
    try:
        for entry in batch['Cases']:
            if sha(entry['Path'])!=entry['Sha256']:raise ValueError('Frozen plan changed')
            plan=read(entry['Path']);out=Path(plan['OutputDirectory']);base=Path(entry['Path']).parent
            if (out/'completed.json').exists():completed[entry['CaseId']]=checked_case(entry);continue
            if (base/'launch.json').exists():
                launched=read(base/'launch.json');proc=launched['Process']
                if identity(proc['Pid'])==proc:active[entry['CaseId']]=dict(Entry=entry,Output=out,Process=None,Identity=proc,Files=[]);continue
            if out.exists():failures[entry['CaseId']]='Preserved interrupted replay; no automatic retry'
            else:pending.append(entry)
        while pending or active:
            while pending and len(active)<batch['Workers'] and len(failures)<3:
                entry=pending.pop(0);base=Path(entry['Path']).parent;stdout=(base/'worker.log').open('xb');stderr=(base/'worker.error.log').open('xb')
                command=['dotnet',batch['Assembly'],'endogenous-replay-case','--plan',entry['Path']]
                proc=subprocess.Popen(command,stdout=stdout,stderr=stderr,env=dict(os.environ,**THREAD_ENV),creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
                ident=identity(proc.pid)
                if ident is None:raise RuntimeError('Replay worker exited before identity capture')
                write(base/'launch.json',dict(Process=ident,StartedUtc=utc(),Command=command,PlanSha256=entry['Sha256'],Environment=THREAD_ENV))
                active[entry['CaseId']]=dict(Entry=entry,Output=Path(read(entry['Path'])['OutputDirectory']),Process=proc,Identity=ident,Files=[stdout,stderr])
            for cid,job in list(active.items()):
                proc=job['Process'];running=proc.poll() is None if proc is not None else identity(job['Identity']['Pid'])==job['Identity']
                if running:continue
                for f in job['Files']:f.close()
                try:
                    if proc is not None and proc.returncode!=0:raise RuntimeError('Worker exit '+str(proc.returncode))
                    completed[cid]=checked_case(job['Entry'])
                    print(json.dumps({'Case':cid,'CompletedCases':len(completed),'Passed':completed[cid]['Passed'],'Seconds':completed[cid]['Seconds']}),flush=True)
                except Exception as e:failures[cid]=str(e)
                del active[cid]
            telemetry(root/'status.json',dict(State='Replaying',UpdatedUtc=utc(),Process=me,TotalCases=len(batch['Cases']),CompletedCases=len(completed),
                ComparedProfiles=sum(x['Rows'] for x in completed.values()),MismatchedCases=sum(not x['Passed'] for x in completed.values()),Pending=len(pending),
                Active=[dict(CaseId=cid,**job['Identity']) for cid,job in active.items()],WorkerFailures=failures))
            if pending and not active and len(failures)>=3:break
            time.sleep(2 if active else .01)
        write(root/'replay-index.json',dict(BatchSha256=sha(root/'batch.json'),Cases=[completed[e['CaseId']] for e in batch['Cases'] if e['CaseId'] in completed],Failures=failures))
        passed=not failures and not pending and len(completed)==len(batch['Cases']) and all(x['Passed'] for x in completed.values())
        write(root/'completed.json',dict(Passed=passed,Cases=len(completed),Profiles=sum(x['Rows'] for x in completed.values()),IndexSha256=sha(root/'replay-index.json'),FinishedUtc=utc(),
            Meaning='Saved-strategy forward reproduction only. Existing equilibrium accuracy/objective qualifications and all source files remain unchanged.'))
        telemetry(root/'status.json',dict(State='Complete' if passed else 'NeedsDiagnosis',UpdatedUtc=utc(),Process=me,TotalCases=len(batch['Cases']),CompletedCases=len(completed),
            ComparedProfiles=sum(x['Rows'] for x in completed.values()),MismatchedCases=sum(not x['Passed'] for x in completed.values()),Active=[],WorkerFailures=failures))
        if passed and batch['Finalize']:
            telemetry(root/'status.json',dict(State='Postprocessing',UpdatedUtc=utc(),Process=me,CompletedCases=len(completed),ComparedProfiles=sum(x['Rows'] for x in completed.values()),Active=[],WorkerFailures={}))
            finalize(root)
            telemetry(root/'status.json',dict(State='ReproductionComplete',UpdatedUtc=utc(),Process=me,CompletedCases=len(completed),ComparedProfiles=sum(x['Rows'] for x in completed.values()),Active=[],WorkerFailures={}))
    finally:
        for job in active.values():
            for f in job['Files']:f.close()
        lock.rename(root/('closed-lock-'+str(time.time_ns())+'.json'))


def finalize(root):
    root=Path(root);batch=read(root/'batch.json');done=read(root/'completed.json');index=read(root/'replay-index.json')
    if not done['Passed'] or len(batch['Cases'])!=2268 or sha(root/'replay-index.json')!=done['IndexSha256']:raise ValueError('Complete passing full-grid replay required')
    workspace=Path(batch['Workspace']);source_path=workspace/'outputs/endogenous-story-report-20261107/data-v2.json';data=read(source_path)
    original_cache=workspace/'outputs/endogenous-traditional-review-data-20261112.json.gz';cache=read(original_cache)
    cases={c['Id']:c for c in data['Cases']};traditional={c['Id']:c['Traditional'] for c in cache['Cases']};counts=Counter();maxima={};exact=0;compared=0
    derived=root/'derived';derived.mkdir(exist_ok=False)
    # Load extraction code from a bound frozen copy, rather than from a changing checkout.
    code=root/'postprocess';sys.path.insert(0,str(code))
    for name,digest in batch['PostprocessSha256'].items():
        if sha(code/name)!=digest:raise ValueError('Postprocessing source changed')
    from endogenous_population_analysis import outcome_metrics
    from endogenous_story_report import span,summarize
    for entry in index['Cases']:
        if checked_case(entry)!=entry:raise ValueError('Replay proof changed')
        value=read(Path(entry['Output'])/'replay.json.gz');case=cases[entry['CaseId']]
        for row in value['Rows']:
            counts[row['Kind']]+=1
            for d in row['Comparisons']:
                compared+=1;exact+=d['BitwiseEqual'];maxima[d['Metric']]=max(maxima.get(d['Metric'],0),d['AbsoluteDifference'] or 0)
            outcomes=row['Outcomes'];metrics=outcome_metrics(outcomes)
            if row['Kind']=='draft':case['SavedPolicyReplay']=metrics
            elif row['Kind']=='initial':case['Selectors'][row['Selector']]['InitialValues'][row['Repetition']-1]=metrics
            elif row['Kind']=='pooled':case['Selectors'][row['Selector']]['Pooled']=metrics
            elif row['Kind']=='traditional-current':
                metrics.update({k:outcomes['Measures'][k] for k in ('PrecautionCost','ForegoneActivityCost')})
                traditional[case['Id']][row['Repetition']-1]['Values']=metrics
        for selector in ('gini','liu'):
            initial=case['Selectors'][selector]['InitialValues']
            case['Selectors'][selector]['InitializationRanges']={metric:span([r[metric] for r in initial]) for metric in case['Published']}
    expected={'draft':2268,'traditional-current':22680,'traditional-average':22680,'initial':45360,'pooled':4536}
    if dict(counts)!=expected:raise ValueError('Full replay coverage differs')
    data['Provenance']['ForwardReplay']=dict(Root=str(root.resolve()),IndexSha256=done['IndexSha256'],OriginalReportDataSha256=sha(source_path),NoSourceChanges=True)
    write(derived/'report-data-replayed.json',data)
    cache['Provenance']['OriginalCacheSha256']=sha(original_cache)
    cache['Provenance']['SourceData']=str((derived/'report-data-replayed.json').resolve());cache['Provenance']['SourceDataSha256']=sha(derived/'report-data-replayed.json')
    with (derived/'traditional-data-replayed.json.gz').open('xb') as f:f.write(gzip.compress(json.dumps(cache,allow_nan=False,separators=(',',':')).encode(),mtime=0))
    write(derived/'comparison-statistics-replayed.json',summarize(data))
    # Data/table regeneration is independently checked even when model execution
    # takes long enough that no conversational agent is waiting for completion.
    command=[sys.executable,str(code/'endogenous_traditional_analysis.py'),'analyze','--cache',str(derived/'traditional-data-replayed.json.gz'),
        '--source',str(derived/'report-data-replayed.json'),'--output',str(derived/'traditional-analysis'),
        '--audit',str(workspace/'outputs/endogenous-traditional-audit-20261112/audit.json')]
    stage(command,derived,'analysis')
    artifacts=regenerate_artifacts(root,derived,workspace,code)
    for path,digest in batch['OriginalFiles'].items():
        if sha(path)!=digest:raise ValueError('Source changed during reproduction: '+path)
    write(root/'reproduction-completed.json',dict(Passed=True,FinishedUtc=utc(),Profiles=dict(counts),ReportedResults=74844,DiagnosticAverageProfiles=22680,
        ComparedValues=compared,BitwiseEqualValues=exact,MaximumAbsoluteDifferences=maxima,ReplayIndexSha256=sha(root/'replay-index.json'),
        ReplayedReportDataSha256=sha(derived/'report-data-replayed.json'),ReplayedTraditionalDataSha256=sha(derived/'traditional-data-replayed.json.gz'),
        OriginalFilesUnchanged=True,QualificationsPreserved=True,ArticleEdited=False,
        Artifacts=artifacts,
        Scope='Fresh model/archived strategies/joint weights -> economic outcomes -> report data -> tables, figures and PDF atlases. Scientific accuracy classifications remain separately qualified.'))


def stage(command,output,name):
    write(output/(name+'-command.json'),dict(Command=command,StartedUtc=utc()))
    with (output/(name+'.log')).open('xb') as out,(output/(name+'.error.log')).open('xb') as err:
        result=subprocess.run(command,stdout=out,stderr=err,env=dict(os.environ,**THREAD_ENV))
    if result.returncode!=0:raise RuntimeError(name+' failed; inspect '+str(output/(name+'.error.log')))


def scientific_differences(expected,actual,path='root',result=None):
    """Separate reproducibility of numerical tables from threshold classifications."""
    if result is None:result=[]
    if type(expected) is bool or type(actual) is bool:
        if expected!=actual:result.append(dict(Path=path,Kind='classification',Expected=expected,Actual=actual))
    elif isinstance(expected,(float,int)) and isinstance(actual,(float,int)):
        tolerance=1e-10+1e-9*abs(expected)
        if not __import__('math').isfinite(actual) or abs(expected-actual)>tolerance:result.append(dict(Path=path,Kind='numeric',Expected=expected,Actual=actual,Tolerance=tolerance))
    elif isinstance(expected,dict) and isinstance(actual,dict):
        if set(expected)!=set(actual):result.append(dict(Path=path,Kind='fields'))
        for key in expected.keys()&actual.keys():scientific_differences(expected[key],actual[key],path+'/'+key,result)
    elif isinstance(expected,list) and isinstance(actual,list):
        if len(expected)!=len(actual):result.append(dict(Path=path,Kind='length',Expected=len(expected),Actual=len(actual)))
        for i,(a,b) in enumerate(zip(expected,actual)):scientific_differences(a,b,path+'/'+str(i),result)
    elif expected!=actual:result.append(dict(Path=path,Kind='value',Expected=expected,Actual=actual))
    return result


def regenerate_artifacts(root,derived,workspace,code):
    from PIL import Image,ImageChops
    from pypdf import PdfReader
    batch=read(root/'batch.json');snapshot=root/'report-snapshot'
    for relative,digest in batch['ReportSnapshotSha256'].items():
        if sha(snapshot/relative)!=digest:raise ValueError('Captured report snapshot changed')
    site=derived/'site';shutil.copytree(snapshot,site)
    proofs=derived/'proofs';proofs.mkdir()
    old_proof=workspace/'outputs/endogenous-initial-economic-review-20261108/completed.json';proof=read(old_proof)
    proof['OriginalProofSha256']=sha(old_proof);proof['OriginalSourceDataSha256']=proof['SourceDataSha256']
    proof['SourceDataSha256']=sha(derived/'report-data-replayed.json')
    proof['ReplayReferenceQualification']='Original deviation-gain ledger reused without alteration. Saved policies and source numerical records are unchanged; forward replay does not reclassify incentive accuracy.'
    write(proofs/'initial-economic-review.json',proof)
    shutil.copy2(old_proof.with_name('selection-rows.jsonl.gz'),proofs/'selection-rows.jsonl.gz')
    report=workspace/'outputs/endogenous-story-report-20261107';manifest=workspace/'outputs/endogenous-population-memory-resumed/repetitions.json'
    # Exactly one marker immediately before the first PDF authoring command.
    stage(['node',str(code/'mark_artifact_operation_started.mjs'),'--operation-kind','create','--expected-output-count','2','--output-format','pdf'],derived,'pdf-authoring-start')
    stage([sys.executable,str(code/'render_endogenous_story.py'),'--data',str(derived/'report-data-replayed.json'),
        '--statistics',str(derived/'comparison-statistics-replayed.json'),'--output',str(site),'--findings',str(report/'reviewed-findings-v8-magnitude.json'),
        '--economic-review',str(report/'economic-accuracy-review-20261107.json'),'--initial-manifest',str(manifest),
        '--initial-economic-review',str(proofs/'initial-economic-review.json')],derived,'report-figures')
    stage([sys.executable,str(code/'endogenous_benchmark_ratios.py'),'--data',str(derived/'report-data-replayed.json'),'--initial-manifest',str(manifest),
        '--output',str(derived/'benchmark-analysis')],derived,'benchmark-analysis')
    stage([sys.executable,str(code/'render_endogenous_benchmarks.py'),'--analysis',str(derived/'benchmark-analysis/analysis.json'),
        '--site',str(site),'--output',str(derived/'benchmark-figures')],derived,'benchmark-figures')
    review=workspace/'outputs/endogenous-traditional-story-review-20261112';notes=read(review/'reviewed-findings-v1.json')
    notes['AuditText']='The complete 22,680-search archive/seed audit has passed. This local reproduction additionally replays all selected saved strategies through freshly reconstructed games. Existing incentive-accuracy and selector-objective qualifications are unchanged.'
    write(derived/'reviewed-findings-replayed.json',notes)
    # Renderer optionally accepts these diagnostic downloads. Reuse explicitly as
    # commentary evidence; new means, SDs and matched effects were rebuilt above.
    for name in ('review-diagnostics.json','conclusion-diagnostics.json'):shutil.copy2(review/name,derived/'traditional-analysis'/name)
    stage([sys.executable,str(code/'render_endogenous_traditional_review.py'),'--analysis',str(derived/'traditional-analysis'),
        '--findings',str(derived/'reviewed-findings-replayed.json'),'--site',str(site)],derived,'traditional-figures')
    differences=scientific_differences(read(snapshot/'tables.json.gz'),read(site/'tables.json.gz'))
    write(derived/'report-table-differences.json',dict(Differences=differences,Count=len(differences)))
    # Compare all exported traditional setting summaries using their natural keys.
    def csv_rows(path):
        import csv
        with gzip.open(path,'rt',encoding='utf-8') as f:return {(r['CaseId'],r['Screen'],r['Metric']):r for r in csv.DictReader(f)}
    old=csv_rows(snapshot/'traditional-review/setting-summaries.csv.gz');new=csv_rows(site/'traditional-review/setting-summaries.csv.gz')
    if set(old)!=set(new):raise ValueError('Regenerated traditional table coverage differs')
    table_changes=[]
    for key,row in old.items():
        for field in ('N','Mean','SD','Minimum','Maximum','Median'):
            a,b=row[field],new[key][field]
            scientific_differences(float(a) if a else None,float(b) if b else None,str(key)+'/'+field,table_changes)
    write(derived/'traditional-table-differences.json',dict(Differences=table_changes,Count=len(table_changes)))
    pngs=[]
    inventories=read(snapshot/'figure-inventory.json'),read(site/'figure-inventory.json')
    if inventories[0]!=inventories[1]:raise ValueError('Regenerated main figure inventory differs')
    active_images=[('figures',r['Name']+'.png') for r in inventories[0]]
    active_images += [('benchmark-ratios',name+'.png') for name in ('cost-levels','fee-change','damages-change','threshold-change')]
    active_images += [('traditional-review',name+'.png') for name in ('rule-curves','policy-effects','component-offsets','composition-rn','composition-ra','component-spread','variation','ratios','participation-exception')]
    for folder,name in active_images:
            old_image=snapshot/folder/name
            fresh=site/folder/old_image.name
            if not fresh.exists():raise ValueError('Missing regenerated figure '+str(fresh))
            with Image.open(old_image) as a,Image.open(fresh) as b:
                exact=a.size==b.size and ImageChops.difference(a.convert('RGB'),b.convert('RGB')).getbbox() is None
            pngs.append(dict(File=folder+'/'+old_image.name,PixelIdentical=exact,OriginalSha256=sha(old_image),ReplayedSha256=sha(fresh)))
    pdfs=[]
    for name in ('scientific-figure-atlas.pdf','benchmark-ratios/benchmark-ratio-analysis.pdf'):
        before,after=snapshot/name,site/name
        pages_a,pages_b=len(PdfReader(before).pages),len(PdfReader(after).pages)
        pdfs.append(dict(File=name,OriginalPages=pages_a,ReplayedPages=pages_b,PageCountMatches=pages_a==pages_b,ReplayedSha256=sha(after)))
    # Any report difference is explicit; never overwrite source artifacts to make
    # the comparison pass, or suppress tiny-sign classification differences.
    result=dict(TableDifferences=len(differences),TraditionalTableDifferences=len(table_changes),TraditionalSummaryRows=len(new),
        Figures=pngs,PDFs=pdfs,PNGPixelIdentical=sum(r['PixelIdentical'] for r in pngs),PNGCompared=len(pngs),
        NeedsReportReview=bool(differences or table_changes or any(not p['PixelIdentical'] for p in pngs) or any(not p['PageCountMatches'] for p in pdfs)))
    write(derived/'artifact-comparison.json',result)
    return result


def main():
    p=argparse.ArgumentParser(description=__doc__);s=p.add_subparsers(dest='command',required=True)
    a=s.add_parser('prepare');a.add_argument('--workspace',required=True);a.add_argument('--assembly',required=True);a.add_argument('--output',required=True);a.add_argument('--cases',default='all');a.add_argument('--workers',type=int,default=20);a.add_argument('--finalize',action='store_true')
    a=s.add_parser('run');a.add_argument('--run',required=True);a.add_argument('--resume',action='store_true')
    a=s.add_parser('finalize');a.add_argument('--run',required=True)
    args=p.parse_args()
    if args.command=='prepare':prepare(args)
    elif args.command=='run':run(args)
    else:finalize(Path(args.run))


if __name__=='__main__':main()
