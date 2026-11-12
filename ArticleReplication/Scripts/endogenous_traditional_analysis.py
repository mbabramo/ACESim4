"""Compare independent CFR searches with the draft and selection-guided results.

No numerical evidence or article source is modified. All-seed distributions and
accuracy-screened subsets remain separate; missing observations are never filled.
"""
import argparse
from collections import defaultdict
from datetime import datetime, timezone
import csv
import gzip
import hashlib
from itertools import combinations
import json
import math
from pathlib import Path
import statistics as stats

from endogenous_population_analysis import outcome_metrics
from endogenous_benchmark_ratios import match_key
from endogenous_story_report import quantile

COMPONENTS = ('ForegoneActivityCost', 'PrecautionCost', 'HarmCost', 'LitigationExpenditures')
METRICS = ('SocialWelfareLoss',) + COMPONENTS + ('PrecautionLevel', 'Engagement',
    'DisputeProbability', 'Filing', 'AnsweringUnconditional', 'Trial', 'Settlement',
    'FilingGivenDispute', 'AnsweringGivenDispute', 'TrialGivenDispute', 'SettlementGivenDispute')
SCREENS = ('all', 'money', 'loose-money', 'strict-money')
LEGAL = ('Fee Shifting Multiplier', 'Damages Multiplier', 'Liability Threshold')


def read(p):
    p=Path(p);raw=p.read_bytes()
    return json.loads(gzip.decompress(raw) if p.suffix=='.gz' else raw)


def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()


def save(p, value):
    p=Path(p);raw=json.dumps(value,allow_nan=False,separators=(',',':')).encode()
    if p.suffix=='.gz':raw=gzip.compress(raw,mtime=0)
    with p.open('xb') as f:f.write(raw)


def distribution(values):
    values=[v for v in values if v is not None]
    if not values:return dict(N=0,Mean=None,SD=None,Minimum=None,Maximum=None,Median=None)
    if any(not math.isfinite(v) for v in values):raise ValueError('Nonfinite observation')
    return dict(N=len(values),Mean=stats.mean(values),SD=stats.stdev(values) if len(values)>1 else None,
                Minimum=min(values),Maximum=max(values),Median=stats.median(values))


def divide(a,b):return a/b if a is not None and b is not None and b!=0 else None


def accepted(row,screen):
    return screen=='all' or (row['EconomicScreenPassed'] and
        (screen=='money' or (row['NashAt1eMinus5'] if screen=='loose-money' else row['NashAtTarget'])))


def split_cost(values):
    """Split opportunity cost under this model's common net benefit beta=.001.

    Published rounding can make the residual slightly negative. Do not fabricate
    a positive component there: retain None and expose the unavailable split.
    """
    result=dict(values)
    result['ForegoneActivityCost']=max(0.,.001*(1-values['Engagement']))
    care=values['OpportunityCost']-result['ForegoneActivityCost']
    result['PrecautionCost']=care if care>=0 else (0. if care>=-1e-12 else None)
    return result


