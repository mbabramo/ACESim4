"""Audited individual profiles -> editable TeX, PDF/PNG, CSV, and study inventory.

Consumes LitigCharts agreement-study-audit outputs. Never solves or averages policies.
"""
from __future__ import annotations
import argparse
import concurrent.futures
import csv
import hashlib
import json
import math
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

FEES = ['American', 'Trial Fee-Shifting', 'Complete Fee-Shifting']
COLORS = ['blue!65!black', 'orange!80!black', 'green!50!black']
METRICS = ['Filing', 'JointFileAnswer', 'AnsweringGivenFiling', 'Settlement', 'Abandonment', 'Default', 'Trial', 'TrialGivenFiling', 'TrialGivenFileAnswer']
AGREEMENT = ['BothAgreeGivenStage', 'OnlyPlaintiffDeclinesGivenStage', 'OnlyDefendantDeclinesGivenStage', 'BothDeclineGivenStage']
HEADINGS = {'Filing': 'Filing', 'JointFileAnswer': 'File and answer', 'AnsweringGivenFiling': 'Answer given filing',
    'Settlement': 'Settlement', 'Abandonment': 'Abandonment', 'Default': 'Default', 'Trial': 'Trial',
    'TrialGivenFiling': 'Trial given filing', 'TrialGivenFileAnswer': 'Trial given file and answer'}
WELFARE = ['Plaintiff shortfall contribution', 'Nonliable defendant contribution', 'Liable defendant contribution', 'Outcome Error Before Legal Costs and Fee Transfers', 'Real Litigation Costs']

def write(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding='utf-8')

def dump(path, obj):
    write(path, json.dumps(obj, indent=2, allow_nan=False))

def fingerprint(path):
    return {'Path': str(path.resolve()), 'Sha256': hashlib.sha256(path.read_bytes()).hexdigest()}

def esc(s):
    return str(s).replace('\\', r'\textbackslash{}').replace('_', r'\_').replace('&', r'\&').replace('%', r'\%')

def wrapper(body):
    return r'''\documentclass[10pt,border=8pt]{standalone}
\usepackage[T1]{fontenc}\usepackage{lmodern,booktabs,array,tikz,pgfplots}
\usetikzlibrary{arrows.meta,positioning,calc,shapes.geometric}
\pgfplotsset{compat=1.18}\hyphenpenalty=10000\exhyphenpenalty=10000
\begin{document}
''' + body + '\n\\end{document}\n'

def csv_write(path, rows, headers=None):
    path.parent.mkdir(parents=True, exist_ok=True)
    if not headers:
        headers = list(dict.fromkeys(k for row in rows for k in row))
    with path.open('w', newline='', encoding='utf-8') as f:
        w = csv.DictWriter(f, headers); w.writeheader(); w.writerows(rows)

def compile_one(source):
    with tempfile.TemporaryDirectory(prefix='acesim-ab-') as tmp:
        tmp = Path(tmp)
        def run(args):
            r = subprocess.run(args, cwd=source.parent, capture_output=True, text=True, timeout=240,
                creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)
            if r.returncode:
                write(source.with_suffix('.compile-error.txt'), r.stdout + '\n' + r.stderr)
                raise RuntimeError(f'Compilation failed: {source}')
        run(['lualatex', '--interaction=nonstopmode', '--halt-on-error', '--jobname=diagram', f'--output-directory={tmp}', str(source)])
        run(['pdftoppm', '-png', '-singlefile', '-r', '150', str(tmp/'diagram.pdf'), str(tmp/'diagram')])
        for ext in ['pdf', 'png']:
            shutil.copyfile(tmp/f'diagram.{ext}', source.parent.parent/f'{source.stem}.{ext}')
        log = (tmp/'diagram.log').read_text(encoding='utf-8', errors='replace')
        warnings = [line for line in log.splitlines() if 'Overfull' in line or 'Missing character' in line]
        if warnings:
            raise RuntimeError(f'Layout warning in {source}: {warnings}')
    return str(source)

