"""A provenance index and replay recipe joining the endogenous study's stages.

This is intentionally distinct from a job scheduler. A recipe records immutable
inputs and exact stage commands; verify checks the evidence without launching any
solve. Existing failures/qualified candidates remain part of the record.
"""
import argparse
from datetime import datetime,timezone
import hashlib
import json
from pathlib import Path
import subprocess
import sys


def sha(p):
    h=hashlib.sha256()
    with Path(p).open('rb') as f:
        for b in iter(lambda:f.read(1024*1024),b''):h.update(b)
    return h.hexdigest()


def record(args):
    workspace=Path(args.workspace).resolve();code=Path(__file__).resolve().parents[2]
    output=Path(args.output).resolve()
    if output.exists():raise FileExistsError('Study recipes are immutable; write a new version')
    artifacts=[]
    def add(name,path,role,required=True):
        path=Path(path)
        if not path.exists():
            if required:raise FileNotFoundError(path)
            return
        relative=path.relative_to(workspace).as_posix() if path.is_relative_to(workspace) else str(path)
        artifacts.append(dict(Id=name,Path=relative,Sha256=sha(path),Role=role))
    inputs=workspace/'outputs/endogenous-population-inputs'
    catalog=json.loads((inputs/'catalog.json').read_text(encoding='utf-8'))
    add('catalog',inputs/'catalog.json','Case grid and exact article/model inputs')
    add('published-csv',catalog['ArticleCsv'],'Published original comparator')
    add('initial-manifest',workspace/'outputs/endogenous-population-memory-resumed/repetitions.json','Original twenty selection results per setting; ten per method')
    add('initial-replacements',workspace/'outputs/endogenous-population-repairs/accepted-replacements.json','Same-seed accepted paired replacements; never extra seeds')
    add('pooled-replacements',workspace/'outputs/endogenous-pooled-repairs/accepted-pooled-repairs.json','Accepted pooled case substitutions; not initial repetitions')
    for name in ('endogenous-original-validation-20261105.json','endogenous-replacement-validation-20261105.json','endogenous-repetitions-reconstruction-20261105.json','endogenous-replacement-harness-compatibility-20261105.json'):
        add(name,workspace/'work'/name,'Immutable source validation')
    report=workspace/'outputs/endogenous-story-report-20261107'
    for name in ('data-v2.json','statistics-v2.json','reviewed-findings-v8-magnitude.json','economic-accuracy-review-20261107.json'):
        add(name,report/name,'Versioned scientific/report input')
    add('initial-economic-review',workspace/'outputs/endogenous-initial-economic-review-20261108/completed.json','Separate economic-size checks')
    for name in ('endogenous-population-memory-config.json','endogenous-final-pooling-config.json'):
        add(name,workspace/'work'/name,'Frozen numerical batch settings')
    for name in ('endogenous-population-memory-runner/population_combined.py','endogenous-final-pooling-runner/population_combined.py'):
        add(name,workspace/'work'/name,'Exact historical orchestration code')
    runtime=workspace/'work/endogenous-population-memory-runtime'
    for name in ('ArticleReplication.dll','ACESimBase.dll','LitigCharts.dll'):add('population-runtime-'+name,runtime/name,'Frozen historical numerical runtime')
    traditional=Path(args.traditional_run).resolve();batch=json.loads((traditional/'batch.json').read_text())
    add('traditional-batch',traditional/'batch.json','Independent original-method starts; separate from Gini/Liu')
    add('traditional-runner',traditional/'runner.py','Frozen finite batch controller',False)
    for name,digest in batch['RuntimeSha256'].items():
        path=Path(batch['Assembly']).parent/name
        if sha(path)!=digest:raise ValueError('Traditional runtime hash differs')
        add('traditional-runtime-'+name,path,'Frozen independent-start runtime')
    add('traditional-candidates',traditional/'candidates.json','Completed original-method results and explicit numerical grades',False)
    add('traditional-completion',traditional/'completed.json','Execution completion, distinct from equilibrium qualification',False)
    add('code-snapshot',args.source_snapshot,'Git source archive for the new work',False) if args.source_snapshot else None
    modules=['endogenous_traditional.py','endogenous_study.py','endogenous_population_inputs.py','population_combined.py','endogenous_population_assembly.py','endogenous_population_analysis.py','endogenous_story_report.py','render_endogenous_story.py','endogenous_benchmark_ratios.py','render_endogenous_benchmarks.py']
    for name in modules:add('source-'+name,code/'ArticleReplication/Scripts'/name,'Versioned stage implementation')
    recipe=dict(Schema='endogenous-study-recipe-v1',CreatedUtc=datetime.now(timezone.utc).isoformat(),Workspace=str(workspace),
        CodeRevision=subprocess.check_output(['git','rev-parse','HEAD'],cwd=code,text=True).strip(),Artifacts=artifacts,
        Stages=[dict(Id='inputs',Purpose='Published CSV plus saved original strategy, parameter grid and source hashes'),
            dict(Id='traditional',DependsOn=['inputs'],Purpose='Ten independent original-method CFR searches per setting; raw, normalized and money-equivalent Nash checks'),
            dict(Id='initial-gini-liu',DependsOn=['inputs'],Purpose='Ten selection-guided searches per method, including preserved same-seed repair histories'),
            dict(Id='pooling',DependsOn=['initial-gini-liu'],Purpose='Union discovered strategies, reselect and expand on a common population; never average outcome numbers'),
            dict(Id='audit',DependsOn=['traditional','pooling'],Purpose='Keep Nash, CCE, economic accuracy, objective quality and archive identities separate'),
            dict(Id='report',DependsOn=['audit'],Purpose='Published comparator, traditional-start sensitivity and Gini/Liu robustness; explicit qualified/missing results')],
        Commands=dict(
            StartTraditional=['python','{traditional}/runner.py','run','--run','{traditional}'],
            ResumeTraditional=['python','{traditional}/runner.py','run','--run','{traditional}','--resume'],
            VerifyRecipe=['python','{code}/ArticleReplication/Scripts/endogenous_study.py','verify','--recipe','{recipe}'],
            RebuildRuntime=['dotnet','publish','{code}/ArticleReplication/ArticleReplication.csproj','-c','Release','--artifacts-path','{fresh-build}','-o','{fresh-runtime}','-p:BuildInParallel=false','-m:1'],
            PrepareFreshTraditional=['python','{code}/ArticleReplication/Scripts/endogenous_traditional.py','prepare','--input','{inputs}','--assembly','{fresh-runtime}/ArticleReplication.dll','--output','{fresh-run}','--workers','10','--repetitions','10','--iterations',str(batch['Iterations']),'--check-every',str(batch['CheckEvery'])],
            ReplayReport=['python','{code}/ArticleReplication/Scripts/render_endogenous_story.py','--data','{report}/data-v2.json','--statistics','{report}/statistics-v2.json','--findings','{report}/reviewed-findings-v8-magnitude.json','--economic-review','{report}/economic-accuracy-review-20261107.json','--initial-manifest','{workspace}/outputs/endogenous-population-memory-resumed/repetitions.json','--initial-economic-review','{workspace}/outputs/endogenous-initial-economic-review-20261108/completed.json','--output','{fresh-report}']),
        Semantics=dict(Replaying='Verify and reuse the exact archived numerical results, then regenerate tables/figures with the recorded code and inputs.',
            Recomputing='Run the numerical methods afresh with the recorded code, initialization scheme, seeds and settings. Newly failed attempts require explicit preserved diagnosis; existing qualified outcomes are never silently promoted.',
            Portability='Paths inside Workspace are relative. External article files are independently hash-bound. verify --workspace relocates internal evidence; exact numerical archives retain their original embedded paths until a separately recorded relocation is performed.',
            InProgress='The traditional batch may still be running. Record a fresh recipe version when its immutable candidate/completion index exists; do not hash mutable status as final evidence.',
            Limitations='The original pooled study retains one Liu candidate that misses 1e-7 and unresolved economic/objective accuracy qualifications. A reproducibility recipe does not certify those results. Integration of traditional outcomes into the report follows completed numerical validation.'))
    output.parent.mkdir(parents=True,exist_ok=True)
    with output.open('x',encoding='utf-8') as f:json.dump(recipe,f,indent=2,allow_nan=False)
    print(json.dumps(dict(Recipe=str(output),Artifacts=len(artifacts),CodeRevision=recipe['CodeRevision'])))


def verify(args):
    recipe=json.loads(Path(args.recipe).read_text(encoding='utf-8'));workspace=Path(args.workspace or recipe['Workspace']).resolve()
    if recipe['Schema']!='endogenous-study-recipe-v1':raise ValueError('Unknown recipe')
    seen=set()
    for item in recipe['Artifacts']:
        if item['Id'] in seen:raise ValueError('Duplicate artifact identifier')
        seen.add(item['Id']);path=Path(item['Path']);path=path if path.is_absolute() else workspace/path
        if sha(path)!=item['Sha256']:raise ValueError('Changed evidence: '+item['Id'])
    print(json.dumps(dict(Passed=True,Artifacts=len(seen),RecipeSha256=sha(args.recipe))))


def main():
    p=argparse.ArgumentParser(description=__doc__);sub=p.add_subparsers(dest='command',required=True)
    r=sub.add_parser('record');r.add_argument('--workspace',required=True);r.add_argument('--traditional-run',required=True);r.add_argument('--output',required=True);r.add_argument('--source-snapshot')
    v=sub.add_parser('verify');v.add_argument('--recipe',required=True);v.add_argument('--workspace')
    args=p.parse_args();record(args) if args.command=='record' else verify(args)


if __name__=='__main__':main()
