"""Scientific figures and responsive three-view report from immutable report data."""
import argparse
from collections import defaultdict
import gzip
import html
import json
import math
from pathlib import Path
import shutil
import statistics
import textwrap

import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.backends.backend_pdf import PdfPages
import numpy as np

from endogenous_story_report import MEASURES, METHODS, method_values, quantile
from endogenous_report_language import OUTCOMES, outcome_name, parameter_name, setting_name
from endogenous_report_pages import render_pages

LABELS={'published':'Original draft','gini':'Gini','liu':'Liu-affinity'}
COLORS={'published':'#30383b','gini':'#007f86','liu':'#c46825'}
CORE=['Fee Shifting Multiplier','Damages Multiplier','Liability Threshold']
SLUGS={CORE[0]:'fee-shifting',CORE[1]:'damages',CORE[2]:'liability-threshold'}
BASE={'Adjudication Mode':'Baseline','Cost Misestimation':'1','Costs Multiplier':'1','Court Noise':'Baseline',
    'Damages Multiplier':'1','Fee Shifting Multiplier':'0','Issue':'Liability','Liability Threshold':'1',
    'Noise Multiplier D':'1','Noise Multiplier P':'1','Num Signals and Offers':'Baseline','Proportion of Costs at Beginning':'0.5',
    'Relative Costs':'1','Risk Aversion':'Risk Neutral','Unit Precaution Cost':'1E-05','Wrongful Attribution Probability':'Normal'}


def esc(value):return html.escape(str(value))
def fmt(value,kind='number'):
    if value is None:return '—'
    if kind=='pct':return f'{100*value:.1f}%'
    if kind=='pp':return f'{100*value:.2f} pp'
    if value==0:return '0'
    if abs(value)<.001 or abs(value)>=10000:return f'{value:.3g}'
    return f'{value:.3f}'.rstrip('0').rstrip('.')


def slice_cases(data,parameter,cost,risk):
    settings=dict(BASE,**{'Costs Multiplier':str(cost),'Risk Aversion':risk})
    selected=[c for c in data['Cases'] if all(c['Parameters'][k]==v for k,v in settings.items() if k!=parameter)]
    levels=sorted({c['Parameters'][parameter] for c in selected},key=float)
    mapping={c['Parameters'][parameter]:c for c in selected}
    if len(mapping)!=len(selected):raise ValueError('Ambiguous baseline slice')
    return levels,mapping


def finish_figure(fig,title,path,pdf):
    fig.suptitle(title,fontsize=14,y=.995)
    fig.text(.01,.006,'Current calculations, not final conclusions. Some risk-averse results need tighter economic accuracy. See the reading guide; ranges are not confidence intervals.',fontsize=7,color='#4a5154')
    fig.tight_layout(rect=(0,.025,1,.965))
    fig.savefig(path.with_suffix('.png'),dpi=160,bbox_inches='tight')
    fig.savefig(path.with_suffix('.svg'),bbox_inches='tight')
    pdf.savefig(fig,bbox_inches='tight');plt.close(fig)


