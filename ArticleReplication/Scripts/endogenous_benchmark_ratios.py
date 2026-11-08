"""Cost ratios and descriptive search variability against matched ideal references."""
import argparse
from collections import defaultdict
import csv
import gzip
import hashlib
import json
import math
from pathlib import Path
import statistics

from endogenous_paired_comparisons import build_pairwise
from endogenous_report_language import RISK_NAMES
from endogenous_story_report import quantile

FRICTIONS={'Adjudication Mode','Costs Multiplier','Court Noise','Noise Multiplier P','Noise Multiplier D'}
METHODS=('gini','liu')
METRIC='SocialWelfareLoss'


def match_key(case):
    return tuple(sorted((k,v) for k,v in case['Parameters'].items() if k not in FRICTIONS))


def describe(values):
    if len(values)<2 or any(not math.isfinite(v) for v in values):raise ValueError('Two or more finite observations required')
    return dict(Mean=statistics.mean(values),SD=statistics.stdev(values),Minimum=min(values),Maximum=max(values),
                Median=statistics.median(values),P10=quantile(values,.1),P90=quantile(values,.9),N=len(values))


def initial_costs(case,method,n):
    values=[r[METRIC] for r in case['Selectors'][method]['InitialValues']]
    if len(values)!=n or any(not math.isfinite(v) or v<0 for v in values):raise ValueError('Invalid or incomplete initial costs')
    return values


