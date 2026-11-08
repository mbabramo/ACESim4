"""Scientific figures and responsive three-view report from immutable report data."""
import argparse
import csv
from collections import defaultdict
import gzip
import hashlib
import html
import io
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
from endogenous_paired_comparisons import (build_pairwise, export_rows, seed_provenance,
    scoped_transition_summaries, transition_export_rows)
from endogenous_report_language import PARAMETERS
from endogenous_initial_economic_summary import load_review
from endogenous_magnitude import build_distributions, distribution_export_rows

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


def atlas_guide(pdf,initial_review=None):
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
        ('Colors and comparison','Grey is the original draft, teal is Gini and orange is Liu. Lower total cost is better under the resource measure. More care or more settlement is not automatically better. The paired-agreement charts show how many of ten same-seed comparisons give lower costs under value B. Each row names A and B, and each panel fixes the other assumptions.')]),
        ('Read before interpreting the risk-averse results',[
        ('A small ratio can hide a large incentive','The calculation divides a party’s available payoff improvement by the full utility range. Under risk aversion, costly possible outcomes can make that range enormous. A very small resulting ratio does not always mean a small economic gain from changing behavior.'),
        ('What the new review found','In nine high-cost, moderately risk-averse settings with double fee reimbursement and double damages, the 18 Gini/Liu defendant improvements are worth about 35–123% of the activity benefit. This converts utility improvements into changes in a guaranteed amount of money with the same utility effect: certainty-equivalent gains.'),
        ('What follows—and what does not','The deviations need not concern participation alone. We have not proved activity suppression is an artifact, but those participation rates are not yet established equilibrium predictions. Agreement across methods or starts does not remove a shared scaling limitation. Some risk, welfare and variability conclusions need reassessment.'),
        ('The limitation is uneven','The reviewed risk-neutral pooled defendant incentive gains are below 0.1% of the activity benefit. The money-equivalent gain is not a bound on error in social cost. Initial-search accuracy must also be assessed in economic units. Cost-misperception variants measure incentives using perceived wealth.'),
        ('Records and further checks','Original data and formal classifications remain unchanged. One Liu output misses the original normalized tolerance of 0.0000001 but passes 0.000001; it remains flagged. Accounting reconciles across all 4,536 outputs. Full final certification and exact optimization of the selection objectives remain separate questions. Definitions and evidence are in the report’s How to read & downloads page.')])]
    pages.append(('Specific comparisons and paired searches',[
        ('What each comparison holds fixed','Each row in a paired-agreement chart compares two values of one rule. All other recorded model parameters are identical within that pair. The three panels separately set litigation expense to one-quarter, baseline and four-times costs. They do not average different environments.'),
        ('Default assumptions for these charts','Both parties are risk-neutral. Unless it is the rule being varied: fee reimbursement is 0, damages multiplier is 1, and liability standard is 1. Adjudication mode, court noise and the number of signals/offers use their baseline settings. Both private-signal noise multipliers are 1. Cost misestimation is 1, relative costs are 1, half the costs occur at the beginning, unit care cost is 0.00001, wrongful attribution is Normal, and the issue is liability.'),
        ('Every available pair, without reverse duplicates','Five available rule values give ten comparisons. A lower number is placed first for consistent display, not because it is preferable. Only observed settings are compared. The online explorer offers every represented fixed background and lists all assumptions explicitly.'),
        ('What ten paired searches means','Repetition 1 under A is compared with repetition 1 under B, and likewise through repetition 10. The same original seeds identify these pairs; accepted repairs retain their original position. Seven lower-cost outcomes means 7 of 10 paired searches, not a 70% real-world probability. Tiny stored differences may be numerical error. Exact ties are shown separately online.'),
        ('Size as well as direction','The online detail table shows all ten before/after costs and differences, the mean and median difference, and separate means across ten searches under each setting. It also shows the original draft and final pooled outputs. Nonoverlapping ranges remain a stronger supplementary check. Numerical accuracy qualifications apply throughout.')]))
    pages.append(('Averages for one specified rule change',[
        ('Start with one transition','Fix the starting and ending rule values, such as no fee reimbursement to full reimbursement. Select litigation expense and, optionally, attitudes to financial risk. Within each background, every other recorded parameter must agree exactly between the two settings.'),
        ('Average the ten-search percentages','For each matched background, count the ten same-seed searches in which the second value B has lower social cost. Divide by ten, then average across the available backgrounds, giving each equal weight. For example, 8/10, 6/10 and 10/10 produce an average of 80%. The online table reports how many backgrounds contribute to each transition.'),
        ('Direction and agreement answer different questions','B cheaper is the average share favoring B. Agreement within backgrounds takes the larger of the A-cheaper and B-cheaper counts in each background, divides by ten, then averages. Strong agreement within backgrounds can coexist with different preferred rules across backgrounds.'),
        ('Ties, coverage and numerical limits','Exact cost ties remain in the denominator and do not favor either direction. A 5-5 split gives 50% directional agreement; ten exact ties give 0% directional agreement and 100% ties. Tiny differences may be numerical error. The grid is incomplete and does not describe real-world frequencies. Only available matched backgrounds are included, with no missing permutations filled in.'),
        ('How the charts differ','Charts labeled fixed risk-neutral settings show one default background at each litigation expense. Charts labeled background averages instead use baseline litigation costs and all available risk attitudes, varying the other represented assumptions. Each row states its A-to-B transition and background count. Some risk-averse results still allow substantial economic improvements.')]))
    if initial_review:
        r=initial_review
        pages.append(('The ten initial searches also need caution',[
            ('A separate screen now covers every initial result',f"All {r['Selections']:,} accepted initial selections were reviewed using their saved raw incentive gains, expected utilities and original seeds, including same-seed repairs. This was a read-only calculation in money-equivalent units; it did not rerun the game or change formal pass labels."),
            ('The same risk-averse limitation appears before pooling',f"In the nine high-cost, moderately risk-averse baseline settings with double fees and double damages, the ten searches give {r['FocusSelections']} Gini/Liu results. Their defendant incentives are worth {100*r['FocusMinimumDefendantGainOverBenefit']:.0f}-{100*r['FocusMaximumDefendantGainOverBenefit']:.0f}% of the activity benefit. In {r['FocusAboveFullBenefit']} results the gain exceeds the entire benefit."),
            ('Why this matters for the agreement percentages','The background-average tables count the signs of observed social-cost differences. Those signs remain correctly counted, but agreement among inadequately accurate searches cannot establish equilibrium robustness. The screen does not prove a particular cost ranking is wrong or bound its social-cost error.'),
            ('The problem is uneven',f"Across {r['RiskNeutralSelections']:,} risk-neutral initial results, the largest defendant gain is {100*r['RiskNeutralMaximumDefendantGainOverBenefit']:.3f}% of the activity benefit. This is much smaller than the problematic risk-averse examples. It still does not prove a social-cost ranking or exact selector-objective optimality."),
            ('What remains to do','Further numerical work should use economically meaningful incentive scales and separately assess selector-objective quality. The existing normalized classifications and saved outputs remain intact. Source-bound audit summaries and selection-level diagnostic rows are available from the online report.')]))
    pages.append(('Magnitude, distributions and policy effects',[
        ('Within a setting','A dot represents one of the ten original-seed searches with every model parameter fixed. The mean, median and full observed range describe those searches. Ten available observations give 45 distinct unordered pairs. Average their absolute differences, then divide by the same setting mean to express pairwise dispersion as a percentage.'),
        ('Effects of one rule change','For each exact A-to-B comparison, calculate (B minus A) divided by A for each corresponding search. Average these ten percentages within the background. The signed average shows direction; the average absolute percentage shows size without cancellation. For example, +10% and -10% average to 0% signed and 10% absolute.'),
        ('Average within backgrounds first','Average the within-background means across the backgrounds selected by the filters, giving each background equal weight. Never combine different starting and ending rule values. Average pairwise dispersion separately under A and under B; that spread is a different quantity from the cost effect of changing the rule.'),
        ('Which distributions are shown?','The fixed-background charts display both cost distributions and the paired-seed links. Online, dots and summary tables can also show care, harm, litigation spending, participation and event rates. Conditional undefined values remain missing, with availability stated. The paired-effect dots show the ten percentage changes for the selected cost comparison.'),
        ('Units and interpretation','A percentage change with zero starting cost is undefined; a complete percentage average then remains unavailable. A setting with ten identical zero outcomes has zero pairwise dispersion. Relative gaps can look large for near-zero outcomes, so raw-unit gaps are retained. These are descriptive numerical distributions, not confidence intervals or a population of every equilibrium. Economic-accuracy qualifications still apply.')]))
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