def atlas_guide(pdf):
    pages=[('Start here: what is being studied?',[
        ('The question','How do litigation rules affect useful activity, care, accidents, lawsuits and total social cost? The draft argues that adjustments in behavior often offset one another: a rule can change who sues or how much care people take without greatly changing the total cost.'),
        ('The model in five steps','A potential injurer decides whether to undertake an activity and how much care to take. An accident, or an injury wrongly attributed to that person, may create a dispute. The potential victim decides whether to sue; the defendant decides whether to contest. They make simultaneous settlement offers. If offers do not meet, they can exit; otherwise the case can proceed to trial.'),
        ('The three legal changes','Fee reimbursement changes how much of the winner’s legal costs the loser must pay: 0 means none, 1 means full reimbursement, and 2 means double. The damages multiplier scales awards relative to the model’s one-unit injury. The liability standard is zero for strict liability; around the negligence benchmark of 1, larger values make liability harder to establish.'),
        ('What counts as a cost?','Total social cost adds care, benefits lost through not undertaking the activity, physical accident losses and resources used in litigation. Damages, settlements and fee reimbursements are transfers between parties, not additional resource losses. Their incentive effects can still alter the total.'),
        ('What the scenarios represent','There are 2,268 combinations of assumptions. They are model scenarios, not data from 2,268 real lawsuits. Litigation expense is commonly shown at one-quarter, baseline and four-times baseline levels. The cost measure counts resources; it does not separately value avoiding financial risk.')]),
        ('How to read the three versions and the charts',[
        ('Original draft, Gini and Liu','The original draft contributes its one published result per setting. The two new approaches search for approximately stable behavior. Gini seeks to spread probability across pairs of complete plans. Liu-affinity uses similarity in economic consequences to construct reference weights, then selects a stable mixture close to them. Neither approach directly minimizes social cost.'),
        ('Why ten searches?','Each approach starts ten times from different initializations. The final calculation pools the plans found and chooses fresh probabilities. It is not an average of the ten outcomes. A range describes only those ten observed results, not all possible equilibria or statistical confidence about real litigation.'),
        ('The equilibrium distinction','The new methods permit correlation between complete plans and test whether either side would gain by committing in advance to another plan. This is a coarse correlated equilibrium, or CCE. Nash uses independent choices. Removing correlation and testing Nash is a separate exercise; the figures show the correlated outcomes.'),
        ('Units and denominators','Costs are shown per 10,000 opportunities to undertake the activity. One physical accident’s harm equals one model unit; values are not dollars. Care is on a 0–7 scale. Litigation events are shown either per 100,000 opportunities or as a percentage of disputes. Four settlements among ten disputes out of 100,000 opportunities means 4 per 100,000 opportunities and 40% of disputes.'),
        ('Colors and comparison','Grey is the original draft, teal is Gini and orange is Liu. Lower total cost is better under the resource measure. More care or more settlement is not automatically better. For comparison summaries, the median is the middle change; the 90th percentile is a larger-change benchmark, not a confidence level.')]),
        ('Read before interpreting the risk-averse results',[
        ('A small ratio can hide a large incentive','The calculation divides a party’s available payoff improvement by the full utility range. Under risk aversion, costly possible outcomes can make that range enormous. A very small resulting ratio does not always mean a small economic gain from changing behavior.'),
        ('What the new review found','In nine high-cost, moderately risk-averse settings with double fee reimbursement and double damages, the 18 Gini/Liu defendant improvements are worth about 35–123% of the activity benefit. This converts utility improvements into changes in a guaranteed amount of money with the same utility effect: certainty-equivalent gains.'),
        ('What follows—and what does not','The deviations need not concern participation alone. We have not proved activity suppression is an artifact, but those participation rates are not yet established equilibrium predictions. Agreement across methods or starts does not remove a shared scaling limitation. Some risk, welfare and variability conclusions need reassessment.'),
        ('The limitation is uneven','The reviewed risk-neutral pooled defendant incentive gains are below 0.1% of the activity benefit. The money-equivalent gain is not a bound on error in social cost. Initial searches need their own economic-accuracy review. Cost-misperception variants measure incentives using perceived wealth.'),
        ('Records and further checks','Original data and formal classifications remain unchanged. One Liu output misses the original normalized tolerance of 0.0000001 but passes 0.000001; it remains flagged. Accounting reconciles across all 4,536 outputs. Full final certification and exact optimization of the selection objectives remain separate questions. Definitions and evidence are in the report’s How to read & downloads page.')])]
    for title,paragraphs in pages:
        fig=plt.figure(figsize=(8.27,11.69),facecolor='white')
        fig.text(.09,.93,title,fontsize=19,weight='bold',color='#17484f')
        y=.865
        for heading,body in paragraphs:
            fig.text(.09,y,heading,fontsize=11.3,weight='bold');y-=.024
            lines=textwrap.wrap(body,88)
            fig.text(.09,y,'\n'.join(lines),fontsize=10.2,va='top',linespacing=1.5)
            y-=len(lines)*.019+.032
        if y<.04:raise ValueError('Atlas guide overflows page')
        fig.text(.09,.035,'Endogenous litigation | Reading guide | Provisional calculations',fontsize=8,color='#52656a')
        pdf.savefig(fig);plt.close(fig)


