"""Three-view comparison with transparent numerical qualifications and seed ranges.

This report reads completed result records without changing scientific acceptance
receipts. An explicitly identified candidate can appear with its achieved accuracy.
"""
import argparse
from collections import defaultdict
from datetime import datetime, timezone
import gzip
import hashlib
import json
import math
from pathlib import Path
import statistics

import population_archives as archives
from endogenous_population_analysis import MEASURES, ANCHORS, matched_pairs, outcome_metrics, published_metrics

METHODS = ('published', 'gini', 'liu')
CANDIDATE = 'endogenous-b281cc858a0f4d76'


def read(path): return archives.read(path)
def sha(path): return hashlib.sha256(archives.physical_path(path).read_bytes()).hexdigest()
def new(path, value):
    encoded=json.dumps(value,allow_nan=False,separators=(',',':'))
    with Path(path).open('x', encoding='utf-8') as f: f.write(encoded)


def safe_ratio(numerator, denominator):
    if denominator==0:return None
    value=numerator/denominator
    return value if math.isfinite(value) else None


def quantile(values, p):
    values = sorted(x for x in values if x is not None and math.isfinite(x))
    if not values: return None
    x = (len(values) - 1) * p; lo = int(x); hi = math.ceil(x)
    return values[lo] + (values[hi] - values[lo]) * (x - lo)


def span(values):
    values = [v for v in values if v is not None]
    if not values: return {'Minimum': None, 'Maximum': None, 'Median': None, 'Width': None, 'RelativeWidth': None, 'Available': 0}
    low, high, med = min(values), max(values), statistics.median(values)
    return {'Minimum': low, 'Maximum': high, 'Median': med, 'Width': high-low,
            'RelativeWidth': safe_ratio(high-low,abs(med)), 'Available': len(values)}


def contrast_envelope(reference, comparison):
    a = [x for x in reference if x is not None]; b = [x for x in comparison if x is not None]
    if not a or not b: return {'Lower': None, 'Upper': None, 'Direction': 'unavailable'}
    low, high = min(b)-max(a), max(b)-min(a)
    return {'Lower': low, 'Upper': high, 'Direction': 'positive' if low>0 else 'negative' if high<0 else 'overlap'}


def method_values(case, method):
    return case['Published'] if method=='published' else case['Selectors'][method]['Pooled']


def joint_pairs(cases,parameters,anchors,label,only=None):
    groups=defaultdict(dict)
    for case in cases:
        settings=case['Parameters'];key=tuple(sorted((k,v) for k,v in settings.items() if k not in parameters))
        level=tuple(settings[k] for k in parameters)
        if level in groups[key]:raise ValueError('Ambiguous joint parameter comparison')
        groups[key][level]=case
    rows=[]
    for group in groups.values():
        if tuple(anchors) not in group:continue
        reference=group[tuple(anchors)]
        for level,case in group.items():
            if level==tuple(anchors) or (only and not only(case)):continue
            rows.append({'Parameter':label,'ReferenceLevel':' / '.join(anchors),'ComparisonLevel':' / '.join(level),
                'ReferenceCase':reference['Id'],'ComparisonCase':case['Id']})
    return sorted(rows,key=lambda r:(r['ReferenceCase'],r['ComparisonLevel']))


def all_pairs(cases):
    return ([p for parameter,anchor in ANCHORS.items() for p in matched_pairs(cases,parameter,anchor)]
        +joint_pairs(cases,['Noise Multiplier P','Noise Multiplier D'],['1','1'],'Signal noise (P and D)')
        +joint_pairs(cases,['Adjudication Mode','Court Noise','Noise Multiplier P','Noise Multiplier D'],
             ['Baseline','Baseline','1','1'],'Perfect adjudication package',only=lambda c:c['Parameters']['Adjudication Mode']=='Perfect'))


def archive_index(path):
    path = archives.physical_path(path)
    value = json.loads(gzip.decompress(path.read_bytes()) if path.suffix=='.gz' else path.read_bytes())
    if value.get('Schema')!='population-policy-reference-v1':
        raise ValueError('Expected indexed archive for this full endogenous run')
    return (value['GameHash'], value['Keys'], [(p['Player'],p['Sha256']) for p in value['Probes']],
            {(p['Player'],p['Sha256']) for p in value['Policies']})