def structure():
    return wrapper(r'''\begin{tikzpicture}[>=Latex,font=\small,
box/.style={draw,rounded corners,align=center,text width=5.2cm,inner sep=6pt},
decision/.style={circle,draw,inner sep=2pt,minimum size=5mm},every edge/.style={draw,->}]
\node[box] (entry) at (0,0) {Plaintiff files; defendant answers\\Private exit commitments: continue or exit if bargaining fails};
\node[decision] (p) at (0,-1.7) {P};
\draw[->] (entry)--node[right]{Agreement to bargain}(p);
\node[decision] (dy) at (-3.8,-3.3) {D};\node[decision] (dn) at (3.8,-3.3) {D};
\draw[->] (p)-|node[pos=.25,above]{Agree}(dy);\draw[->] (p)-|node[pos=.25,above]{Decline}(dn);
\draw[densely dashed,thick] (dy)--node[above,align=center]{Same D information set\\P's agreement is not yet observed}(dn);
\node[box,text width=4.2cm] (offers) at (-4,-5.2) {Both decisions now observable\\Both agree: simultaneous offers};
\node[box,text width=5.2cm] (fail) at (2,-5.2) {Either declines: offers skipped\\Bargaining unsuccessful};
\draw[->] (dy)--node[left]{Agree}(offers);
\draw[->] (dy)--node[above,sloped]{Decline}(fail);
\draw[->] (dn)--node[right]{Agree or decline}(fail);
\node[box,text width=3cm] (settle) at (-5.3,-8.1) {Overlapping offers\\Settlement};
\node[box,text width=6.8cm] (exit) at (1,-8.1) {Resolve existing exit commitments\\P only exits: abandonment; D only exits: default\\Both exit: existing 50--50 lottery\\Neither exits: trial};
\draw[->] (offers)--(settle);\draw[->] (offers)--node[above,sloped,pos=.7,font=\scriptsize]{No overlap}(exit);\draw[->] (fail)--(exit);
\node[align=center,text width=13cm] at (0,-10.1) {Each player knows its own signal and private exit commitment. The opponent's exit commitment stays private. Agreement decisions are simultaneous and observable after both choices. Refusal adds no cost or fee trigger; existing disposition rules apply.};
\end{tikzpicture}''')

def axis(title, series, offers=False):
    opts = r'width=7cm,height=4.1cm,xmin=0.5,xmax=10.5,xtick={1,...,10},ymin=0,ymax=1,ytick={0,.25,.5,.75,1},tick label style={font=\scriptsize},label style={font=\small},title style={font=\small},xlabel={Own signal},ylabel={' + ('Offer amount' if offers else 'Probability') + '},title={' + title + '},clip=false'
    body = '\\begin{tikzpicture}\\begin{axis}[' + opts + ']\n'
    for color, mark, points in series:
        if offers:
            for x, y, probability in points:
                body += f'\\addplot[only marks,mark={mark},color={color},mark size={0.5+2.8*math.sqrt(probability):.4f}pt] coordinates {{({x:.4f},{y:.6f})}};\n'
        else:
            body += f'\\addplot[only marks,mark={mark},color={color},mark size=1.9pt] coordinates {{' + ' '.join(f'({x},{y:.10g})' for x,y in points if y is not None) + '};\n'
    return body + '\\end{axis}\\end{tikzpicture}'