def figures(data,summary,out):
    target=out/'figures';target.mkdir(exist_ok=True)
    plt.rcParams.update({'font.family':'DejaVu Sans','font.size':9,'axes.spines.top':False,'axes.spines.right':False,
        'axes.grid':True,'grid.alpha':.18,'axes.axisbelow':True,'svg.fonttype':'none'})
    inventory=[]
    def keep(fig,title,name,pdf):
        finish_figure(fig,title,target/name,pdf);inventory.append({'Name':name,'Title':title})
    with PdfPages(out/'scientific-figure-atlas.pdf') as pdf:
        atlas_guide(pdf)
        for parameter in CORE:
            slug=SLUGS[parameter]
            for risk,risk_slug in [('Risk Neutral','rn'),('Moderately Risk Averse','mra')]:
                fig,axes=plt.subplots(2,3,figsize=(12,7),squeeze=False)
                for col,cost in enumerate((.25,1,4)):
                    levels,cases=slice_cases(data,parameter,cost,risk);x=np.array(list(map(float,levels)))
                    for row,metric in enumerate(('SocialWelfareLoss','PrecautionLevel')):
                        ax=axes[row,col]
                        for method in METHODS:
                            y=[method_values(cases[level],method)[metric] for level in levels]
                            y=np.array([np.nan if v is None else v for v in y])*(1e4 if row==0 else 1)
                            ax.plot(x,y,label=LABELS[method],color=COLORS[method],marker='o',ms=3,lw=1.5)
                        ax.set_xticks(x,levels);ax.set_xlabel(parameter_name(parameter));ax.set_title({.25:'Quarter litigation costs',1:'Baseline litigation costs',4:'Four-times litigation costs'}[cost])
                        ax.set_ylabel('Cost per 10,000 opportunities' if row==0 else 'Average care (0–7 scale)')
                        if row==1:ax.set_ylim(0,7.3)
                axes[0,0].legend(fontsize=7)
                keep(fig,f'{parameter_name(parameter)}: total cost and care — {risk}',f'{slug}-{risk_slug}-comparison',pdf)
                for conditional,denom in [(False,'unconditional'),(True,'given-dispute')]:
                    metrics=['Filing','AnsweringUnconditional','Trial','Settlement'] if not conditional else ['FilingGivenDispute','AnsweringGivenDispute','TrialGivenDispute','SettlementGivenDispute']
                    fig,axes=plt.subplots(4,3,figsize=(12,11),squeeze=False)
                    for col,cost in enumerate((.25,1,4)):
                        levels,cases=slice_cases(data,parameter,cost,risk);x=np.array(list(map(float,levels)))
                        for row,metric in enumerate(metrics):
                            ax=axes[row,col]
                            for method in METHODS:
                                y=[method_values(cases[level],method)[metric] for level in levels]
                                ax.plot(x,[np.nan if v is None else v*(100 if conditional else 1e5) for v in y],color=COLORS[method],marker='o',ms=2.5,lw=1.3,label=LABELS[method])
                            ax.set_xticks(x,levels);ax.set_ylabel(['Filed','Contested','Trial','Settled'][row]+(' (% of disputes)' if conditional else ' per 100,000 opportunities'))
                            if conditional:ax.set_ylim(-2,102)
                            if row==0:ax.set_title({.25:'Quarter litigation costs',1:'Baseline litigation costs',4:'Four-times litigation costs'}[cost])
                            if row==3:ax.set_xlabel(parameter_name(parameter))
                    axes[0,0].legend(fontsize=6)
                    keep(fig,f'{parameter_name(parameter)}: '+('among disputes' if conditional else 'among all opportunities')+f' — {risk}',f'{slug}-{risk_slug}-{denom}',pdf)
            for method in METHODS:
                fig,axes=plt.subplots(3,2,figsize=(11,9),squeeze=False)
                for row,cost in enumerate((.25,1,4)):
                    maximum=0
                    for risk in ('Risk Neutral','Moderately Risk Averse'):
                        levels,cases=slice_cases(data,parameter,cost,risk)
                        maximum=max([maximum]+[method_values(c,other)['SocialWelfareLoss']*1e4 for c in cases.values() for other in METHODS])
                    for col,risk in enumerate(('Risk Neutral','Moderately Risk Averse')):
                        levels,cases=slice_cases(data,parameter,cost,risk);x=np.arange(len(levels));bottom=np.zeros(len(x));ax=axes[row,col]
                        for metric,label,color in [('OpportunityCost','Care + forgone activity','#476e82'),('HarmCost','Harm','#b55459'),('LitigationExpenditures','Litigation','#a58b43')]:
                            y=np.array([method_values(cases[level],method)[metric]*1e4 for level in levels])
                            ax.bar(x,y,bottom=bottom,label=label,color=color,width=.7);bottom+=y
                        totals=[method_values(cases[level],method)['SocialWelfareLoss']*1e4 for level in levels]
                        ax.plot(x,totals,color='#1c2428',marker='_',ls='none',label='Total social cost')
                        ax.set_xticks(x,levels);ax.set_ylim(0,maximum*1.12);ax.set_title(f'{risk}; cost {cost:g}')
                        ax.set_xlabel(parameter_name(parameter));ax.set_ylabel('Cost per 10,000 opportunities')
                handles,labels=axes[0,0].get_legend_handles_labels()
                fig.legend(handles,labels,loc='upper center',bbox_to_anchor=(.5,.965),ncol=4,fontsize=7,frameon=False)
                keep(fig,f'{LABELS[method]}: what makes up total cost under {parameter_name(parameter).lower()}',f'{method}-{slug}-components',pdf)
        fig,axes=plt.subplots(2,2,figsize=(11,8))
        for ax,metric in zip(axes.ravel(),['SocialWelfareLoss','PrecautionLevel','TrialGivenDispute','SettlementGivenDispute']):
            for method in ('gini','liu'):
                vals=[c['Selectors'][method]['InitializationRanges'][metric] for c in data['Cases']]
                values=sorted((r['RelativeWidth']*100 if metric=='SocialWelfareLoss' else r['Width']*(100 if metric.endswith('GivenDispute') else 1)) for r in vals if (r['RelativeWidth'] if metric=='SocialWelfareLoss' else r['Width']) is not None)
                ax.plot(values,np.arange(1,len(values)+1)/len(values),color=COLORS[method],label=LABELS[method])
            ax.set_title(outcome_name(metric));ax.set_ylabel('Share of scenarios')
            ax.set_xlabel('Range across ten searches (% of middle result)' if metric=='SocialWelfareLoss' else 'Range across ten searches (percentage points)' if metric.endswith('GivenDispute') else 'Range across ten searches (care levels)')
            ax.set_xlim(left=0);ax.set_ylim(0,1);ax.legend(fontsize=7)
        keep(fig,'How much do the ten searches disagree?', 'initialization-variability',pdf)
        selected=[r for r in summary['MatchedSummary'] if r['Metric']=='SocialWelfareLoss' and r['Method']!='published']
        parameters=list(dict.fromkeys(r['Parameter'] for r in selected));matrix=np.array([[next(r['FractionObservedRangeIdentifiesSign'] for r in selected if r['Parameter']==p and r['Method']==s) for s in ('gini','liu')] for p in parameters])
        fig,ax=plt.subplots(figsize=(8,9));im=ax.imshow(matrix,vmin=0,vmax=1,cmap='YlGnBu',aspect='auto')
        ax.set_xticks([0,1],['Gini','Liu-affinity']);ax.set_yticks(range(len(parameters)),[parameter_name(p) for p in parameters])
        for i in range(len(parameters)):
            for j in range(2):ax.text(j,i,f'{matrix[i,j]:.0%}',ha='center',va='center',color='white' if matrix[i,j]>.55 else '#202b30')
        fig.colorbar(im,ax=ax,label='Share of comparisons with separated observed ranges')
        keep(fig,'When do all observed results agree on which rule costs less?','policy-interpretability',pdf)
        rows=[r for r in summary['MatchedSummary'] if r['Metric']=='SocialWelfareLoss']
        parameters=list(dict.fromkeys(r['Parameter'] for r in rows))
        fig,ax=plt.subplots(figsize=(11,9))
        for offset,method in zip((-.23,0,.23),METHODS):
            selected=[next(r for r in rows if r['Parameter']==p and r['Method']==method) for p in parameters]
            y=np.arange(len(parameters))+offset
            med=np.array([r['MedianAbsoluteRelativeChange']*100 for r in selected]);tail=np.array([r['P90AbsoluteRelativeChange']*100 for r in selected])
            ax.barh(y,med,height=.19,color=COLORS[method],label=LABELS[method],alpha=.8)
            for yi,m,t in zip(y,med,tail):ax.plot([m,t],[yi,yi],color=COLORS[method],lw=1);ax.plot(t,yi,'o',color=COLORS[method],ms=3)
        ax.set_yticks(range(len(parameters)),[parameter_name(p) for p in parameters]);ax.invert_yaxis()
        ax.set_xlabel('Size of cost change (%): bar = middle; dot = larger-change benchmark (90th percentile)')
        ax.legend(loc='lower right',fontsize=8)
        keep(fig,'How much do changes in rules and assumptions move total cost?','robustness-effects',pdf)
    return inventory