def build_analysis(data,seeds,defaults):
    n=data['Repetitions'];cases=data['Cases'];by_id={c['Id']:c for c in cases}
    if n!=len(seeds) or len(set(seeds))!=n or len(by_id)!=len(cases):raise ValueError('Ambiguous cases or seeds')
    refs={};benchmarks=[]
    for case in cases:
        if case['Parameters']['Adjudication Mode']!='Perfect':continue
        p=case['Parameters']
        if any(p[k]!=v for k,v in {'Costs Multiplier':'0','Court Noise':'Perfect','Noise Multiplier P':'0','Noise Multiplier D':'0'}.items()):raise ValueError('Ideal reference must remove all specified frictions')
        key=match_key(case)
        if key in refs:raise ValueError('Multiple references for the same remaining parameters')
        values={m:initial_costs(case,m,n) for m in METHODS};combined=values['gini']+values['liu'];benchmark=statistics.mean(combined)
        if benchmark<=0:raise ValueError('A positive reference cost is required')
        row=dict(Id=case['Id'],Parameters=p,Cost=benchmark,ReferenceDistribution=describe(combined),
                 Methods={m:describe(v) for m,v in values.items()},Values=values)
        refs[key]=row;benchmarks.append(row)
    rows=[];raw=[];unmatched=[];ratios={};matched_cases=set()
    for case in cases:
        reference=refs.get(match_key(case))
        if reference is None:
            unmatched.append(dict(CaseId=case['Id'],Name=case['OptionSetName'],Parameters=case['Parameters'],Reason='No ideal reference with identical remaining economic/legal parameters'))
            continue
        matched_cases.add(case['Id']);denominator=reference['Cost']
        for method in METHODS:
            values=initial_costs(case,method,n);rr=[v/denominator for v in values];summary=describe(rr)
            row=dict(CaseId=case['Id'],Name=case['OptionSetName'],Parameters=case['Parameters'],Method=method,
                IsReference=case['Parameters']['Adjudication Mode']=='Perfect',ReferenceCase=reference['Id'],BenchmarkCost=denominator,
                Ratios=rr,Costs=values,Ratio=summary,PublishedRatio=case['Published'][METRIC]/denominator,
                PooledRatio=case['Selectors'][method]['Pooled'][METRIC]/denominator,
                BenchmarkObservedRelativeSD=reference['ReferenceDistribution']['SD']/denominator,
                MeanRatioUsingBenchmarkMinimum=statistics.mean(values)/reference['ReferenceDistribution']['Minimum'],
                MeanRatioUsingBenchmarkMaximum=statistics.mean(values)/reference['ReferenceDistribution']['Maximum'],
                Candidate=case['ProvisionalCandidate'],RepairedRepetitions=case['RepairedRepetitions'])
            rows.append(row);ratios[case['Id'],method]=row
            for i,seed in enumerate(seeds):raw.append(dict(CaseId=case['Id'],Method=method,Repetition=i+1,Seed=seed,Cost=values[i],ReferenceCase=reference['Id'],BenchmarkCost=denominator,Ratio=rr[i],Repaired=i+1 in case['RepairedRepetitions']))
    comparisons=[]
    for group in build_pairwise(data,seeds,defaults)['Groups']:
        if group['Background']['Adjudication Mode']=='Perfect':continue
        for pair in group['Pairs']:
            if pair['A'] not in matched_cases or pair['B'] not in matched_cases:continue
            # Hold one denominator fixed on BOTH sides of a rule contrast.
            # Endpoint B's own ideal ratio remains separately available above.
            common=refs[match_key(by_id[pair['A']])]
            for method in METHODS:
                a,b=ratios[pair['A'],method],ratios[pair['B'],method]
                before=[v/common['Cost'] for v in a['Costs']];after=[v/common['Cost'] for v in b['Costs']]
                delta=[bb-aa for aa,bb in zip(before,after)];dd=describe(delta)
                comparisons.append(dict(Id=pair['Id'],Parameter=group['Parameter'],From=pair['From'],To=pair['To'],Background=group['Background'],
                    A=pair['A'],B=pair['B'],Method=method,CommonReference=common['Id'],BenchmarkCost=common['Cost'],
                    AOnCommonReference=describe(before),BOnCommonReference=describe(after),Difference=dd,PairedDifferences=delta,
                    AverageEffectSmallerThanSearchSD=abs(dd['Mean'])<dd['SD'],
                    BLower=sum(v<0 for v in delta),ALower=sum(v>0 for v in delta),Ties=sum(v==0 for v in delta),
                    PublishedDifference=(by_id[pair['B']]['Published'][METRIC]-by_id[pair['A']]['Published'][METRIC])/common['Cost'],
                    PooledDifference=(by_id[pair['B']]['Selectors'][method]['Pooled'][METRIC]-by_id[pair['A']]['Selectors'][method]['Pooled'][METRIC])/common['Cost']))
    cells=[];buckets=defaultdict(list)
    for row in rows:
        if not row['IsReference']:buckets[row['Parameters']['Risk Aversion'],row['Parameters']['Costs Multiplier'],row['Method']].append(row)
    for (risk,cost,method),values in sorted(buckets.items()):
        means=[v['Ratio']['Mean'] for v in values];sds=[v['Ratio']['SD'] for v in values]
        cells.append(dict(Risk=risk,Cost=cost,Method=method,Backgrounds=len(values),AverageGroupMean=statistics.mean(means),
            AverageWithinGroupSD=statistics.mean(sds),MedianWithinGroupSD=statistics.median(sds),P90WithinGroupSD=quantile(sds,.9),
            BetweenGroupMeanSD=statistics.stdev(means) if len(means)>1 else None))
    policies=[];buckets=defaultdict(list)
    for row in comparisons:buckets[row['Parameter'],row['From'],row['To'],row['Background']['Risk Aversion'],row['Background']['Costs Multiplier'],row['Method']].append(row)
    for (parameter,start,end,risk,cost,method),values in sorted(buckets.items(),key=lambda item:(item[0][0],float(item[0][1]),float(item[0][2]),item[0][3],float(item[0][4]),item[0][5])):
        policies.append(dict(Parameter=parameter,From=start,To=end,Risk=risk,Cost=cost,Method=method,Backgrounds=len(values),
            AverageSignedEffect=statistics.mean(v['Difference']['Mean'] for v in values),
            AverageAbsoluteMeanEffect=statistics.mean(abs(v['Difference']['Mean']) for v in values),
            AverageWithinGroupEffectSD=statistics.mean(v['Difference']['SD'] for v in values),
            ShareMeanEffectSmallerThanSD=statistics.mean(v['AverageEffectSmallerThanSearchSD'] for v in values),
            AverageBLowerShare=statistics.mean(v['BLower']/n for v in values)))
    return dict(Schema='ideal-reference-ratios-v1',Repetitions=n,Seeds=seeds,
        Denominator='One fixed arithmetic mean of the 20 initial ideal-reference costs (ten Gini and ten Liu), shared by both methods. Benchmark variability is reported separately. This is a descriptive reference, not a claim of an exact optimum.',
        ReferenceChanges=sorted(FRICTIONS),MatchedParameters='Every recorded parameter except the five explicitly replaced frictions must agree exactly, including risk, damages, fee reimbursement, liability threshold and care cost.',
        PolicyDifferenceDenominator='For each A-to-B contrast, both costs use the SAME ideal-reference mean matched to A. This preserves the sign of the raw cost difference when B has a different own-rule reference.',
        SDDefinition='Sample standard deviation with divisor n-1 across the ten saved searches; it describes search sensitivity, not statistical confidence or all equilibria. Mean effect smaller than one SD is a descriptive scale comparison, not a significance test.',
        Coverage=dict(AllSettings=len(cases),ReferenceSettings=len(benchmarks),MatchedSettings=len(matched_cases),OrdinaryMatchedSettings=len(matched_cases)-len(benchmarks),UnmatchedSettings=len(unmatched),
                      RatioRows=len(rows),SearchRatios=len(raw),PolicyComparisonRows=len(comparisons)),
        Benchmarks=benchmarks,Rows=rows,SearchRatios=raw,Unmatched=unmatched,Comparisons=comparisons,CellSummaries=cells,PolicySummaries=policies)