def figures(data,summary,out,paired,initial_review=None):
    target=out/'figures';target.mkdir(exist_ok=True)
    plt.rcParams.update({'font.family':'DejaVu Sans','font.size':9,'axes.spines.top':False,'axes.spines.right':False,
        'axes.grid':True,'grid.alpha':.18,'axes.axisbelow':True,'svg.fonttype':'none'})
    inventory=[]
    def keep(fig,title,name,pdf):
        finish_figure(fig,title,target/name,pdf);inventory.append({'Name':name,'Title':title})
    with PdfPages(out/'scientific-figure-atlas.pdf') as pdf:
        atlas_guide(pdf,initial_review)
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
        for parameter in CORE:
            fig,axes=plt.subplots(1,3,figsize=(12,7),squeeze=False)
            for col,cost in enumerate(('0.25','1','4')):
                matches=[g for g in paired['Groups'] if g['Parameter']==parameter and g['Default'] and g['Background']['Risk Aversion']=='Risk Neutral' and g['Background']['Costs Multiplier']==cost]
                if len(matches)!=1:raise ValueError('Missing or ambiguous fixed-background chart')
                group=matches[0];ax=axes[0,col];positions=np.arange(len(group['Pairs']))
                for method,offset in [('gini',-.12),('liu',.12)]:
                    ax.scatter([p[method]['BLower'] for p in group['Pairs']],positions+offset,color=COLORS[method],label=LABELS[method],s=30)
                ax.set_yticks(positions,[p['From']+' → '+p['To'] for p in group['Pairs']]);ax.invert_yaxis()
                ax.set_xticks(range(0,11,2));ax.set_xlim(-.4,10.4);ax.set_xlabel('Paired searches where B costs less (of 10)')
                ax.set_ylabel('Rule value A → rule value B');ax.set_title({'0.25':'Quarter litigation costs','1':'Baseline litigation costs','4':'Four-times litigation costs'}[cost])
                ax.legend(fontsize=7)
            keep(fig,f'{parameter_name(parameter)}: same-seed rankings, fixed risk-neutral settings',SLUGS[parameter]+'-paired-agreement',pdf)
        for parameter in CORE:
            rows=[r for r in paired['TransitionSummaries'] if r['Parameter']==parameter and r['RiskScope']=='*' and r['CostScope']=='1']
            fig,axes=plt.subplots(1,2,figsize=(12,7))
            positions=np.arange(len(rows))
            for ax,key,title in zip(axes,['MeanBLowerFraction','MeanDirectionalAgreement'],['Average searches where B is cheaper','Average agreement within backgrounds']):
                for method,offset in [('gini',-.12),('liu',.12)]:
                    ax.scatter([100*r[method][key] for r in rows],positions+offset,color=COLORS[method],label=LABELS[method],s=30)
                ax.set_yticks(positions,[r['From']+' → '+r['To']+f" (n={r['Backgrounds']})" for r in rows]);ax.invert_yaxis()
                ax.set_xlim(-2,102);ax.set_xticks(range(0,101,20));ax.set_xlabel('Equal-weight average of background percentages (%)')
                ax.set_ylabel('Rule value A → B; n = matched backgrounds');ax.set_title(title);ax.legend(fontsize=8)
            keep(fig,f'{parameter_name(parameter)}: background averages\nBaseline litigation costs; all available risk attitudes',SLUGS[parameter]+'-background-averages',pdf)
        for parameter in CORE:
            rows=[r for r in paired['TransitionSummaries'] if r['Parameter']==parameter and r['RiskScope']=='*' and r['CostScope']=='1']
            fig,axes=plt.subplots(1,2,figsize=(12,7));positions=np.arange(len(rows))
            for ax,key,title in zip(axes,['MeanSignedRelativeEffect','MeanAbsoluteRelativeEffect'],['Mean signed percentage effect','Mean absolute percentage effect']):
                for method,offset in [('gini',-.12),('liu',.12)]:
                    ax.scatter([100*r[method][key] for r in rows],positions+offset,color=COLORS[method],label=LABELS[method],s=30)
                ax.set_yticks(positions,[r['From']+' → '+r['To']+f" (n={r['Backgrounds']})" for r in rows]);ax.invert_yaxis()
                ax.axvline(0,color='#697778',lw=.8);ax.set_xlabel('Percentage change, averaged within backgrounds first (%)')
                ax.set_ylabel('Rule value A → B; n = matched backgrounds');ax.set_title(title);ax.legend(fontsize=8)
            keep(fig,f'{parameter_name(parameter)}: size of cost effects\nBaseline litigation costs; all available risk attitudes',SLUGS[parameter]+'-effect-magnitudes',pdf)
        for parameter,start,end in [(CORE[0],'0','1'),(CORE[1],'0.5','2'),(CORE[2],'0','1')]:
            group=next(g for g in paired['Groups'] if g['Parameter']==parameter and g['Default'] and g['Background']['Risk Aversion']=='Risk Neutral' and g['Background']['Costs Multiplier']=='1')
            pair=next(p for p in group['Pairs'] if p['From']==start and p['To']==end)
            fig,axes=plt.subplots(1,2,figsize=(12,4.8),sharex=True,sharey=True)
            values=[v*10000 for m in ('gini','liu') for key in ('Before','After') for v in pair[m][key]]
            for ax,method in zip(axes,('gini','liu')):
                before=np.array(pair[method]['Before'])*10000;after=np.array(pair[method]['After'])*10000;jitter=np.array([(i%5-2)*.028 for i in range(10)])
                for i in range(10):ax.plot([before[i],after[i]],[jitter[i],1+jitter[i]],color=COLORS[method],alpha=.2,lw=1)
                for i,v in enumerate((before,after)):
                    ax.scatter(v,i+jitter,color=COLORS[method],s=28,label='Searches' if i==0 else None,zorder=3)
                    ax.plot([statistics.mean(v)]*2,[i-.2,i+.2],color='#162e33',lw=2,label='Mean' if i==0 else None)
                    ax.scatter([statistics.median(v)],[i],facecolors='white',edgecolors=COLORS[method],marker='D',s=58,label='Median' if i==0 else None,zorder=4)
                ax.set_yticks([0,1],[f'A: {start}',f'B: {end}']);ax.set_ylim(1.45,-.45);ax.set_title(LABELS[method]);ax.set_xlabel('Total social cost per 10,000 opportunities');ax.legend(loc='best',fontsize=7)
            keep(fig,f'{parameter_name(parameter)} {start} → {end}: ten observed searches\nDefault risk-neutral setting; baseline litigation costs',SLUGS[parameter]+'-search-distributions',pdf)
    return inventory


