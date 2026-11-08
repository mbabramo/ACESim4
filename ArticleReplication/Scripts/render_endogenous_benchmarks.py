"""Render the matched ideal-reference analysis without changing numerical evidence."""
import argparse
import gzip
import hashlib
from html import escape
import json
from pathlib import Path
import shutil

import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.backends.backend_pdf import PdfPages
from reportlab.lib import colors
from reportlab.lib.styles import getSampleStyleSheet
from reportlab.lib.units import inch
from reportlab.platypus import SimpleDocTemplate,Paragraph,Spacer,Table,TableStyle,PageBreak
from pypdf import PdfReader,PdfWriter

from endogenous_report_language import parameter_name,setting_name
from render_endogenous_story import BASE,page

COLORS={'gini':'#087c82','liu':'#b76632'}
RISKS=('Risk Neutral','Moderately Risk Averse')
RULES={'Fee Shifting Multiplier':('0','2'),'Damages Multiplier':('0.5','2'),'Liability Threshold':('0.8','1.2')}


def default_background(parameters,omit=()):
    return all(v==BASE.get(k) for k,v in parameters.items() if k not in ('Costs Multiplier','Risk Aversion',*omit))


def num(v):return f'{v:.3f}'


def table(headers,rows):
    return '<div class="table-wrap"><table><thead><tr>'+''.join('<th>'+escape(h)+'</th>' for h in headers)+'</tr></thead><tbody>'+''.join('<tr>'+''.join('<td>'+escape(str(v))+'</td>' for v in row)+'</tr>' for row in rows)+'</tbody></table></div>'


def figures(data,folder):
    plt.rcParams.update({'font.size':10,'axes.spines.top':False,'axes.spines.right':False,'axes.titleweight':'bold','savefig.facecolor':'white'})
    paths=[]
    with PdfPages(folder/'chart-pages.pdf') as pdf:
        for key in ['levels',*RULES]:
            fig,axes=plt.subplots(1,2,figsize=(11,7.7),sharex=True)
            for ax,risk in zip(axes,RISKS):
                for method in ('gini','liu'):
                    if key=='levels':
                        rows=[r for r in data['Rows'] if not r['IsReference'] and r['Method']==method and r['Parameters']['Risk Aversion']==risk and default_background(r['Parameters'])]
                        rows.sort(key=lambda r:float(r['Parameters']['Costs Multiplier']))
                        x=[float(r['Parameters']['Costs Multiplier']) for r in rows];y=[r['Ratio']['Mean'] for r in rows];sd=[r['Ratio']['SD'] for r in rows]
                    else:
                        start,end=RULES[key]
                        rows=[r for r in data['Comparisons'] if r['Method']==method and r['Parameter']==key and r['From']==start and r['To']==end and r['Background']['Risk Aversion']==risk and default_background(r['Background'])]
                        rows.sort(key=lambda r:float(r['Background']['Costs Multiplier']))
                        x=[float(r['Background']['Costs Multiplier']) for r in rows];y=[r['Difference']['Mean'] for r in rows];sd=[r['Difference']['SD'] for r in rows]
                    ax.errorbar([v+(-.025 if method=='gini' else .025) for v in x],y,yerr=sd,fmt='o-',capsize=4,lw=1.5,label=method.title(),color=COLORS[method])
                ax.set_title(risk);ax.set_xlabel('Litigation expense multiplier');ax.grid(axis='y',alpha=.2);ax.set_xticks([0,.25,.5,1,2,4]);ax.tick_params(axis='x',labelsize=8,labelrotation=45)
                ax.axhline(1 if key=='levels' else 0,color='#697878',lw=1,ls='--');ax.legend()
            if key=='levels':
                title='Total social cost relative to the matched ideal benchmark'
                ylabel='Mean cost ratio ± one search SD'
                note='Each point: one fixed setting, ten searches. Default legal and economic parameters; expense varies.\nEach setting uses its own-rule ideal reference, fixed across searches and both methods. Ratio 1 = reference cost.'
            else:
                start,end=RULES[key];title=f'{parameter_name(key)}: {start} to {end}'
                ylabel='Mean paired cost change / benchmark ± one SD'
                note='Each point: ten same-seed B-minus-A differences in one fixed background. All other parameters fixed.\nBoth sides use A’s ideal reference. Negative = B costs less. Bars show search sensitivity, not confidence intervals.'
            fig.suptitle(title,fontsize=16,y=.98);axes[0].set_ylabel(ylabel);fig.text(.07,.055,note,fontsize=10,linespacing=1.5)
            fig.text(.07,.012,'Risk-averse economic accuracy remains qualified. Ideal adjudication is not a certified social optimum.',fontsize=9,color='#815817')
            fig.subplots_adjust(top=.86,bottom=.24,wspace=.27,left=.08,right=.97)
            name='cost-levels' if key=='levels' else {'Fee Shifting Multiplier':'fee-change','Damages Multiplier':'damages-change','Liability Threshold':'threshold-change'}[key]
            for ext in ('png','svg'):fig.savefig(folder/(name+'.'+ext),dpi=180)
            pdf.savefig(fig);plt.close(fig);paths.append((name,title,note))
    return paths


