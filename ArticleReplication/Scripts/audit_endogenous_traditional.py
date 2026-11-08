"""Read-only audit of independent original-method starts and their saved evidence.

Independently reconstructs seeded initial probabilities and uses 60-digit CARA
inversion. Passing this audit does not turn a numerically unverified candidate into
an equilibrium. It writes a fresh immutable receipt only after complete coverage.
"""
import argparse
from collections import defaultdict
from decimal import Decimal,localcontext
import hashlib
import json
import math
from pathlib import Path
import statistics
import struct

import endogenous_traditional as runner
import population_archives

ALPHAS={'Risk Neutral':(0,0),'Mildly Risk Averse':(1,1),'Moderately Risk Averse':(2,2),'Highly Risk Averse':(4,4),
        'P Risk Averse':(2,0),'D Risk Averse':(0,2),'P More Risk Averse':(4,2),'D More Risk Averse':(2,4)}


class SeededRandom:
    """Independent implementation of the seeded .NET compatibility generator."""
    def __init__(self,seed):
        big=2147483647;mj=161803398-abs(seed);self.a=[0]*56;self.a[55]=mj;mk=1
        for i in range(1,55):
            j=21*i%55;self.a[j]=mk;mk=mj-mk
            if mk<0:mk+=big
            mj=self.a[j]
        for _ in range(4):
            for i in range(1,56):
                self.a[i]-=self.a[1+(i+30)%55]
                if self.a[i]<0:self.a[i]+=big
        self.i=0;self.j=21
    def next(self):
        self.i=self.i+1 if self.i<55 else 1;self.j=self.j+1 if self.j<55 else 1
        value=self.a[self.i]-self.a[self.j]
        if value==2147483647:value-=1
        if value<0:value+=2147483647
        self.a[self.i]=value
        return value/2147483647


def money_gain(utility,gain,alpha):
    with localcontext() as ctx:
        ctx.prec=60;u=Decimal(str(utility));g=Decimal(str(max(0,gain)));a=Decimal(alpha)
        if not a:return float(g)
        scale=Decimal(10)/((2*a).exp()-1);denominator=scale+1000-u-g
        if denominator<=0:return None
        return float(((denominator+g)/denominator).ln()/a)


def profile_hash(archive):
    digest=hashlib.sha256()
    for p in sorted(archive['Policies'],key=lambda x:x['Player']):
        digest.update(bytes([p['Player']]))
        for row in p['Probabilities']:
            for v in row:digest.update(struct.pack('<d',v if v else 0.))
    return digest.hexdigest()


def archive_rows(archive):
    policies={p['Player']:p for p in archive['Policies']}
    if set(policies)!={0,1} or len(archive['Policies'])!=2:raise ValueError('Expected one independent policy per player')
    positions={0:0,1:0};rows=[]
    for key in archive['Keys']:
        player=int(key.split('/')[0]);row=policies[player]['Probabilities'][positions[player]];positions[player]+=1
        if not row or any(not math.isfinite(v) or v<0 for v in row) or abs(sum(row)-1)>1e-10:raise ValueError('Invalid behavioral probabilities')
        rows.append(row)
    if any(positions[p]!=len(policies[p]['Probabilities']) for p in (0,1)):raise ValueError('Policy/layout coverage mismatch')
    return rows