def collect(workspace, candidate_audit, output):
    workspace=Path(workspace).resolve(); pool=workspace/'outputs/endogenous-population-pooled'
    batch=read(pool/'batch-plan.json');manifest=read(pool/'repetitions.json')
    cases=manifest['Cases']; assert len(cases)==2268 and len(manifest['Runs'])==10
    catalog_path=Path(batch['Settings']['input'])/'catalog.json'; catalog=read(catalog_path)
    assert sha(catalog['ArticleCsv'])==catalog['ArticleCsvSha256']
    assert {c['Id']:c for c in cases}=={c['Id']:c for c in catalog['Cases']}
    initial_proof=read(workspace/'work/endogenous-original-validation-20261105.json')
    replacement_proof=read(workspace/'work/endogenous-replacement-validation-20261105.json')
    assert initial_proof['Passed'] and replacement_proof['Passed']
    initial={};repaired=set()
    for rep,path in enumerate(manifest['Runs'],1):
        run=Path(path);proof=initial_proof['Repetitions'][rep-1]
        assert sha(run/'completed.json')==proof['CompletedSha256'] and sha(run/'plan.json')==proof['PlanSha256']
        done=read(run/'completed.json');assert done['Passed'] and done['Cases']==2268 and done['Runs']==4536
        initial[rep]={(r['CaseId'],r['Selector']):r for r in done['Results']};assert len(initial[rep])==4536
    initial_index_path=workspace/'outputs/endogenous-population-repairs/accepted-replacements.json'
    initial_index=read(initial_index_path);assert sha(initial_index_path)==replacement_proof['ReplacementIndexSha256']
    assert manifest['Replacements']==initial_index['Replacements']
    for receipt in initial_index['Receipts']:assert sha(receipt['Path'])==receipt['Sha256'] and read(receipt['Path'])['Passed']
    for item in manifest['Replacements']:
        rep,run=item['repetition'],Path(item['run']);p=read(run/'plan.json');cid=p['Cases'][0]['Id']
        assert (rep,cid) not in repaired and p['InitialSeed']==1729+(rep-1)*989;repaired.add((rep,cid))
        initial[rep].update({(r['CaseId'],r['Selector']):r for r in read(run/'completed.json')['Results']})
    assert all(r['Status']=='FullGameCceVerified' and max(r['NormalizedGains'])<=1e-7 for rows in initial.values() for r in rows.values())
    pooled_index_path=workspace/'outputs/endogenous-pooled-repairs/accepted-pooled-repairs.json'
    pooled_index=read(pooled_index_path);pooled_repaired={}
    for entry in pooled_index['Replacements']:
        assert sha(entry['Receipt'])==entry['ReceiptSha256']
        assert entry['CaseId'] not in pooled_repaired;pooled_repaired[entry['CaseId']]=entry
    result=[];hashes=[]
    for i,case in enumerate(cases):
        cid=case['Id'];provisional_candidate=cid==CANDIDATE and cid not in pooled_repaired
        if provisional_candidate:
            run=workspace/'outputs/endogenous-pooled-repairs'/f'{cid}-a001/round-01/solve'
            rows=read(run/'completed.json')['Results'];audit_path=Path(candidate_audit).resolve()
            receipt_hash=None
        else:
            receipt_path=Path(pooled_repaired[cid]['Receipt']) if cid in pooled_repaired else pool/'combined'/cid/'completed.json'
            receipt=read(receipt_path);assert receipt['Passed'] and receipt['Case']==case and receipt['Repetitions']==10
            run=Path(receipt['FinalRun']);rows=read(run/'completed.json')['Results'];audit_path=Path(receipt['Audit'])
            assert rows==receipt['Results'];receipt_hash=sha(receipt_path)
        assert len(rows)==2 and {r['Selector'] for r in rows}=={'gini','liu'}
        plan=read(run/'plan.json');assert plan['Cases']==[case] and plan['InitialSeed']==1729
        assert plan['Settings']['MaximumNormalizedGain']==1e-7
        audit=read(audit_path);assert audit['Passed'] and len(audit['Rows'])==2
        audited={r['Selector']:r for r in audit['Rows']}
        signatures=[archive_index(run/cid/s/'population.json') for s in ('gini','liu')]
        assert signatures[0]==signatures[1],cid
        published,resolution=published_metrics(case);selectors={}
        for row in rows:
            selector=row['Selector'];assert row['CaseId']==cid
            detail=run/row['DetailsFile'];assert sha(detail)==row['DetailsSha256']
            a=audited[selector];assert a['CaseId']==cid and a['SourceResultSha256']==sha(detail)
            assert a['ArchiveSha256']==sha(run/cid/selector/'population.json')
            gain=max(row['NormalizedGains']); assert math.isfinite(gain) and gain<=1e-6
            assert max(abs(x-y) for x,y in zip(a['NormalizedCceGains'],row['NormalizedGains']))<=1e-8
            if not provisional_candidate:assert row['Status']=='FullGameCceVerified' and gain<=1e-7
            starts=[outcome_metrics(initial[rep][cid,selector]['Outcomes']) for rep in range(1,11)]
            nash=max(a['NormalizedProductNashGains'])
            selectors[selector]={'Pooled':outcome_metrics(row['Outcomes']),'InitialValues':starts,
                'InitializationRanges':{metric:span([r[metric] for r in starts]) for metric in MEASURES},
                'RawCceGains':row['RawGains'],'NormalizedCceGains':row['NormalizedGains'],
                'CceAt1eMinus7':gain<=1e-7,'CceAt1eMinus6':gain<=1e-6,'CceAt1eMinus5':gain<=1e-5,
                'NormalizedProductNashGains':a['NormalizedProductNashGains'],'ProductNashAt1eMinus7':nash<=1e-7,
                'ProductNashAt1eMinus5':nash<=1e-5,'Status':row['Status'],
                'ResultSha256':sha(detail),'ReferencePopulationCounts':[row['PlaintiffPolicies'],row['DefendantPolicies']]}
        assert outcome_metrics(rows[0]['Baseline'])==outcome_metrics(rows[1]['Baseline'])
        result.append({'Id':cid,'Family':case['Family'],'OptionSetName':case['OptionSetName'],'Parameters':case['Parameters'],
            'Published':published,'PublishedHalfPrintedUnit':resolution,'SavedPolicyReplay':outcome_metrics(rows[0]['Baseline']),
            'Selectors':selectors,'ProvisionalCandidate':provisional_candidate,'RepairedRepetitions':[rep for rep in range(1,11) if (rep,cid) in repaired]})
        hashes.append({'CaseId':cid,'FinalRun':str(run),'ReceiptSha256':receipt_hash,'CompletedSha256':sha(run/'completed.json'),
                       'Audit':str(audit_path),'AuditSha256':sha(audit_path),'ResultHashes':{s:selectors[s]['ResultSha256'] for s in selectors}})
        if i%100==0:print(json.dumps({'Collected':i+1,'Total':len(cases)}),flush=True)
    data={'Schema':'endogenous-story-comparison-v1','CreatedUtc':datetime.now(timezone.utc).isoformat(),
        'ArticleCases':len(result),'Repetitions':10,'AcceptedInitialSelections':45360,'PooledSelections':4536,
        'Cases':result,'MatchedPairs':all_pairs(cases),
        'Measures':{k:{'PublishedColumn':v[0],'Unit':v[1]} for k,v in MEASURES.items()},
        'Qualification':{'FullArchiveAudit':'Ongoing; this is a provisional scientific report. It uses checked compact outcomes, full result file hashes, existing independent oracle/product audits, and identical final policy indices. Exhaustive blob/weight validation is a separate running audit.',
            'CandidateCase':CANDIDATE,'CandidateInterpretation':'One explicitly labelled Liu candidate passes 1e-6, not the frozen 1e-7 target. Original acceptance records/settings are unchanged.',
            'Objective':'These are numerical selector outputs, not independently certified exact Gini/KL optima. CCE, entropy objective optimality and Nash are separate questions.',
            'Ranges':'Ten accepted independent starts per selector, with documented same-seed repairs. Descriptive observed ranges, not confidence intervals or an exhaustive equilibrium set.',
            'FirstEquilibrium':'The draft published CSV contains one Only Eq per setting. That published equilibrium is the first-equilibrium comparator.'},
        'Provenance':{'CatalogSha256':sha(catalog_path),'PublishedCsvSha256':catalog['ArticleCsvSha256'],
            'Manuscript':str(Path(catalog['ArticleCsv']).parents[1]/'Article and bibliography/endog_disputes.tex'),
            'OriginalValidationSha256':sha(workspace/'work/endogenous-original-validation-20261105.json'),
            'InitialReplacementValidationSha256':sha(workspace/'work/endogenous-replacement-validation-20261105.json'),
            'InitialReplacementIndexSha256':sha(initial_index_path),'PooledReplacementIndexSha256':sha(pooled_index_path),'FinalSources':hashes}}
    new(output,data);return data


