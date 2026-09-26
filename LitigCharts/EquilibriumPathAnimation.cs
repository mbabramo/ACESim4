using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using ACESim;
using System.Globalization;
using ACESimBase.Games.LitigGame.ManualReports;
using static ACESimBase.Games.LitigGame.ManualReports.ArticleEquilibriumPaths;

namespace LitigCharts;

/// <summary>Offline, data-driven player for verified original ECTA derivations.</summary>
public static class EquilibriumPathAnimation
{
    public sealed record Run(PathResult Metadata, PathFrame[] Frames);
    public static Run ReadVerified(string metadataFile)
    {
        var m = JsonSerializer.Deserialize<PathResult>(File.ReadAllText(metadataFile), CompactJson)
            ?? throw new InvalidDataException("Empty trace metadata.");
        if (m.Schema != "2" || !m.Exact || m.Seed != 0 || m.Pivots != m.OriginalPivots ||
            m.Steps != m.Pivots + 1 || m.MaximumSavedPolicyDifference > 1e-10 || m.FinalEpsilon > 1e-7)
            throw new InvalidDataException("Not a verified original-equilibrium replay.");
        string framesFile = Path.ChangeExtension(metadataFile, ".jsonl");
        CheckHash(framesFile, m.Frames.Sha256);
        foreach (var input in m.Inputs) CheckHash(input.Path, input.Sha256);
        var frames = File.ReadLines(framesFile).Select(line =>
            JsonSerializer.Deserialize<PathFrame>(line, CompactJson)
            ?? throw new InvalidDataException("Empty frame.")).ToArray();
        ValidateFrames(m, frames);
        return new(m, frames);
    }

    public static void ValidateFrames(PathResult m, PathFrame[] frames)
    {
        int actions = m.InformationSets.Sum(s => s.Actions.Length);
        if (frames.Length != m.Steps || frames.Length < 2 ||
            frames[0].Kind != "initial-prior" || frames[0].Native != null ||
            frames[^1].Native?.Final != true || frames[^1].Native.Auxiliary != 0)
            throw new InvalidDataException("Incomplete pivot stream.");
        int offset = 0;
        foreach (var (set, i) in m.InformationSets.Select((s, i) => (s, i)))
        {
            if (set.Index != i || set.FirstAction != offset || set.Actions.Length == 0)
                throw new InvalidDataException("Invalid information-set ordering.");
            offset += set.Actions.Length;
        }
        for (int i = 0; i < frames.Length; i++)
        {
            var f = frames[i]; var s = f.Strategy;
            if (f.Step != i || (i > 0 && (f.Native?.Pivot != i || f.Native.Final != (i == frames.Length - 1))) ||
                s.Probabilities.Length != actions || s.ActionAdvantages.Length != actions ||
                s.ActionUtilities.Length != actions || s.ActualReach.Length != m.InformationSets.Length ||
                s.LocalGaps.Length != m.InformationSets.Length || !double.IsFinite(s.Epsilon))
                throw new InvalidDataException("Invalid frame ordering or dimensions.");
            foreach (var set in m.InformationSets)
            {
                var p = s.Probabilities.Skip(set.FirstAction).Take(set.Actions.Length).ToArray();
                if (p.Any(v => !double.IsFinite(v) || v < 0 || v > 1) || Math.Abs(p.Sum() - 1) > 1e-9)
                    throw new InvalidDataException("Invalid frame probabilities.");
                if (i == 0 && p.Any(v => Math.Abs(v - 1.0 / p.Length) > 1e-14))
                    throw new InvalidDataException("Initial frame is not the original uniform prior.");
            }
            if (s.ActionUtilities.Concat(s.ActionAdvantages).Any(v => v.HasValue && !double.IsFinite(v.Value)))
                throw new InvalidDataException("Nonfinite local incentive.");
        }
        if (Math.Abs(frames[^1].Strategy.Epsilon - m.FinalEpsilon) > 1e-10)
            throw new InvalidDataException("Final diagnostic does not match metadata.");
    }