def guide(data,folder):
    styles=getSampleStyleSheet();styles['BodyText'].fontSize=10.5;styles['BodyText'].leading=15
    flow=[]
    def p(text,style='BodyText'):flow.append(Paragraph(text,styles[style]));flow.append(Spacer(1,9))
    p('How much do the results move across searches?','Title')
    p('Total social cost measured against matched costless, accurate adjudication with no private information noise.','Heading2')
    p('A ratio of 1.20 means social cost is 20% above the matched reference. An SD of 0.03 means the ten saved searches have a standard deviation equal to 3% of that reference cost. It does not mean a 3% confidence interval or a bound on economic error.')
    p('What is held fixed','Heading2')
    p('The reference removes litigation expense, court error and private information noise. Every other recorded parameter, including risk attitude, damages, fee reimbursement, liability threshold and care price, must match exactly. There are 18 such references, supporting 756 ordinary settings; 1,494 settings lack an exact reference and are excluded. Nothing is imputed.')
    p('One denominator per setting','Heading2')
    p('The denominator is the fixed mean of all 20 saved reference costs: ten Gini and ten Liu. Sharing it makes the two methods directly comparable and keeps benchmark search variation out of the target SD. The reference distribution is exported separately; its largest observed SD is about 0.35% of its mean. This is a descriptive benchmark, not a proven social optimum.')
    p('Comparing rules','Heading2')
    p('For a particular A-to-B policy change, pair search 1 with search 1 and so on. Divide both costs by the same reference matched to A. Report the mean and SD of these ten differences. Using different rule-specific denominators on the two sides could change the apparent policy ranking. The tables retain both the own-rule ratios and the common-reference comparison.')
    flow.append(PageBreak());p('What the data currently support','Title')
    p('Some large effects survive search variability','Heading2')
    p('In the default risk-neutral high-expense setting, raising fee reimbursement from 0 to 2 lowers cost in all ten paired searches under each method. Mean changes are -0.297 benchmark units (Gini) and -0.244 (Liu), with SDs 0.101 and 0.104. At baseline expense, raising damages from 0.5 to 2 likewise lowers cost in every paired search: -0.121 / -0.142, SD 0.044 / 0.047. The draft gives the opposite sign for this damages example.')
    p('Some small effects are not stable','Heading2')
    p('For default moderately risk-averse settings at baseline expense, fee reimbursement 0 to 2 has mean changes +0.008 under both methods but SDs 0.045 / 0.025. Full reimbursement costs less in only 5/10 Gini and 4/10 Liu pairs. A strong directional claim from that small average would be misleading. Risk-averse numerical accuracy also remains a separate qualification.')
    p('Do not merge different kinds of spread','Heading2')
    p('The downloadable summaries average each fixed setting’s mean and SD separately. Variation across different backgrounds is reported separately; it is not used as an error bar for ten searches in one background. Policy summaries hold the exact transition, risk and expense scope fixed and weight available backgrounds equally. Their coverage is descriptive, not an empirical population weight.')
    p('Limits','Heading2')
    p('These are ten numerical searches, not draws from a known population of equilibria. SD is sensitivity, not statistical confidence. Utility normalization permits economically substantial residual incentives in some risk-averse cases. Stability across searches does not cure that issue. The strict-liability threshold 0 example has no exact ideal reference here. The downloadable unmatched-settings list makes the missing coverage explicit. No simulation was rerun for this analysis.')
    doc=SimpleDocTemplate(str(folder/'reading-guide.pdf'),pagesize=(11*inch,7.7*inch),rightMargin=45,leftMargin=45,topMargin=35,bottomMargin=35)
    doc.build(flow)
    writer=PdfWriter()
    for name in ('reading-guide.pdf','chart-pages.pdf'):
        for pg in PdfReader(folder/name).pages:writer.add_page(pg)
    with (folder/'benchmark-ratio-analysis.pdf').open('wb') as f:writer.write(f)


