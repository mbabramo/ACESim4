"""Render the reviewed traditional-search analysis without editing the article."""
import argparse
import gzip
import html
import json
from pathlib import Path
import shutil
import statistics as stats
import textwrap

import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
import numpy as np

from endogenous_traditional_analysis import read, save, sha, accepted, distribution, COMPONENTS, METRICS, LEGAL
from render_endogenous_story import BASE

LABELS={'SocialWelfareLoss':'Total social cost','ForegoneActivityCost':'Forgone activity',
    'PrecautionCost':'Precaution spending','HarmCost':'Accident losses','LitigationExpenditures':'Litigation spending',
    'PrecautionLevel':'Care level','Engagement':'Activity participation','DisputeProbability':'Dispute probability',
    'Filing':'Filing per opportunity','AnsweringUnconditional':'Answering per opportunity','Trial':'Trial per opportunity',
    'Settlement':'Settlement per opportunity','FilingGivenDispute':'Filing given a dispute','AnsweringGivenDispute':'Answering given a dispute',
    'TrialGivenDispute':'Trial given a dispute','SettlementGivenDispute':'Settlement given a dispute'}
RULES={'Fee Shifting Multiplier':'Fee reimbursement','Damages Multiplier':'Damages multiplier','Liability Threshold':'Liability threshold'}
COLORS=['#af6988','#78a18a','#d7ad64','#6793ac']


def default_cases(data,risk,cost,parameter=None):
    target=dict(BASE,**{'Risk Aversion':risk,'Costs Multiplier':cost})
    return sorted([c for c in data['Cases'] if all(c['Parameters'][k]==v for k,v in target.items() if k!=parameter)],key=lambda c:float(c['Parameters'][parameter]) if parameter else c['Id'])


def write_fig(fig,path):
    fig.savefig(str(path)+'.png',dpi=165,bbox_inches='tight');fig.savefig(str(path)+'.svg',bbox_inches='tight');plt.close(fig)


def bar_composition(data,out,risk):
    fig,axes=plt.subplots(3,1,figsize=(12,10.5));fig.suptitle(f'Cost breakdowns: {risk}\nAll other assumptions at defaults; only fee reimbursement changes',fontsize=15)
    for ax,cost in zip(axes,('0.25','1','4')):
        cases=[c for c in default_cases(data,risk,cost,LEGAL[0]) if c['Parameters'][LEGAL[0]] in ('0','1','2')];x=0;xt=[];labels=[]
        for c in cases:
            values=[c['Published'],{m:stats.mean(r['Values'][m] for r in c['Traditional']) for m in METRICS},c['Selectors']['gini']['Pooled'],c['Selectors']['liu']['Pooled']]
            for label,v in zip(('Draft','Traditional\nmean','Gini\npooled','Liu\npooled'),values):
                bottom=0
                for key,color in zip(COMPONENTS,COLORS):
                    h=v[key]*1e4 if v[key] is not None else 0;ax.bar(x,h,bottom=bottom,color=color,width=.72);bottom+=h
                if label.startswith('Traditional'):
                    yy=[r['Values']['SocialWelfareLoss']*1e4 for r in c['Traditional']]
                    ax.scatter(x+np.linspace(-.24,.24,10),yy,c=['#154c45' if accepted(r,'strict-money') else '#6d6d6d' for r in c['Traditional']],s=12,zorder=5)
                    ax.errorbar(x,stats.mean(yy),yerr=stats.stdev(yy),fmt='none',ecolor='#172929',capsize=3,lw=1)
                xt.append(x);labels.append(label);x+=1
            ax.text(x-2.5,-.25,f'Reimbursement {c["Parameters"][LEGAL[0]]}',transform=ax.get_xaxis_transform(),ha='center',fontsize=9);x+=.8
        ax.set(xticks=xt,xticklabels=labels,ylabel='Cost per 10,000 opportunities',title=f'Litigation expense = {cost} × baseline')
        ax.tick_params(axis='x',labelsize=8);ax.grid(axis='y',alpha=.2)
    handles=[plt.Rectangle((0,0),1,1,color=c,label=LABELS[k]) for c,k in zip(COLORS,COMPONENTS)]
    fig.legend(handles=handles,loc='lower center',ncol=4,fontsize=9,bbox_to_anchor=(.5,.005));fig.subplots_adjust(hspace=.8,bottom=.1,top=.9)
    write_fig(fig,out/('composition-rn' if risk=='Risk Neutral' else 'composition-ra'))


