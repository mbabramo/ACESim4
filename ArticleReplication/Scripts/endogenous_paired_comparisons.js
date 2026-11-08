/* Fixed environments, all available unordered rule pairs, same-seed searches. */
async function pairedComparisons(){
  const root=document.querySelector('#paired-comparisons');if(!root)return;
  const response=await fetch('paired-comparisons.json.gz');
  const data=await new Response(response.body.pipeThrough(new DecompressionStream('gzip'))).json();
  const rule=document.querySelector('#policy-rule'),risk=document.querySelector('#policy-risk'),cost=document.querySelector('#policy-cost'),background=document.querySelector('#policy-background');
  const endpoint=(p,x)=>p==='Fee Shifting Multiplier'?({'0':'None (0)','0.5':'Half (0.5)','1':'Full (1)','2':'Double (2)','5':'Fivefold (5)'}[x]||x):p==='Damages Multiplier'?({'0.5':'Half damages (0.5)','1':'Ordinary damages (1)','2':'Double damages (2)','4':'Quadruple damages (4)'}[x]||x):({'0':'Strict liability (0)','1':'Negligence benchmark (1)'}[x]||'Standard '+x);
  const number=x=>x==null?'—':x===0?'0':Math.abs(x)<.00005?x.toExponential(3):x.toLocaleString(undefined,{maximumSignificantDigits:7});
  const units=x=>x==null?'—':number(x*10000);
  const delta=x=>x==null?'—':(x>0?'+':'')+units(x);
  const percent=x=>x==null?'—':(x>0?'+':'')+number(x*100)+'%';
  const tableRow=(body,values)=>{const tr=document.createElement('tr');for(const value of values){const td=document.createElement('td');td.textContent=value;tr.append(td)}body.append(tr);return tr};
  let currentGroup,currentPair;
  function inspect(pair){
    currentPair=pair;const g=currentGroup;
    document.querySelector('#pair-detail').hidden=false;
    document.querySelector('#pair-title').textContent='A: '+endpoint(g.Parameter,pair.From)+' → B: '+endpoint(g.Parameter,pair.To);
    document.querySelector('#pair-case-ids').textContent='Setting A: '+pair.A+'; setting B: '+pair.B+'.';
    const summary=document.querySelector('#pair-summary tbody');summary.replaceChildren();
    for(const m of ['gini','liu']){const s=pair[m];tableRow(summary,[m==='gini'?'Gini':'Liu-affinity',`${s.BLower}/${s.Pairs} (${100*s.BLowerFraction}%)`,`${s.ALower}/${s.Pairs} (${100*s.ALowerFraction}%)`,String(s.ExactTies),delta(s.MeanDelta),delta(s.MedianDelta),delta(s.MinimumDelta)+' to '+delta(s.MaximumDelta)])}
    const comparisons=document.querySelector('#pair-levels tbody');comparisons.replaceChildren();
    tableRow(comparisons,['Original draft',units(pair.Published.Before),units(pair.Published.After),delta(pair.Published.Delta)]);
    for(const m of ['gini','liu']){const s=pair[m],name=m==='gini'?'Gini':'Liu-affinity';tableRow(comparisons,[name+': mean across ten searches',units(s.BeforeMean),units(s.AfterMean),delta(s.MeanDelta)]);tableRow(comparisons,[name+': final pooled result',units(s.Pooled.Before),units(s.Pooled.After),delta(s.Pooled.Delta)])}
    const rows=document.querySelector('#pair-repetitions tbody');rows.replaceChildren();
    for(let i=0;i<data.Repetitions;i++){const repaired=[pair.RepairedA.includes(i+1)?'A repaired':'',pair.RepairedB.includes(i+1)?'B repaired':''].filter(Boolean).join('; ');tableRow(rows,[`${i+1} / ${data.Seeds[i]}`+(repaired?' ('+repaired+')':''),units(pair.gini.Before[i]),units(pair.gini.After[i]),delta(pair.gini.Deltas[i]),percent(pair.gini.RelativeChanges[i]),units(pair.liu.Before[i]),units(pair.liu.After[i]),delta(pair.liu.Deltas[i]),percent(pair.liu.RelativeChanges[i])])}
    document.querySelector('#pair-stronger').textContent='Stronger, supplementary range check: Gini — '+pair.gini.SeparatedRanges+'; Liu — '+pair.liu.SeparatedRanges+'. This asks whether every observed cost under one rule is lower than every observed cost under the other, allowing all cross-seed combinations. Overlap can coexist with 10/10 same-seed agreement.';
    document.querySelector('#pair-candidate').textContent=pair.CandidateA||pair.CandidateB?'This pair includes the flagged final Liu candidate that misses the original normalized tolerance. The ten initial searches are separate outputs. All results remain subject to the economic-accuracy qualification.':'';
  }
  function draw(){
    const previous=currentPair,previousRule=currentGroup?.Parameter;
    const g=data.Groups.find(g=>g.Id===background.value);currentGroup=g;
    const body=document.querySelector('#paired-comparisons tbody'),fixed=document.querySelector('#fixed-assumptions tbody');body.replaceChildren();fixed.replaceChildren();
    document.querySelector('#pair-detail').hidden=true;currentPair=null;
    document.querySelector('#policy-qualification').textContent='';
    if(!g){document.querySelector('#policy-description').textContent='No environment with at least two available rule values matches these filters. Missing settings remain missing.';return}
    document.querySelector('#policy-description').textContent=`One fixed environment. ${g.Levels.length} available rule values give ${g.Pairs.length} distinct pairwise comparisons. Each row compares exactly two settings; no different environments are averaged together.`;
    for(const [key,value] of Object.entries(g.Background)){tableRow(fixed,[data.AssumptionLabels[key]||key,value])}
    for(const p of g.Pairs){const tr=document.createElement('tr');for(const v of [endpoint(g.Parameter,p.From),endpoint(g.Parameter,p.To)]){const td=document.createElement('td');td.textContent=v;tr.append(td)}
      for(const m of ['gini','liu']){const s=p[m],td=document.createElement('td'),bold=document.createElement('strong'),small=document.createElement('span');bold.textContent=`B lower: ${s.BLower}/${s.Pairs} (${100*s.BLowerFraction}%)`;small.className='note';small.textContent=`A lower: ${s.ALower}/${s.Pairs}; exact ties: ${s.ExactTies}. Mean Δ: ${delta(s.MeanDelta)}`;td.append(bold,document.createElement('br'),small);tr.append(td)}
      const td=document.createElement('td'),button=document.createElement('button');button.textContent='Inspect '+p.From+' → '+p.To;button.onclick=()=>{inspect(p);document.querySelector('#pair-detail').scrollIntoView({behavior:'smooth',block:'start'})};td.append(button);tr.append(td);body.append(tr)
    }
    document.querySelector('#policy-qualification').textContent='Counts use the signs of unrounded stored differences; an exact tie is not a finding of economic equivalence. Tiny differences may reflect numerical error. Economic incentive size remains a separate qualification; agreement does not bound social-cost error.'+(risk.value!=='Risk Neutral'?' Some risk-averse results allow economically meaningful improvements and need particular caution.':'');
    inspect((previousRule===g.Parameter&&previous&&g.Pairs.find(p=>p.From===previous.From&&p.To===previous.To))||g.Pairs.find(p=>p.From==='0'&&p.To==='1')||g.Pairs[0]);
  }
  function chooseBackground(){
    const old=background.value,groups=data.Groups.filter(g=>g.Parameter===rule.value&&g.Background['Risk Aversion']===risk.value&&g.Background['Costs Multiplier']===cost.value).sort((a,b)=>Number(b.Default)-Number(a.Default)||a.Label.localeCompare(b.Label));
    background.replaceChildren();for(const g of groups){const option=document.createElement('option');option.value=g.Id;option.textContent=g.Label;background.append(option)}
    if(groups.some(g=>g.Id===old))background.value=old;
    background.disabled=!groups.length;draw();
  }
  const summaryRule=document.querySelector('#summary-rule'),summaryRisk=document.querySelector('#summary-risk'),summaryCost=document.querySelector('#summary-cost');
  const fraction=x=>(100*x).toLocaleString(undefined,{minimumFractionDigits:1,maximumFractionDigits:1})+'%';
  let currentTransition;
  const scopeLabel=control=>control.options[control.selectedIndex].text;
  function showBackgroundPair(g,p){
    document.querySelector('#individual-comparisons').open=true;
    rule.value=g.Parameter;risk.value=g.Background['Risk Aversion'];cost.value=g.Background['Costs Multiplier'];
    chooseBackground();background.value=g.Id;draw();inspect(p);
    document.querySelector('#pair-detail').scrollIntoView({behavior:'smooth',block:'start'});
  }
  function inspectTransition(row){
    currentTransition=row;document.querySelector('#transition-detail').hidden=false;
    document.querySelector('#transition-title').textContent=endpoint(row.Parameter,row.From)+' → '+endpoint(row.Parameter,row.To)+': '+row.Backgrounds+' matched backgrounds';
    document.querySelector('#transition-direction-counts').textContent=['gini','liu'].map(m=>{const s=row[m];return (m==='gini'?'Gini':'Liu-affinity')+`: B is cheaper more often in ${s.BMoreOftenBackgrounds} backgrounds; A in ${s.AMoreOftenBackgrounds}; the directional counts tie in ${s.TiedDirectionBackgrounds}.`}).join(' ');
    const body=document.querySelector('#transition-backgrounds tbody');body.replaceChildren();
    for(const id of row.GroupIds){const g=data.Groups.find(g=>g.Id===id),p=g.Pairs.find(p=>p.From===row.From&&p.To===row.To);
      const label=(Array.from(risk.options).find(o=>o.value===g.Background['Risk Aversion'])?.text||g.Background['Risk Aversion'])+'; '+(Array.from(cost.options).find(o=>o.value===g.Background['Costs Multiplier'])?.text||g.Background['Costs Multiplier'])+'; '+g.Label;
      const tr=tableRow(body,[label,...['gini','liu'].map(m=>{const s=p[m];return `B cheaper: ${s.BLower}/10; A cheaper: ${s.ALower}/10; exact ties: ${s.ExactTies}/10.`})]);
      const td=document.createElement('td'),button=document.createElement('button');button.textContent='See ten searches';button.onclick=()=>showBackgroundPair(g,p);td.append(button);tr.append(td);
    }
  }
  function drawTransitionSummary(){
    const rows=data.TransitionSummaries.filter(r=>r.Parameter===summaryRule.value&&r.RiskScope===summaryRisk.value&&r.CostScope===summaryCost.value);
    const body=document.querySelector('#transition-summaries tbody');body.replaceChildren();document.querySelector('#transition-detail').hidden=true;
    document.querySelector('#transition-scope').textContent=rows.length?`${scopeLabel(summaryCost)}; ${scopeLabel(summaryRisk)}. Each row averages the ten-search percentages across every available matched background for that exact transition. Background coverage can differ between rows.`:'No matched backgrounds contain both rule values for this scope. Missing combinations remain missing.';
    for(const r of rows){const tr=tableRow(body,[endpoint(r.Parameter,r.From),endpoint(r.Parameter,r.To),String(r.Backgrounds)]);
      for(const m of ['gini','liu']){const s=r[m],td=document.createElement('td'),b=document.createElement('strong'),agreement=document.createElement('strong'),small=document.createElement('span');b.textContent='B cheaper: '+fraction(s.MeanBLowerFraction);agreement.textContent='Agreement within backgrounds: '+fraction(s.MeanDirectionalAgreement);small.className='note';small.textContent='A cheaper: '+fraction(s.MeanALowerFraction)+'; exact ties: '+fraction(s.MeanExactTieFraction)+'.';td.append(b,document.createElement('br'),agreement,document.createElement('br'),small);tr.append(td)}
      const td=document.createElement('td'),button=document.createElement('button');button.textContent='Inspect backgrounds '+r.From+' → '+r.To;button.onclick=()=>{inspectTransition(r);document.querySelector('#transition-detail').scrollIntoView({behavior:'smooth',block:'start'})};td.append(button);tr.append(td);body.append(tr);
    }
    document.querySelector('#transition-qualification').textContent='Every included background receives equal weight. The grid is not a complete factorial design or a representative sample of legal environments. Numerical accuracy qualifications apply to these descriptive counts.'+(summaryRisk.value!=='Risk Neutral'?' This scope includes risk-averse results, some of which still allow economically meaningful improvements. The separate economic review also finds substantial remaining incentives in some initial risk-averse searches.':' The ten initial searches still need their own economic-accuracy review.');
    if(currentTransition){const previous=rows.find(r=>r.From===currentTransition.From&&r.To===currentTransition.To&&r.Parameter===currentTransition.Parameter);if(previous)inspectTransition(previous);else currentTransition=null;}
  }
  summaryRule.onchange=summaryRisk.onchange=summaryCost.onchange=drawTransitionSummary;
  rule.onchange=risk.onchange=cost.onchange=chooseBackground;background.onchange=draw;
  document.querySelector('#download-selected-pair').onclick=()=>{if(!currentPair)return;const rows=[];for(const m of ['gini','liu'])for(let i=0;i<data.Repetitions;i++)rows.push({Parameter:currentGroup.Parameter,From:currentPair.From,To:currentPair.To,CaseA:currentPair.A,CaseB:currentPair.B,...currentGroup.Background,Method:m,Repetition:i+1,Seed:data.Seeds[i],CostA:currentPair[m].Before[i],CostB:currentPair[m].After[i],DeltaBMinusA:currentPair[m].Deltas[i],RelativeChange:currentPair[m].RelativeChanges[i],ARepaired:currentPair.RepairedA.includes(i+1),BRepaired:currentPair.RepairedB.includes(i+1)});download('selected-paired-searches.csv',csv(rows),'text/csv;charset=utf-8')};
  document.querySelector('#pair-coverage').textContent=`Coverage: ${data.PairCount.toLocaleString()} distinct rule comparisons across ${data.GroupCount.toLocaleString()} fixed backgrounds (counted separately for each rule). Each has ${data.Repetitions} same-seed pairs per method.`;
  chooseBackground();
  drawTransitionSummary();
}
document.querySelectorAll('[data-paired-export]').forEach(button=>button.onclick=async()=>{const name=button.dataset.pairedExport;const r=await fetch(name+'.csv.gz');download(name+'.csv',await new Response(r.body.pipeThrough(new DecompressionStream('gzip'))).text(),'text/csv;charset=utf-8')});
pairedComparisons().catch(error=>{for(const id of ['policy-description','transition-scope']){const node=document.getElementById(id);if(node)node.textContent='The paired-comparison data could not be loaded. Please reload the page.';}console.error(error)});
