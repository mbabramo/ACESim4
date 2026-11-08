/* Every dot is a saved observation; no fitted distribution or uncertainty band. */
const settingDistributionPromises={};
let selectedDistributionCase;
const magnitudeColors={gini:'#007f86',liu:'#c46825'};
const magnitudeNumber=x=>x==null?'—':x===0?'0':Math.abs(x)<.00005?x.toExponential(3):x.toLocaleString(undefined,{maximumSignificantDigits:6});
const magnitudePercent=x=>x==null?'—':(x>0?'+':'')+magnitudeNumber(x*100)+'%';
async function settingDistributions(metric='SocialWelfareLoss'){
  if(!settingDistributionPromises[metric])settingDistributionPromises[metric]=(async()=>{const r=await fetch('setting-distributions-'+metric+'.json.gz');if(!r.ok)throw Error('Distribution download failed');return new Response(r.body.pipeThrough(new DecompressionStream('gzip'))).json()})();
  return settingDistributionPromises[metric];
}
function magnitudeSvg(tag,attributes={},text){const e=document.createElementNS('http://www.w3.org/2000/svg',tag);for(const [k,v] of Object.entries(attributes))e.setAttribute(k,v);if(text!==undefined)e.textContent=text;return e}
function observedDotPlot(container,rows,seeds,{name,scale=1,units='%',minimum=null,maximum=null}){
  const present=rows.flatMap(r=>r.Values.filter(v=>v!=null).map(v=>v*scale));container.replaceChildren();
  if(!present.length){container.textContent='No defined observations are available for this outcome.';return}
  let low=Math.min(...present),high=Math.max(...present);const padding=(high-low)*.08||Math.abs(low)*.05||1;low-=padding;high+=padding;if(minimum!=null)low=Math.max(low,minimum);if(maximum!=null)high=Math.min(high,maximum);
  const width=900,left=145,right=28,top=42,spacing=65,height=top+spacing*rows.length+60,x=v=>left+(v-low)/(high-low)*(width-left-right),y=i=>top+spacing*i+22;
  const svg=magnitudeSvg('svg',{viewBox:`0 0 ${width} ${height}`,role:'img','aria-label':name});svg.append(magnitudeSvg('title',{},name));
  for(let i=0;i<6;i++){const value=low+(high-low)*i/5;svg.append(magnitudeSvg('line',{x1:x(value),x2:x(value),y1:top-12,y2:height-62,stroke:'#dde5e1'}));svg.append(magnitudeSvg('text',{x:x(value),y:height-39,'text-anchor':'middle','font-size':12,fill:'#34474b'},magnitudeNumber(value)))}
  if(low<0&&high>0)svg.append(magnitudeSvg('line',{x1:x(0),x2:x(0),y1:top-12,y2:height-62,stroke:'#50666a','stroke-dasharray':'4 4'}));
  for(let i=0;i<rows.length-1;i++)if(rows[i].Method===rows[i+1].Method&&rows[i].CaseId!==rows[i+1].CaseId){
    for(let k=0;k<seeds.length;k++){const a=rows[i].Values[k],b=rows[i+1].Values[k];if(a!=null&&b!=null)svg.append(magnitudeSvg('line',{x1:x(a*scale),x2:x(b*scale),y1:y(i)+(k-4.5)*6,y2:y(i+1)+(k-4.5)*6,stroke:magnitudeColors[rows[i].Method],opacity:.16,'stroke-width':1}))}
  }
  rows.forEach((row,i)=>{
    svg.append(magnitudeSvg('text',{x:left-16,y:y(i)+4,'text-anchor':'end','font-size':14,fill:'#203d43'},row.Label));
    for(let k=0;k<row.Values.length;k++){const value=row.Values[k];if(value==null)continue;const dot=magnitudeSvg('circle',{cx:x(value*scale),cy:y(i)+(k-4.5)*6,r:3.3,fill:magnitudeColors[row.Method],stroke:'white','stroke-width':.7});dot.append(magnitudeSvg('title',{},`${row.Label}; repetition ${k+1}, seed ${seeds[k]}: ${magnitudeNumber(value*scale)} ${units}`));svg.append(dot)}
    const valid=row.Values.filter(v=>v!=null);if(valid.length){const mean=valid.reduce((a,b)=>a+b,0)/valid.length,sorted=[...valid].sort((a,b)=>a-b),median=(sorted[Math.floor((sorted.length-1)/2)]+sorted[Math.floor(sorted.length/2)])/2,mx=x(mean*scale),med=x(median*scale);svg.append(magnitudeSvg('line',{x1:mx,x2:mx,y1:y(i)-19,y2:y(i)+19,stroke:'#162e33','stroke-width':2}));svg.append(magnitudeSvg('polygon',{points:`${med},${y(i)-7} ${med+7},${y(i)} ${med},${y(i)+7} ${med-7},${y(i)}`,fill:'white',stroke:magnitudeColors[row.Method],'stroke-width':2}))}
  });
  svg.append(magnitudeSvg('text',{x:(left+width-right)/2,y:height-12,'text-anchor':'middle','font-size':13,fill:'#34474b'},units));container.append(svg);
}
function distributionStatistics(container,rows,scale){
  const table=document.createElement('table'),head=document.createElement('thead'),body=document.createElement('tbody'),hr=document.createElement('tr');
  for(const label of ['Method / setting','Available','Mean','Median','Minimum','Maximum','Mean absolute pairwise gap','Gap ÷ setting mean']){const th=document.createElement('th');th.textContent=label;hr.append(th)}head.append(hr);table.append(head,body);
  for(const r of rows){const s=r.Stats,tr=document.createElement('tr');for(const value of [r.Label,`${s.Observations}/${s.ExpectedObservations}`,s.Mean==null?'—':magnitudeNumber(s.Mean*scale),s.Median==null?'—':magnitudeNumber(s.Median*scale),s.Minimum==null?'—':magnitudeNumber(s.Minimum*scale),s.Maximum==null?'—':magnitudeNumber(s.Maximum*scale),s.MeanAbsolutePairwiseGap==null?'—':magnitudeNumber(s.MeanAbsolutePairwiseGap*scale),s.PairwiseGapFractionOfMean==null?'—':magnitudeNumber(s.PairwiseGapFractionOfMean*100)+'%']){const td=document.createElement('td');td.textContent=value;tr.append(td)}body.append(tr)}container.replaceChildren(table);
}
async function renderSearchDistributions(ids,metric,chartId,statsId,noteId){
  const data=await settingDistributions(metric),definition=data.Outcomes[metric],scale=definition.Kind==='cost'?10000:definition.Kind==='probability'?100:1;
  const units=definition.Kind==='cost'?'Model cost units per 10,000 opportunities':definition.Kind==='probability'?'Outcome rate (%); absolute gaps are percentage points':'Care level (0–7)';
  const rows=[];for(const m of ['gini','liu'])for(let i=0;i<ids.length;i++){const c=data.Cases[ids[i]],s=c.Methods[m][metric];rows.push({CaseId:ids[i],Method:m,Label:(m==='gini'?'Gini':'Liu')+(ids.length>1?' · '+(i?'B':'A'):''),Values:s.Values,Stats:s})}
  observedDotPlot(document.getElementById(chartId),rows,data.Seeds,{name:definition.Name+': observed initial-search distribution',scale,units,minimum:0,maximum:definition.Kind==='probability'?100:definition.Kind==='level'?7:null});
  distributionStatistics(document.getElementById(statsId),rows,scale);
  document.getElementById(noteId).textContent=definition.Name+'. '+definition.Meaning+' '+units+'. Dots: searches; black bar: mean; hollow diamond: median. Pairwise gaps use all unordered available pairs within a setting and method (45 when all ten are available). Missing conditional outcomes stay missing. These distributions describe numerical searches, not all possible equilibria or statistical confidence.';
}
async function showSettingDistribution(cid,metric){
  selectedDistributionCase=cid;
  try{const data=await settingDistributions(metric);if(selectedDistributionCase!==cid)return;document.getElementById('setting-distribution-title').textContent=data.Cases[cid].Name;await renderSearchDistributions([cid],metric,'setting-distribution-chart','setting-distribution-stats','setting-distribution-note');document.getElementById('setting-distributions').scrollIntoView({behavior:'smooth',block:'start'})}catch(error){document.getElementById('setting-distribution-note').textContent='The observations could not be loaded. Please reload.';console.error(error)}
}
function refreshSettingDistribution(metric){if(selectedDistributionCase)showSettingDistribution(selectedDistributionCase,metric)}
function addSettingDistributionButton(tr,row,metric){const td=document.createElement('td'),button=document.createElement('button');button.textContent='Inspect ten searches';button.onclick=()=>showSettingDistribution(row.Id,metric);td.append(button);tr.append(td)}
