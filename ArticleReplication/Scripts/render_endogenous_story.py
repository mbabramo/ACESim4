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

import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.backends.backend_pdf import PdfPages
import numpy as np

from endogenous_story_report import MEASURES, METHODS, method_values, quantile

LABELS={'published':'Draft: first equilibrium','gini':'Pooled Gini','liu':'Pooled Liu-affinity'}
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
    fig.text(.01,.006,'Published PREC010 comparator; pooled numerical selectors. Provisional archive audit; ranges are descriptive, not confidence intervals.',fontsize=7,color='#4a5154')
    fig.tight_layout(rect=(0,.025,1,.965))
    fig.savefig(path.with_suffix('.png'),dpi=160,bbox_inches='tight')
    fig.savefig(path.with_suffix('.svg'),bbox_inches='tight')
    pdf.savefig(fig,bbox_inches='tight');plt.close(fig)


def figures(data,summary,out):
    target=out/'figures';target.mkdir(exist_ok=True)
    plt.rcParams.update({'font.family':'DejaVu Sans','font.size':9,'axes.spines.top':False,'axes.spines.right':False,
        'axes.grid':True,'grid.alpha':.18,'axes.axisbelow':True,'svg.fonttype':'none'})
    inventory=[]
    def keep(fig,title,name,pdf):
        finish_figure(fig,title,target/name,pdf);inventory.append({'Name':name,'Title':title})
    with PdfPages(out/'scientific-figure-atlas.pdf') as pdf:
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
                        ax.set_xticks(x,levels);ax.set_xlabel(parameter);ax.set_title(f'Litigation-cost multiplier {cost:g}')
                        ax.set_ylabel('Social loss (×10⁻⁴)' if row==0 else 'Mean precaution level')
                        if row==1:ax.set_ylim(0,7.3)
                axes[0,0].legend(fontsize=7)
                keep(fig,f'{parameter}: total social cost and precaution — {risk}',f'{slug}-{risk_slug}-comparison',pdf)
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
                            ax.set_xticks(x,levels);ax.set_ylabel(metric.replace('GivenDispute','').replace('Unconditional','')+(' (%)' if conditional else ' (×10⁻⁵)'))
                            if conditional:ax.set_ylim(-2,102)
                            if row==0:ax.set_title(f'Cost multiplier {cost:g}')
                            if row==3:ax.set_xlabel(parameter)
                    axes[0,0].legend(fontsize=6)
                    keep(fig,f'{parameter}: litigation outcomes {denom.replace("-"," ")} — {risk}',f'{slug}-{risk_slug}-{denom}',pdf)
            for method in METHODS:
                fig,axes=plt.subplots(3,2,figsize=(11,9),squeeze=False)
                for row,cost in enumerate((.25,1,4)):
                    maximum=0
                    for risk in ('Risk Neutral','Moderately Risk Averse'):
                        levels,cases=slice_cases(data,parameter,cost,risk)
                        maximum=max([maximum]+[method_values(c,other)['SocialWelfareLoss']*1e4 for c in cases.values() for other in METHODS])
                    for col,risk in enumerate(('Risk Neutral','Moderately Risk Averse')):
                        levels,cases=slice_cases(data,parameter,cost,risk);x=np.arange(len(levels));bottom=np.zeros(len(x));ax=axes[row,col]
                        for metric,label,color in [('OpportunityCost','Precaution + foregone activity','#476e82'),('HarmCost','Harm','#b55459'),('LitigationExpenditures','Litigation','#a58b43')]:
                            y=np.array([method_values(cases[level],method)[metric]*1e4 for level in levels])
                            ax.bar(x,y,bottom=bottom,label=label,color=color,width=.7);bottom+=y
                        totals=[method_values(cases[level],method)['SocialWelfareLoss']*1e4 for level in levels]
                        ax.plot(x,totals,color='#1c2428',marker='_',ls='none',label='Reported total')
                        ax.set_xticks(x,levels);ax.set_ylim(0,maximum*1.12);ax.set_title(f'{risk}; cost {cost:g}')
                        ax.set_xlabel(parameter);ax.set_ylabel('Expected loss (×10⁻⁴)')
                handles,labels=axes[0,0].get_legend_handles_labels()
                fig.legend(handles,labels,loc='upper center',bbox_to_anchor=(.5,.965),ncol=4,fontsize=7,frameon=False)
                keep(fig,f'{LABELS[method]}: cost decomposition under {parameter.lower()}',f'{method}-{slug}-components',pdf)
        fig,axes=plt.subplots(2,2,figsize=(11,8))
        for ax,metric in zip(axes.ravel(),['SocialWelfareLoss','PrecautionLevel','TrialGivenDispute','SettlementGivenDispute']):
            for method in ('gini','liu'):
                vals=[c['Selectors'][method]['InitializationRanges'][metric] for c in data['Cases']]
                values=sorted((r['RelativeWidth']*100 if metric=='SocialWelfareLoss' else r['Width']*(100 if metric.endswith('GivenDispute') else 1)) for r in vals if (r['RelativeWidth'] if metric=='SocialWelfareLoss' else r['Width']) is not None)
                ax.plot(values,np.arange(1,len(values)+1)/len(values),color=COLORS[method],label=LABELS[method])
            ax.set_title(metric);ax.set_ylabel('Fraction of article settings')
            ax.set_xlabel('Observed ten-start range (% of median)' if metric=='SocialWelfareLoss' else 'Observed range (percentage points)' if metric.endswith('GivenDispute') else 'Observed range (precaution levels)')
            ax.set_xlim(left=0);ax.set_ylim(0,1);ax.legend(fontsize=7)
        keep(fig,'How much do outcomes vary across ten independent starts?', 'initialization-variability',pdf)
        selected=[r for r in summary['MatchedSummary'] if r['Metric']=='SocialWelfareLoss' and r['Method']!='published']
        parameters=list(dict.fromkeys(r['Parameter'] for r in selected));matrix=np.array([[next(r['FractionObservedRangeIdentifiesSign'] for r in selected if r['Parameter']==p and r['Method']==s) for s in ('gini','liu')] for p in parameters])
        fig,ax=plt.subplots(figsize=(8,9));im=ax.imshow(matrix,vmin=0,vmax=1,cmap='YlGnBu',aspect='auto')
        ax.set_xticks([0,1],['Gini','Liu-affinity']);ax.set_yticks(range(len(parameters)),parameters)
        for i in range(len(parameters)):
            for j in range(2):ax.text(j,i,f'{matrix[i,j]:.0%}',ha='center',va='center',color='white' if matrix[i,j]>.55 else '#202b30')
        fig.colorbar(im,ax=ax,label='Fraction of matched contrasts with non-overlapping observed ranges')
        keep(fig,'When does the observed equilibrium range identify the sign of a policy effect?','policy-interpretability',pdf)
        rows=[r for r in summary['MatchedSummary'] if r['Metric']=='SocialWelfareLoss']
        parameters=list(dict.fromkeys(r['Parameter'] for r in rows))
        fig,ax=plt.subplots(figsize=(11,9))
        for offset,method in zip((-.23,0,.23),METHODS):
            selected=[next(r for r in rows if r['Parameter']==p and r['Method']==method) for p in parameters]
            y=np.arange(len(parameters))+offset
            med=np.array([r['MedianAbsoluteRelativeChange']*100 for r in selected]);tail=np.array([r['P90AbsoluteRelativeChange']*100 for r in selected])
            ax.barh(y,med,height=.19,color=COLORS[method],label=LABELS[method],alpha=.8)
            for yi,m,t in zip(y,med,tail):ax.plot([m,t],[yi,yi],color=COLORS[method],lw=1);ax.plot(t,yi,'o',color=COLORS[method],ms=3)
        ax.set_yticks(range(len(parameters)),parameters);ax.invert_yaxis()
        ax.set_xlabel('Absolute matched social-cost change (%): bar = median; dot = 90th percentile')
        ax.legend(loc='lower right',fontsize=8)
        keep(fig,'Legal, informational and robustness variations across the three methods','robustness-effects',pdf)
    return inventory


