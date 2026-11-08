"""All available legal-rule pairs, with every other parameter held fixed."""
from collections import defaultdict
from itertools import combinations
import hashlib
import json
import math
from pathlib import Path
import statistics

from endogenous_story_report import safe_ratio
from endogenous_report_language import parameter_name
from endogenous_magnitude import distribution

LEGAL_PARAMETERS = ('Fee Shifting Multiplier', 'Damages Multiplier', 'Liability Threshold')
METRIC = 'SocialWelfareLoss'


def fingerprint(value):
    return hashlib.sha256(json.dumps(value,sort_keys=True,separators=(',',':')).encode()).hexdigest()


def paired_outcomes(before, after):
    if len(before) != len(after) or not before:
        raise ValueError('Paired searches require equal, nonempty ordered repetitions')
    if not all(isinstance(v,(float,int)) and math.isfinite(v) for v in before+after):
        raise ValueError('Missing or nonfinite social cost must not become a tie')
    deltas = [b-a for a,b in zip(before,after)]
    relative = [safe_ratio(d,abs(a)) for a,d in zip(before,deltas)]
    lower, higher = sum(d<0 for d in deltas),sum(d>0 for d in deltas)
    valid_relative=[v for v in relative if v is not None]
    return dict(Before=before,After=after,Deltas=deltas,RelativeChanges=relative,
        BLower=lower,ALower=higher,ExactTies=len(deltas)-lower-higher,Pairs=len(deltas),
        BLowerFraction=lower/len(deltas),ALowerFraction=higher/len(deltas),
        BeforeMean=statistics.mean(before),AfterMean=statistics.mean(after),
        BeforeMedian=statistics.median(before),AfterMedian=statistics.median(after),
        MeanDelta=statistics.mean(deltas),MedianDelta=statistics.median(deltas),
        MinimumDelta=min(deltas),MaximumDelta=max(deltas),
        MeanAbsoluteDelta=statistics.mean(abs(d) for d in deltas),
        RelativeChangeCount=len(valid_relative),
        MeanRelativeChange=statistics.mean(valid_relative) if len(valid_relative)==len(deltas) else None,
        MeanAbsoluteRelativeChange=statistics.mean(abs(v) for v in valid_relative) if len(valid_relative)==len(deltas) else None,
        RelativeChangeDistribution=distribution(relative),
        BeforeDistribution=distribution(before),AfterDistribution=distribution(after),
        SeparatedRanges='B lower' if max(after)<min(before) else 'A lower' if max(before)<min(after) else 'Overlap or touch')


def seed_provenance(manifest_path, repetitions):
    path=Path(manifest_path);manifest=json.loads(path.read_text(encoding='utf-8'))
    if len(manifest['Runs']) != repetitions:
        raise ValueError('Manifest repetition count differs from report')
    rows=[]
    for i,run in enumerate(manifest['Runs'],1):
        plan=Path(run)/'plan.json';raw=plan.read_bytes();p=json.loads(raw)
        rows.append(dict(Repetition=i,Seed=p['InitialSeed'],Plan=str(plan),PlanSha256=hashlib.sha256(raw).hexdigest()))
    if len({r['Seed'] for r in rows}) != repetitions:
        raise ValueError('Repeated initialization seed')
    return dict(Manifest=str(path.resolve()),ManifestSha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                Repetitions=rows,Order='InitialValues[k] is repetition k+1, as stored by endogenous_story_report.collect; accepted same-seed repairs replace that position.')