def component_spread(data,out):
    cases=default_cases(data,'Risk Neutral','4',LEGAL[0]);fig,axes=plt.subplots(2,2,figsize=(12,8))
    for ax,key,color in zip(axes.flat,COMPONENTS,COLORS):
        for j,c in enumerate(cases):
            yy=[r['Values'][key]*1e4 for r in c['Traditional']]
            ax.scatter(j+np.linspace(-.12,.12,10),yy,color=['#154c45' if accepted(r,'strict-money') else '#888' for r in c['Traditional']],s=21)
            ax.errorbar(j,stats.mean(yy),yerr=stats.stdev(yy),fmt='o',color='#111',capsize=4,label='Traditional mean ± search SD' if j==0 else None)
            for offset,v,marker,cc,name in [(-.23,c['Published'],'x','#333','Draft'),(.23,c['Selectors']['gini']['Pooled'],'s','#007f86','Gini pooled'),(.33,c['Selectors']['liu']['Pooled'],'D','#c46825','Liu pooled')]:
                if v[key] is not None:ax.scatter(j+offset,v[key]*1e4,marker=marker,c=cc,s=38,label=name if j==0 else None)
        ax.set(title=LABELS[key],xticks=range(len(cases)),xticklabels=[c['Parameters'][LEGAL[0]] for c in cases],xlabel='Fee reimbursement multiplier',ylabel='Cost per 10,000 opportunities');ax.grid(alpha=.2)
    axes[0,0].legend(fontsize=8);fig.suptitle('Are the components themselves stable?\nRisk neutral; expense 4 × baseline; every other assumption fixed',fontsize=14)
    fig.tight_layout(rect=(0,0,1,.92));write_fig(fig,out/'component-spread')


def rule_curves(data,out):
    fig,axes=plt.subplots(2,3,figsize=(14,8))
    for row,cost in enumerate(('1','4')):
        for ax,param in zip(axes[row],LEGAL):
            cases=default_cases(data,'Risk Neutral',cost,param);xx=np.arange(len(cases));values=np.array([[r['Values']['SocialWelfareLoss']*1e4 for r in c['Traditional']] for c in cases])
            ax.plot(xx,values,color='#bababa',alpha=.5,lw=.65)
            ax.errorbar(xx,values.mean(axis=1),yerr=values.std(axis=1,ddof=1),color='#1c554b',marker='o',capsize=3,label='Traditional mean ± search SD')
            ax.plot(xx,[c['Published']['SocialWelfareLoss']*1e4 for c in cases],color='#252525',ls='--',label='Draft')
            for m,color in [('gini','#007f86'),('liu','#c46825')]:ax.plot(xx,[c['Selectors'][m]['Pooled']['SocialWelfareLoss']*1e4 for c in cases],color=color,ls=':',marker='s' if m=='gini' else 'D',ms=4,label=f'{m.title()} pooled')
            ax.set(xticks=xx,xticklabels=[c['Parameters'][param] for c in cases],title=f'{RULES[param]}; expense {cost} ×',ylabel='Cost per 10,000 opportunities');ax.grid(alpha=.2)
    axes[0,0].legend(fontsize=7);fig.suptitle('Which rule rankings survive ten traditional starts?\nRisk neutral; every other assumption at its default. Gray lines connect the same search seeds.',fontsize=14)
    fig.tight_layout(rect=(0,0,1,.91));write_fig(fig,out/'rule-curves')