def collect(run,source,output):
    run=Path(run).resolve();index=read(run/'candidates.json');done=read(run/'completed.json');batch=read(run/'batch.json')
    if sha(run/'candidates.json')!=done['IndexSha256'] or not done['ComputationCompleted'] or index['Failures']:
        raise ValueError('Incomplete/changed computation')
    if index['BatchSha256']!=sha(run/'batch.json'):raise ValueError('Batch identity differs')
    data=read(source);cases={c['Id']:c for c in data['Cases']};n=data['Repetitions']
    expected={(cid,rep) for cid in cases for rep in range(1,n+1)};seen=set();rows=defaultdict(list)
    for i,item in enumerate(index['Candidates']):
        key=item['CaseId'],item['Repetition'];path=Path(item['Run'])
        if key in seen or key not in expected or item['Seed']!=batch['Seeds'][key[1]-1]:raise ValueError('Duplicate/missing seed position')
        seen.add(key)
        if sha(path/'completed.json')!=item['CompletionSha256'] or sha(path/'result.json')!=item['ResultSha256']:raise ValueError('Result/completion hash differs')
        result=read(path/'result.json');evidence=read(path/'completed.json')
        if evidence['Files']['result.json']!=item['ResultSha256']:raise ValueError('Result ledger differs')
        current=result['Current'];out=result['Outcomes'];metrics=outcome_metrics(out)
        metrics.update({m:out['Measures'][m] for m in COMPONENTS})
        if abs(sum(metrics[k] for k in COMPONENTS)-metrics['SocialWelfareLoss'])>1e-12:raise ValueError('Resource costs do not reconcile')
        if metrics['SocialWelfareLoss']!=item['SocialCost']:raise ValueError('Indexed cost differs')
        for flag in ('NashAtTarget','NashAt1eMinus5','EconomicScreenPassed'):
            if current[flag]!=item[flag]:raise ValueError('Accuracy flag differs')
        rows[key[0]].append(dict(Repetition=key[1],Seed=item['Seed'],Values=metrics,
            **{k:item[k] for k in ('NashAtTarget','NashAt1eMinus5','EconomicScreenPassed','MaximumNormalizedNashGain','MaximumCeGainFraction','ResultSha256','CompletionSha256','Run')}))
        if (i+1)%2000==0:print(json.dumps({'Collected':i+1}),flush=True)
    if seen!=expected:raise ValueError('Incomplete case/repetition coverage')
    result=dict(Schema='traditional-comparison-data-v1',CreatedUtc=datetime.now(timezone.utc).isoformat(),Seeds=batch['Seeds'],
        Cases=[dict(Id=c['Id'],Traditional=sorted(rows[c['Id']],key=lambda r:r['Repetition'])) for c in data['Cases']],
        Provenance=dict(TraditionalRoot=str(run),BatchSha256=sha(run/'batch.json'),IndexSha256=sha(run/'candidates.json'),
            SourceData=str(Path(source).resolve()),SourceDataSha256=sha(source),CollectorSha256=sha(__file__),
            Qualification='Ten independent approximate Nash candidates, with numerical and economic grades; not ten certified distinct equilibria.'))
    save(output,result);print(json.dumps({'Collected':len(seen),'Output':str(output)}),flush=True)


def composition(observations):
    """Normalize component movements by one fixed within-setting mean total."""
    n=len(observations);total=stats.mean(v['SocialWelfareLoss'] for v in observations) if n else None
    pairs=list(combinations(observations,2))
    total_gap=stats.mean(abs(a['SocialWelfareLoss']-b['SocialWelfareLoss']) for a,b in pairs) if pairs else None
    l1=stats.mean(sum(abs(a[k]-b[k]) for k in COMPONENTS) for a,b in pairs) if pairs else None
    return dict(N=n,MeanTotal=total,TotalSDRelativeToMean=divide(distribution([v['SocialWelfareLoss'] for v in observations])['SD'],total),
        MeanPairwiseTotalGapRelativeToMean=divide(total_gap,total),MeanPairwiseComponentMovementRelativeToMean=divide(l1,total),
        ComponentMovementCancelledInTotal=1-total_gap/l1 if l1 else None)


def contrasts(before,after,mask,metric):
    pairs=[(a['Values'][metric],b['Values'][metric]) for a,b,keep in zip(before,after,mask) if keep and a['Values'][metric] is not None and b['Values'][metric] is not None]
    delta=[b-a for a,b in pairs];summary=distribution(delta);a=distribution([a for a,b in pairs]);b=distribution([b for a,b in pairs])
    mean_relative=stats.mean((bb-aa)/abs(aa) for aa,bb in pairs) if pairs and all(aa!=0 for aa,bb in pairs) else None
    return dict(Pairs=len(pairs),Before=a,After=b,Difference=summary,MeanPerSeedRelativeChange=mean_relative,
        RelativeChangeInMeans=divide(summary['Mean'],abs(a['Mean'])) if a['Mean'] is not None else None,
        BLower=sum(v<0 for v in delta),ALower=sum(v>0 for v in delta),ExactTies=sum(v==0 for v in delta),
        MeanSmallerThanOneSearchSD=abs(summary['Mean'])<summary['SD'] if summary['SD'] is not None else None)


def exact_pairs(cases,parameter):
    groups=defaultdict(dict)
    for case in cases:
        key=tuple(sorted((k,v) for k,v in case['Parameters'].items() if k!=parameter));level=case['Parameters'][parameter]
        if level in groups[key]:raise ValueError('Ambiguous matched background')
        groups[key][level]=case
    for key,group in sorted(groups.items()):
        for a,b in combinations(sorted(group,key=lambda x:float(x) if parameter!='Risk Aversion' else x),2):
            yield dict(Parameter=parameter,From=a,To=b,A=group[a]['Id'],B=group[b]['Id'],Background=dict(key))