def build_pairwise(data, seeds, baseline):
    n=data['Repetitions']
    if len(seeds)!=n or len(set(seeds))!=n:
        raise ValueError('One distinct seed is required for each ordered repetition')
    if len({c['Id'] for c in data['Cases']}) != len(data['Cases']):
        raise ValueError('Duplicate case identity')
    groups=[]
    for parameter in LEGAL_PARAMETERS:
        matching=defaultdict(dict)
        for case in data['Cases']:
            fixed=tuple(sorted((k,v) for k,v in case['Parameters'].items() if k!=parameter))
            level=case['Parameters'][parameter]
            if level in matching[fixed]:
                raise ValueError('Ambiguous cases with identical model parameters')
            matching[fixed][level]=case
        for fixed,levels in sorted(matching.items()):
            if len(levels)<2:
                continue
            background=dict(fixed);ordered=sorted(levels,key=float);pairs=[]
            group_id=fingerprint([parameter,background])[:20]
            for start,end in combinations(ordered,2):
                a,b=levels[start],levels[end]
                row=dict(Id=fingerprint([parameter,a['Id'],b['Id']])[:20],A=a['Id'],B=b['Id'],From=start,To=end,
                         CandidateA=a['ProvisionalCandidate'],CandidateB=b['ProvisionalCandidate'],
                         RepairedA=a['RepairedRepetitions'],RepairedB=b['RepairedRepetitions'])
                av,bv=a['Published'][METRIC],b['Published'][METRIC]
                row['Published']=dict(Before=av,After=bv,Delta=bv-av,RelativeChange=safe_ratio(bv-av,abs(av)))
                for method in ('gini','liu'):
                    before=[r[METRIC] for r in a['Selectors'][method]['InitialValues']]
                    after=[r[METRIC] for r in b['Selectors'][method]['InitialValues']]
                    if len(before)!=n or len(after)!=n:
                        raise ValueError('Missing initial repetition')
                    result=paired_outcomes(before,after)
                    pa,pb=(c['Selectors'][method]['Pooled'][METRIC] for c in (a,b))
                    result['Pooled']=dict(Before=pa,After=pb,Delta=pb-pa,RelativeChange=safe_ratio(pb-pa,abs(pa)))
                    row[method]=result
                pairs.append(row)
            deviations=[parameter_name(k)+': '+v for k,v in fixed if k not in ('Risk Aversion','Costs Multiplier') and v!=baseline.get(k)]
            groups.append(dict(Id=group_id,Parameter=parameter,Background=background,Levels=ordered,Pairs=pairs,
                               Label='; '.join(deviations) or 'Default other assumptions',
                               Default=all(v==baseline.get(k) for k,v in fixed if k not in ('Risk Aversion','Costs Multiplier'))))
    return dict(Schema='fixed-background-paired-searches-v1',Metric=METRIC,Seeds=seeds,Repetitions=n,
                ParameterLabels={k:parameter_name(k) for k in LEGAL_PARAMETERS},Groups=groups,
                GroupCount=len(groups),PairCount=sum(len(g['Pairs']) for g in groups),
                TieRule='Exact equality of stored costs; no economic or numerical equivalence is inferred.',
                Units='Raw costs per opportunity; the interface multiplies costs and absolute differences by 10000.')


def export_rows(payload, repetitions=False):
    rows=[]
    for g in payload['Groups']:
        for p in g['Pairs']:
            common=dict(GroupId=g['Id'],PairId=p['Id'],Parameter=g['Parameter'],From=p['From'],To=p['To'],
                        CaseA=p['A'],CaseB=p['B'],CandidateA=p['CandidateA'],CandidateB=p['CandidateB'],
                        **{'Fixed '+k:v for k,v in g['Background'].items()})
            for method in ('gini','liu'):
                s=p[method]
                if repetitions:
                    for i,seed in enumerate(payload['Seeds']):
                        rows.append(dict(common,Method=method,Repetition=i+1,Seed=seed,CostA=s['Before'][i],CostB=s['After'][i],
                                         DeltaBMinusA=s['Deltas'][i],RelativeChange=s['RelativeChanges'][i],
                                         ARepaired=i+1 in p['RepairedA'],BRepaired=i+1 in p['RepairedB']))
                else:
                    rows.append(dict(common,Method=method,Pairs=s['Pairs'],BLower=s['BLower'],ALower=s['ALower'],ExactTies=s['ExactTies'],
                                     BLowerFraction=s['BLowerFraction'],ALowerFraction=s['ALowerFraction'],
                                     MeanCostA=s['BeforeMean'],MeanCostB=s['AfterMean'],MedianCostA=s['BeforeMedian'],MedianCostB=s['AfterMedian'],
                                     MeanDelta=s['MeanDelta'],MedianDelta=s['MedianDelta'],MinimumDelta=s['MinimumDelta'],MaximumDelta=s['MaximumDelta'],
                                     SeparatedRanges=s['SeparatedRanges'],PooledCostA=s['Pooled']['Before'],PooledCostB=s['Pooled']['After'],
                                     PublishedCostA=p['Published']['Before'],PublishedCostB=p['Published']['After'],
                                     MeanRelativeChange=s['MeanRelativeChange'],MeanAbsoluteRelativeChange=s['MeanAbsoluteRelativeChange'],
                                     MeanAbsoluteDelta=s['MeanAbsoluteDelta'],RelativeChangeCount=s['RelativeChangeCount'],
                                     MeanAbsolutePairwiseGapA=s['BeforeDistribution']['MeanAbsolutePairwiseGap'],
                                     MeanAbsolutePairwiseGapB=s['AfterDistribution']['MeanAbsolutePairwiseGap'],
                                     PairwiseGapFractionOfMeanA=s['BeforeDistribution']['PairwiseGapFractionOfMean'],
                                     PairwiseGapFractionOfMeanB=s['AfterDistribution']['PairwiseGapFractionOfMean']))
    return rows