STYLE='''*{box-sizing:border-box}html{scroll-behavior:smooth}body{margin:0;background:#faf9f5;color:#263238;font:16px/1.65 system-ui,-apple-system,Segoe UI,sans-serif}a{color:#006f77}header,main,footer{max-width:1120px;margin:auto;padding:25px}header{padding-top:45px}h1,h2,h3{font-family:Georgia,serif;line-height:1.18;color:#19292d}h1{font-size:clamp(2rem,5vw,3.6rem);max-width:950px;margin:.4em 0}h2{font-size:2rem;margin-top:2em}h3{font-size:1.4rem}.eyebrow{font-size:.8rem;letter-spacing:.1em;text-transform:uppercase;color:#677777}.lede{font-size:1.16rem;max-width:850px}.status{border-left:4px solid #b37c20;background:#fff3d9;padding:15px 18px;font-size:.9rem}.nav{display:flex;flex-wrap:wrap;gap:9px;margin-top:25px}.nav a{border:1px solid #cdd6d5;border-radius:24px;padding:7px 13px;text-decoration:none;font-size:.88rem}.nav a[aria-current]{background:#073f47;color:white;border-color:#073f47}section{padding:8px 0 25px;border-bottom:1px solid #d9dfdc}.table-wrap{overflow:auto;border:1px solid #dbe1df;border-radius:5px;background:white}table{border-collapse:collapse;width:100%;font-size:.86rem}th,td{padding:11px 13px;border-bottom:1px solid #e3e7e4;text-align:right;vertical-align:top}th{background:#edf2ee;font-weight:600}th:first-child,td:first-child{text-align:left}tr:last-child td{border-bottom:0}.note{font-size:.87rem;color:#53686d}figure{margin:25px 0;background:white;border:1px solid #e1e4df;padding:12px}figure img{width:100%;height:auto}figcaption{padding:8px 10px;font-size:.87rem;color:#506068}button,.button{display:inline-block;background:#07565f;color:white;border:0;border-radius:5px;padding:10px 14px;font:inherit;font-size:.88rem;cursor:pointer;text-decoration:none;margin:5px 7px 5px 0}label{display:inline-flex;flex-direction:column;font-size:.85rem;margin-right:14px;gap:5px}select,input{font:inherit;max-width:100%;padding:8px;border:1px solid #aebeba;border-radius:4px;background:white}.finding{padding:10px 0 15px;max-width:950px}.finding strong{color:#07545b}.kpis{display:flex;gap:35px;flex-wrap:wrap;margin:25px 0}.kpis div{min-width:170px}.kpis b{font:2rem Georgia,serif;display:block}.code{font-family:monospace;overflow-wrap:anywhere}.two{display:grid;grid-template-columns:1fr 1fr;gap:20px}.pill{font-size:.75rem;border:1px solid #d9b877;background:#fff5df;padding:2px 7px;border-radius:5px}.legend{display:flex;gap:18px;flex-wrap:wrap}.swatch{display:inline-block;width:10px;height:10px;margin-right:7px;border-radius:50%}footer{font-size:.82rem;color:#6c7779;border-top:1px solid #d9dfdc;margin-top:35px}@media(max-width:700px){header,main,footer{padding:18px}.two{grid-template-columns:1fr}h2{font-size:1.7rem}table{font-size:.78rem}th,td{padding:9px}.kpis{gap:20px}.nav{gap:6px}.nav a{padding:6px 10px;font-size:.8rem}}'''