def analyze(cache,source,output,audit=None):
    cache=Path(cache);data=read(cache);source=Path(source);old=read(source);by={c['Id']:c for c in old['Cases']};trad={c['Id']:c['Traditional'] for c in data['Cases']}
    if data['Provenance']['SourceDataSha256']!=sha(source) or set(trad)!=set(by):raise ValueError('Comparison source changed')
    audit_value=read(audit) if audit and Path(audit).exists() else None
    if audit_value and (not audit_value['Passed'] or audit_value['IndexSha256']!=data['Provenance']['IndexSha256']):raise ValueError('Independent audit does not bind these candidates')
    case_rows=[];benchmark_map={};benchmarks=[]
    for c in old['Cases']:
        if c['Parameters']['Adjudication Mode']=='Perfect':
            if any(c['Parameters'][k]!=v for k,v in {'Costs Multiplier':'0','Court Noise':'Perfect','Noise Multiplier P':'0','Noise Multiplier D':'0'}.items()):raise ValueError('Imperfect ideal reference')
            costs=[r['SocialWelfareLoss'] for m in ('gini','liu') for r in c['Selectors'][m]['InitialValues']]
            if match_key(c) in benchmark_map:raise ValueError('Duplicate ideal reference')
            benchmark_map[match_key(c)]=(c['Id'],stats.mean(costs))
            benchmarks.append(dict(CaseId=c['Id'],Cost=stats.mean(costs),PriorInitialReference=distribution(costs),Traditional=distribution([r['Values']['SocialWelfareLoss'] for r in trad[c['Id']]]),QualifiedTraditional=sum(accepted(r,'strict-money') for r in trad[c['Id']])))
    for c in old['Cases']:
        cid=c['Id'];initial=trad[cid];reference=benchmark_map.get(match_key(c));summaries={}
        for screen in SCREENS:
            selected=[r['Values'] for r in initial if accepted(r,screen)]
            summaries[screen]=dict(Metrics={m:distribution([r[m] for r in selected]) for m in METRICS},Composition=composition(selected))
        other={m:dict(Pooled=split_cost(c['Selectors'][m]['Pooled']),Initial=[split_cost(r) for r in c['Selectors'][m]['InitialValues']],
             ProductNashAt1eMinus7=c['Selectors'][m]['ProductNashAt1eMinus7'],CceAt1eMinus7=c['Selectors'][m]['CceAt1eMinus7']) for m in ('gini','liu')}
        case_rows.append(dict(Id=cid,Name=c['OptionSetName'],Parameters=c['Parameters'],Published=split_cost(c['Published']),
            Traditional=initial,Summaries=summaries,Selectors=other,Reference=reference))
    pairs=[];pair_parameters=LEGAL+('Costs Multiplier','Unit Precaution Cost','Noise Multiplier P','Noise Multiplier D','Cost Misestimation','Relative Costs','Proportion of Costs at Beginning')
    for parameter in pair_parameters:
        for pair in exact_pairs(old['Cases'],parameter):
            a,b=by[pair['A']],by[pair['B']];aa,bb=trad[a['Id']],trad[b['Id']];effects={}
            for screen in SCREENS:
                mask=[accepted(x,screen) and accepted(y,screen) for x,y in zip(aa,bb)]
                effects[screen]={m:contrasts(aa,bb,mask,m) for m in METRICS}
            ref=benchmark_map.get(match_key(a))
            pairs.append(dict(**pair,Effects=effects,Reference=ref,Published={m:None if a['Published'].get(m) is None or b['Published'].get(m) is None else b['Published'][m]-a['Published'][m] for m in ('SocialWelfareLoss','OpportunityCost','HarmCost','LitigationExpenditures','PrecautionLevel','Engagement')},
                Pooled={method:{m:b['Selectors'][method]['Pooled'][m]-a['Selectors'][method]['Pooled'][m] for m in ('SocialWelfareLoss','OpportunityCost','HarmCost','LitigationExpenditures','PrecautionLevel','Engagement')} for method in ('gini','liu')}))
    output=Path(output);output.mkdir(exist_ok=False)
    result=dict(Schema='traditional-story-review-v1',CreatedUtc=datetime.now(timezone.utc).isoformat(),Cases=case_rows,Pairs=pairs,Benchmarks=benchmarks,
        Provenance=dict(CacheSha256=sha(cache),PriorSourceSha256=sha(source),IndexSha256=data['Provenance']['IndexSha256'],
            IndependentAuditSha256=sha(audit) if audit_value else None,IndependentAuditPassed=bool(audit_value),AnalyzerSha256=sha(__file__)),
        Definitions=dict(Screens={'all':'All ten saved candidates, whether or not accurate enough.','money':'Money-equivalent best-response gain <=0.1% of activity benefit, both players.','loose-money':'Money screen AND normalized Nash gain <=1e-5.','strict-money':'Money screen AND normalized Nash gain <=1e-7.'},
            Units='Expected model resource costs per activity opportunity. Transfers and risk premiums are not resource costs.',
            Variation='Sample SD with n-1 divisor among observed searches; not a confidence interval or proof of distinct exact equilibria.',
            Matching='Every recorded parameter except the named variable is fixed. Pair repetition to same original seed; this is a descriptive coupling, not random sampling inference.',
            Screening='Only jointly passing endpoints contribute to screened paired differences. Screening changes sample composition and can select equilibria. Counts remain explicit; missing results are not imputed.',
            Benchmark='Keep the earlier fixed mean of 20 Gini/Liu ideal-reference initial costs, so adding traditional results does not move the previous denominator. Traditional ideal-reference outcomes and grades are shown separately. Not a certified social optimum.',
            PolicyRatio='Use A’s single fixed ideal-reference denominator for BOTH endpoints. Raw effects are available even without an ideal match.',
            Components='Foregone activity = .001*(1-engagement). Opportunity cost = foregone activity plus precaution; exact native components are used for traditional results, the other series are split from these identities. Published rounded splits can be unavailable.'))
    save(output/'analysis.json.gz',result)
    rows=[]
    for c in case_rows:
        for screen,v in c['Summaries'].items():
            for metric,s in v['Metrics'].items():rows.append(dict(CaseId=c['Id'],Screen=screen,Metric=metric,**s,**c['Parameters']))
    write_csv(output/'setting-summaries.csv.gz',rows)
    write_csv(output/'search-outcomes.csv.gz',[dict(CaseId=c['Id'],Repetition=r['Repetition'],Seed=r['Seed'],NashAt1eMinus7=r['NashAtTarget'],NashAt1eMinus5=r['NashAt1eMinus5'],EconomicScreenPassed=r['EconomicScreenPassed'],MaximumNormalizedNashGain=r['MaximumNormalizedNashGain'],MaximumCeGainFraction=r['MaximumCeGainFraction'],**r['Values']) for c in case_rows for r in c['Traditional']])
    write_csv(output/'paired-policy-effects.csv.gz',[dict(Parameter=p['Parameter'],From=p['From'],To=p['To'],A=p['A'],B=p['B'],Screen=screen,Metric=m,Pairs=v['Pairs'],MeanDelta=v['Difference']['Mean'],SDDelta=v['Difference']['SD'],MeanPerSeedRelativeChange=v['MeanPerSeedRelativeChange'],RelativeChangeInMeans=v['RelativeChangeInMeans'],BLower=v['BLower'],ALower=v['ALower'],ExactTies=v['ExactTies'],PublishedDelta=p['Published'].get(m),GiniPooledDelta=p['Pooled']['gini'].get(m),LiuPooledDelta=p['Pooled']['liu'].get(m),**p['Background']) for p in pairs for screen,metrics in p['Effects'].items() for m,v in metrics.items()])
    overview=summarize(result);save(output/'overview.json',overview)
    policies=policy_summaries(result);save(output/'transition-summaries.json',policies)
    write_csv(output/'transition-summaries.csv.gz',policies)
    print(json.dumps({k:v for k,v in overview.items() if k!='RiskCostCells'}),flush=True)