JS=r'''const f=x=>x!==0&&Math.abs(x)<.0005?Number(x).toExponential(2):Number(x).toFixed(3);let D;
function options(id,values){const n=document.getElementById(id);for(const v of values){const o=document.createElement('option');o.value=v;o.textContent=v;n.append(o)}}
function cells(tr,values){for(const v of values){const c=document.createElement('td');c.textContent=v;tr.append(c)}}
function plot(values){const lo=Math.min(...values),hi=Math.max(...values),span=hi-lo||.01;const box=document.createElementNS('http://www.w3.org/2000/svg','svg');box.setAttribute('viewBox','0 0 180 32');box.setAttribute('width','180');box.setAttribute('height','32');box.setAttribute('role','img');box.setAttribute('aria-label','Ten ratios; horizontal scale '+f(lo)+' to '+f(hi));values.forEach((v,i)=>{const c=document.createElementNS(box.namespaceURI,'circle');c.setAttribute('cx',10+160*(v-lo)/span);c.setAttribute('cy',12+(i%3)*5);c.setAttribute('r',3);c.setAttribute('fill','#087c82');const t=document.createElementNS(box.namespaceURI,'title');t.textContent='Search '+(i+1)+': '+v;c.append(t);box.append(c)});return box}
function drawCases(){const rows=D.Rows.filter(r=>!r.IsReference&&r.Method===document.getElementById('method').value&&r.Parameters['Risk Aversion']===document.getElementById('risk').value&&r.Parameters['Costs Multiplier']===document.getElementById('expense').value&&r.Label.toLowerCase().includes(document.getElementById('query').value.toLowerCase()));const body=document.querySelector('#ratio-table tbody');body.replaceChildren();document.getElementById('ratio-count').textContent=rows.length+' exact settings; showing '+Math.min(50,rows.length)+'. Each row contains ten searches.';for(const r of rows.slice(0,50)){const tr=document.createElement('tr');cells(tr,[r.Label,f(r.Ratio.Mean),f(r.Ratio.SD),f(r.Ratio.Minimum)+' to '+f(r.Ratio.Maximum),f(r.PublishedRatio),f(r.PooledRatio)]);const c=document.createElement('td');c.append(plot(r.Ratios));tr.append(c);body.append(tr)}}
function drawEffects(){const rule=document.getElementById('rule').value,transition=document.getElementById('transition').value.split('|'),risk=document.getElementById('erisk').value,cost=document.getElementById('ecost').value;const rows=D.Comparisons.filter(r=>r.Parameter===rule&&r.From===transition[0]&&r.To===transition[1]&&r.Background['Risk Aversion']===risk&&r.Background['Costs Multiplier']===cost);const body=document.querySelector('#effect-table tbody');body.replaceChildren();document.getElementById('effect-count').textContent=(rows.length/2)+' exact matched backgrounds; showing up to 40 method rows. Both costs use A’s benchmark.';for(const r of rows.slice(0,40)){const tr=document.createElement('tr');cells(tr,[r.Label,r.Method,f(r.AOnCommonReference.Mean)+' / '+f(r.AOnCommonReference.SD),f(r.BOnCommonReference.Mean)+' / '+f(r.BOnCommonReference.SD),f(r.Difference.Mean),f(r.Difference.SD),r.BLower+'/10',f(r.PublishedDifference),f(r.PooledDifference)]);body.append(tr)}const s=D.PolicySummaries.filter(r=>r.Parameter===rule&&r.From===transition[0]&&r.To===transition[1]&&r.Risk===risk&&r.Cost===cost);document.getElementById('effect-summary').textContent=s.length?s.map(r=>r.Method.toUpperCase()+': '+r.Backgrounds+' backgrounds; average signed effect '+f(r.AverageSignedEffect)+', average absolute group mean effect '+f(r.AverageAbsoluteMeanEffect)+', average paired-effect SD '+f(r.AverageWithinGroupEffectSD)+'. '+(100*r.ShareMeanEffectSmallerThanSD).toFixed(1)+'% have |mean effect| smaller than their SD.').join(' '):'No exactly matched benchmark coverage for this scope.'}
function transitions(){const n=document.getElementById('transition');n.replaceChildren();const rule=document.getElementById('rule').value;options('transition',[...new Set(D.Comparisons.filter(r=>r.Parameter===rule).map(r=>r.From+'|'+r.To))]);for(const o of n.options)o.textContent=o.value.replace('|',' to ');if([...n.options].some(o=>o.value==='0|2'))n.value='0|2';drawEffects()}
fetch('benchmark-ratios/explorer.json?v=VERSION').then(r=>r.json()).then(data=>{D=data;for(const id of ['risk','erisk'])options(id,['Risk Neutral','Moderately Risk Averse']);for(const id of ['expense','ecost']){options(id,[...new Set(D.Rows.filter(r=>!r.IsReference).map(r=>r.Parameters['Costs Multiplier']))].sort((a,b)=>+a-+b));document.getElementById(id).value='1'}options('method',['gini','liu']);options('rule',[...new Set(D.Comparisons.map(r=>r.Parameter))]);document.getElementById('rule').value='Fee Shifting Multiplier';for(const id of ['risk','method','expense'])document.getElementById(id).onchange=drawCases;document.getElementById('query').oninput=drawCases;for(const id of ['erisk','ecost','transition'])document.getElementById(id).onchange=drawEffects;document.getElementById('rule').onchange=transitions;drawCases();transitions()}).catch(e=>{document.getElementById('ratio-count').textContent='Data could not load: '+e.message;console.error(e)});
'''