def csv_write(path,rows):
    fields=list(dict.fromkeys(k for row in rows for k in row))
    with path.open('x',encoding='utf-8',newline='') as f:
        writer=csv.DictWriter(f,fieldnames=fields);writer.writeheader();writer.writerows(rows)


def save_analysis(result,out):
    with (out/'analysis.json').open('x',encoding='utf-8') as f:json.dump(result,f,separators=(',',':'),allow_nan=False)
    csv_write(out/'case-ratios.csv',[dict(CaseId=r['CaseId'],Method=r['Method'],ReferenceCase=r['ReferenceCase'],BenchmarkCost=r['BenchmarkCost'],IsReference=r['IsReference'],
        **{'Ratio'+k:v for k,v in r['Ratio'].items()},PublishedRatio=r['PublishedRatio'],PooledRatio=r['PooledRatio'],
        BenchmarkObservedRelativeSD=r['BenchmarkObservedRelativeSD'],MeanRatioUsingBenchmarkMinimum=r['MeanRatioUsingBenchmarkMinimum'],MeanRatioUsingBenchmarkMaximum=r['MeanRatioUsingBenchmarkMaximum'],**r['Parameters']) for r in result['Rows']])
    csv_write(out/'search-ratios.csv',result['SearchRatios'])
    csv_write(out/'benchmarks.csv',[dict(CaseId=r['Id'],Cost=r['Cost'],**{'Reference'+k:v for k,v in r['ReferenceDistribution'].items()},**{m+k:v for m in METHODS for k,v in r['Methods'][m].items()},**r['Parameters']) for r in result['Benchmarks']])
    csv_write(out/'policy-comparisons.csv',[dict(ComparisonId=r['Id'],Parameter=r['Parameter'],From=r['From'],To=r['To'],Method=r['Method'],CaseA=r['A'],CaseB=r['B'],CommonReference=r['CommonReference'],BenchmarkCost=r['BenchmarkCost'],
        **{'A'+k:v for k,v in r['AOnCommonReference'].items()},**{'B'+k:v for k,v in r['BOnCommonReference'].items()},**{'Difference'+k:v for k,v in r['Difference'].items()},
        BLower=r['BLower'],ALower=r['ALower'],Ties=r['Ties'],AverageEffectSmallerThanSearchSD=r['AverageEffectSmallerThanSearchSD'],PublishedDifference=r['PublishedDifference'],PooledDifference=r['PooledDifference'],**r['Background']) for r in result['Comparisons']])
    csv_write(out/'policy-summaries.csv',result['PolicySummaries']);csv_write(out/'background-summaries.csv',result['CellSummaries'])
    csv_write(out/'unmatched-settings.csv',[dict(CaseId=r['CaseId'],Name=r['Name'],Reason=r['Reason'],**r['Parameters']) for r in result['Unmatched']])


def main():
    from endogenous_paired_comparisons import seed_provenance
    from render_endogenous_story import BASE
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--data',required=True);parser.add_argument('--initial-manifest',required=True);parser.add_argument('--output',required=True)
    args=parser.parse_args();source=Path(args.data);data=json.loads(source.read_text(encoding='utf-8'))
    provenance=seed_provenance(args.initial_manifest,data['Repetitions']);result=build_analysis(data,[r['Seed'] for r in provenance['Repetitions']],BASE)
    result['Provenance']=dict(SourceData=str(source.resolve()),SourceDataSha256=hashlib.sha256(source.read_bytes()).hexdigest(),ScriptSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),InitialManifest=provenance)
    out=Path(args.output);out.mkdir();save_analysis(result,out)
    print(json.dumps(result['Coverage']))
    print(json.dumps(result['CellSummaries']))


if __name__=='__main__':main()