def write_csv(path,rows):
    rows=list(rows);fields=list(dict.fromkeys(k for r in rows for k in r))
    with gzip.open(path,'xt',encoding='utf-8',newline='') as f:
        w=csv.DictWriter(f,fieldnames=fields);w.writeheader();w.writerows(rows)


def summarize(data):
    cells=[];groups=defaultdict(list)
    for c in data['Cases']:groups[c['Parameters']['Risk Aversion'],c['Parameters']['Costs Multiplier']].append(c)
    for (risk,cost),cases in sorted(groups.items()):
        for screen in SCREENS:
            selected=[r for c in cases for r in c['Traditional'] if accepted(r,screen)];v=[c['Summaries'][screen] for c in cases]
            cells.append(dict(Risk=risk,Cost=cost,Screen=screen,Settings=len(cases),Searches=len(selected),AllTen=sum(x['Composition']['N']==10 for x in v),
                AtLeastTwo=sum(x['Composition']['N']>=2 for x in v),MedianRelativeSD=quantile([x['Composition']['TotalSDRelativeToMean'] for x in v],.5),
                P90RelativeSD=quantile([x['Composition']['TotalSDRelativeToMean'] for x in v],.9),
                MedianComponentMovement=quantile([x['Composition']['MeanPairwiseComponentMovementRelativeToMean'] for x in v],.5),
                MedianCancelled=quantile([x['Composition']['ComponentMovementCancelledInTotal'] for x in v],.5)))
    methods=[]
    for method in ('traditional','gini','liu'):
        metrics=defaultdict(list)
        for c in data['Cases']:
            values=[r['Values'] for r in c['Traditional']] if method=='traditional' else c['Selectors'][method]['Initial']
            if any(any(v[k] is None for k in COMPONENTS) for v in values):continue
            co=composition(values)
            for k,v in co.items():
                if k not in ('N','MeanTotal'):metrics[k].append(v)
        methods.append(dict(Method=method,Summary={k:dict(Median=quantile(v,.5),P90=quantile(v,.9)) for k,v in metrics.items()}))
    return dict(Settings=len(data['Cases']),Searches=sum(len(c['Traditional']) for c in data['Cases']),
        GradeCounts={s:sum(accepted(r,s) for c in data['Cases'] for r in c['Traditional']) for s in SCREENS},
        MatchedPairs=len(data['Pairs']),LegalPairs=sum(p['Parameter'] in LEGAL for p in data['Pairs']),IdealReferences=len(data['Benchmarks']),
        MatchedReferenceSettings=sum(c['Reference'] is not None for c in data['Cases']),RiskCostCells=cells,Methods=methods,
        AuditPassed=data['Provenance']['IndependentAuditPassed'])