STYLE='''*{box-sizing:border-box}html{scroll-behavior:smooth}body{margin:0;background:#faf9f5;color:#263238;font:16px/1.65 system-ui,-apple-system,Segoe UI,sans-serif}a{color:#006f77}header,main,footer{max-width:1120px;margin:auto;padding:25px}header{padding-top:45px}h1,h2,h3{font-family:Georgia,serif;line-height:1.18;color:#19292d}h1{font-size:clamp(2rem,5vw,3.6rem);max-width:950px;margin:.4em 0}h2{font-size:2rem;margin-top:2em}h3{font-size:1.4rem}.eyebrow{font-size:.8rem;letter-spacing:.1em;text-transform:uppercase;color:#677777}.lede{font-size:1.16rem;max-width:850px}.status{border-left:4px solid #b37c20;background:#fff3d9;padding:15px 18px;font-size:.9rem}.nav{display:flex;flex-wrap:wrap;gap:9px;margin-top:25px}.nav a{border:1px solid #cdd6d5;border-radius:24px;padding:7px 13px;text-decoration:none;font-size:.88rem}.nav a[aria-current]{background:#073f47;color:white;border-color:#073f47}section{padding:8px 0 25px;border-bottom:1px solid #d9dfdc}.table-wrap{overflow:auto;border:1px solid #dbe1df;border-radius:5px;background:white}table{border-collapse:collapse;width:100%;font-size:.86rem}th,td{padding:11px 13px;border-bottom:1px solid #e3e7e4;text-align:right;vertical-align:top}th{background:#edf2ee;font-weight:600}th:first-child,td:first-child{text-align:left}tr:last-child td{border-bottom:0}.note{font-size:.87rem;color:#53686d}figure{margin:25px 0;background:white;border:1px solid #e1e4df;padding:12px}figure img{width:100%;height:auto}figcaption{padding:8px 10px;font-size:.87rem;color:#506068}button,.button{display:inline-block;background:#07565f;color:white;border:0;border-radius:5px;padding:10px 14px;font:inherit;font-size:.88rem;cursor:pointer;text-decoration:none;margin:5px 7px 5px 0}label{display:inline-flex;flex-direction:column;font-size:.85rem;margin-right:14px;gap:5px}select,input{font:inherit;max-width:100%;padding:8px;border:1px solid #aebeba;border-radius:4px;background:white}.finding{padding:10px 0 15px;max-width:950px}.finding strong{color:#07545b}.kpis{display:flex;gap:35px;flex-wrap:wrap;margin:25px 0}.kpis div{min-width:170px}.kpis b{font:2rem Georgia,serif;display:block}.code{font-family:monospace;overflow-wrap:anywhere}.two{display:grid;grid-template-columns:1fr 1fr;gap:20px}.pill{font-size:.75rem;border:1px solid #d9b877;background:#fff5df;padding:2px 7px;border-radius:5px}.legend{display:flex;gap:18px;flex-wrap:wrap}.swatch{display:inline-block;width:10px;height:10px;margin-right:7px;border-radius:50%}footer{font-size:.82rem;color:#6c7779;border-top:1px solid #d9dfdc;margin-top:35px}@media(max-width:700px){header,main,footer{padding:18px}.two{grid-template-columns:1fr}h2{font-size:1.7rem}table{font-size:.78rem}th,td{padding:9px}.kpis{gap:20px}.nav{gap:6px}.nav a{padding:6px 10px;font-size:.8rem}}'''