STYLE='''*{box-sizing:border-box}html{scroll-behavior:smooth}body{margin:0;background:#faf9f5;color:#263238;font:16px/1.65 system-ui,-apple-system,Segoe UI,sans-serif}a{color:#006f77}header,main,footer{max-width:1120px;margin:auto;padding:25px}header{padding-top:45px}h1,h2,h3{font-family:Georgia,serif;line-height:1.18;color:#19292d}h1{font-size:clamp(2rem,5vw,3.6rem);max-width:950px;margin:.4em 0}h2{font-size:2rem;margin-top:2em}h3{font-size:1.4rem}.eyebrow{font-size:.8rem;letter-spacing:.1em;text-transform:uppercase;color:#677777}.lede{font-size:1.16rem;max-width:850px}.status{border-left:4px solid #b37c20;background:#fff3d9;padding:15px 18px;font-size:.9rem}.nav{display:flex;flex-wrap:wrap;gap:9px;margin-top:25px}.nav a{border:1px solid #cdd6d5;border-radius:24px;padding:7px 13px;text-decoration:none;font-size:.88rem}.nav a[aria-current]{background:#073f47;color:white;border-color:#073f47}section{padding:8px 0 25px;border-bottom:1px solid #d9dfdc}.table-wrap{overflow:auto;border:1px solid #dbe1df;border-radius:5px;background:white}table{border-collapse:collapse;width:100%;font-size:.86rem}th,td{padding:11px 13px;border-bottom:1px solid #e3e7e4;text-align:right;vertical-align:top}th{background:#edf2ee;font-weight:600}th:first-child,td:first-child{text-align:left}tr:last-child td{border-bottom:0}.note{font-size:.87rem;color:#53686d}figure{margin:25px 0;background:white;border:1px solid #e1e4df;padding:12px}figure img{width:100%;height:auto}figcaption{padding:8px 10px;font-size:.87rem;color:#506068}button,.button{display:inline-block;background:#07565f;color:white;border:0;border-radius:5px;padding:10px 14px;font:inherit;font-size:.88rem;cursor:pointer;text-decoration:none;margin:5px 7px 5px 0}label{display:inline-flex;flex-direction:column;font-size:.85rem;margin-right:14px;gap:5px}select,input{font:inherit;max-width:100%;padding:8px;border:1px solid #aebeba;border-radius:4px;background:white}.finding{padding:10px 0 15px;max-width:950px}.finding strong{color:#07545b}.kpis{display:flex;gap:35px;flex-wrap:wrap;margin:25px 0}.kpis div{min-width:170px}.kpis b{font:2rem Georgia,serif;display:block}.code{font-family:monospace;overflow-wrap:anywhere}.two{display:grid;grid-template-columns:1fr 1fr;gap:20px}.pill{font-size:.75rem;border:1px solid #d9b877;background:#fff5df;padding:2px 7px;border-radius:5px}.legend{display:flex;gap:18px;flex-wrap:wrap}.swatch{display:inline-block;width:10px;height:10px;margin-right:7px;border-radius:50%}footer{font-size:.82rem;color:#6c7779;border-top:1px solid #d9dfdc;margin-top:35px}@media(max-width:700px){header,main,footer{padding:18px}.two{grid-template-columns:1fr}h2{font-size:1.7rem}table{font-size:.78rem}th,td{padding:9px}.kpis{gap:20px}.nav{gap:6px}.nav a{padding:6px 10px;font-size:.8rem}}'''