STYLE += '\n#explorer td:first-child,#explorer th:first-child{min-width:190px;max-width:240px;position:sticky;left:0;background:#fff;z-index:1}#explorer th:first-child{background:#edf2ee}\n'


STYLE += '''\n.three{display:grid;grid-template-columns:repeat(3,1fr);gap:22px}.three article{background:#eef3ef;padding:18px;border-radius:6px}.three h3{margin-top:0}.steps{padding-left:25px;max-width:900px}.steps li{padding:8px 0}.read-next{background:#edf4f1;padding:15px;border-radius:6px}p{max-width:950px}details{margin:20px 0;padding:14px 18px;background:#f0f3f0;border:1px solid #d6dfd9;border-radius:5px}summary{cursor:pointer;font-weight:650;color:#07545b}details[open] summary{margin-bottom:15px}.skip{position:absolute;left:-10000px}.skip:focus{position:static;display:block;padding:12px}#explorer td:first-child{font-size:.78rem} @media(max-width:750px){.three{grid-template-columns:1fr}.three article{padding:15px}label{display:flex;margin:10px 0}h1{overflow-wrap:anywhere}}\n'''


STYLE += '\n.wide-control{display:flex;max-width:100%;margin-top:16px}#policy-background{width:100%;text-overflow:ellipsis}#paired-comparisons td{min-width:110px}#paired-comparisons td:nth-child(3),#paired-comparisons td:nth-child(4){min-width:180px}#pair-detail{scroll-margin-top:20px}#pair-repetitions td{white-space:nowrap}\n'
STYLE += '\n#transition-summaries td{min-width:100px}#transition-summaries td:nth-child(4),#transition-summaries td:nth-child(5){min-width:180px}#transition-backgrounds td:first-child{min-width:250px;max-width:430px}#transition-detail{scroll-margin-top:20px}\n'
STYLE += '\n.distribution-chart{max-width:100%;overflow:auto;background:white;border:1px solid #dbe1df;border-radius:5px;margin:18px 0}.distribution-chart svg{display:block;width:100%;min-width:660px;height:auto}#pair-magnitude td,#pair-distribution-stats td,#setting-distribution-stats td{white-space:nowrap}#setting-distributions{scroll-margin-top:20px}\n'