def policy_effects(data,out):
    examples=[(LEGAL[0],'0','2'),(LEGAL[1],'0.5','2'),(LEGAL[2],'0','1')]
    fig,axes=plt.subplots(3,3,figsize=(13,10))
    for row,cost in enumerate(('0.25','1','4')):
        for ax,(param,start,end) in zip(axes[row],examples):
            cs=default_cases(data,'Risk Neutral',cost,param);a=next(c for c in cs if c['Parameters'][param]==start);b=next(c for c in cs if c['Parameters'][param]==end)
            delta=[(y['Values']['SocialWelfareLoss']-x['Values']['SocialWelfareLoss'])*1e4 for x,y in zip(a['Traditional'],b['Traditional'])]
            ax.scatter(1+np.linspace(-.15,.15,10),delta,c=['#154c45' if accepted(x,'strict-money') and accepted(y,'strict-money') else '#999' for x,y in zip(a['Traditional'],b['Traditional'])],s=22)
            ax.errorbar(1,stats.mean(delta),yerr=stats.stdev(delta),fmt='o',color='#172929',capsize=4)
            ax.scatter(0,(b['Published']['SocialWelfareLoss']-a['Published']['SocialWelfareLoss'])*1e4,c='#333',marker='x',s=50)
            for x,m,color in [(2,'gini','#007f86'),(3,'liu','#c46825')]:ax.scatter(x,(b['Selectors'][m]['Pooled']['SocialWelfareLoss']-a['Selectors'][m]['Pooled']['SocialWelfareLoss'])*1e4,c=color,marker='s' if x==2 else 'D',s=32)
            ax.axhline(0,color='#777',lw=.7);ax.set(xticks=range(4),xticklabels=['Draft','Traditional','Gini\npooled','Liu\npooled'],ylabel='B − A cost per 10,000',title=f'{RULES[param]} {start} → {end}\nExpense {cost} ×');ax.tick_params(axis='x',labelsize=8);ax.grid(axis='y',alpha=.15)
    fig.suptitle('Specific policy changes: effect size versus search variation\nRisk neutral; all other parameters fixed. Negative means B is cheaper. Green paired dots pass both strict checks.',fontsize=14)
    fig.tight_layout(rect=(0,0,1,.91));write_fig(fig,out/'policy-effects')


def ratios(data,out):
    fig,axes=plt.subplots(1,2,figsize=(12,5))
    for ax,risk in zip(axes,('Risk Neutral','Moderately Risk Averse')):
        for j,cost in enumerate(('0.25','1','4')):
            c=default_cases(data,risk,cost)[0];den=c['Reference'][1];v=[r['Values']['SocialWelfareLoss']/den for r in c['Traditional']]
            ax.scatter(j+np.linspace(-.1,.1,10),v,color=['#154c45' if accepted(r,'strict-money') else '#999' for r in c['Traditional']],s=20)
            ax.errorbar(j,stats.mean(v),yerr=stats.stdev(v),fmt='o',color='#172929',capsize=4,label='Traditional mean ± search SD' if j==0 else None)
            for offset,value,name,color,marker in [(-.22,c['Published']['SocialWelfareLoss'],'Draft','#333','x'),(.22,c['Selectors']['gini']['Pooled']['SocialWelfareLoss'],'Gini pooled','#007f86','s'),(.33,c['Selectors']['liu']['Pooled']['SocialWelfareLoss'],'Liu pooled','#c46825','D')]:
                ax.scatter(j+offset,value/den,label=name if j==0 else None,c=color,marker=marker,s=35)
        ax.axhline(1,color='#888',ls='--',lw=.7);ax.set(title=risk,xticks=range(3),xticklabels=['0.25','1','4'],xlabel='Litigation expense multiplier',ylabel='Total social cost / ideal-reference cost');ax.grid(alpha=.2)
    axes[0].legend(fontsize=8);fig.suptitle('Add the traditional searches without changing the earlier benchmark\nDefault settings; fixed prior ideal-reference denominator. Risk-averse economic accuracy remains qualified.',fontsize=13)
    fig.tight_layout(rect=(0,0,1,.87));write_fig(fig,out/'ratios')