STYLE += '\n#explorer td:first-child,#explorer th:first-child{min-width:190px;max-width:240px;position:sticky;left:0;background:#fff;z-index:1}#explorer th:first-child{background:#edf2ee}\n'


STYLE += '''\n.three{display:grid;grid-template-columns:repeat(3,1fr);gap:22px}.three article{background:#eef3ef;padding:18px;border-radius:6px}.three h3{margin-top:0}.steps{padding-left:25px;max-width:900px}.steps li{padding:8px 0}.read-next{background:#edf4f1;padding:15px;border-radius:6px}p{max-width:950px}details{margin:20px 0;padding:14px 18px;background:#f0f3f0;border:1px solid #d6dfd9;border-radius:5px}summary{cursor:pointer;font-weight:650;color:#07545b}details[open] summary{margin-bottom:15px}.skip{position:absolute;left:-10000px}.skip:focus{position:static;display:block;padding:12px}#explorer td:first-child{font-size:.78rem} @media(max-width:750px){.three{grid-template-columns:1fr}.three article{padding:15px}label{display:flex;margin:10px 0}h1{overflow-wrap:anywhere}}\n'''


def table(headers,rows):
    return '<div class="table-wrap"><table><thead><tr>'+''.join('<th>'+esc(h)+'</th>' for h in headers)+'</tr></thead><tbody>'+''.join('<tr>'+''.join('<td>'+str(v)+'</td>' for v in row)+'</tr>' for row in rows)+'</tbody></table></div>'