def individual(p):
    panels = []
    panels += [axis('Plaintiff filing', [(COLORS[0], '*', [(s['Signal'], s['Filing']) for s in p['BySignal']])]),
               axis('Defendant answering given filing', [(COLORS[0], '*', [(s['Signal'], s['AnsweringGivenFiling']) for s in p['BySignal']])])]
    for party, title in [('P','Plaintiff agreement'), ('D','Defendant agreement')]:
        series=[]
        for exit_action, color, mark in [(2, COLORS[0], '*'), (1,COLORS[1],'triangle*')]:
            series.append((color,mark,[(n['Signal'],n['Probabilities'][0]) for n in p['Strategies']
                if n['Decision']==party+'AgreeToBargain' and n['OwnExit']==exit_action and n['Reach']>0]))
        panels.append(axis(title, series))
    for decision, title in [('PAbandon','Plaintiff commitment to abandon'), ('DDefault','Defendant commitment to default')]:
        panels.append(axis(title, [(COLORS[0],'*',[(n['Signal'], n['Probabilities'][0]) for n in p['Strategies'] if n['Decision']==decision and n['Reach']>0])]))
    for party, title in [('P','Plaintiff offers: both must agree'), ('D','Defendant offers: both must agree')]:
        series=[]
        for exit_action,color,mark in [(2,COLORS[0],'*'),(1,COLORS[1],'triangle*')]:
            pts=[(n['Signal']+(-.12 if exit_action==2 else .12), .05+.1*a, prob)
                for n in p['Strategies'] if n['Decision']==party+'Offer' and n['OwnExit']==exit_action and n['Reach']>0
                for a,prob in enumerate(n['Probabilities']) if prob>0]
            series.append((color,mark,pts))
        panels.append(axis(title,series,True))
    title=f"{p['FeeRule']} | {'Risk neutral' if p['Alpha']==0 else 'CARA alpha = 2'} | Cost multiplier {p['CostMultiplier']:g}"
    body=r'\begin{minipage}{15cm}\centering{\large '+esc(title)+r'}\par\medskip'+'\n'
    body+=r'\begin{tabular}{cc}'+'\n'+' \\\\[5pt]\n'.join(panels[i]+' & '+panels[i+1] for i in range(0,8,2))+r'\end{tabular}\par'
    body+=r'''\smallskip\footnotesize In the agreement and offer panels, blue circles indicate own commitment to continue; orange triangles indicate own commitment to exit.
Agreement and offer probabilities condition on reaching that decision with the indicated commitment.
Blank signal/commitment positions are unreached and undefined. Saved off-path prescriptions remain in the data.
Offer symbols retain each action probability; radius grows with its square root (largest = probability 1).
Small horizontal offsets separate commitment histories.\end{minipage}'''
    return wrapper(body)

def table(rows, columns, headings, caption, widths=None):
    body=r'\begin{minipage}{18cm}\centering\begin{tabular}{'+'l'*len(columns)+'}\n\\toprule\n'
    body+=' & '.join(headings)+r' \\ \midrule'+'\n'
    body+='\n'.join(' & '.join(esc(row[c]) for c in columns)+r' \\' for row in rows)
    body+=r'\bottomrule\end{tabular}\par\medskip\footnotesize '+esc(caption)+r'\end{minipage}'
    return wrapper(body)

def single_value(values):
    values=[float(x) for x in values if x is not None]
    if len(values)>1: raise ValueError('Expected one equilibrium per case')
    return 'undefined' if not values else f'{values[0]:.4f}'

def comparison_plots(profiles, baseline, alpha, cost, metrics, titles):
    panels=[]
    for metric,title in zip(metrics,titles):
        probability_limits = r',ymin=0,ymax=1,ytick={0,.25,.5,.75,1}' if metric not in WELFARE else ''
        body=r'\begin{tikzpicture}\begin{axis}[width=6cm,height=4.6cm,xmin=.5,xmax=3.5,xtick={1,2,3},xticklabels={American,Trial,Complete},title={'+title+r'},tick label style={font=\scriptsize},scaled y ticks=false,yticklabel style={/pgf/number format/fixed,/pgf/number format/precision=3},title style={font=\small},ylabel={Per potential dispute},label style={font=\scriptsize}'+probability_limits+']'+'\n'
        for enabled, data, color, mark, dx in [(False,baseline,'gray','square*',-.09),(True,profiles,COLORS[0],'*',.09)]:
            for fee_index,fee in enumerate(FEES,1):
                vals=[p['Metrics'][metric] for p in data if p['Alpha']==alpha and p['CostMultiplier']==cost and p['FeeRule']==fee]
                vals=[v for v in vals if v is not None]
                if not vals: continue
                x=fee_index+dx
                for v in sorted(set(round(v,10) for v in vals)):
                    body+=f'\\addplot[only marks,color={color},mark={mark},mark size=1.3pt] coordinates {{({x},{v})}};\n'
        panels.append(body+r'\end{axis}\end{tikzpicture}')
    if len(panels)%3: panels+=['']*(3-len(panels)%3)
    body=r'\begin{minipage}{19cm}\centering{\large '+('Risk neutrality' if alpha==0 else 'Symmetric CARA, alpha = 2')+f' | Cost multiplier {cost:g}'+r'}\par\medskip\begin{tabular}{ccc}'+'\n'
    body+=' \\\\[8pt]\n'.join(' & '.join(panels[i:i+3]) for i in range(0,len(panels),3))+r'\end{tabular}\par\footnotesize Gray squares: agreement disabled. Blue circles: agreement enabled. One verified equilibrium per case. The agreement-enabled and existing agreement-disabled profiles are evaluated separately.\end{minipage}'
    return wrapper(body)