def quality_and_spread(data,out):
    fig,axes=plt.subplots(1,2,figsize=(12,5));colors=['#1c554b','#007f86','#c46825']
    for ax,risk in zip(axes,('Risk Neutral','Moderately Risk Averse')):
        for offset,method,color in zip((-.22,0,.22),('traditional','gini','liu'),colors):
            vv=[]
            for cost in ('0.25','1','4'):
                cases=[c for c in data['Cases'] if c['Parameters']['Risk Aversion']==risk and c['Parameters']['Costs Multiplier']==cost]
                vals=[]
                for c in cases:
                    obs=[r['Values']['SocialWelfareLoss'] for r in c['Traditional']] if method=='traditional' else [r['SocialWelfareLoss'] for r in c['Selectors'][method]['Initial']]
                    vals.append(100*stats.stdev(obs)/stats.mean(obs))
                vv.append(vals)
            bp=ax.boxplot(vv,positions=np.arange(3)+offset,widths=.18,patch_artist=True,showfliers=False,manage_ticks=False)
            for box in bp['boxes']:box.set(facecolor=color,alpha=.6)
            ax.plot([],[],color=color,lw=6,label=method.title())
        ax.set(title=risk,xticks=range(3),xticklabels=['0.25','1','4'],xlabel='Litigation expense multiplier',ylabel='Within-setting SD / mean total cost (%)');ax.grid(axis='y',alpha=.2)
    axes[0].legend(fontsize=8);fig.suptitle('How much do ten searches differ within the SAME setting?\nBoxes summarize setting-level SDs across the available grid; they are not confidence intervals. All candidates included.',fontsize=13)
    fig.tight_layout(rect=(0,0,1,.87));write_fig(fig,out/'variation')


def participation_exception(data,out):
    c=next(c for c in data['Cases'] if c['Id']=='endogenous-139f17cd30143a8b')
    fig,axes=plt.subplots(1,2,figsize=(12,5));ax=axes[0];bottom=np.zeros(10)
    for key,color in zip(COMPONENTS,COLORS):
        yy=np.array([r['Values'][key]*1e4 for r in c['Traditional']]);ax.bar(np.arange(10)+1,yy,bottom=bottom,color=color,label=LABELS[key]);bottom+=yy
    ax.axhline(c['Published']['SocialWelfareLoss']*1e4,color='#222',ls='--',label='Draft total')
    ax.set(xlabel='Independent search number (not equilibrium probability)',ylabel='Cost per 10,000 opportunities',title='All ten pass strict Nash + monetary screens',xticks=range(1,11));ax.legend(fontsize=7)
    ax=axes[1];ax.scatter([r['Values']['Engagement']*100 for r in c['Traditional']],[r['Values']['SocialWelfareLoss']*1e4 for r in c['Traditional']],s=60,c='#1c554b')
    ax.set(xlabel='Participation (%)',ylabel='Cost per 10,000 opportunities',title='Most of the difference is forgone activity');ax.grid(alpha=.2)
    fig.suptitle('A localized exception: two well-screened outcome clusters\nModerate risk aversion; expense 1 ×; unit precaution cost 0.000001; damages 2; other assumptions at defaults',fontsize=13)
    fig.tight_layout(rect=(0,0,1,.87));write_fig(fig,out/'participation-exception')