def image(name,caption):return f'<figure><a href="figures/{name}.svg"><img loading="lazy" src="figures/{name}.png" alt="{esc(caption)}"></a><figcaption>{esc(caption)} · <a href="figures/{name}.svg">Open scalable chart</a></figcaption></figure>'


def page(title,body,active,economic_review=None):
    navigation=[('index.html','Start here'),('published.html','Original draft'),('gini.html','Gini approach'),('liu.html','Liu approach'),('variability.html','Consistency'),('methods.html','How to read & downloads')]
    nav=''.join(f'<a href="{url}"'+(' aria-current="page"' if url==active else '')+'>'+label+'</a>' for url,label in navigation)
    notice='<strong>Results are provisional.</strong> Some calculations involving aversion to financial risk still allow economically meaningful improvements in behavior. Those results need greater accuracy before we draw firm conclusions. <a href="methods.html#economic-accuracy">What this means</a>.' if economic_review else '<strong>Results are provisional.</strong> Read the methods and limitations before interpreting the figures.'
    return f'<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>{esc(title)} | Litigation rules and behavior</title><meta name="description" content="A plain-English guide to how litigation rules affect care, accidents, lawsuits and social costs, comparing the original draft with Gini and Liu calculations."><link rel="stylesheet" href="report.css"></head><body><a class="skip" href="#main">Skip to the explanation</a><header><div class="eyebrow">Litigation rules, behavior and social costs</div><h1>{esc(title)}</h1><div class="status">{notice}</div><nav class="nav" aria-label="Report pages">{nav}</nav></header><main id="main">{body}</main><footer>These are model calculations, not estimates from observed lawsuits. The original published values are kept separate from later replay calculations. Ranges describe ten numerical searches; they are not confidence intervals. <a href="methods.html#glossary">Definitions</a> · Private research report.</footer><script src="report.js" defer></script></body></html>'