STYLE += '\n#explorer td:first-child,#explorer th:first-child{min-width:190px;max-width:240px;position:sticky;left:0;background:#fff;z-index:1}#explorer th:first-child{background:#edf2ee}\n'


def table(headers,rows):
    return '<div class="table-wrap"><table><thead><tr>'+''.join('<th>'+esc(h)+'</th>' for h in headers)+'</tr></thead><tbody>'+''.join('<tr>'+''.join('<td>'+str(v)+'</td>' for v in row)+'</tr>' for row in rows)+'</tbody></table></div>'


def image(name,caption):return f'<figure><a href="figures/{name}.svg"><img loading="lazy" src="figures/{name}.png" alt="{esc(caption)}"></a><figcaption>{esc(caption)} · <a href="figures/{name}.svg">SVG</a></figcaption></figure>'


def page(title,body,active,economic_review=None):
    navigation=[('index.html','Comparison'),('published.html','First equilibrium'),('gini.html','Gini'),('liu.html','Liu-affinity'),('variability.html','Variability'),('methods.html','Methods & downloads')]
    nav=''.join(f'<a href="{url}"'+(' aria-current="page"' if url==active else '')+'>'+label+'</a>' for url,label in navigation)
    if economic_review:
        body='<section class="finding"><h2>Economic accuracy qualification · November 7</h2><p><strong>The risk-averse activity and welfare interpretations need tighter numerical checks.</strong> The full terminal-utility range can make a small normalized deviation economically large. In the nine high-cost, moderately risk-averse activity-suppression settings, audited unilateral improvements are worth 35–123% of the activity benefit in certainty-equivalent terms. Agreement across selectors and starts does not resolve a shared scaling issue. These charts and grid summaries remain diagnostic outputs, not established equilibrium predictions. <a href="methods.html#economic-accuracy">Evidence and interpretation</a>.</p></section>'+body
    return f'<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>{esc(title)} | Endogenous litigation</title><meta name="description" content="Scientific comparison of the draft equilibrium, Gini and Liu-affinity outcomes, and equilibrium variability."><link rel="stylesheet" href="report.css"></head><body><header><div class="eyebrow">Endogenous litigation · equilibrium comparison</div><h1>{esc(title)}</h1><div class="status"><strong>Provisional scientific report.</strong> Final certification remains incomplete. All initial selections pass the formal normalized 10⁻⁷ criterion; one pooled Liu candidate passes 10⁻⁶ and is explicitly flagged. Exact selector-objective optimality is not independently certified.</div><nav class="nav">{nav}</nav></header><main>{body}</main><footer>Published PREC010 values and saved-policy replay are distinct comparators. Ten-start ranges are observed selection sensitivity, not confidence intervals or bounds over every equilibrium. Private report · generated from source-hashed simulation outputs.</footer><script src="report.js" defer></script></body></html>'


