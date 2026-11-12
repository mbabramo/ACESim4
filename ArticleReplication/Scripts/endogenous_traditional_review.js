/* Fixed-background traditional-search explorer. No observations are imputed. */
(() => {
  'use strict';
  const $=id=>document.getElementById(id);
  const rules={'Fee Shifting Multiplier':'Fee reimbursement','Damages Multiplier':'Damages multiplier','Liability Threshold':'Liability threshold'};
  const esc=x=>String(x).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const mean=a=>a.length?a.reduce((x,y)=>x+y,0)/a.length:null;
  const sd=a=>a.length>1?Math.sqrt(a.reduce((x,y)=>x+(y-mean(a))**2,0)/(a.length-1)):null;
  const fmt=x=>x===null||x===undefined?'unavailable':Number(x).toLocaleString(undefined,{maximumSignificantDigits:5});
  const passes=(r,screen)=>screen==='all'||(r.EconomicScreenPassed&&(screen==='money'||(screen==='loose-money'?r.NashAt1eMinus5:r.NashAtTarget)));
  let data,groups=[],shown=[];
  const parameter=()=>$('tr-rule').value;
  const metric=()=>$('tr-metric').value;
  const isCost=m=>m.endsWith('Cost')||m==='SocialWelfareLoss'||m==='LitigationExpenditures';
  const scale=m=>isCost(m)?10000:m==='PrecautionLevel'?1:100;
  const unit=(m,difference=false)=>isCost(m)?'model cost per 10,000 opportunities':m==='PrecautionLevel'?'care levels':difference?'percentage points':'percent';
  function options(id,items,preferred){
    const el=$(id);el.innerHTML=items.map(([v,label])=>`<option value="${esc(v)}">${esc(label)}</option>`).join('');
    if(items.some(([v])=>v===preferred))el.value=preferred;
  }
  function regroup(){
    const rule=parameter(),risk=$('tr-risk').value,cost=$('tr-cost').value,map=new Map();
    for(const c of data.Cases){
      if(c.Parameters['Risk Aversion']!==risk||c.Parameters['Costs Multiplier']!==cost)continue;
      const fixed=Object.fromEntries(Object.entries(c.Parameters).filter(([k])=>k!==rule).sort(([a],[b])=>a.localeCompare(b))),key=JSON.stringify(fixed);
      if(!map.has(key))map.set(key,{fixed,cases:[]});map.get(key).cases.push(c);
    }
    groups=[...map.values()].filter(g=>g.cases.length>=2);
    for(const g of groups){g.cases.sort((a,b)=>Number(a.Parameters[rule])-Number(b.Parameters[rule]));g.deviations=Object.entries(g.fixed).filter(([k,v])=>!['Risk Aversion','Costs Multiplier'].includes(k)&&data.Base[k]!==v);}
    groups.sort((a,b)=>a.deviations.length-b.deviations.length||JSON.stringify(a.fixed).localeCompare(JSON.stringify(b.fixed)));
    options('tr-background',groups.map((g,i)=>[String(i),g.deviations.length?g.deviations.map(([k,v])=>`${k}: ${v}`).join(' · '):'Default values for every other assumption']),'0');
    show();
  }
  function show(){
    const g=groups[Number($('tr-background').value)],rule=parameter(),m=metric(),screen=$('tr-screen').value,factor=scale(m);
    if(!g){$('tr-status').textContent='No available matched background in this scope.';$('tr-rows').innerHTML='';$('tr-chart').innerHTML='';$('tr-delta').textContent='';return;}
    shown=g.cases;const all=shown.map(c=>c.Traditional.filter(r=>passes(r,screen)));
    $('tr-fixed').innerHTML=Object.entries(g.fixed).map(([k,v])=>`<dt>${esc(k)}</dt><dd>${esc(v)}</dd>`).join('');
    $('tr-status').textContent=`${shown.length} rule values in one exactly controlled background. Displayed outcome: ${data.Labels[m]}, in ${unit(m)}. Green dots pass BOTH strict screens; gray dots do not. Whiskers are one sample SD, not confidence intervals.`;
    $('tr-rows').innerHTML=shown.map((c,i)=>{
      const v=all[i].map(r=>r.Values[m]).filter(x=>x!==null),mu=mean(v),st=sd(v),total=mean(all[i].map(r=>r.Values.SocialWelfareLoss)),ref=c.Reference;
      return `<tr><td>${esc(c.Parameters[rule])}</td><td>${v.length}/10<br><small>${c.Traditional.filter(r=>passes(r,'strict-money')).length} strict + money</small></td><td>${fmt(mu===null?null:mu*factor)}</td><td>${fmt(st===null?null:st*factor)}</td><td>${v.length?`${fmt(Math.min(...v)*factor)} – ${fmt(Math.max(...v)*factor)}`:'unavailable'}</td><td>${fmt(c.Published[m]===null?null:c.Published[m]*factor)}</td><td>${fmt(c.Selectors.gini.Pooled[m]===null?null:c.Selectors.gini.Pooled[m]*factor)}</td><td>${fmt(c.Selectors.liu.Pooled[m]===null?null:c.Selectors.liu.Pooled[m]*factor)}</td><td>${fmt(ref&&total!==null?total/ref[1]:null)}</td></tr>`;
    }).join('');
    plot(shown,all,m,factor);
    const oldA=$('tr-a').value,oldB=$('tr-b').value,items=shown.map(c=>[c.Id,c.Parameters[rule]]);
    options('tr-a',items,items.some(([v])=>v===oldA)?oldA:items[0][0]);options('tr-b',items,items.some(([v])=>v===oldB)?oldB:items.at(-1)[0]);difference();
  }
  function difference(){
    const a=shown.find(c=>c.Id===$('tr-a').value),b=shown.find(c=>c.Id===$('tr-b').value),m=metric(),screen=$('tr-screen').value;
    if(!a||!b)return;
    if(a.Id===b.Id){$('tr-delta').textContent='Choose two different values.';return;}
    const delta=[],before=[];
    for(let i=0;i<10;i++)if(passes(a.Traditional[i],screen)&&passes(b.Traditional[i],screen)){
      const av=a.Traditional[i].Values[m],bv=b.Traditional[i].Values[m];
      if(av!==null&&bv!==null){before.push(av);delta.push(bv-av);}
    }
    if(!delta.length){$('tr-delta').textContent='0/10 jointly available seed pairs. No paired effect, SD or directional share can be calculated under this screen.';return;}
    const mu=mean(delta),st=sd(delta),share=delta.filter(v=>v<0).length,ref=a.Reference;
    const relative=delta.length&&before.every(v=>v!==0)?mean(delta.map((v,i)=>v/Math.abs(before[i])))*100:null;
    const text=`${delta.length}/10 jointly available seed pairs. B − A mean: ${fmt(mu===null?null:mu*scale(m))}; SD: ${fmt(st===null?null:st*scale(m))} ${unit(m,true)}. B lower: ${share}/${delta.length}; A lower: ${delta.filter(v=>v>0).length}/${delta.length}; exact ties: ${delta.filter(v=>v===0).length}. Average within-seed percentage change: ${fmt(relative)}${relative===null?'':'%'}.`;
    $('tr-delta').textContent=text+(m==='SocialWelfareLoss'&&ref&&mu!==null?` On A’s fixed ideal-reference denominator, the mean change is ${fmt(mu/ref[1])} reference-cost units.`:'');
  }
  function plot(cases,selected,m,factor){
    const w=1000,h=390,left=85,right=25,top=25,bottom=65,ww=w-left-right,hh=h-top-bottom;
    const values=[];
    for(let i=0;i<cases.length;i++){
      const c=cases[i],v=selected[i].map(r=>r.Values[m]).filter(x=>x!==null);values.push(...v);
      if(v.length>1)values.push(mean(v)-sd(v),mean(v)+sd(v));
      for(const x of [c.Published[m],c.Selectors.gini.Pooled[m],c.Selectors.liu.Pooled[m]])if(x!==null)values.push(x);
    }
    if(!values.length){$('tr-chart').textContent='No observations pass this screen.';return;}
    let lo=Math.min(...values)*factor,hi=Math.max(...values)*factor;const pad=Math.max((hi-lo)*.12,Math.abs(hi)*.005,1e-8);lo-=pad;hi+=pad;
    const y=v=>top+(hi-v*factor)/(hi-lo)*hh,x=i=>left+(i+.5)*ww/cases.length;
    let svg=`<svg viewBox="0 0 ${w} ${h}" xmlns="http://www.w3.org/2000/svg"><title>${esc(data.Labels[m])}: ten traditional searches and separate draft/Gini/Liu reference points</title>`;
    for(let k=0;k<5;k++){const v=lo+(hi-lo)*k/4,yy=y(v/factor);svg+=`<line x1="${left}" y1="${yy}" x2="${w-right}" y2="${yy}" stroke="#dbe2dd"/><text x="${left-8}" y="${yy+4}" text-anchor="end" font-size="12">${fmt(v)}</text>`;}
    for(let i=0;i<cases.length;i++){
      const c=cases[i],v=selected[i].filter(r=>r.Values[m]!==null),vv=v.map(r=>r.Values[m]);
      for(let j=0;j<v.length;j++)svg+=`<circle cx="${x(i)+(j-(v.length-1)/2)*3}" cy="${y(v[j].Values[m])}" r="3.5" fill="${passes(v[j],'strict-money')?'#1c554b':'#999'}"><title>Seed ${v[j].Seed}: ${fmt(v[j].Values[m]*factor)}</title></circle>`;
      if(vv.length){const mu=mean(vv),st=sd(vv);if(st!==null)svg+=`<path d="M${x(i)} ${y(mu-st)}V${y(mu+st)}M${x(i)-6} ${y(mu-st)}H${x(i)+6}M${x(i)-6} ${y(mu+st)}H${x(i)+6}" stroke="#172929" fill="none"/>`;svg+=`<circle cx="${x(i)}" cy="${y(mu)}" r="5" fill="#172929"/>`;}
      for(const [offset,value,color] of [[-23,c.Published[m],'#4a4a4a'],[23,c.Selectors.gini.Pooled[m],'#007f86'],[34,c.Selectors.liu.Pooled[m],'#c46825']])if(value!==null)svg+=`<rect x="${x(i)+offset-3}" y="${y(value)-3}" width="6" height="6" fill="${color}"/>`;
      svg+=`<text x="${x(i)}" y="${h-bottom+25}" text-anchor="middle" font-size="13">${esc(c.Parameters[parameter()])}</text>`;
    }
    svg+=`<text x="${w/2}" y="${h-13}" text-anchor="middle" font-size="12">Rule values · black: traditional mean/SD · gray square: draft · teal: Gini pooled · orange: Liu pooled</text></svg>`;
    $('tr-chart').innerHTML=svg;
  }
  async function start(){
    const response=await fetch('traditional-review/browser-data.json.gz',{cache:'no-store'});if(!response.ok)throw Error(`Data request failed: ${response.status}`);
    data=await new Response(response.body.pipeThrough(new DecompressionStream('gzip'))).json();
    options('tr-rule',Object.entries(rules),'Fee Shifting Multiplier');
    options('tr-risk',[...new Set(data.Cases.map(c=>c.Parameters['Risk Aversion']))].sort().map(x=>[x,x]),'Risk Neutral');
    options('tr-cost',[...new Set(data.Cases.map(c=>c.Parameters['Costs Multiplier']))].sort((a,b)=>+a-+b).map(x=>[x,x]),'1');
    options('tr-metric',Object.entries(data.Labels),'SocialWelfareLoss');
    for(const id of ['tr-rule','tr-risk','tr-cost'])$(id).addEventListener('change',regroup);
    for(const id of ['tr-screen','tr-metric','tr-background'])$(id).addEventListener('change',show);
    for(const id of ['tr-a','tr-b'])$(id).addEventListener('change',difference);
    regroup();
  }
  start().catch(e=>{$('tr-status').textContent=`Unable to load the explorer: ${e.message}. Download tables remain available.`;console.error(e);});
})();