SCRIPT='function csv(rows){if(!rows.length)return \'\';const cols=Object.keys(rows[0]);const q=x=>x==null?\'\':(\'"\'+String(x).replaceAll(\'"\',\'""\')+\'"\');return cols.map(q).join(\',\')+\'\\r\\n\'+rows.map(r=>cols.map(c=>q(r[c])).join(\',\')).join(\'\\r\\n\')}\nfunction download(name,content,type){const a=document.createElement(\'a\');a.href=URL.createObjectURL(new Blob([content],{type}));a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(a.href),5000)}\ndocument.querySelectorAll(\'[data-export]\').forEach(b=>b.onclick=async()=>{const data=await(await fetch(\'tables.json\')).json();download(b.dataset.export+\'.csv\',csv(data[b.dataset.export]),\'text/csv;charset=utf-8\')});\nasync function explorer(){const node=document.querySelector(\'#explorer\');if(!node)return;const rows=await(await fetch(\'explorer.json\')).json();const metric=document.querySelector(\'#metric\'),search=document.querySelector(\'#search\'),sort=document.querySelector(\'#sort\');\nconst probability=m=>![\'SocialWelfareLoss\',\'OpportunityCost\',\'HarmCost\',\'LitigationExpenditures\',\'PrecautionLevel\'].includes(m);\nfunction format(x,m){if(x==null)return \'—\';if(probability(m))return (100*x).toLocaleString(undefined,{maximumFractionDigits:6})+\'%\';if(x===0)return \'0\';return Math.abs(x)<1e-9?x.toExponential(3):x.toLocaleString(undefined,{maximumFractionDigits:10})}\nfunction draw(){const m=metric.value,s=search.value.toLowerCase();let a=rows.filter(r=>(r.Name+\' \'+r.OriginalName+\' \'+r.Family).toLowerCase().includes(s));const width=r=>Math.max(r.gini.Ranges[m].Width??0,r.liu.Ranges[m].Width??0);if(sort.value===\'width\')a.sort((x,y)=>width(y)-width(x));else a.sort((x,y)=>x.Name.localeCompare(y.Name));document.querySelector(\'#count\').textContent=a.length+\' matching scenarios; showing first \'+Math.min(80,a.length)+\'.\';document.querySelector(\'#metric-help\').textContent=probability(m)?\'Rates are percentages. Among-dispute rates use all disputes, including those never filed.\':m===\'PrecautionLevel\'?\'Average care on the 0–7 scale, including zero for nonparticipation.\':\'Costs are model units per opportunity, not dollars. One physical accident causes one unit of harm.\';const body=document.querySelector(\'#explorer tbody\');body.replaceChildren();for(const r of a.slice(0,80)){const tr=document.createElement(\'tr\');const values=[r.Name+(r.Candidate?\' [Liu misses the original numerical target]\':\'\'),format(r.published[m],m),format(r.gini.Pooled[m],m),format(r.liu.Pooled[m],m),format(r.gini.Ranges[m].Minimum,m)+\' – \'+format(r.gini.Ranges[m].Maximum,m),format(r.liu.Ranges[m].Minimum,m)+\' – \'+format(r.liu.Ranges[m].Maximum,m)];for(const value of values){const td=document.createElement(\'td\');td.textContent=value;tr.append(td)}body.append(tr)}}metric.onchange=search.oninput=sort.onchange=draw;draw()}explorer();'