def distinct_groups(profiles, vector, tolerance=1e-7):
    reps=[]; groups=[]
    for p in profiles:
        v=vector(p)
        match=next((i for i,r in enumerate(reps) if max((abs(v.get(k,0)-r.get(k,0)) for k in v.keys()|r.keys()),default=0)<=tolerance),None)
        if match is None: match=len(reps); reps.append(v); groups.append([])
        groups[match].append(p['Equilibrium'])
    return groups

def strategy_vector(p):
    return {(n['InformationSet'],a):v for n in p['Strategies'] for a,v in enumerate(n['Probabilities'])}

def reached_vector(p):
    return {tuple(h[k] for k in h if k!='Probability'):h['Probability'] for h in p['ReachedHistories']}

def load_profiles(root):
    result=[]
    for path in sorted((root/'Sources'/'Profiles').glob('*.json')):
        p=json.loads(path.read_text(encoding='utf-8-sig'));p['_source']=path;result.append(p)
    with (root/'Sources'/'equilibrium-outcomes.csv').open(encoding='utf-8-sig',newline='') as f: outcomes=list(csv.DictReader(f))
    # Use the five existing headline fields, in their declared report order.
    welfare=[k for k in WELFARE if k in outcomes[0]]
    if len(welfare)!=5:
        raise ValueError(f'Expected five headline fields; got {welfare}')
    for p in result:
        row=next(r for r in outcomes if r['OptionSetName']==p['OptionSet'] and int(r['Equilibrium'])==p['Equilibrium'])
        p['OutcomeRow']=row
        for k in welfare: p['Metrics'][k]=float(row[k])
        p['Metrics'].update({'NoFiling':1-p['Metrics']['Filing'],'NoAnswer':p['Metrics']['Filing']-p['Metrics']['JointFileAnswer'],'PLoses':float(row['P Loses']),'PWins':float(row['P Wins'])})
    return result,welfare