def table(headers,rows):
    return '<div class="table-wrap"><table><thead><tr>'+''.join('<th>'+esc(h)+'</th>' for h in headers)+'</tr></thead><tbody>'+''.join('<tr>'+''.join('<td>'+str(v)+'</td>' for v in row)+'</tr>' for row in rows)+'</tbody></table></div>'


def image(name,caption):return f'<figure><a href="figures/{name}.svg"><img loading="lazy" src="figures/{name}.png" alt="{esc(caption)}"></a><figcaption>{esc(caption)} · <a href="figures/{name}.svg">Open scalable chart</a></figcaption></figure>'


def page(title,body,active,economic_review=None):
    navigation=[('index.html','Start here'),('published.html','Original draft'),('gini.html','Gini approach'),('liu.html','Liu approach'),('variability.html','Consistency'),('benchmarks.html','Cost ratios & variability'),('methods.html','How to read & downloads')]
    nav=''.join(f'<a href="{url}"'+(' aria-current="page"' if url==active else '')+'>'+label+'</a>' for url,label in navigation)
    notice='<strong>Results are provisional.</strong> Some calculations involving aversion to financial risk still allow economically meaningful improvements in behavior. Those results need greater accuracy before we draw firm conclusions. <a href="methods.html#economic-accuracy">What this means</a>.' if economic_review else '<strong>Results are provisional.</strong> Read the methods and limitations before interpreting the figures.'
    if economic_review and economic_review.get('InitialSearchReview'):
        notice=notice.replace('Those results need greater accuracy','This also affects some initial searches used in the agreement percentages. Those results need greater accuracy')
    return f'<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>{esc(title)} | Litigation rules and behavior</title><meta name="description" content="A plain-English guide to how litigation rules affect care, accidents, lawsuits and social costs, comparing the original draft with Gini and Liu calculations."><link rel="stylesheet" href="report.css"></head><body><a class="skip" href="#main">Skip to the explanation</a><header><div class="eyebrow">Litigation rules, behavior and social costs</div><h1>{esc(title)}</h1><div class="status">{notice}</div><nav class="nav" aria-label="Report pages">{nav}</nav></header><main id="main">{body}</main><footer>These are model calculations, not estimates from observed lawsuits. The original published values are kept separate from later replay calculations. Ranges describe ten numerical searches; they are not confidence intervals. <a href="methods.html#glossary">Definitions</a> · Private research report.</footer><script src="report.js" defer></script></body></html>'