def audit(root,output,repeat_root=None,population_manifest=None):
    root=Path(root);output=Path(output)
    if output.exists():raise FileExistsError('Audit receipts cannot be overwritten')
    config=runner.read(root/'batch.json');runner.validate_configuration(config)
    done=runner.read(root/'completed.json');index=runner.read(root/'candidates.json')
    if not done['ComputationCompleted'] or runner.sha(root/'candidates.json')!=done['IndexSha256']:raise ValueError('Batch is not completely recorded')
    if index['Failures']:raise ValueError('Worker/validation failures require diagnosis')
    entries={(r['CaseId'],r['Repetition']):r for r in index['Candidates']}
    if len(entries)!=len(index['Candidates']) or set(entries)!={(c,r) for c in config['Cases'] for r in range(1,config['Repetitions']+1)}:raise ValueError('Missing or duplicate independent starts')
    population_run=None
    if population_manifest:population_run=Path(runner.read(population_manifest)['Runs'][0])
    rows=[];profiles=defaultdict(set);largest_initial_error=0.;largest_ce_error=0.
    for key,item in entries.items():
        path=Path(item['Run']);plan=runner.job_plan(config,*key,path);verified=runner.validate_result(path,plan)
        if verified!=item:raise ValueError('Candidate index no longer matches evidence')
        result=runner.read(path/'result.json');game=runner.read(path/'game.json')
        archives={name:population_archives.read(path/name) for name in ('initial-population.json','population.json','average-population.json')}
        expected_keys=archives['initial-population.json']['Keys'];initial_rows=archive_rows(archives['initial-population.json'])
        random=SeededRandom(plan['Seed'])
        for row in initial_rows:
            weights=[1. if plan['Initialization']=='uniform' else -math.log(max(1e-15,random.next())) for _ in row];total=sum(weights)
            error=max(abs(v-w/total) for v,w in zip(row,weights));largest_initial_error=max(largest_initial_error,error)
            if error>2e-14:raise ValueError('Actual initial probabilities do not reproduce the stated seed')
        for archive in archives.values():
            archive_rows(archive)
            if archive['Keys']!=expected_keys or archive['GameHash']!=game['Fingerprint']['CompleteSha256']:raise ValueError('Archive game/layout mismatch')
        if population_run and runner.read(population_run/key[0]/'game.json')['Fingerprint']['CompleteSha256']!=game['Fingerprint']['CompleteSha256']:raise ValueError('Traditional/population strategic games differ')
        risk=game['Case']['Parameters']['Risk Aversion']
        for label in ('Current','AverageDiagnostic'):
            row=result[label]
            for player,alpha in enumerate(ALPHAS[risk]):
                ce=money_gain(row['ExpectedUtilities'][player],row['RawNashGains'][player],alpha);reported=row['CertaintyEquivalentGains'][player]
                if (ce is None)!=(reported is None):raise ValueError('CARA domain classification mismatch')
                if ce is not None:
                    error=abs(ce-reported);largest_ce_error=max(largest_ce_error,error)
                    if error>max(2e-12,abs(ce)*1e-9):raise ValueError('CARA money-equivalent gain differs from independent inverse')
        profiles[key[0]].add(profile_hash(archives['population.json']))
        rows.append(dict(CaseId=key[0],Repetition=key[1],Seed=plan['Seed'],CompletionSha256=runner.sha(path/'completed.json'),
            NashAtTarget=item['NashAtTarget'],EconomicScreenPassed=item['EconomicScreenPassed'],Seconds=item['TotalSeconds']))
    repeated=0
    if repeat_root:
        other=runner.read(Path(repeat_root)/'candidates.json')
        for item in other['Candidates']:
            key=item['CaseId'],item['Repetition'];original=entries[key];a=Path(original['Run']);b=Path(item['Run'])
            pa=runner.read(a/'plan.json');pb=runner.read(b/'plan.json')
            if not runner.compatible(pa,pb):raise ValueError('Repeat settings differ')
            for name in ('initial-population.json','population.json','average-population.json'):
                if population_archives.read(a/name)!=population_archives.read(b/name):raise ValueError('Same-seed archived profiles differ')
            ra=runner.read(a/'result.json');rb=runner.read(b/'result.json')
            for name in ('Current','AverageDiagnostic','Outcomes','Iterations','BestIteration'):
                if ra[name]!=rb[name]:raise ValueError('Same-seed numerical results differ')
            repeated+=1
    receipt=dict(Passed=True,Meaning='All recorded evidence and independent checks agree. Inspect numerical/economic grades; this is not blanket equilibrium acceptance.',
        BatchSha256=runner.sha(root/'batch.json'),IndexSha256=runner.sha(root/'candidates.json'),CodeSha256=runner.sha(__file__),
        Candidates=len(rows),NashAtTarget=sum(r['NashAtTarget'] for r in rows),EconomicScreenPassed=sum(r['EconomicScreenPassed'] for r in rows),
        FullyQualified=sum(r['NashAtTarget'] and r['EconomicScreenPassed'] for r in rows),
        ExactDistinctCurrentProfiles={k:len(v) for k,v in profiles.items()},SameSeedRepeatsVerified=repeated,
        MaximumInitialProbabilityReconstructionError=largest_initial_error,MaximumDecimalCeError=largest_ce_error,
        Seconds=dict(Mean=statistics.mean(r['Seconds'] for r in rows),Minimum=min(r['Seconds'] for r in rows),Maximum=max(r['Seconds'] for r in rows)),Rows=rows)
    runner.write(output,receipt);print(json.dumps({k:v for k,v in receipt.items() if k not in ('Rows','ExactDistinctCurrentProfiles')}))


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--run',required=True);p.add_argument('--output',required=True);p.add_argument('--repeat-root');p.add_argument('--population-manifest');a=p.parse_args()
    audit(a.run,a.output,a.repeat_root,a.population_manifest)


if __name__=='__main__':main()