def transition_summary(groups):
    """Equal-weight background percentages for each exact A-to-B transition.

    Agreement counts the more common *direction*, excluding exact ties from
    the numerator but retaining them in the ten-search denominator.
    """
    matching=defaultdict(list)
    seen=set()
    for group in groups:
        for pair in group['Pairs']:
            identity=(group['Id'],pair['From'],pair['To'])
            if identity in seen:
                raise ValueError('A background must contribute once per transition')
            seen.add(identity)
            matching[(group['Parameter'],pair['From'],pair['To'])].append((group,pair))
    rows=[]
    for (parameter,start,end),entries in sorted(matching.items(),key=lambda r:(r[0][0],float(r[0][1]),float(r[0][2]))):
        row=dict(Parameter=parameter,From=start,To=end,Backgrounds=len(entries),GroupIds=[g['Id'] for g,p in entries])
        for method in ('gini','liu'):
            values=[p[method] for g,p in entries]
            for s in values:
                if s['Pairs']<=0 or s['BLower']+s['ALower']+s['ExactTies']!=s['Pairs']:
                    raise ValueError('Direction counts must cover every paired search')
            row[method]=dict(
                MeanBLowerFraction=statistics.mean(s['BLower']/s['Pairs'] for s in values),
                MeanALowerFraction=statistics.mean(s['ALower']/s['Pairs'] for s in values),
                MeanExactTieFraction=statistics.mean(s['ExactTies']/s['Pairs'] for s in values),
                MeanDirectionalAgreement=statistics.mean(max(s['BLower'],s['ALower'])/s['Pairs'] for s in values),
                BMoreOftenBackgrounds=sum(s['BLower']>s['ALower'] for s in values),
                AMoreOftenBackgrounds=sum(s['ALower']>s['BLower'] for s in values),
                TiedDirectionBackgrounds=sum(s['ALower']==s['BLower'] for s in values),
                SearchPairs=sum(s['Pairs'] for s in values))
            magnitude={
                'MeanSignedRelativeEffect':[s['MeanRelativeChange'] for s in values],
                'MeanAbsoluteRelativeEffect':[s['MeanAbsoluteRelativeChange'] for s in values],
                'MeanWithinSettingSpreadA':[s['BeforeDistribution']['PairwiseGapFractionOfMean'] for s in values],
                'MeanWithinSettingSpreadB':[s['AfterDistribution']['PairwiseGapFractionOfMean'] for s in values]}
            for key,observations in magnitude.items():
                row[method][key]=statistics.mean(observations) if all(v is not None for v in observations) else None
            row[method]['MeanSignedCostEffect']=statistics.mean(s['MeanDelta'] for s in values)
            row[method]['MeanAbsoluteCostEffect']=statistics.mean(s['MeanAbsoluteDelta'] for s in values)
            row[method]['RelativeBackgroundsAvailable']=sum(s['RelativeChangeCount']==s['Pairs'] for s in values)
        rows.append(row)
    return rows


def scoped_transition_summaries(payload):
    risks=sorted({g['Background']['Risk Aversion'] for g in payload['Groups']})
    costs=sorted({g['Background']['Costs Multiplier'] for g in payload['Groups']},key=float)
    rows=[]
    for risk in ['*']+risks:
        for cost in ['*']+costs:
            groups=[g for g in payload['Groups'] if (risk=='*' or g['Background']['Risk Aversion']==risk)
                    and (cost=='*' or g['Background']['Costs Multiplier']==cost)]
            for row in transition_summary(groups):
                rows.append(dict(row,RiskScope=risk,CostScope=cost))
    return rows


def transition_export_rows(payload):
    return [dict(Parameter=row['Parameter'],From=row['From'],To=row['To'],RiskScope=row['RiskScope'],
                 CostScope=row['CostScope'],Backgrounds=row['Backgrounds'],Method=method,**row[method])
            for row in payload['TransitionSummaries'] for method in ('gini','liu')]