SCRIPT='''function csv(rows){if(!rows.length)return '';const cols=Object.keys(rows[0]);const q=x=>x==null?'':('"'+String(x).replaceAll('"','""')+'"');return cols.map(q).join(',')+'\\r\\n'+rows.map(r=>cols.map(c=>q(r[c])).join(',')).join('\\r\\n')}function download(name,content,type){const a=document.createElement('a');a.href=URL.createObjectURL(new Blob([content],{type}));a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(a.href),5000)}document.querySelectorAll('[data-export]').forEach(b=>b.onclick=async()=>{const data=await(await fetch('tables.json')).json();download(b.dataset.export+'.csv',csv(data[b.dataset.export]),'text/csv;charset=utf-8')});async function explorer(){const node=document.querySelector('#explorer');if(!node)return;const rows=await(await fetch('explorer.json')).json();const metric=document.querySelector('#metric');const search=document.querySelector('#search');const sort=document.querySelector('#sort');const format=x=>x==null?'—':x===0?'0':Math.abs(x)<.001?x.toExponential(3):x.toPrecision(4);function draw(){const m=metric.value,s=search.value.toLowerCase();let a=rows.filter(r=>(r.Name+' '+r.Family).toLowerCase().includes(s));const width=r=>Math.max(r.gini.Ranges[m].Width??0,r.liu.Ranges[m].Width??0);if(sort.value==='width')a.sort((x,y)=>width(y)-width(x));else a.sort((x,y)=>x.Name.localeCompare(y.Name));document.querySelector('#count').textContent=a.length+' matching settings; showing first '+Math.min(80,a.length)+'.';const body=document.querySelector('#explorer tbody');body.replaceChildren();for(const r of a.slice(0,80)){const tr=document.createElement('tr');const values=[r.Name+(r.Candidate?' [10⁻⁶ Liu candidate]':''),format(r.published[m]),format(r.gini.Pooled[m]),format(r.liu.Pooled[m]),format(r.gini.Ranges[m].Minimum)+' – '+format(r.gini.Ranges[m].Maximum),format(r.liu.Ranges[m].Minimum)+' – '+format(r.liu.Ranges[m].Maximum)];for(const value of values){const td=document.createElement('td');td.textContent=value;tr.append(td)}body.append(tr)}}metric.onchange=search.oninput=sort.onchange=draw;draw()}explorer();'''