    private static void CheckHash(string file, string expected)
    {
        using var stream = File.OpenRead(file);
        if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Trace/input fingerprint mismatch: " + file);
    }

    public static string[] Render(string requestFile)
    {
        requestFile = Path.GetFullPath(requestFile);
        var request = JsonSerializer.Deserialize<PathRequest>(File.ReadAllText(requestFile), CompactJson);
        string output = Path.GetFullPath(request.OutputDirectory, Path.GetDirectoryName(requestFile));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "equilibrium-paths-manifest.json")));
        if (manifest.RootElement.GetProperty("Schema").GetString() != "2")
            throw new InvalidDataException("A completed original-replay manifest is required.");
        var fingerprints = manifest.RootElement.GetProperty("Results").EnumerateArray()
            .ToDictionary(e => Path.GetFileName(e.GetProperty("Path").GetString()), e => e.GetProperty("Sha256").GetString());
        var runs = request.Equilibria.Select(e =>
        {
            string file = Path.Combine(output, e.Source + ".json");
            CheckHash(file, fingerprints[Path.GetFileName(file)]);
            var run = ReadVerified(file);
            if (run.Metadata.Id != e.Source) throw new InvalidDataException("Wrong selected trace.");
            return run;
        }).ToArray();
        var written = new List<string>();
        foreach (var run in runs)
        {
            string file = Path.Combine(output, run.Metadata.Id + ".html");
            File.WriteAllText(file, BuildHtml(new[] { run }));
            written.Add(file);
        }
        string combined = Path.Combine(output, "all-equilibrium-solution-paths.html");
        File.WriteAllText(combined, BuildHtml(runs)); written.Add(combined);
        return written.ToArray();
    }

    public static string BuildHtml(Run[] runs)
    {
        if (runs.Length == 0) throw new ArgumentException("Supply at least one verified run.");
        var data = runs.Select(run =>
        {
            var m = run.Metadata;
            var groups = new List<object>();
            for (byte player = 0; player < 2; player++)
            {
                string prefix = player == 0 ? "P" : "D";
                bool agreement = m.InformationSets.Any(s => s.Decision is "P Agrees To Bargain" or "D Agrees To Bargain");
                var specifications = agreement ? new[] {
                    (Decision: player == 0 ? "P Files" : "D Answers", Commit: (int?)null, Label: "Enter"),
                    (Decision: player == 0 ? "P Abandons" : "D Defaults", Commit: (int?)null, Label: "Commit to exit"),
                    (Decision: prefix + " Agrees To Bargain", Commit: (int?)2, Label: "Agree · continue"),
                    (Decision: prefix + " Agrees To Bargain", Commit: (int?)1, Label: "Agree · exit"),
                    (Decision: prefix + " Offer", Commit: (int?)2, Label: "Offer · continue"),
                    (Decision: prefix + " Offer", Commit: (int?)1, Label: "Offer · exit")
                } : new[] {
                    (Decision: player == 0 ? "P Files" : "D Answers", Commit: (int?)null, Label: "Enter"),
                    (Decision: prefix + " Offer", Commit: (int?)2, Label: "Offer · continue"),
                    (Decision: prefix + " Offer", Commit: (int?)1, Label: "Offer · exit"),
                    (Decision: player == 0 ? "P Abandons" : "D Defaults", Commit: (int?)null, Label: "Exit")
                };
                var included = new HashSet<int>();
                foreach (var spec in specifications)
                {
                    var sets = m.InformationSets.Where(s => s.Player == player && s.Decision == spec.Decision &&
                        s.ExitCommitment == spec.Commit).OrderByDescending(s => s.Signal).ToArray();
                    if (sets.Length == 0 || sets.Any(s => !s.Actions.SequenceEqual(sets[0].Actions)) ||
                        sets.Select(s => s.Signal).Distinct().Count() != sets.Length)
                        throw new InvalidDataException("Unsupported strategy-panel structure.");
                    foreach (var set in sets) included.Add(set.Index);
                    groups.Add(new { player, label = spec.Label, sets = sets.Select(s => s.Index).ToArray(), actions = sets[0].Actions });
                }
                if (included.Count != m.InformationSets.Count(s => s.Player == player))
                    throw new InvalidDataException("The panel layout would omit information sets.");
            }
            double maximumGain = run.Frames.SelectMany(f => f.Strategy.ActionAdvantages)
                .Where(a => a.HasValue).Select(a => Math.Max(0, a.Value)).DefaultIfEmpty().Max();
            var options = ArticleWorkedPathExtraction.CreateOptions(m.OptionSet);
            string title = LitigGameCorrelatedSignalsArticleLauncher.FeeRuleLabel(options) + " · " +
                ArticleResultsLayout.Risk(Convert.ToDouble(options.VariableSettings["CARA Alpha"], CultureInfo.InvariantCulture));
            return new
            {
                id = m.Id, title, pivots = m.Pivots, maxGain = maximumGain, groups,
                sets = m.InformationSets.Select(s => new { index = s.Index, tree = s.TreeIndex, player = s.Player,
                    decision = s.Decision, signal = s.SignalValue, exitCommitment = s.ExitCommitment,
                    start = s.FirstAction, actions = s.Actions }),
                frames = run.Frames.Select(f => new { step = f.Step, z = f.Native?.Auxiliary ?? 1,
                    epsilon = f.Strategy.Epsilon, p = f.Strategy.Probabilities, q = f.Strategy.ActionUtilities,
                    a = f.Strategy.ActionAdvantages, r = f.Strategy.ActualReach,
                    gaps = f.Strategy.LocalGaps, outside = f.Strategy.OutsideSupportGaps,
                    fallback = f.PriorCompletedInformationSets })
            };
        }).ToArray();
        using var packed = new MemoryStream();
        using (var gzip = new GZipStream(packed, CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data, CompactJson)));
        return Template.Replace("__TRACE_DATA__", Convert.ToBase64String(packed.ToArray()));
    }

    private const string Template = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Solution paths</title>