def summarize(data):
    cases=data['Cases'];by_id={c['Id']:c for c in cases};metrics=list(MEASURES)
    variability=[];differences=[];contrasts=[];agreements=[]
    for metric in metrics:
        for selector in ('gini','liu'):
            ranges=[c['Selectors'][selector]['InitializationRanges'][metric] for c in cases]
            variability.append({'Metric':metric,'Selector':selector,'MedianWidth':quantile([r['Width'] for r in ranges],.5),
                'P90Width':quantile([r['Width'] for r in ranges],.9),'MedianRelativeWidth':quantile([r['RelativeWidth'] for r in ranges],.5),
                'P90RelativeWidth':quantile([r['RelativeWidth'] for r in ranges],.9)})
            changes=[(c['Selectors'][selector]['Pooled'][metric]-c['Published'][metric])/abs(c['Published'][metric])
                     for c in cases if c['Published'][metric] is not None and c['Published'][metric]!=0 and c['Selectors'][selector]['Pooled'][metric] is not None]
            differences.append({'Metric':metric,'Selector':selector,'NonzeroPublishedCases':len(changes),
                'MedianRelativeChange':quantile(changes,.5),'MedianAbsoluteRelativeChange':quantile([abs(x) for x in changes],.5),
                'P90AbsoluteRelativeChange':quantile([abs(x) for x in changes],.9)})
    grouped=defaultdict(list)
    for pair in data['MatchedPairs']:
        a,b=by_id[pair['ReferenceCase']],by_id[pair['ComparisonCase']]
        for metric in metrics:
            deltas={};percents={};envelopes={}
            for method in METHODS:
                av,bv=method_values(a,method)[metric],method_values(b,method)[metric]
                delta=None if av is None or bv is None else bv-av
                deltas[method]=delta;percents[method]=safe_ratio(delta,abs(av)) if delta is not None else None
                if method!='published':
                    envelopes[method]=contrast_envelope([x[metric] for x in a['Selectors'][method]['InitialValues']],
                                                        [x[metric] for x in b['Selectors'][method]['InitialValues']])
                grouped[pair['Parameter'],metric,method].append({'Delta':delta,'Percent':percents[method],
                    'Envelope':envelopes.get(method),'Risk':a['Parameters']['Risk Aversion'],'Cost':a['Parameters']['Costs Multiplier'],
                    'Level':pair['ComparisonLevel'],'Candidate':a['ProvisionalCandidate'] or b['ProvisionalCandidate']})
            directions=[0 if x==0 else 1 if x>0 else -1 for x in deltas.values() if x is not None]
            agree=len(directions)==3 and len(set(directions))==1
            contrasts.append({**pair,'Metric':metric,'Deltas':deltas,'RelativeChanges':percents,'InitialContrastEnvelopes':envelopes,
                'AllThreeDirectionsAgree':agree,'IncludesCandidate':a['ProvisionalCandidate'] or b['ProvisionalCandidate']})
    for (parameter,metric,method),rows in grouped.items():
        values=[r['Percent'] for r in rows if r['Percent'] is not None]
        agreements.append({'Parameter':parameter,'Metric':metric,'Method':method,'Pairs':len(rows),
            'MedianRelativeChange':quantile(values,.5),'MedianAbsoluteRelativeChange':quantile([abs(x) for x in values],.5),
            'P90AbsoluteRelativeChange':quantile([abs(x) for x in values],.9),
            'FractionObservedRangeIdentifiesSign':sum(r['Envelope']['Direction'] in ('positive','negative') for r in rows)/len(rows) if method!='published' else None})
    fragile=[]
    for case in cases:
        for metric in ('SocialWelfareLoss','PrecautionLevel','Engagement','TrialGivenDispute','SettlementGivenDispute'):
            for selector in ('gini','liu'):
                r=case['Selectors'][selector]['InitializationRanges'][metric]
                fragile.append({'CaseId':case['Id'],'Name':case['OptionSetName'],'Metric':metric,'Selector':selector,**r})
    return {'Variability':variability,'DifferencesFromPublished':differences,'MatchedSummary':agreements,'Contrasts':contrasts,
            'VariabilityDetails':fragile,'Numerical':{s:{'At1eMinus7':sum(c['Selectors'][s]['CceAt1eMinus7'] for c in cases),
                'At1eMinus6':sum(c['Selectors'][s]['CceAt1eMinus6'] for c in cases),
                'NashAt1eMinus7':sum(c['Selectors'][s]['ProductNashAt1eMinus7'] for c in cases),
                'NashAt1eMinus5':sum(c['Selectors'][s]['ProductNashAt1eMinus5'] for c in cases)} for s in ('gini','liu')}}


def main():
    parser=argparse.ArgumentParser(description=__doc__);sub=parser.add_subparsers(dest='action',required=True)
    p=sub.add_parser('collect');p.add_argument('--workspace',required=True);p.add_argument('--candidate-audit',required=True);p.add_argument('--output',required=True)
    p=sub.add_parser('summarize');p.add_argument('--data',required=True);p.add_argument('--output',required=True)
    args=parser.parse_args()
    if args.action=='collect':collect(args.workspace,args.candidate_audit,args.output)
    else:new(args.output,summarize(read(args.data)))


if __name__=='__main__':main()