def html_reports(data,summary,out,inventory,findings,economic_review=None):
    render_page=lambda title,body,active:page(title,body,active,economic_review)
    (out/'report.css').write_text(STYLE,encoding='utf-8');(out/'report.js').write_text(SCRIPT,encoding='utf-8')
    methods_stats={m:[r for r in summary['MatchedSummary'] if r['Method']==m and r['Metric']=='SocialWelfareLoss'] for m in METHODS}
    body='<p class="lede">The draft describes a system that adjusts care and litigation in ways that often dampen the welfare effects of legal interventions. This report asks whether that story survives equilibrium selection, and where the observed equilibria leave a policy effect ambiguous.</p>'
    body+='<div class="kpis"><div><b>2,268</b>article settings</div><div><b>10</b>independent starts per selector</div><div><b>3 views</b>published, Gini, Liu-affinity</div></div>'
    body+='<section><h2>What survives, and what needs qualification?</h2>'
    for f in findings:body+=f'<div class="finding"><strong>{esc(f["Title"])}</strong><p>{esc(f["Text"])}</p></div>'
    body+='</section><section><h2>How large are matched policy effects?</h2><p>Each comparison holds every other recorded parameter fixed. The table pools the available calibration grid; it describes that grid, not a population-weighted empirical average.</p>'
    rows=[]
    for parameter in CORE+['Costs Multiplier','Risk Aversion','Cost Misestimation','Unit Precaution Cost']:
        vals=[next(r for r in methods_stats[m] if r['Parameter']==parameter) for m in METHODS]
        rows.append([esc(parameter),str(vals[0]['Pairs'])]+[fmt(r['MedianAbsoluteRelativeChange'],'pct') for r in vals])
    body+=table(['Changed parameter','Matched pairs','Draft median |Δcost|','Gini median |Δcost|','Liu median |Δcost|'],rows)
    body+='<p class="note">Percent changes use the matched reference case’s social cost. These are magnitudes; beneficial and harmful effects are both included. Read direction and observed-range robustness in the downloadable matched table. Excluding the qualified Liu candidate changes these three core median-effect summaries by less than 0.001 percentage points; the displayed baseline examples do not use its calibration.</p><button data-export="matched-summary">Download matched summary CSV</button></section>'
    body+='<section><h2>Compare the core figures directly</h2><p>Risk-neutral slices reproduce the draft’s default calibration, varying one legal parameter and litigation-cost level. Separate panels show moderately risk-averse cases in each method report. Open any figure for a vector version.</p>'
    for parameter in CORE:body+=image(SLUGS[parameter]+'-rn-comparison',parameter+': identical calibration and axes for all three methods')
    body+='</section><section><h2>Risk, information and other robustness variants</h2><p>The comparisons also cover risk aversion, misestimation, precaution prices, court noise, signal-grid resolution, wrongful attribution, asymmetric costs and the timing of costs. Each contrast holds the remaining recorded parameters fixed. Misestimation comparisons use the same reduced grid on both sides. Signal-noise comparisons vary the recorded P/D pair jointly. The perfect-adjudication package changes adjudication and court/party noise together at zero litigation cost, so its effect cannot be assigned to a single component.</p>'
    body+=image('robustness-effects','Median and 90th-percentile absolute effects over exactly matched calibration pairs; the tails are descriptive, not confidence intervals')
    body+='</section><section><h2>Where does equilibrium choice limit interpretation?</h2><p>A policy direction is identified by an observed range only when every observed outcome under the comparison policy lies above, or below, every observed outcome under its reference policy. Overlap means the ten starts do not identify that direction without an equilibrium-selection assumption. It does not prove the policy has no effect.</p>'
    body+=image('policy-interpretability','Share of exactly matched social-cost contrasts whose observed ten-start ranges do not overlap')
    body+='<a class="button" href="variability.html">Explore outcome variability</a><a class="button" href="scientific-figure-atlas.pdf">Download complete figure atlas</a></section>'
    (out/'index.html').write_text(render_page('Does the overall story stay the same?',body,'index.html'),encoding='utf-8')
    for method in METHODS:
        body=f'<p class="lede">{esc(LABELS[method])}: the same article settings, outcome definitions and default comparative-static slices.</p>'
        if method=='published':body+='<p>The original CSV labels the one published equilibrium in each setting “Only Eq.” These printed values are used directly. Unrounded replay of the saved policy is available separately in the downloadable data and is not silently substituted.</p>'
        else:
            n=summary['Numerical'][method]
            body+=f'<p>{n["At1eMinus7"]:,} of 2,268 pooled outputs satisfy the frozen 10⁻⁷ CCE threshold; {n["At1eMinus6"]:,} satisfy 10⁻⁶. Product marginals pass the separate Nash check in {n["NashAt1eMinus7"]:,} cases at 10⁻⁷ and {n["NashAt1eMinus5"]:,} at 10⁻⁵. Correlated joint outcomes and product-marginal outcomes are distinct objects.</p>'
            body+='<p>Weights are reselected on a shared union of policies from ten independent starts and documented same-seed repairs. The pooled outcome is not an average of the starts. These are approximate numerical selector outputs; exact objective optimality is a separate, unresolved numerical-quality question.</p>'
        body+='<section><h2>Cost composition</h2><p>Each bar reconciles precaution and foregone-activity cost, harm, and litigation expenditures. Corresponding cost rows use the same scale across all three report versions.</p>'
        for parameter in CORE:body+=image(method+'-'+SLUGS[parameter]+'-components',LABELS[method]+': '+parameter)
        body+='</section><section><h2>Litigation rates and the denominator</h2><p>Unconditional probabilities refer to all activity opportunities. Conditional probabilities divide by the probability of a dispute, including wrongly attributed accidents. Answering here is conditional on a dispute, not conditional on filing.</p>'
        for parameter in CORE:
            body+=f'<h3>{esc(parameter)}</h3>'
            for denom in ('unconditional','given-dispute'):body+=image(SLUGS[parameter]+'-rn-'+denom,'Three-method comparison: '+parameter+', '+denom.replace('-',' '))
        body+='</section><section><h2>Risk-averse comparison</h2>'
        for parameter in CORE:body+=image(SLUGS[parameter]+'-mra-comparison',parameter+': moderately risk-averse calibration')
        body+='</section><a class="button" href="scientific-figure-atlas.pdf">Full scientific atlas, including risk-averse litigation rates</a>'
        (out/(method+'.html')).write_text(render_page(LABELS[method],body,method+'.html'),encoding='utf-8')
    body='<p class="lede">Stable total cost can coexist with unstable precaution, trial or settlement. The range tables keep those outcomes separate.</p><section><h2>Typical observed ranges</h2>'
    rows=[]
    for metric in ('SocialWelfareLoss','HarmCost','LitigationExpenditures','PrecautionLevel','Engagement','TrialGivenDispute','SettlementGivenDispute'):
        vals=[next(r for r in summary['Variability'] if r['Metric']==metric and r['Selector']==s) for s in ('gini','liu')]
        kind='pct' if metric in ('SocialWelfareLoss','HarmCost','LitigationExpenditures') else 'pp' if metric in ('Engagement','TrialGivenDispute','SettlementGivenDispute') else 'number'
        field='RelativeWidth' if kind=='pct' else 'Width'
        rows.append([esc(metric)]+[fmt(v['Median'+field],kind) for v in vals]+[fmt(v['P90'+field],kind) for v in vals])
    body+=table(['Outcome','Gini median range','Liu median range','Gini 90th-percentile range','Liu 90th-percentile range'],rows)
    body+='<p class="note">Cost ranges are percentages of each setting’s ten-start median. Probability ranges use percentage points. Relative ranges are unavailable when the median is zero; an unavailable denominator is not treated as zero variability.</p>'
    body+=image('initialization-variability','Cumulative distribution of observed ten-start outcome ranges')+'</section><section><h2>Inspect individual settings</h2>'
    options=''.join(f'<option value="{m}"'+(' selected' if m=='SocialWelfareLoss' else '')+f'>{esc(m)}</option>' for m in MEASURES)
    body+=f'<label>Outcome<select id="metric" aria-label="Outcome">{options}</select></label><label>Search settings<input id="search" type="search" placeholder="e.g. Risk Neutral CM 4"></label><label>Order<select id="sort" aria-label="Order"><option value="width">Largest absolute range</option><option value="name">Setting name</option></select></label><p id="count" class="note"></p>'
    body+='<div id="explorer" class="table-wrap"><table><thead><tr><th>Setting</th><th>Draft</th><th>Gini pooled</th><th>Liu pooled</th><th>Gini ten-start range</th><th>Liu ten-start range</th></tr></thead><tbody></tbody></table></div><button data-export="case-outcomes">Download all outcomes and ranges CSV</button></section><section><h2>How to read variability</h2><p>The observed spread can reflect equilibrium selection, population discovery and numerical approximation; it does not by itself establish multiple exact equilibria. The ten repetitions sample initialization sensitivity within each numerical method. They are not an exhaustive enumeration of equilibria or a random sample with a known population distribution. Narrow observed ranges support stability among the runs examined; they do not establish uniqueness. Wide or overlapping ranges weaken a claim that the legal parameter alone explains a particular outcome. The difference between Gini and Liu is a separate selection-rule sensitivity.</p></section>'
    (out/'variability.html').write_text(render_page('Where are outcomes stable or variable?',body,'variability.html'),encoding='utf-8')
    body='<section><h2>Sources and methods</h2><p>The primary comparator is the published PREC010 CSV. The saved current-policy replay is stored separately, including discrepancies beyond printed precision. All parameter pairs hold every other recorded setting fixed; absent combinations are not imputed.</p><p>The new solutions are normal-form approximate coarse correlated equilibria over complete policies. Gini and economic-affinity relative entropy select joint weights using different objectives on the same final policy population. The Liu-affinity reference uses five fixed features: normalized plaintiff and defendant utility, filing, unconditional answering, and trial. It is not a Nash-selection theorem.</p><p>All 45,360 independent-start outputs have accepted 10⁻⁷ CCE results, including 55 paired same-seed repair cases covering 58 originally unverified selections. Repairs preserve original attempts and use unchanged tolerance and inner settings, with up to 300 additional outer iterations per initial repair attempt. Some include within-seed policy sharing or a documented deterministic reordering. Pooled recoveries are separate and do not add independent repetitions.</p><p>The frozen full-game target is 10⁻⁷. One Liu candidate at 3.12848×10⁻⁷ is included transparently as a 10⁻⁶ approximation; its source status remains MetaSolverLimit. The report does not certify it as passing the original target. All original settings and records are preserved. A completed exhaustive audit covers 2,265 cases; three later case outputs require separate coverage. No report claim should be read as final certification of all outputs or of economically meaningful approximation accuracy.</p><p>Order sensitivity and optimizer stopping diagnostics also limit claims about exact selector-objective optimality, even for outputs with valid CCE certificates. Independent CCE checks certify deviation gains; product-Nash checks and objective-optimality checks answer different questions.</p></section><section><h2>Downloads</h2><a class="button" href="scientific-figure-atlas.pdf">Scientific figure atlas PDF</a><a class="button" href="report-data.json.gz">Full report data JSON.gz</a><a class="button" href="comparison-statistics.json.gz">Matched comparisons JSON.gz</a><button data-export="case-outcomes">Outcomes and ranges CSV</button><button data-export="matched-summary">Matched comparison summary CSV</button><button data-export="numerical-checks">CCE and Nash checks CSV</button><p>Individual vector figures are linked below each chart. CSV downloads are generated from the same typed report data shown in the tables.</p></section>'
    if economic_review:
        body+='<section id="economic-accuracy"><h2>Economic accuracy and foregone activity</h2><p>Resource accounting reconciles for all 4,536 pooled rows: foregone activity benefit plus precaution, physical harm and litigation expenditure equals reported social cost, with maximum discrepancy below 6.2e-15. With activity benefit beta and engagement probability p, loss equals beta(1-p) plus the other resource costs. Net surplus equals beta minus that loss.</p><p>The remaining concern is approximation accuracy. Normalized gains divide utility gains by the entire terminal-utility range. Extreme costly tails under CARA utility inflate that range. A formal 1e-7 certificate can therefore permit an economically substantial profitable deviation. In nine high-cost moderately risk-averse baseline settings (fee shifting 2, damages 2), the 18 Gini/Liu defendant certainty-equivalent gains range from 0.000349 to 0.001234 against an activity benefit of 0.001. These are gains from the best unilateral policy deviation, not necessarily from changing participation alone. They do not prove that the observed participation reductions are artifacts, but they prevent treating those reductions as established equilibrium predictions.</p><p>Certainty-equivalent gains explicitly invert the affine-transformed CARA utility and use independent audit values. They measure the economic size of incentives; they are not bounds on welfare error. In cost-misestimation variants, they concern perceived wealth. Formal CCE/Nash classifications and all original outputs remain unchanged. Agreement across Gini, Liu and repeated starts is not independent evidence against a shared scaling limitation. Risk-neutral pooled defendant CCE gains in this review are all below 0.1% of the activity benefit, so the issue is uneven across the grid. Initial-start economic accuracy still needs separate review.</p><p>Activity assumptions also matter: a common binary activity benefit, no heterogeneity in activity value, no activity-intensity margin, private benefit equated with social benefit, and resource-cost welfare despite risk-averse decisions. The benefit must mean net value relative to the best alternative, before modeled care and litigation costs. Its calibration (0.001 versus maximum expected physical harm of 0.0001 before precautions) makes productive activity valuable by assumption. Benefit, heterogeneity and risk/insurance sensitivity should follow an economically meaningful numerical-accuracy check.</p><p>Coordinated fee shifting and aggressive settlement-offer/betting mechanisms are reserved for a separate paper. The present report evaluates the existing paper on its own terms.</p><a class="button" href="economic-accuracy-review.json.gz">Download economic accuracy audit</a></section>'
    body+='<section><h2>Data identities</h2>'+table(['Source','SHA256'],[[esc(k),'<span class="code">'+esc(data['Provenance'][k])+'</span>'] for k in ('CatalogSha256','PublishedCsvSha256','OriginalValidationSha256','InitialReplacementIndexSha256','PooledReplacementIndexSha256')])+'</section>'
    (out/'methods.html').write_text(render_page('Methods, qualifications and downloads',body,'methods.html'),encoding='utf-8')
    explorer=[];case_rows=[];numerical=[]
    for c in data['Cases']:
        explorer.append({'Id':c['Id'],'Name':c['OptionSetName'],'Family':c['Family'],'Candidate':c['ProvisionalCandidate'],
            'published':c['Published'],**{s:{'Pooled':c['Selectors'][s]['Pooled'],'Ranges':c['Selectors'][s]['InitializationRanges']} for s in ('gini','liu')}})
        for metric in MEASURES:
            case_rows.append({'CaseId':c['Id'],'Setting':c['OptionSetName'],'Outcome':metric,'Published':c['Published'][metric],
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
    table_data=json.dumps({'case-outcomes':case_rows,'matched-summary':summary['MatchedSummary'],'numerical-checks':numerical},separators=(',',':'),allow_nan=False).encode()
    (out/'tables.json.gz').write_bytes(gzip.compress(table_data,compresslevel=6,mtime=0))
    script=(out/'report.js').read_text(encoding='utf-8')
    script=script.replace("const data=await(await fetch('tables.json')).json();", "const response=await fetch('tables.json.gz');const data=await new Response(response.body.pipeThrough(new DecompressionStream('gzip'))).json();")
    script=script.replace("const rows=await(await fetch('explorer.json')).json();", "const response=await fetch('explorer.json.gz');const rows=await new Response(response.body.pipeThrough(new DecompressionStream('gzip'))).json();")
    (out/'report.js').write_text(script,encoding='utf-8')


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--data',required=True);parser.add_argument('--statistics',required=True)
    parser.add_argument('--output',required=True);parser.add_argument('--findings',required=True);parser.add_argument('--html-only',action='store_true');parser.add_argument('--economic-review')
    args=parser.parse_args();data=json.loads(Path(args.data).read_text());summary=json.loads(Path(args.statistics).read_text());out=Path(args.output);out.mkdir(exist_ok=True,parents=True)
    if args.html_only:inventory=json.loads((out/'figure-inventory.json').read_text())
    else:
        inventory=figures(data,summary,out);(out/'figure-inventory.json').write_text(json.dumps(inventory,indent=2))
    economic_review=json.loads(Path(args.economic_review).read_text()) if args.economic_review else None
    if economic_review:
        assert economic_review['Cases']==len(data['Cases']) and not economic_review['Errors']
        (out/'economic-accuracy-review.json.gz').write_bytes(gzip.compress(Path(args.economic_review).read_bytes(),compresslevel=6,mtime=0))
    html_reports(data,summary,out,inventory,json.loads(Path(args.findings).read_text()),economic_review)
    for source,name in [(args.data,'report-data.json.gz'),(args.statistics,'comparison-statistics.json.gz')]:
        (out/name).write_bytes(gzip.compress(Path(source).read_bytes(),compresslevel=6,mtime=0))
    print(json.dumps({'Pages':6,'Figures':len(inventory),'Output':str(out.resolve())}))


if __name__=='__main__':main()