SCRIPT='function csv(rows){if(!rows.length)return \'\';const cols=Object.keys(rows[0]);const q=x=>x==null?\'\':(\'"\'+String(x).replaceAll(\'"\',\'""\')+\'"\');return cols.map(q).join(\',\')+\'\\r\\n\'+rows.map(r=>cols.map(c=>q(r[c])).join(\',\')).join(\'\\r\\n\')}\nfunction download(name,content,type){const a=document.createElement(\'a\');a.href=URL.createObjectURL(new Blob([content],{type}));a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(a.href),5000)}\ndocument.querySelectorAll(\'[data-export]\').forEach(b=>b.onclick=async()=>{const data=await(await fetch(\'tables.json\')).json();download(b.dataset.export+\'.csv\',csv(data[b.dataset.export]),\'text/csv;charset=utf-8\')});\nasync function explorer(){const node=document.querySelector(\'#explorer\');if(!node)return;const rows=await(await fetch(\'explorer.json\')).json();const metric=document.querySelector(\'#metric\'),search=document.querySelector(\'#search\'),sort=document.querySelector(\'#sort\');\nconst probability=m=>![\'SocialWelfareLoss\',\'OpportunityCost\',\'HarmCost\',\'LitigationExpenditures\',\'PrecautionLevel\'].includes(m);\nfunction format(x,m){if(x==null)return \'—\';if(probability(m))return (100*x).toLocaleString(undefined,{maximumFractionDigits:6})+\'%\';if(x===0)return \'0\';return Math.abs(x)<1e-9?x.toExponential(3):x.toLocaleString(undefined,{maximumFractionDigits:10})}\nfunction draw(){const m=metric.value,s=search.value.toLowerCase();let a=rows.filter(r=>(r.Name+\' \'+r.OriginalName+\' \'+r.Family).toLowerCase().includes(s));const width=r=>Math.max(r.gini.Ranges[m].Width??0,r.liu.Ranges[m].Width??0);if(sort.value===\'width\')a.sort((x,y)=>width(y)-width(x));else a.sort((x,y)=>x.Name.localeCompare(y.Name));document.querySelector(\'#count\').textContent=a.length+\' matching scenarios; showing first \'+Math.min(80,a.length)+\'.\';document.querySelector(\'#metric-help\').textContent=probability(m)?\'Rates are percentages. Among-dispute rates use all disputes, including those never filed.\':m===\'PrecautionLevel\'?\'Average care on the 0–7 scale, including zero for nonparticipation.\':\'Costs are model units per opportunity, not dollars. One physical accident causes one unit of harm.\';const body=document.querySelector(\'#explorer tbody\');body.replaceChildren();for(const r of a.slice(0,80)){const tr=document.createElement(\'tr\');const values=[r.Name+(r.Candidate?\' [Liu misses the original numerical target]\':\'\'),format(r.published[m],m),format(r.gini.Pooled[m],m),format(r.liu.Pooled[m],m),format(r.gini.Ranges[m].Minimum,m)+\' – \'+format(r.gini.Ranges[m].Maximum,m),format(r.liu.Ranges[m].Minimum,m)+\' – \'+format(r.liu.Ranges[m].Maximum,m)];for(const value of values){const td=document.createElement(\'td\');td.textContent=value;tr.append(td)}body.append(tr)}}metric.onchange=search.oninput=sort.onchange=draw;draw()}explorer();'