def policy_summaries(data):
    """Average within-background summaries only for identical transitions/scopes."""
    buckets=defaultdict(list)
    for pair in data['Pairs']:
        p=pair['Background']
        for screen in SCREENS:
            v=pair['Effects'][screen]['SocialWelfareLoss']
            if v['Pairs']<2:continue
            key=pair['Parameter'],pair['From'],pair['To'],p['Risk Aversion'],p.get('Costs Multiplier','varied'),screen
            buckets[key].append((pair,v))
    rows=[]
    for key,values in sorted(buckets.items()):
        parameter,start,end,risk,cost,screen=key
        rel=[v['MeanPerSeedRelativeChange'] for p,v in values]
        comparable=[(p,v) for p,v in values if p['Published']['SocialWelfareLoss'] is not None and p['Published']['SocialWelfareLoss']!=0 and v['Difference']['Mean']!=0]
        rows.append(dict(Parameter=parameter,From=start,To=end,Risk=risk,Cost=cost,Screen=screen,Backgrounds=len(values),
            AllTenPairs=sum(v['Pairs']==10 for p,v in values),SearchPairs=sum(v['Pairs'] for p,v in values),
            MeanPairedPercentageChange=100*stats.mean(rel) if all(v is not None for v in rel) else None,
            MeanAbsolutePairedPercentageChange=100*stats.mean(abs(v) for v in rel) if all(v is not None for v in rel) else None,
            AverageBLowerShare=stats.mean(v['BLower']/v['Pairs'] for p,v in values),
            ShareMeanSmallerThanSD=stats.mean(v['MeanSmallerThanOneSearchSD'] for p,v in values),
            DraftSameDirectionShare=stats.mean((p['Published']['SocialWelfareLoss']>0)==(v['Difference']['Mean']>0) for p,v in comparable) if comparable else None,
            DraftDirectionComparisons=len(comparable)))
    return rows