def component_offsets(data,out):
    fig,axes=plt.subplots(1,2,figsize=(12,5))
    for ax,cost in zip(axes,('0.25','4')):
        cases=default_cases(data,'Risk Neutral',cost,LEGAL[1]);a=next(c for c in cases if c['Parameters'][LEGAL[1]]=='0.5');b=next(c for c in cases if c['Parameters'][LEGAL[1]]=='2')
        keys=COMPONENTS+('SocialWelfareLoss',);yy=[];ss=[]
        for key in keys:
            v=[(y['Values'][key]-x['Values'][key])*1e4 for x,y in zip(a['Traditional'],b['Traditional'])];yy.append(stats.mean(v));ss.append(stats.stdev(v))
        ax.bar(range(5),yy,color=COLORS+['#263735'],yerr=ss,capsize=3)
        ax.axhline(0,color='#777',lw=.8);ax.set(xticks=range(5),xticklabels=['Forgone\nactivity','Precaution','Accident\nlosses','Litigation','Net total'],ylabel='B − A cost per 10,000 opportunities',title=f'Expense {cost} × baseline');ax.tick_params(axis='x',labelsize=8);ax.grid(axis='y',alpha=.2)
    fig.suptitle('The buffering mechanism and an important exception\nDamages 0.5 → 2; default risk-neutral background; bars = ten-search mean change, whiskers = search SD',fontsize=13)
    fig.tight_layout(rect=(0,0,1,.87));write_fig(fig,out/'component-offsets')


def esc(s):return html.escape(str(s))