def html_reports(data,summary,out,inventory,findings,economic_review,paired,distributions):
    (out/'report.css').write_text(STYLE,encoding='utf-8')
    paired_script=Path(__file__).with_name('endogenous_paired_comparisons.js').read_text(encoding='utf-8')
    magnitude_script=Path(__file__).with_name('endogenous_magnitude_views.js').read_text(encoding='utf-8')
    script=SCRIPT.replace('body.append(tr)}}metric.onchange','addSettingDistributionButton(tr,r,m);body.append(tr)}}metric.onchange')
    script=script.replace('metric.onchange=search.oninput=sort.onchange=draw;','metric.onchange=()=>{draw();refreshSettingDistribution(metric.value)};search.oninput=sort.onchange=draw;')
    (out/'report.js').write_text(script+magnitude_script+paired_script,encoding='utf-8')
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
    (out/'paired-comparisons.json.gz').write_bytes(gzip.compress(json.dumps(paired,separators=(',',':'),allow_nan=False).encode(),compresslevel=6,mtime=0))
    (out/'setting-distributions.json.gz').write_bytes(gzip.compress(json.dumps(distributions,separators=(',',':'),allow_nan=False).encode(),compresslevel=6,mtime=0))
    for metric in distributions['Outcomes']:
        chunk=dict(Schema=distributions['Schema'],Seeds=distributions['Seeds'],Outcomes={metric:distributions['Outcomes'][metric]},
            Cases={cid:dict(Name=c['Name'],Methods={m:{metric:c['Methods'][m][metric]} for m in ('gini','liu')}) for cid,c in distributions['Cases'].items()})
        (out/('setting-distributions-'+metric+'.json.gz')).write_bytes(gzip.compress(json.dumps(chunk,separators=(',',':'),allow_nan=False).encode(),compresslevel=6,mtime=0))
    for rows,name in [(export_rows(paired,False),'paired-comparison-summary'),
                      (export_rows(paired,True),'paired-search-differences'),
                      (transition_export_rows(paired),'transition-background-summary'),
                      (distribution_export_rows(distributions),'setting-distribution-summary')]:
        buffer=io.StringIO(newline='')
        fields=list(dict.fromkeys(k for row in rows for k in row))
        writer=csv.DictWriter(buffer,fieldnames=fields);writer.writeheader();writer.writerows(rows)
        (out/(name+'.csv.gz')).write_bytes(gzip.compress(buffer.getvalue().encode(),compresslevel=6,mtime=0))
    table_data=json.dumps({'case-outcomes':case_rows,'numerical-checks':numerical,'outcome-guide':[{'Code':k,'Name':v[0],'Meaning':v[1],'Units':v[2]} for k,v in OUTCOMES.items()]},separators=(',',':'),allow_nan=False).encode()
    (out/'tables.json.gz').write_bytes(gzip.compress(table_data,compresslevel=6,mtime=0))
    script=(out/'report.js').read_text(encoding='utf-8')
    script=script.replace("const data=await(await fetch('tables.json')).json();", "const response=await fetch('tables.json.gz');const data=await new Response(response.body.pipeThrough(new DecompressionStream('gzip'))).json();")
    script=script.replace("const rows=await(await fetch('explorer.json')).json();", "const response=await fetch('explorer.json.gz');const rows=await new Response(response.body.pipeThrough(new DecompressionStream('gzip'))).json();")
    revision=hashlib.sha256((out/'paired-comparisons.json.gz').read_bytes()).hexdigest()[:16]
    script=script.replace("fetch('paired-comparisons.json.gz')",f"fetch('paired-comparisons.json.gz?v={revision}')")
    script=script.replace("fetch(name+'.csv.gz')",f"fetch(name+'.csv.gz?v={revision}')")
    revision=hashlib.sha256((out/'setting-distributions.json.gz').read_bytes()).hexdigest()[:16]
    script=script.replace("fetch('setting-distributions.json.gz')",f"fetch('setting-distributions.json.gz?v={revision}')")
    script=script.replace("fetch('setting-distributions-'+metric+'.json.gz')",f"fetch('setting-distributions-'+metric+'.json.gz?v={revision}')")
    (out/'report.js').write_text(script,encoding='utf-8')
    for name in ('report.js','report.css','scientific-figure-atlas.pdf'):
        revision=hashlib.sha256((out/name).read_bytes()).hexdigest()[:16]
        for document in out.glob('*.html'):
            content=document.read_text(encoding='utf-8').replace('"'+name+'"','"'+name+'?v='+revision+'"')
            document.write_text(content,encoding='utf-8')


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--data',required=True);parser.add_argument('--statistics',required=True)
    parser.add_argument('--output',required=True);parser.add_argument('--findings',required=True);parser.add_argument('--html-only',action='store_true');parser.add_argument('--economic-review');parser.add_argument('--initial-manifest',required=True);parser.add_argument('--initial-economic-review')
    args=parser.parse_args();data=json.loads(Path(args.data).read_text(encoding='utf-8'));summary=json.loads(Path(args.statistics).read_text(encoding='utf-8'));out=Path(args.output);out.mkdir(exist_ok=True,parents=True)
    provenance=seed_provenance(args.initial_manifest,data['Repetitions'])
    paired=build_pairwise(data,[r['Seed'] for r in provenance['Repetitions']],BASE)
    paired['Provenance']=provenance
    paired['TransitionSummaries']=scoped_transition_summaries(paired)
    paired['SummaryWeighting']='Each available exact matched background has equal weight within the chosen rule transition and filters. Exact search ties remain in the denominator. No missing combinations are imputed.'
    paired['AssumptionLabels']={k:parameter_name(k) for k in data['Cases'][0]['Parameters']}
    distributions=build_distributions(data,[r['Seed'] for r in provenance['Repetitions']],OUTCOMES)
    for case in data['Cases']:distributions['Cases'][case['Id']]['Name']=setting_name(case,BASE)
    economic_review=json.loads(Path(args.economic_review).read_text(encoding='utf-8')) if args.economic_review else None
    if economic_review:
        assert economic_review['Cases']==len(data['Cases']) and not economic_review['Errors']
        (out/'economic-accuracy-review.json.gz').write_bytes(gzip.compress(Path(args.economic_review).read_bytes(),compresslevel=6,mtime=0))
    initial_review=None
    if args.initial_economic_review:
        assert economic_review, 'The initial-search review supplements the pooled review'
        initial_review=load_review(args.initial_economic_review,data,hashlib.sha256(Path(args.data).read_bytes()).hexdigest())
        economic_review['InitialSearchReview']=initial_review
        (out/'initial-economic-accuracy-review.json.gz').write_bytes(gzip.compress(Path(args.initial_economic_review).read_bytes(),compresslevel=6,mtime=0))
        shutil.copyfile(Path(args.initial_economic_review).with_name('selection-rows.jsonl.gz'),out/'initial-economic-selection-rows.jsonl.gz')
    if args.html_only:inventory=json.loads((out/'figure-inventory.json').read_text())
    else:
        inventory=figures(data,summary,out,paired,initial_review);(out/'figure-inventory.json').write_text(json.dumps(inventory,indent=2))
    html_reports(data,summary,out,inventory,json.loads(Path(args.findings).read_text(encoding='utf-8')),economic_review,paired,distributions)
    for source,name in [(args.data,'report-data.json.gz'),(args.statistics,'comparison-statistics.json.gz')]:
        (out/name).write_bytes(gzip.compress(Path(source).read_bytes(),compresslevel=6,mtime=0))
    print(json.dumps({'Pages':6,'Figures':len(inventory),'FixedBackgrounds':paired['GroupCount'],'Pairs':paired['PairCount'],'Output':str(out.resolve())}))


if __name__=='__main__':main()