def main():
    ap=argparse.ArgumentParser();ap.add_argument('--study',type=Path,required=True);ap.add_argument('--baseline',type=Path)
    ap.add_argument('--jobs',type=int,default=4);ap.add_argument('--sources-only',action='store_true');ap.add_argument('--structure-only',action='store_true')
    a=ap.parse_args();root=a.study.resolve();exhibits=[]
    def exhibit(folder,stem,tex,data):
        source=root/folder/'Sources'/f'{stem}.tex';write(source,tex);dump(source.with_suffix('.json'),data);exhibits.append(source)
    exhibit('Structure','agreement-stage',structure(),{'Model':'Observable simultaneous agreement stage before offers','Refusal':'No extra cost or disposition; resolve private exit commitments, then trial if neither exits','NoPostOfferVeto':True})
    if not a.structure_only:
        profiles,welfare=load_profiles(root);baseline,bw=load_profiles(a.baseline.resolve() if a.baseline else root/'Baseline')
        if welfare!=bw:raise ValueError('Welfare columns differ')
        costs=[.25,.5,1,2,4]
        expected={(cost,alpha,fee) for cost in costs for alpha in [0,2] for fee in FEES}
        def key(p): return (p['CostMultiplier'],p['Alpha'],p['FeeRule'])
        for name,data in [('enabled',profiles),('disabled',baseline)]:
            if len(data)!=30 or {key(p) for p in data}!=expected:
                raise ValueError(f'Expected exactly 30 distinct {name} cases')
        def identity(p):
            return {'OptionSet':p['OptionSet'],'CostMultiplier':p['CostMultiplier'],'Alpha':p['Alpha'],
                'FeeRule':p['FeeRule'],'Equilibrium':p['Equilibrium']}
        for p in profiles:
            risk='Risk neutral' if p['Alpha']==0 else 'Risk averse'
            folder=f'Profiles/Cost {p["CostMultiplier"]:g}/{risk}/{p["FeeRule"]}';stem='equilibrium'
            exhibit(folder,stem,individual(p),{'SourceProfile':fingerprint(p['_source']),**identity(p)})
            rows=[]
            for n in p['Strategies']:
                for action,prob in enumerate(n['Probabilities'],1):
                    rows.append({**{k:n[k] for k in ['Player','InformationSet','Decision','Signal','OwnExit','Reach']},
                        'Action':action,'SavedStrategyProbability':prob,'ReachedConditionalProbability':prob if n['Reach']>0 else None})
            csv_write(root/folder/'Sources'/f'{stem}-strategies.csv',rows)
        rows=[{'Agreement':'Enabled' if enabled else 'Disabled',**identity(p),**p['Metrics']}
            for enabled,data in [(False,baseline),(True,profiles)] for p in data]
        csv_write(root/'Comparisons'/'Sources'/'all-profile-metrics.csv',rows)
        bysignal=[{'Agreement':'Enabled' if enabled else 'Disabled',**identity(p),**s}
            for enabled,data in [(False,baseline),(True,profiles)] for p in data for s in p['BySignal']]
        csv_write(root/'Comparisons'/'Sources'/'filing-answering-by-signal.csv',bysignal)
        agreement_rows=[]
        for p in profiles:
            for n in p['Strategies']:
                if n['Decision'] not in ['PAgreeToBargain','DAgreeToBargain']:continue
                agreement_rows.append({**identity(p),'Party':'P' if n['Player']==0 else 'D','Signal':n['Signal'],
                    'OwnExitCommitment':'Exit' if n['OwnExit']==1 else 'Continue','InformationSetReach':n['Reach'],
                    'AgreementGivenReach':n['Probabilities'][0] if n['Reach']>0 else None,
                    'SavedAgreementPrescription':n['Probabilities'][0]})
        csv_write(root/'Comparisons'/'Sources'/'agreement-by-signal-and-commitment.csv',agreement_rows)
        for cost in costs:
          for alpha in [0,2]:
            risk='Risk neutral' if alpha==0 else 'Risk averse'
            prefix=f'cost-{cost:g}-'+risk.lower().replace(' ','-')
            context=f'{risk}; cost multiplier {cost:g}. '
            def select(data,fee):return [p for p in data if key(p)==(cost,alpha,fee)]
            agreement_table=[]
            for fee in FEES:
                selected=select(profiles,fee)
                agreement_table.append({'Fee':fee.replace(' Fee-Shifting',''),**{k:single_value([p['Metrics'][k] for p in selected]) for k in AGREEMENT}})
            exhibit('Tables',prefix+'-agreement',table(agreement_table,['Fee']+AGREEMENT,
                ['Fee rule','Both agree','Only P declines','Only D declines','Both decline'],context+
                'Joint probabilities conditional on reaching the agreement stage, integrated over signals and private commitments.'),agreement_table)
            for quantity,label in [('Filing','Filing'),('AnsweringGivenFiling','Answering conditional on filing')]:
                signal_table=[];columns=[f'{i}-{enabled}' for i in range(3) for enabled in [False,True]]
                for signal in range(1,11):
                    row={'Signal':signal}
                    for i,fee in enumerate(FEES):
                        for enabled,data in [(False,baseline),(True,profiles)]:
                            values=[s[quantity] for p in select(data,fee) for s in p['BySignal'] if s['Signal']==signal]
                            row[f'{i}-{enabled}']=single_value(values)
                    signal_table.append(row)
                exhibit('Tables',prefix+'-'+quantity.lower()+'-by-signal',table(signal_table,['Signal']+columns,
                    ['Signal','Am. off','Am. on','Trial off','Trial on','Complete off','Complete on'],context+label+
                    '. Off/on indicates the agreement stage. Undefined means that the conditioning event is unreached.'),signal_table)
            for group,metrics,titles in [('dispositions',['NoFiling','NoAnswer','Settlement','Abandonment','Default','PLoses','PWins','Trial','JointFileAnswer'],
                ['No filing','No answer','Settlement','Abandonment','Default','Trial: P loses','Trial: P wins','All trial','File and answer']),
                ('welfare',welfare,['Plaintiff shortfall','Nonliable D burden','Liable D burden','Gross outcome error','Real expenditures'])]:
                exhibit('Comparisons',prefix+'-'+group,comparison_plots(profiles,baseline,alpha,cost,metrics,titles),
                    {'Inputs':[fingerprint(p['_source']) for p in profiles+baseline if p['Alpha']==alpha and p['CostMultiplier']==cost],'Metrics':metrics})
            for subset,columns,labels in [('participation',METRICS[:3],['Filing','File + answer','Answer / file']),
                ('dispositions',METRICS[3:7],['Settlement','Abandon','Default','Trial']),
                ('trial-conditioning',METRICS[7:],['Trial / file','Trial / (file + answer)']),
                ('welfare',welfare,['P shortfall','Nonliable D','Liable D','Gross error','Real costs'])]:
                tab=[]
                for fee in FEES:
                    for enabled,data in [(False,baseline),(True,profiles)]:
                        selected=select(data,fee)
                        tab.append({'Fee':fee.replace(' Fee-Shifting',''),'Stage':'On' if enabled else 'Off',
                            **{k:single_value([p['Metrics'][k] for p in selected]) for k in columns}})
                exhibit('Tables',prefix+'-'+subset,table(tab,['Fee','Stage']+columns,['Fee rule','Agreement']+labels,
                    context+'One verified equilibrium per case. Undefined means the conditioning event has zero probability.'),tab)
        # Select a verified reached refusal path where one exists; otherwise label a counterfactual.
        selected=max(profiles,key=lambda p:(p['WorkedRefusalPath'] or {}).get('EquilibriumProbability',-1));worked=selected['WorkedRefusalPath']
        if worked:
            lines=[r'\textbf{'+esc(worked['Purpose'])+r'}\\[5pt]',esc(selected['FeeRule'])+f"; alpha {selected['Alpha']}; cost multiplier {selected['CostMultiplier']:g}\\\\[5pt]",
                f"History probability: {worked['EquilibriumProbability']:.8f}\\\\[8pt]"]
            for step in worked['Steps']:
                label=next(x['Label'] for x in step['Actions'] if x['Action']==step['SelectedAction'])
                lines.append(esc(step['Label'])+': '+esc(label)+r'\\')
            lines += [r'\medskip\textbf{Existing terminal outcomes}\\']
            for o in worked['Outcomes']:
                lines.append(esc(o['Outcome'])+f": probability {o['ConditionalProbability']:.4f}; P monetary payoff {o['PlaintiffNetMonetaryPayoff']:.4f}; D monetary payoff {o['DefendantNetMonetaryPayoff']:.4f}"+r'\\')
            exhibit('Worked path','refusal-path',wrapper(r'\begin{minipage}{15cm}'+ '\n'.join(lines)+r'\end{minipage}'),{'SourceProfile':fingerprint(selected['_source']),'Path':worked})
        dump(root/'Sources'/'exhibit-data-index.json',{'Profiles':[fingerprint(p['_source']) for p in profiles],'BaselineProfiles':[fingerprint(p['_source']) for p in baseline],'WelfareColumns':welfare})
    if not a.sources_only:
        with concurrent.futures.ThreadPoolExecutor(max_workers=a.jobs) as pool:
            for result in pool.map(compile_one,exhibits): print(result,flush=True)
    dump(root/'exhibit-inventory.json',{'Compiled':not a.sources_only,'Exhibits':[{'Source':fingerprint(p),'Data':fingerprint(p.with_suffix('.json')),**({ext:fingerprint(p.parent.parent/f'{p.stem}.{ext}') for ext in ['pdf','png']} if not a.sources_only else {})} for p in exhibits]})

if __name__=='__main__':main()