def diagnose(root):
    """Reproduce the thesis diagnostics from exported setting/search/pair tables."""
    root=Path(root);data=read(root/'render-data.json.gz');cases=data['Cases'];by={c['Id']:c for c in cases}
    comparisons=defaultdict(dict)
    with gzip.open(root/'paired-policy-effects.csv.gz','rt',encoding='utf-8') as f:
        for row in csv.DictReader(f):
            if row['Parameter'] not in LEGAL or row['Screen'] not in ('all','strict-money') or row['Metric'] not in ('SocialWelfareLoss',)+COMPONENTS:continue
            key=row['A'],row['B'],row['Screen'];comparisons[key][row['Metric']]=row
    rows=[]
    for screen in ('all','strict-money'):
        for min_pairs in (2,10):
            for min_draft_effect in (0,.001,.01):
                vv=[];offsets=[]
                for (a,b,s),metrics in comparisons.items():
                    if s!=screen:continue
                    row=metrics['SocialWelfareLoss'];n=int(row['Pairs'])
                    if n<min_pairs:continue
                    draft=float(row['PublishedDelta']);base=by[a]['Published']['SocialWelfareLoss'];effect=float(row['MeanDelta'])
                    if abs(draft)/base<min_draft_effect or draft==0 or effect==0:continue
                    movement=sum(abs(float(metrics[k]['MeanDelta'])) for k in COMPONENTS)
                    if movement:offsets.append(1-abs(effect)/movement)
                    vv.append((row,draft,effect))
                rows.append(dict(Screen=screen,MinimumPairs=min_pairs,MinimumAbsoluteDraftEffect=min_draft_effect,Comparisons=len(vv),
                    SameDirection=stats.mean((draft>0)==(effect>0) for r,draft,effect in vv) if vv else None,
                    MedianCancellationOfPolicyComponentMovements=stats.median(offsets) if offsets else None))
    cohorts=[]
    for minimum in (2,5,10):
        selected=[c for c in cases if c['Summaries']['strict-money']['Composition']['N']>=minimum]
        cohorts.append(dict(MinimumQualified=minimum,Settings=len(selected),
            AllSearchMedianSD=quantile([c['Summaries']['all']['Composition']['TotalSDRelativeToMean'] for c in selected],.5),
            ScreenedMedianSD=quantile([c['Summaries']['strict-money']['Composition']['TotalSDRelativeToMean'] for c in selected],.5),
            ScreenedP90SD=quantile([c['Summaries']['strict-money']['Composition']['TotalSDRelativeToMean'] for c in selected],.9),
            MedianDraftGap=quantile([abs(c['Summaries']['strict-money']['Metrics']['SocialWelfareLoss']['Mean']-c['Published']['SocialWelfareLoss'])/c['Published']['SocialWelfareLoss'] for c in selected],.5)))
    high=sorted([dict(CaseId=c['Id'],Name=c['Name'],N=c['Summaries']['strict-money']['Composition']['N'],SD=c['Summaries']['strict-money']['Composition']['TotalSDRelativeToMean']) for c in cases if c['Summaries']['strict-money']['Composition']['N']>=5],key=lambda r:r['SD'],reverse=True)[:12]
    result=dict(InputHashes={name:sha(root/name) for name in ('render-data.json.gz','paired-policy-effects.csv.gz')},
        ScriptSha256=sha(__file__),DirectionSensitivity=rows,SameSettingScreenCohorts=cohorts,LargestRemainingQualifiedDispersion=high,
        Note='Draft-effect floors are sensitivity descriptions, not numerical acceptance criteria or significance tests. Exact ties are excluded from sign agreement and remain recorded in raw tables. Screened subsets select candidates; a fall in dispersion does not by itself prove numerical error caused all earlier spread.')
    save(root/'conclusion-diagnostics.json',result);print(json.dumps(result),flush=True)


def main():
    p=argparse.ArgumentParser(description=__doc__);sub=p.add_subparsers(dest='command',required=True)
    c=sub.add_parser('collect');c.add_argument('--run',required=True);c.add_argument('--source',required=True);c.add_argument('--output',required=True)
    a=sub.add_parser('analyze');a.add_argument('--cache',required=True);a.add_argument('--source',required=True);a.add_argument('--output',required=True);a.add_argument('--audit')
    q=sub.add_parser('diagnose');q.add_argument('--analysis',required=True)
    args=p.parse_args()
    if args.command=='collect':collect(args.run,args.source,args.output)
    elif args.command=='analyze':analyze(args.cache,args.source,args.output,args.audit)
    else:diagnose(args.analysis)


if __name__=='__main__':main()
