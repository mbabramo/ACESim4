"""Magnitude of observed search variation, kept within exact settings first."""
from itertools import combinations
import math
import statistics

from endogenous_story_report import quantile


def distribution(values):
    available=[v for v in values if v is not None]
    if any(not isinstance(v,(int,float)) or not math.isfinite(v) for v in available):
        raise ValueError('Nonfinite observations cannot enter a distribution')
    if not available:
        return dict(Observations=0,ExpectedObservations=len(values),Pairs=0,Mean=None,Median=None,
                    Minimum=None,Maximum=None,P10=None,P90=None,MeanAbsolutePairwiseGap=None,PairwiseGapFractionOfMean=None)
    gaps=[abs(b-a) for a,b in combinations(available,2)]
    mean=statistics.mean(available);gap=statistics.mean(gaps) if gaps else None
    relative=gap/abs(mean) if gap is not None and mean else 0. if gap==0 else None
    return dict(Observations=len(available),ExpectedObservations=len(values),Pairs=len(gaps),Mean=mean,
        Median=statistics.median(available),Minimum=min(available),Maximum=max(available),
        P10=quantile(available,.1),P90=quantile(available,.9),MeanAbsolutePairwiseGap=gap,PairwiseGapFractionOfMean=relative)


def build_distributions(data,seeds,outcomes):
    cases={}
    for case in data['Cases']:
        methods={}
        for method in ('gini','liu'):
            observations=case['Selectors'][method]['InitialValues']
            if len(observations)!=len(seeds):raise ValueError('Missing ordered initialization')
            methods[method]={}
            for metric in outcomes:
                values=[r[metric] for r in observations]
                methods[method][metric]=dict(Values=values,**distribution(values))
        cases[case['Id']]=dict(Name=case['OptionSetName'],Parameters=case['Parameters'],
            RepairedRepetitions=case['RepairedRepetitions'],Methods=methods)
    return dict(Schema='within-setting-distributions-v1',Seeds=seeds,Repetitions=len(seeds),Cases=cases,
        Outcomes={k:dict(Name=v[0],Meaning=v[1],Units=v[2],Kind='probability' if v[2].startswith('Fraction') else 'level' if k=='PrecautionLevel' else 'cost') for k,v in outcomes.items()},
        Interpretation='Observed initial searches, not a distribution over all equilibria. Each mean absolute pairwise gap uses every unordered pair within one setting and method, divided by that setting mean for the relative version. Conditional undefined observations are omitted with explicit availability; they never become zeros.')


def distribution_export_rows(payload):
    return [dict(CaseId=cid,Name=case['Name'],Method=method,Outcome=metric,OutcomeLabel=payload['Outcomes'][metric]['Name'],
                 **{k:v for k,v in stats.items() if k!='Values'},**case['Parameters'])
            for cid,case in payload['Cases'].items() for method,metrics in case['Methods'].items() for metric,stats in metrics.items()]