def html_reports(data,summary,out,inventory,findings,economic_review=None):
    (out/'report.css').write_text(STYLE,encoding='utf-8')
    (out/'report.js').write_text(SCRIPT,encoding='utf-8')
    render_pages(data,summary,out,findings,economic_review,(esc,fmt,table,image,page,CORE,SLUGS,LABELS))
    explorer=[];case_rows=[];numerical=[]
    for c in data['Cases']:
        explorer.append({'Id':c['Id'],'Name':setting_name(c,BASE),'OriginalName':c['OptionSetName'],'Family':c['Family'],'Candidate':c['ProvisionalCandidate'],
            'published':c['Published'],**{s:{'Pooled':c['Selectors'][s]['Pooled'],'Ranges':c['Selectors'][s]['InitializationRanges']} for s in ('gini','liu')}})
        for metric in MEASURES:
            case_rows.append({'CaseId':c['Id'],'Setting':c['OptionSetName'],'Outcome':metric,'OutcomeLabel':outcome_name(metric),'Units':OUTCOMES[metric][2],'Published':c['Published'][metric],
                'SavedPolicyReplay':c['SavedPolicyReplay'][metric],'GiniPooled':c['Selectors']['gini']['Pooled'][metric],
                'LiuPooled':c['Selectors']['liu']['Pooled'][metric],
                **{s.title()+k:c['Selectors'][s]['InitializationRanges'][metric][k] for s in ('gini','liu') for k in ('Minimum','Median','Maximum','Available')},
                'CandidateCase':c['ProvisionalCandidate'],**c['Parameters']})
        for s in ('gini','liu'):
            r=c['Selectors'][s];numerical.append({'CaseId':c['Id'],'Setting':c['OptionSetName'],'Selector':s,'MaxRawCceGain':max(r['RawCceGains']),
                'MaxNormalizedCceGain':max(r['NormalizedCceGains']),'CceAt1eMinus7':r['CceAt1eMinus7'],'CceAt1eMinus6':r['CceAt1eMinus6'],
                'MaxProductNashGain':max(r['NormalizedProductNashGains']),'NashAt1eMinus7':r['ProductNashAt1eMinus7'],'NashAt1eMinus5':r['ProductNashAt1eMinus5'],
                'SourceStatus':r['Status'],'ResultSha256':r['ResultSha256']})
    explorer_data=json.dumps(explorer,separators=(',',':'),allow_nan=False).encode()
    (out/'explorer.json.gz').write_bytes(gzip.compress(explorer_data,compresslevel=6,mtime=0))
    table_data=json.dumps({'case-outcomes':case_rows,'matched-summary':[dict(r,ParameterLabel=parameter_name(r['Parameter']),OutcomeLabel=outcome_name(r['Metric'])) for r in summary['MatchedSummary']],'numerical-checks':numerical,'outcome-guide':[{'Code':k,'Name':v[0],'Meaning':v[1],'Units':v[2]} for k,v in OUTCOMES.items()]},separators=(',',':'),allow_nan=False).encode()
    (out/'tables.json.gz').write_bytes(gzip.compress(table_data,compresslevel=6,mtime=0))
    script=(out/'report.js').read_text(encoding='utf-8')
    script=script.replace("const data=await(await fetch('tables.json')).json();", "const response=await fetch('tables.json.gz');const data=await new Response(response.body.pipeThrough(new DecompressionStream('gzip'))).json();")
    script=script.replace("const rows=await(await fetch('explorer.json')).json();", "const response=await fetch('explorer.json.gz');const rows=await new Response(response.body.pipeThrough(new DecompressionStream('gzip'))).json();")
    (out/'report.js').write_text(script,encoding='utf-8')


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--data',required=True);parser.add_argument('--statistics',required=True)
    parser.add_argument('--output',required=True);parser.add_argument('--findings',required=True);parser.add_argument('--html-only',action='store_true');parser.add_argument('--economic-review')
    args=parser.parse_args();data=json.loads(Path(args.data).read_text(encoding='utf-8'));summary=json.loads(Path(args.statistics).read_text(encoding='utf-8'));out=Path(args.output);out.mkdir(exist_ok=True,parents=True)
    if args.html_only:inventory=json.loads((out/'figure-inventory.json').read_text())
    else:
        inventory=figures(data,summary,out);(out/'figure-inventory.json').write_text(json.dumps(inventory,indent=2))
    economic_review=json.loads(Path(args.economic_review).read_text(encoding='utf-8')) if args.economic_review else None
    if economic_review:
        assert economic_review['Cases']==len(data['Cases']) and not economic_review['Errors']
        (out/'economic-accuracy-review.json.gz').write_bytes(gzip.compress(Path(args.economic_review).read_bytes(),compresslevel=6,mtime=0))
    html_reports(data,summary,out,inventory,json.loads(Path(args.findings).read_text(encoding='utf-8')),economic_review)
    for source,name in [(args.data,'report-data.json.gz'),(args.statistics,'comparison-statistics.json.gz')]:
        (out/name).write_bytes(gzip.compress(Path(source).read_bytes(),compresslevel=6,mtime=0))
    print(json.dumps({'Pages':6,'Figures':len(inventory),'Output':str(out.resolve())}))


if __name__=='__main__':main()
