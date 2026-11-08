"""Verify and summarize the separate economic-size audit for initial searches."""
import gzip
import hashlib
import json
import math
from pathlib import Path


def load_review(path,data,source_sha256):
    path=Path(path);proof=json.loads(path.read_text(encoding='utf-8'))
    assert proof['Passed'] and not proof['Errors']
    assert proof['SourceDataSha256']==source_sha256
    assert proof['Selections']==len(data['Cases'])*data['Repetitions']*2==45360
    ledger=path.with_name('selection-rows.jsonl.gz')
    assert hashlib.sha256(ledger.read_bytes()).hexdigest()==proof['RowsSha256']
    cases={c['Id']:c for c in data['Cases']};seen=set();rn=[];focus=[];all_gains=[]
    for line in gzip.open(ledger,'rt',encoding='utf-8'):
        row=json.loads(line);key=(row['Repetition'],row['CaseId'],row['Selector'])
        assert key not in seen;seen.add(key)
        assert row['CaseId'] in cases and row['Selector'] in ('gini','liu') and 1<=row['Repetition']<=data['Repetitions']
        assert row['Seed']==1729+(row['Repetition']-1)*989
        gain=row['GainAsFractionOfActivityBenefit'][1]
        assert gain is not None and math.isfinite(gain) and gain>=0
        case=cases[row['CaseId']];parameters=case['Parameters']
        assert row['Risk']==parameters['Risk Aversion'] and row['Cost']==parameters['Costs Multiplier']
        all_gains.append(gain)
        if row['Risk']=='Risk Neutral':rn.append(gain)
        if (case['Family']=='Baseline' and row['Risk']=='Moderately Risk Averse' and row['Cost']=='4'
                and parameters['Fee Shifting Multiplier']=='2' and parameters['Damages Multiplier']=='2'):
            focus.append(gain)
    assert len(seen)==45360 and len(focus)==180 and len(rn)==21060
    return dict(Selections=len(seen),ProofSha256=hashlib.sha256(path.read_bytes()).hexdigest(),RowsSha256=proof['RowsSha256'],
        RiskNeutralSelections=len(rn),RiskNeutralMaximumDefendantGainOverBenefit=max(rn),
        FocusSelections=len(focus),FocusMinimumDefendantGainOverBenefit=min(focus),FocusMaximumDefendantGainOverBenefit=max(focus),
        FocusAboveFullBenefit=sum(g>1 for g in focus),AllDefendantAboveOnePercentOfBenefit=sum(g>.01 for g in all_gains),
        AllDefendantAboveTenPercentOfBenefit=sum(g>.1 for g in all_gains),AllDefendantAboveFullBenefit=sum(g>1 for g in all_gains),
        MaximumDefendantGainOverBenefit=max(all_gains),DecimalChecks=len(proof['DecimalChecks']),MaximumDecimalDifference=proof['MaximumDecimalDifference'])