<style>
:root{color-scheme:light;font-family:Segoe UI,Arial,sans-serif;color:#253441;background:#fafbf9}
body{margin:0;padding:22px}main{max-width:1080px;margin:auto}
h1{font-size:21px;font-weight:600;margin:0 0 6px}
.controls,.status,.legend{display:flex;gap:12px;align-items:center;flex-wrap:wrap}
.controls{margin:10px 0}button,select{font:inherit;font-size:13px;padding:7px 11px;border:1px solid #b7c3c9;border-radius:4px;background:white;color:#253441}
button{cursor:pointer}button.primary{background:#234f70;color:white;border-color:#234f70}
label{font-size:13px;display:inline-flex;align-items:center;gap:5px}.status{justify-content:space-between;font-size:13px;font-variant-numeric:tabular-nums;margin:12px 0 4px}
#case-title{font-size:17px;font-weight:600}#scrub{width:100%;accent-color:#234f70;margin:9px 0}
#stage{position:relative;background:white}canvas{display:block;width:100%}
.legend{font-size:12px;gap:18px;margin:12px 0}.key{display:inline-flex;gap:6px;align-items:center}.swatch{display:inline-block;width:68px;height:12px}
.prob{background:linear-gradient(90deg,#e1ecf4,#234f70)}.gain{background:linear-gradient(90deg,#ffe1b2,#c54b08)}
.unreached{width:18px;background:white;border:1px solid #b7c3c9}
#tip{position:absolute;display:none;pointer-events:none;white-space:pre-line;background:#fffef7;border:1px solid #89979e;padding:9px 12px;font-size:12px;line-height:1.5;box-shadow:0 3px 12px #0002;z-index:2;max-width:320px}
#detail{font-size:12px;min-height:18px;margin:8px 0;color:#52616b}
@media(max-width:600px){body{padding:12px}.controls{gap:8px}#case-title{font-size:15px}}
</style>
</head>
<body>
<main>
<h1>Solution paths</h1>
<div class="controls">
<label>Case <select id="case" aria-label="Case"></select></label>
<button id="play" class="primary">Play</button>
<button id="previous" aria-label="Previous pivot">◀ Step</button>
<button id="next" aria-label="Next pivot">Step ▶</button>
<button id="end">Equilibrium</button>
<label>Speed <select id="speed" aria-label="Playback speed"><option value="4">4 steps/s</option><option value="12" selected>12 steps/s</option><option value="30">30 steps/s</option></select></label>
</div>
<div class="status"><span id="case-title"></span><span id="numbers"></span></div>
<input id="scrub" type="range" min="0" step="1" value="0" aria-label="Pivot in selected case">
<div id="stage"><canvas id="map" role="img" aria-label="Complete strategies: plaintiff above defendant; low signals at the bottom, high signals at the top; columns are actions. Blue encodes action probability and orange corners encode positive action advantage."></canvas><div id="tip" role="tooltip"></div></div>
<p id="protocol" style="font-size:13px"></p>
<div class="legend">
<span class="key"><span class="swatch prob"></span>Probability 0 → 1</span>
<span class="key"><span class="swatch gain"></span><span id="gain-range">▲ Positive action advantage</span></span>
<span class="key"><span class="swatch unreached"></span>Unreached information set</span>
</div>
<div class="controls">
<label><input id="skip" type="checkbox" checked>Skip unchanged probabilities</label>
<label><input id="smooth" type="checkbox" checked>Smooth color transitions</label>
<button id="png">Save frame as PNG</button>
</div>
<div id="detail">Hover or tap a cell for its probability and conditional utility.</div>
</main>
<script id="trace-data" type="application/gzip">__TRACE_DATA__</script>
<script>
'use strict';
async function initializePlayer() {
const bytes=Uint8Array.from(atob(document.getElementById('trace-data').textContent.trim()),c=>c.charCodeAt(0));
const unpacked=new Blob([bytes]).stream().pipeThrough(new DecompressionStream('gzip'));
const cases=JSON.parse(await new Response(unpacked).text());
const $ = id => document.getElementById(id);
const canvas = $('map'), ctx = canvas.getContext('2d');
cases.forEach((c,i) => {
 const option = document.createElement('option'); option.value=i; option.textContent=c.title; $('case').appendChild(option); });
let position=0, playing=false, lastTick=0, boxes=[], currentCase=0, width=1000, height=500, transition=null;
const reducedMotion=window.matchMedia('(prefers-reduced-motion: reduce)');
$('smooth').checked=!reducedMotion.matches; $('smooth').disabled=reducedMotion.matches;
const number = x => x === null ? 'undefined' : Math.abs(x) < 1e-10 ? '0' : Number(x).toPrecision(4);
function color(low,high,t) { t=Math.max(0,Math.min(1,t)); return 'rgb('+low.map((v,i)=>Math.round(v+(high[i]-v)*t)).join(',')+')'; }
function text(value,x,y,size=14,align='left') { ctx.fillStyle='#253441'; ctx.font=size+'px Segoe UI,Arial,sans-serif'; ctx.textAlign=align; ctx.fillText(value,x,y); }
function draw() {
 const c=cases[currentCase], fi=position, f=c.frames[fi];
 const progress=transition?Math.min(1,Math.max(0,(performance.now()-transition.start)/transition.duration)):1;
 const blend=progress*progress*(3-2*progress);
 $('case').value=currentCase; $('scrub').max=c.frames.length-1; $('scrub').value=position; $('case-title').textContent=c.title;
 $('previous').disabled=fi===0; $('next').disabled=fi===c.frames.length-1;
 $('numbers').textContent='Pivot '+f.step+' / '+c.pivots+'   ·   ε '+number(f.epsilon)+'   ·   z₀ '+number(f.z);
 $('gain-range').textContent='▲ Action advantage 0 → '+number(c.maxGain);
 $('scrub').setAttribute('aria-valuetext',c.title+', pivot '+f.step+' of '+c.pivots);
 const compact=canvas.parentElement.clientWidth<680; width=canvas.parentElement.clientWidth;
 const rowHeight=15, rowCount=Math.max(...c.groups.map(g=>g.sets.length));
 const panelHeight=rowCount*rowHeight+55, left=compact?35:65, gap=compact?9:20, top=32;
 const bands=[0,1].flatMap(player=>{
  const groups=c.groups.filter(g=>g.player===player);
  return groups.length>4?[{player,groups:groups.slice(0,4)},{player,groups:groups.slice(4)}]:[{player,groups}];
 });
 height=panelHeight*bands.length+10; const dpr=Math.min(window.devicePixelRatio||1,2);
 canvas.width=Math.round(width*dpr); canvas.height=Math.round(height*dpr); canvas.style.height=height+'px'; ctx.scale(dpr,dpr);
 ctx.fillStyle='#ffffff';ctx.fillRect(0,0,width,height);boxes=[];
 for(let band=0;band<bands.length;band++) {
  const {player,groups}=bands[band], cols=groups.reduce((n,g)=>n+g.actions.length,0);
  const cw=(width-left-16-gap*(groups.length-1))/cols, y=top+band*panelHeight;
  text(player===0?'P':'D',compact?12:22,y+rowCount*rowHeight/2,22,'center');
  let x=left;
  for(const g of groups) {
   const span=g.actions.length*cw;
   const heading=compact?g.label.replace(' · continue',' / stay').replace(' · exit',' / exit').replace('Commit to exit','Exit intent'):g.label;
   text(heading,x+span/2,y-15,compact?11:14,'center');
   g.sets.forEach((si,row) => {
    const s=c.sets[si], yy=y+row*rowHeight, unreached=f.r[si]<=1e-10;
    for(let a=0;a<g.actions.length;a++) {
     const j=s.start+a, xx=x+a*cw, w=cw-1.5, h=rowHeight-1.5;
     const fillProbability=transition?transition.from.p[j]+(f.p[j]-transition.from.p[j])*blend:f.p[j];
     // Reach belongs to the destination pivot, even during a visual probability fade.
     // Blank rows do not depict the arbitrary stored off-path completion.
     ctx.fillStyle=unreached?'#ffffff':color([225,236,244],[35,79,112],fillProbability);ctx.fillRect(xx,yy,w,h);
     const advantage=f.a[j];
     if(!unreached && advantage!==null && advantage>1e-10) {
      const size=Math.min(w*.48,h*.74);
      ctx.fillStyle=color([255,225,178],[197,75,8],Math.sqrt(advantage/Math.max(c.maxGain,1e-10)));
      ctx.beginPath();ctx.moveTo(xx+w,yy);ctx.lineTo(xx+w-size,yy);ctx.lineTo(xx+w,yy+size);ctx.closePath();ctx.fill();
     }
     boxes.push({x:xx,y:yy,w,h,si,j,a});
    }
   });
   if(g.actions.length===2 && !compact) {text('yes',x+cw/2,y+rowCount*rowHeight+16,11,'center');text('no',x+cw*1.5,y+rowCount*rowHeight+16,11,'center');}
   x+=span+gap;
  }
 }
 $('protocol').textContent=c.groups.some(g=>g.label.startsWith('Agree'))?'Private exit commitments precede simultaneous agreement decisions. Offers occur only after both parties agree. These are numerical ECTA paths, not litigant learning.':'';
 if(!compact){ctx.save();ctx.translate(45,top+rowCount*rowHeight/2);ctx.rotate(-Math.PI/2);text('signal: low → high',0,0,11,'center');ctx.restore();}
 $('detail').textContent=fi===c.frames.length-1?'Original equilibrium verified · '+c.pivots+' pivots · all saved probabilities matched.':'Hover or tap a cell for its probability and conditional utility.';
 canvas.setAttribute('aria-label',c.title+', pivot '+f.step+'. Complete P and D strategies, low signals at the bottom and high signals at the top. Maximum unrestricted gain '+number(f.epsilon)+'.');
 $('tip').style.display='none';
}
function setPosition(value,animate=false){
 const c=cases[currentCase],from=c.frames[position];
 position=Math.max(0,Math.min(c.frames.length-1,Number(value))); transition=null;
 if(animate && $('smooth').checked && !reducedMotion.matches && !same(from,c.frames[position]))
  transition={from,start:performance.now(),duration:Math.min(220,750/Number($('speed').value))};
 draw();
}
function stop(){playing=false;transition=null;$('play').textContent='Play';draw();}
function same(a,b){return a.p.every((p,i)=>Math.abs(p-b.p[i])<=1e-12);}
function advance() {
 const c=cases[currentCase];
 if(position===c.frames.length-1){stop();return;}
 let next=position+1;
 if($('skip').checked)
  while(next<c.frames.length-1 && same(c.frames[position],c.frames[next]))next++;
 setPosition(next,true);
}
$('play').onclick=()=>{if(playing)stop();else {if(position===cases[currentCase].frames.length-1)setPosition(0);playing=true;$('play').textContent='Pause';lastTick=performance.now();}};
$('previous').onclick=()=>{stop();setPosition(position-1,true);};
$('next').onclick=()=>{stop();setPosition(position+1,true);};
$('end').onclick=()=>{stop();setPosition(cases[currentCase].frames.length-1);};
$('case').onchange=()=>{const selected=Number($('case').value);stop();currentCase=selected;position=0;setPosition(0);};
$('scrub').oninput=()=>{const selected=$('scrub').value;stop();setPosition(selected);};
$('smooth').onchange=()=>{transition=null;draw();};
reducedMotion.addEventListener('change',()=>{$('smooth').checked=!reducedMotion.matches;$('smooth').disabled=reducedMotion.matches;transition=null;draw();});
function tick(now) {
 if(transition){if(now-transition.start>=transition.duration)transition=null;draw();}
 if(playing && !transition) {
  if(position===cases[currentCase].frames.length-1){stop();requestAnimationFrame(tick);return;}
  const interval=position===0?1100:1000/Number($('speed').value);
  if(now-lastTick>=interval){advance();lastTick=now;}
 }
 requestAnimationFrame(tick);
}
function inspect(event) {
 const rect=canvas.getBoundingClientRect(), x=event.clientX-rect.left,y=event.clientY-rect.top;
 const b=boxes.find(b=>x>=b.x&&x<b.x+b.w&&y>=b.y&&y<b.y+b.h), tip=$('tip');
 if(!b){tip.style.display='none';return;}
 const c=cases[currentCase],f=c.frames[position],s=c.sets[b.si];
 const label=(s.player===0?'P':'D')+' · '+s.decision+' · signal '+s.signal.toFixed(2)+' · action '+s.actions[b.a];
 const unreached=f.r[b.si]<=1e-10;
 const details=label+(unreached?'\nUnreached information set\nStored off-path probability: ':'\nProbability: ')+number(f.p[b.j])+'\nConditional utility: '+number(f.q[b.j])+
  '\nAdvantage over current mix: '+number(f.a[b.j])+'\nInformation-set gap: '+number(f.gaps[b.si])+
  '\nOutside-support gap: '+number(f.outside[b.si])+'\nActual reach: '+number(f.r[b.si])+
  (f.fallback.includes(s.tree)?'\nUniform completion at zero realization':'');
 tip.textContent=details;tip.style.display='block';tip.style.left=Math.max(0,Math.min(x+14,width-tip.offsetWidth-4))+'px';
 tip.style.top=Math.max(0,Math.min(y+14,height-tip.offsetHeight-4))+'px';$('detail').textContent=label+' · p '+number(f.p[b.j])+' · advantage '+number(f.a[b.j]);
}
canvas.onpointermove=inspect;canvas.onclick=inspect;canvas.onpointerleave=()=>{$('tip').style.display='none';};
$('png').onclick=()=>{stop();const a=document.createElement('a');a.download=cases[currentCase].id+'-pivot-'+position+'.png';a.href=canvas.toDataURL('image/png');a.click();};
if(cases.length===1)$('case').parentElement.hidden=true;
stop();new ResizeObserver(()=>draw()).observe(canvas.parentElement);requestAnimationFrame(tick);
}
initializePlayer().catch(error=>{document.getElementById('detail').textContent='Unable to load animation: '+error.message;document.getElementById('detail').setAttribute('role','alert');});
</script>
</body>
</html>
""";
}
