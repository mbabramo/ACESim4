"""Describe specific legal-rule transitions without mixing their endpoints."""
from collections import defaultdict
from endogenous_story_report import METHODS, method_values, quantile, safe_ratio
from endogenous_report_language import parameter_name

LEGAL_PARAMETERS = ('Fee Shifting Multiplier', 'Damages Multiplier', 'Liability Threshold')


def transition_tables(data):
    cases = {c['Id']: c for c in data['Cases']}
    groups = defaultdict(list)
    pairs = []
    seen = set()
    for pair in data['MatchedPairs']:
        parameter = pair['Parameter']
        if parameter not in LEGAL_PARAMETERS:
            continue
        a, b = cases[pair['ReferenceCase']], cases[pair['ComparisonCase']]
        identity = (parameter, a['Id'], b['Id'])
        if identity in seen:
            raise ValueError('Duplicate policy comparison')
        seen.add(identity)
        if {k:v for k,v in a['Parameters'].items() if k != parameter} != {k:v for k,v in b['Parameters'].items() if k != parameter}:
            raise ValueError('Policy comparison changes another assumption')
        start, end = a['Parameters'][parameter], b['Parameters'][parameter]
        if (start, end) != (pair['ReferenceLevel'], pair['ComparisonLevel']):
            raise ValueError('Policy comparison endpoints do not match cases')
        row = dict(Parameter=parameter, ParameterLabel=parameter_name(parameter), From=start, To=end,
                   ReferenceCase=a['Id'], ComparisonCase=b['Id'],
                   Risk=a['Parameters']['Risk Aversion'], LitigationCostFactor=a['Parameters']['Costs Multiplier'],
                   IncludesCandidate=a['ProvisionalCandidate'] or b['ProvisionalCandidate'])
        for method in METHODS:
            before, after = (method_values(c, method)['SocialWelfareLoss'] for c in (a, b))
            row[method+'Before'] = before
            row[method+'After'] = after
            row[method+'RelativeChange'] = safe_ratio(after-before, abs(before)) if before is not None and after is not None else None
        row.update({'Reference '+k:v for k,v in a['Parameters'].items()})
        pairs.append(row)
        groups[parameter, start, end, row['Risk'], row['LitigationCostFactor']].append(row)
    summaries = []
    for (parameter, start, end, risk, cost), rows in sorted(groups.items()):
        row = dict(Parameter=parameter, ParameterLabel=parameter_name(parameter), From=start, To=end,
                   Risk=risk, LitigationCostFactor=cost, MatchedPairs=len(rows),
                   CandidatePairs=sum(r['IncludesCandidate'] for r in rows))
        for method in METHODS:
            values = [r[method+'RelativeChange'] for r in rows if r[method+'RelativeChange'] is not None]
            row.update({method+'Available':len(values), method+'Median':quantile(values,.5),
                        method+'Minimum':min(values) if values else None,
                        method+'Maximum':max(values) if values else None})
        summaries.append(row)
    return summaries, pairs


SCRIPT = r'''
async function policyTransitions(){
  const body=document.querySelector('#policy-transitions tbody');if(!body)return;
  const data=await(await fetch('policy-transitions.json')).json();
  const rule=document.querySelector('#policy-rule'),risk=document.querySelector('#policy-risk'),cost=document.querySelector('#policy-cost');
  const signed=x=>x==null?'—':(x>0?'+':'')+(100*x).toLocaleString(undefined,{minimumFractionDigits:1,maximumFractionDigits:1})+'%';
  const endpoint=(p,x)=>p==='Fee Shifting Multiplier'?({'0':'None (0)','0.5':'Half (0.5)','1':'Full (1)','2':'Double (2)','5':'Fivefold (5)'}[x]||x):p==='Damages Multiplier'?({'0.5':'Half damages (0.5)','1':'Ordinary damages (1)','2':'Double damages (2)','4':'Quadruple damages (4)'}[x]||x):({'0':'Strict liability (0)','1':'Negligence benchmark (1)'}[x]||'Standard '+x);
  function draw(){
    const rows=data.filter(r=>r.Parameter===rule.value&&r.Risk===risk.value&&r.LitigationCostFactor===cost.value).sort((a,b)=>Number(a.From)-Number(b.From)||Number(a.To)-Number(b.To));
    body.replaceChildren();
    for(const r of rows){const tr=document.createElement('tr');
      for(const value of [endpoint(r.Parameter,r.From),endpoint(r.Parameter,r.To),String(r.MatchedPairs)]){const td=document.createElement('td');td.textContent=value;tr.append(td)}
      for(const m of ['published','gini','liu']){const td=document.createElement('td'),strong=document.createElement('strong'),span=document.createElement('span');strong.textContent=signed(r[m+'Median']);span.className='note';span.textContent='Observed: '+signed(r[m+'Minimum'])+' to '+signed(r[m+'Maximum'])+(r[m+'Available']!==r.MatchedPairs?' ('+r[m+'Available']+' available)':'');td.append(strong,document.createElement('br'),span);tr.append(td)}body.append(tr)
    }
    document.querySelector('#policy-description').textContent=rows.length?'Each row keeps the displayed endpoints, risk assumption and litigation-cost factor fixed. Other assumptions vary across matched pairs, but are identical within each pair.':'No matched comparisons are available for this combination. Missing scenarios are not filled in.';
    const n=rows.reduce((s,r)=>s+r.CandidatePairs,0);document.querySelector('#policy-qualification').textContent=(risk.value==='Risk Neutral'?'These pooled risk-neutral results have smaller economically measured incentive gains, but remain provisional.':'Risk-averse results may retain economically meaningful incentives to change behavior; these numbers need greater accuracy before firm interpretation.')+(n?' '+n+' comparison(s) include the flagged Liu candidate that misses the original numerical target.':'');
  }
  rule.onchange=risk.onchange=cost.onchange=draw;draw();
}policyTransitions();
'''