def render(data,source,site,folder):
    folder.mkdir(exist_ok=True);paths=figures(data,folder);guide(data,folder)
    target=site/'benchmark-ratios';target.mkdir(exist_ok=True)
    payload={k:data[k] for k in ('Rows','Comparisons','PolicySummaries')}
    for r in payload['Rows']:
        r['Label']=setting_name(dict(Parameters=r['Parameters'],OptionSetName=r['Name']),BASE)
        if default_background(r['Parameters'],RULES):r['Label']='Default other assumptions; '+r['Label']
    payload['Rows'].sort(key=lambda r:(sum(v!=BASE.get(k) for k,v in r['Parameters'].items() if k not in ('Risk Aversion','Costs Multiplier',*RULES)),r['Label']))
    for r in payload['Comparisons']:r['Label']='; '.join(parameter_name(k)+': '+v for k,v in r['Background'].items() if v!=BASE.get(k) and k not in ('Risk Aversion','Costs Multiplier')) or 'Default other assumptions'
    raw=json.dumps(payload,separators=(',',':')).encode();version=hashlib.sha256(raw).hexdigest()[:12]
    (target/'explorer.json').write_bytes(raw);(target/'explorer.js').write_text(JS.replace('VERSION',version),encoding='utf-8')
    for p in folder.glob('*'):
        if p.suffix in ('.png','.svg') or p.name=='benchmark-ratio-analysis.pdf':shutil.copy2(p,target/p.name)
    for name in ('case-ratios.csv','search-ratios.csv','benchmarks.csv','policy-comparisons.csv','policy-summaries.csv','background-summaries.csv','unmatched-settings.csv'):
        shutil.copy2(source.parent/name,target/name)
    shutil.copy2(source,target/'analysis.json')
    body='''<p class="lede">Compare total social cost with an otherwise matched world in which adjudication is accurate, litigation is costless and neither party has private information noise. Then ask whether the ten searches move enough to weaken a particular conclusion.</p>
<section><h2>How to read the ratio</h2><p><strong>1.20 means 20% above the reference cost.</strong> An SD of 0.03 means variation across the ten searches equal to 3% of that reference cost. The SD is calculated within one fixed setting, separately for Gini and Liu. It is descriptive sensitivity, not statistical confidence.</p><p>The denominator is one fixed mean of the 20 ideal-reference searches (ten per method), shared by both methods. The reference’s own search variation is reported separately; its largest observed SD is about 0.35% of its mean. Perfect, costless adjudication is a useful reference, not a certified social optimum; ratios below 1 are possible.</p></section>
<section><h2>What is covered?</h2><p>There are 18 exact references, covering <strong>756 ordinary settings</strong> plus the references themselves. The other <strong>1,494 settings are excluded</strong>. We match every recorded parameter except adjudication mode, litigation expense, court error and the two parties’ private information noise. Risk, damages, fee reimbursement, care price and liability threshold must still agree exactly. Missing matches are never filled in.</p><p>The strict-liability threshold 0 example has no exact ideal reference in these data. Nor does this subset cover all the combinations implicated by the risk-averse economic-accuracy concern.</p></section>
<section><h2>Does variability erase the policy effects?</h2><p><strong>Some large effects survive.</strong> In the default risk-neutral setting with litigation expense four times baseline, raising fee reimbursement from 0 to 2 lowers cost in all ten same-seed comparisons under each method. The mean reduction is 0.297 benchmark units under Gini and 0.244 under Liu, compared with paired-effect SDs of 0.101 and 0.104.</p><p>At baseline expense, raising damages from 0.5 to 2 also lowers cost in every risk-neutral paired search: mean changes −0.121 / −0.142, SDs 0.044 / 0.047. The draft gives the opposite direction for this example. The reversal is therefore present throughout the ten observed searches, rather than resting solely on the pooled selection.</p><p><strong>Small effects can be unstable.</strong> For default moderately risk-averse settings at baseline expense, fee reimbursement 0 to 2 changes mean cost by only about +0.008 benchmark units under either method, with SDs 0.045 / 0.025. Reimbursement lowers cost in only 5/10 Gini and 4/10 Liu pairs. A firm directional claim from that small mean would be weak. Risk-averse numerical accuracy remains a separate unresolved qualification.</p></section>'''
    for name,title,note in paths:body+=f'<figure><img src="benchmark-ratios/{name}.png?v={version}" alt="{escape(title)}; means with one descriptive search SD"><figcaption><strong>{escape(title)}</strong>. {escape(note).replace(chr(10)," ")}</figcaption></figure>'
    headers=['Risk','Expense','Method','Settings','Average mean ratio','Average within-setting SD','Between-setting mean SD']
    rows=[[r['Risk'],r['Cost'],r['Method'],r['Backgrounds'],num(r['AverageGroupMean']),num(r['AverageWithinGroupSD']),num(r['BetweenGroupMeanSD'])] for r in data['CellSummaries'] if r['Cost'] in ('0.25','1','4')]
    body+='<section><h2>Within-setting sensitivity rises with litigation expense</h2><p>Each setting contributes its own ten-search mean and SD first; these are then averaged with equal weight across the available settings in the row. The last column describes differences <em>between</em> settings and is kept separate. These are selected grid summaries, not population estimates.</p>'+table(headers,rows)+'</section>'
    body+='''<section id="settings"><h2>Inspect each fixed setting</h2><p>The dots show all ten ratios. Each row’s dot scale runs from its own minimum to maximum; use the numeric SD and range for comparisons between rows. Published and pooled ratios are separate reference points, not extra observations in the ten-search SD.</p><label>Method<select id="method"></select></label><label>Risk<select id="risk"></select></label><label>Litigation expense<select id="expense"></select></label><label>Find settings<input id="query" placeholder="e.g. damages"></label><p id="ratio-count" aria-live="polite">Loading data…</p>'''+table(['Setting','Mean ratio','SD','Observed range','Draft ratio','Pooled ratio','Ten searches'],[])+'</section>'
    body=body.replace('<table><thead><tr><th>Setting</th>','<table id="ratio-table"><thead><tr><th>Setting</th>')
    body+='''<section id="changes"><h2>Compare specific rules, holding everything else fixed</h2><p>Pair the same original search seeds across A and B. Both sides use <strong>one common denominator: A’s ideal reference</strong>. This preserves the direction of the raw cost change even when B’s own-rule reference differs. Mean/SD are in benchmark-cost units. Negative differences favor B.</p><label>Rule<select id="rule"></select></label><label>A to B<select id="transition"></select></label><label>Risk<select id="erisk"></select></label><label>Litigation expense<select id="ecost"></select></label><p id="effect-summary" aria-live="polite"></p><p class="note">The summary averages background-level statistics with equal weights for this exact transition and scope. “Mean smaller than SD” is a descriptive scale comparison, not a significance test.</p><p id="effect-count"></p>'''+table(['Other fixed assumptions','Method','A mean / SD','B mean / SD','Paired mean Δ','Paired SD','B cheaper','Draft Δ','Pooled Δ'],[])+'</section>'
    body=body.replace('<table><thead><tr><th>Other fixed assumptions</th>','<table id="effect-table"><thead><tr><th>Other fixed assumptions</th>')
    body+='<section><h2>Download the data and figures</h2><p>The CSVs contain unrounded values, case identifiers, exact parameters and reference identities. Ratios have no currency unit. Raw cost remains in model units per opportunity.</p>'
    for name,label in [('benchmark-ratio-analysis.pdf','Reading guide and four charts (PDF)'),('case-ratios.csv','Setting means, SDs and ranges'),('search-ratios.csv','All 15,480 individual ratios'),('policy-comparisons.csv','756 matched policy comparisons, both methods'),('policy-summaries.csv','Transition summaries across backgrounds'),('background-summaries.csv','Expense/risk summaries'),('benchmarks.csv','All 18 benchmark distributions'),('unmatched-settings.csv','Settings without a reference'),('analysis.json','Full analysis and provenance (JSON)')]:
        body+=f'<a class="button" href="benchmark-ratios/{name}?v={version}" download>{escape(label)}</a>'
    body+='</section><section><h2>What this cannot establish</h2><p>These ten searches do not sample all equilibria with known probabilities. Numerical error can contribute to the spread; agreement does not certify accuracy. In particular, the existing monetary-incentive concerns for some risk-averse calculations remain. We have changed no solver, tolerance, numerical result or acceptance record for this analysis.</p><p>The benchmark removes several frictions together. The ratio does not identify which friction causes a gap. For a causal model contrast, use the exactly matched policy comparisons above; their signs still require the stated scientific qualifications.</p></section>'
    html=page('Cost ratios and search sensitivity',body,'benchmarks.html',{'InitialSearchReview':True})
    html=html.replace('</body>',f'<script src="benchmark-ratios/explorer.js?v={version}" defer></script></body>')
    (site/'benchmarks.html').write_text(html,encoding='utf-8')
    manifest=dict(Source=str(source.resolve()),SourceSha256=hashlib.sha256(source.read_bytes()).hexdigest(),RendererSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),Coverage=data['Coverage'],Files={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in target.iterdir() if p.is_file()},PageSha256=hashlib.sha256((site/'benchmarks.html').read_bytes()).hexdigest())
    (folder/'render-manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    print(json.dumps(dict(Page=str(site/'benchmarks.html'),PDF=str(folder/'benchmark-ratio-analysis.pdf'),Pages=len(PdfReader(folder/'benchmark-ratio-analysis.pdf').pages))))


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--analysis',required=True);p.add_argument('--site',required=True);p.add_argument('--output',required=True);a=p.parse_args()
    source=Path(a.analysis);render(json.loads(source.read_text(encoding='utf-8')),source,Path(a.site),Path(a.output))


if __name__=='__main__':main()