def render(analysis,findings,site,html_only=False):
    analysis=Path(analysis);site=Path(site);data=read(analysis/'render-data.json.gz' if (analysis/'render-data.json.gz').exists() else analysis/'analysis.json.gz');notes=read(findings);out=site/'traditional-review';out.mkdir(exist_ok=True)
    if not html_only:
        for func in (component_spread,rule_curves,policy_effects,ratios,quality_and_spread,participation_exception,component_offsets):func(data,out)
        for risk in ('Risk Neutral','Moderately Risk Averse'):bar_composition(data,out,risk)
    else:
        for name in ('component-spread','rule-curves','policy-effects','ratios','variation','participation-exception','component-offsets','composition-rn','composition-ra'):
            if not (out/(name+'.png')).exists() or not (out/(name+'.svg')).exists():raise ValueError('HTML-only render requires existing figures')
    # Browser data omits the large precomputed effect table; selected fixed
    # backgrounds and screens are computed directly from the saved observations.
    compact=dict(Cases=[dict(Id=c['Id'],Name=c['Name'],Parameters=c['Parameters'],Published=c['Published'],Reference=c['Reference'],
        Traditional=[{k:r[k] for k in ('Repetition','Seed','Values','NashAtTarget','NashAt1eMinus5','EconomicScreenPassed')} for r in c['Traditional']],
        Selectors={m:dict(Pooled=c['Selectors'][m]['Pooled']) for m in ('gini','liu')}) for c in data['Cases']],
                 Definitions=data['Definitions'],Labels=LABELS,Base=BASE)
    browser=out/'browser-data.json.gz'
    if browser.exists():browser.unlink()
    save(browser,compact)
    for name in ('setting-summaries.csv.gz','search-outcomes.csv.gz','paired-policy-effects.csv.gz','transition-summaries.csv.gz','overview.json','conclusion-diagnostics.json','review-diagnostics.json'):
        shutil.copy2(analysis/name,out/name)
    shutil.copy2(Path(__file__).with_name('endogenous_traditional_review.js'),out/'explorer.js')
    audit=Path(notes['AuditPath'])
    if audit.exists():shutil.copy2(audit,out/'independent-audit.json')
    else:shutil.copy2(audit.parent/'request.json',out/'independent-audit-request.json')
    def figure(name,caption):return f'<figure><a href="traditional-review/{name}.svg"><img loading="lazy" src="traditional-review/{name}.png" alt="{esc(caption)}"></a><figcaption>{esc(caption)} · <a href="traditional-review/{name}.svg">Scalable figure</a></figcaption></figure>'
    narrative=''.join(f'<article class="finding"><h3>{esc(n["Title"])}</h3><p>{esc(n["Text"])}</p></article>' for n in notes['Findings'])
    plan=''.join(f'<li><strong>{esc(n["Title"])}</strong> {esc(n["Text"])}</li>' for n in notes['Plan'])
    body=f'''<p class="lede">The draft used one saved outcome for each setting. We have now run ten independent traditional searches per setting, starting from different strategies. Here we ask which conclusions survive, where selecting an outcome matters, and what to show in the paper’s charts. The article itself has not been edited.</p>
    <section><h2>What is being compared?</h2><p><strong>Draft:</strong> the directly published number. <strong>Traditional:</strong> ten independent searches for approximately Nash behavior, using the original method. <strong>Gini / Liu pooled:</strong> separately selected outcomes after combining the strategies discovered in the earlier searches. Those methods allow correlation between the players’ complete plans, so they are robustness comparisons with a different equilibrium concept, not additional traditional equilibria.</p><p>Each number is the model’s expected cost per opportunity to engage in the activity. Charts multiply this by 10,000 for legibility; these are model units, not observed dollars or a sample of 10,000 lawsuits. Costs include lost activity benefits, care, accident losses and litigation resources. Transfers between the parties are excluded.</p></section>
    <section><h2>What the analysis says</h2>{narrative}</section>
    <section><h2>Accuracy comes before equilibrium interpretation</h2><p>All 22,680 searches finished, but only 15,962 pass normalized Nash at 10⁻⁷; 22,592 pass at 10⁻⁵. The separate money-equivalent incentive screen passes 17,181. A total of 14,836 pass both the stricter Nash and monetary screens. These thresholds do not bound the error in social cost.</p><p>Green dots pass both strict checks. Gray dots are saved candidates that do not. All ten remain visible. The explorer can show screened subsets, with explicit counts; a subset with fewer observations is not a replacement ten-equilibrium sample. Standard deviations describe search variation, not statistical confidence. Distinct floating-point profiles do not prove distinct exact equilibria.</p><p>{esc(notes['AuditText'])} The earlier Gini/Liu economic and objective-accuracy qualifications remain, including the one Liu candidate missing 10⁻⁷. Agreement between methods does not resolve a shared numerical limitation.</p></section>
    {figure('rule-curves','Exactly controlled rule comparisons. Traditional means and one search SD accompany the draft and each pooled selection; thin gray lines show the ten seed-paired traditional results. All candidates are included.')}
    {figure('policy-effects','Nine specific changes with every other assumption fixed. A large mean compared with its search SD is more selection-stable than a small mean with mixed directions. This is not a significance test.')}
    {figure('component-offsets','Care and harm changes largely offset in the cheap-litigation example. With expensive litigation, litigation savings make the same damages change much more consequential. Eight of ten cheap-expense pairs and all ten high-expense pairs pass both strict screens; bars retain all ten candidates.')}
    <section><h2>Stable totals can conceal unstable cost breakdowns</h2><p>A stacked bar shows a mean breakdown, not a typical individual equilibrium. Component means add to the mean total. Dots above traditional bars show the ten total costs; the whisker is their sample SD. To assess whether the components themselves are stable, use the companion component chart. The risk-averse chart remains economically qualified.</p></section>
    {figure('composition-rn','Risk-neutral cost breakdowns at default assumptions. Within each expense panel, only fee reimbursement changes: 0 = none; 1 = full; 2 = double. Traditional bars average ten searches; other bars are separate reference outcomes.')}
    {figure('component-spread','Each cost component is shown separately for the same default high-expense risk-neutral comparison. A stable total does not establish that its parts are stable. All component scales are in model costs per 10,000 opportunities.')}
    <details><summary>Show the economically qualified risk-averse cost breakdowns</summary>{figure('composition-ra','Moderately risk-averse counterparts. Several results do not meet economically meaningful incentive accuracy; do not treat this as validated substantive evidence.')}</details>
    {figure('variation','Each observation underlying a box is the within-setting SD of ten total costs divided by their mean. Different background settings contribute separate SDs; no error bar mixes between-setting heterogeneity with within-setting variation.')}
    <section><h2>Where a single average would conceal an important difference</h2><p>The following setting has ten candidates passing both strict screens, yet two substantially different outcome clusters. Eight searches reach one cluster and two reach the other. Those counts describe this initialization procedure; they are not probabilities that society selects an equilibrium. The money-equivalent remaining incentives are less than three millionths of the activity benefit here, much smaller than in the previously flagged high-cost risk-averse examples. Approximate verification still does not prove exact multiplicity.</p><p>The difference comes mainly from the decision to participate in the activity, rather than from a large change in precaution spending. The model assumes the same net benefit of activity for everyone and no intermediate intensity or substitution. Those assumptions therefore deserve attention before treating the participation mechanism as an empirical claim.</p></section>
    {figure('participation-exception','Ten separately shown traditional-search breakdowns in one fixed setting. A mean bar would lie between the two observed cost clusters. Foregone activity uses the model’s net benefit of 0.001 per opportunity; this is not a measured lost income estimate.')}
    {figure('ratios','The earlier noiseless, perfect and costless adjudication reference is held fixed. Ratio 1 means its cost; the reference is a model benchmark, not a certified optimum. Missing exact matches are never borrowed.')}
    <section id="explorer"><h2>Inspect one controlled background</h2><p>Choose the rule to vary, risk attitude and litigation expense, then one combination of every other assumption. The table shows every available value of that rule for that exact background. Changing screens may remove observations and alter which backgrounds have usable comparisons.</p><div class="controls"><label>Rule<select id="tr-rule"></select></label><label>Risk<select id="tr-risk"></select></label><label>Litigation expense<select id="tr-cost"></select></label><label>Accuracy screen<select id="tr-screen"><option value="all">All ten saved candidates</option><option value="money">Monetary screen</option><option value="loose-money">Nash 10⁻⁵ + monetary</option><option value="strict-money">Nash 10⁻⁷ + monetary</option></select></label><label>Outcome<select id="tr-metric"></select></label></div><label style="display:flex">All other fixed assumptions<select id="tr-background"></select></label><details><summary>Show every fixed assumption</summary><dl id="tr-fixed"></dl></details><p id="tr-status" aria-live="polite">Loading saved observations…</p><div id="tr-chart" role="img" aria-label="Ten traditional outcomes and mean for each rule value"></div><div class="table-wrap"><table><thead><tr><th>Rule value</th><th>Searches</th><th>Traditional mean</th><th>SD</th><th>Observed range</th><th>Draft</th><th>Gini pooled</th><th>Liu pooled</th><th>Mean total / ideal</th></tr></thead><tbody id="tr-rows"></tbody></table></div><h3>Compare two specific values</h3><label>From A<select id="tr-a"></select></label><label>To B<select id="tr-b"></select></label><p id="tr-delta"></p><p class="note">Only identical original seed positions are paired. Under a screen, both endpoints must pass. Relative policy effects use one fixed A denominator. Zero or unavailable denominators stay unavailable. The descriptive same-seed coupling does not make these random samples or confidence intervals.</p></section>
    <section><h2>A plan for the paper, after we review the findings</h2><ol>{plan}</ol><p>Coordinated fee shifting and aggressive settlement offers, including the betting regime, remain a separate paper. The present study can identify the problem such a design might address without claiming that untested combinations solve it.</p></section>
    <section><h2>Downloads and reproducibility</h2><p>All source results remain unchanged. The tables distinguish all-search summaries from each accuracy screen and keep every exact background and endpoint pair separate. The full set of matched permutations varies by transition; no missing comparisons are imputed.</p><a class="button" download href="traditional-review/search-outcomes.csv.gz">22,680 outcomes and accuracy grades (CSV.gz)</a><a class="button" download href="traditional-review/setting-summaries.csv.gz">Setting means, SDs and component summaries (CSV.gz)</a><a class="button" download href="traditional-review/paired-policy-effects.csv.gz">Exact-background paired effects (CSV.gz)</a><a class="button" download href="traditional-review/transition-summaries.csv.gz">Identical-transition background averages (CSV.gz)</a><a class="button" download href="traditional-review/overview.json">Coverage and within-setting summaries (JSON)</a><a class="button" download href="traditional-review/independent-audit.json">Independent audit (JSON)</a><p class="note">Compressed CSVs can be opened after standard GZip decompression. Every plotted figure also has a scalable SVG link. Reference identity and numerical definitions are retained in the analysis files.</p></section>'''
    existing=(site/'methods.html').read_text(encoding='utf-8');nav=existing.split('<nav class="nav"')[1].split('</nav>')[0]
    nav='<nav class="nav"'+nav+'</nav>';nav=nav.replace(' aria-current="page"','')
    if 'href="traditional.html"' in nav:nav=nav.replace('href="traditional.html"','href="traditional.html" aria-current="page"',1)
    else:nav=nav.replace('</nav>','<a href="traditional.html" aria-current="page">Ten traditional searches</a></nav>')
    notice='Traditional searches are complete; scientific interpretation remains accuracy-qualified. The article has not been changed.'
    if not audit.exists():body=body.replace('href="traditional-review/independent-audit.json">Independent audit (JSON)','href="traditional-review/independent-audit-request.json">Independent audit request (audit still running)')
    page=f'<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Ten traditional searches: what survives? | Litigation report</title><link rel="stylesheet" href="report.css"><style>#tr-chart svg{{width:100%;height:auto}}.controls{{display:flex;flex-wrap:wrap;gap:10px}}#tr-background{{width:100%}}details{{padding:15px;background:#eef2ef}}.finding p{{max-width:950px}}ol li{{margin-bottom:18px}}</style></head><body><header><div class="eyebrow">Study review · November 12, 2026</div><h1>Ten traditional searches: what survives?</h1><div class="status">{notice}</div>{nav}</header><main id="main">{body}</main><footer>Descriptive model outcomes, not empirical estimates. Search variation is not statistical confidence. No article edits.</footer><script src="traditional-review/explorer.js?v={sha(browser)[:12]}" defer></script></body></html>'
    (site/'traditional.html').write_text(page,encoding='utf-8')
    for name in ('index.html','published.html','gini.html','liu.html','variability.html','methods.html','benchmarks.html'):
        p=site/name;t=p.read_text(encoding='utf-8')
        if 'href="traditional.html"' not in t:t=t.replace('</nav>','<a href="traditional.html">Ten traditional searches</a></nav>',1)
        if 'id="traditional-update"' not in t:
            notice='<p class="status" id="traditional-update"><strong>November 12 update:</strong> <a href="traditional.html">Ten traditional searches, cost breakdowns and a proposed paper plan are now available.</a> The earlier Gini/Liu pages remain separate robustness comparisons; their differences from the draft should not automatically be attributed to traditional Nash multiplicity.</p>'
            t=t.replace('</header>','</header>'+notice,1)
        p.write_text(t,encoding='utf-8')
    (out/'build-manifest.json').write_text(json.dumps(dict(AnalysisSha256=sha(analysis/'analysis.json.gz'),FindingsSha256=sha(findings),RendererSha256=sha(__file__),BrowserDataSha256=sha(browser))),encoding='utf-8')
    print(json.dumps(dict(Page=str(site/'traditional.html'),Figures=9,BrowserDataBytes=browser.stat().st_size)))


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--analysis',required=True);p.add_argument('--findings',required=True);p.add_argument('--site',required=True);p.add_argument('--html-only',action='store_true');a=p.parse_args();render(a.analysis,a.findings,a.site,a.html_only)
