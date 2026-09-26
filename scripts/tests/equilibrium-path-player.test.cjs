// Unit-test the C#-embedded player with a fake clock/DOM/canvas, not a browser.
// Run with: node --test scripts/tests/equilibrium-path-player.test.cjs
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../../LitigCharts/EquilibriumPathAnimation.cs'), 'utf8');
const script = source.match(/<script>\s*([\s\S]*?)<\/script>/)[1];

function fixture(id, count) {
  const sets = [0, 1].map(player => ({ index: player, tree: player, player,
    decision: player ? 'D Answers' : 'P Files', signal: .25, start: player * 2, actions: ['Yes', 'No'] }));
  const frames = Array.from({ length: count }, (_, step) => {
    const p = step < 2 ? .5 : step === count - 1 ? 1 : .8;
    return { step, p: [p, 1-p, 1-p, p], q: [2, 1, 1, 2], a: [.1, -.1, -.1, .1],
      r: [.5, .5], gaps: [.1, .1], outside: [0, 0], fallback: [], epsilon: count-1-step, z: step ? .5 : 1 };
  });
  return { id, title: id, pivots: count-1, maxGain: .1, sets, frames,
    groups: sets.map(s => ({ player: s.player, label: 'Enter', sets: [s.index], actions: s.actions })) };
}

async function player({ reduced = false, width = 1000, cases = [fixture('American', 4), fixture('British', 5)] } = {}) {
  let now = 0, raf, fills = [], rectangles = [], markers = 0, download;
  const context = new Proxy({ fillRect(x, y, w, h) {
    if (x === 0 && y === 0) { fills = []; rectangles = []; markers = 0; }
    else { fills.push(this.fillStyle); rectangles.push({x,y,w,h}); }
  }, fill() { markers++; } }, { get: (target, key) => key in target ? target[key] : () => {} });
  const elements = new Map();
  function element(id) {
    if (!elements.has(id)) elements.set(id, {
      value: '', textContent: '', checked: false, disabled: false, style: {}, attributes: {},
      parentElement: { clientWidth: width }, offsetWidth: 150, offsetHeight: 100,
      setAttribute(key, value) { this.attributes[key] = value; }, appendChild() {},
      getContext() { return context; }, getBoundingClientRect() { return { left: 0, top: 0 }; },
      toDataURL() { return 'data:image/png;base64,test'; }
    });
    return elements.get(id);
  }
  element('speed').value = '12'; element('skip').checked = true;
  const media = { matches: reduced, addEventListener(_, callback) { this.change = callback; } };
  const document = { getElementById: element, createElement(tag) {
    return tag === 'a' ? { click() { download = this.download; } } : {};
  } };
  const environment = {
    document, Uint8Array, atob: () => '',
    Blob: class { stream() { return { pipeThrough() { return {}; } }; } },
    DecompressionStream: class {}, Response: class { async text() { return JSON.stringify(cases); } },
    window: { devicePixelRatio: 1, matchMedia: () => media },
    performance: { now: () => now }, requestAnimationFrame: callback => { raf = callback; },
    ResizeObserver: class { observe() {} }
  };
  await vm.runInNewContext(script, environment);
  assert.doesNotMatch(element('detail').textContent, /Unable to load/);
  return {
    element, media, cases,
    tick(time) { now = time; const callback = raf; raf = undefined; callback(time); },
    click(id) { if (!element(id).disabled) element(id).onclick(); },
    select(index) { element('case').value = String(index); element('case').onchange(); },
    scrub(value) { element('scrub').value = String(value); element('scrub').oninput(); },
    get firstFill() { return fills[0]; }, get fills() { return fills; }, get markers() { return markers; },
    get rectangles() { return rectangles; },
    get download() { return download; }
  };
}

test('Agreement histories remain complete and inside the canvas at desktop and narrow widths', async () => {
  const labels = ['Enter', 'Commit to exit', 'Agree · continue', 'Agree · exit', 'Offer · continue', 'Offer · exit'];
  const c = fixture('Agreement', 3);
  c.sets = Array.from({length:12}, (_,i) => ({index:i,tree:i,player:Math.floor(i/6),
    decision:labels[i%6],signal:.25,start:i*2,actions:['Yes','No']}));
  c.groups = c.sets.map(s => ({player:s.player,label:s.decision,sets:[s.index],actions:s.actions}));
  c.frames = c.frames.map(f => ({...f,p:Array(24).fill(.5),q:Array(24).fill(1),
    a:Array(24).fill(0),r:Array(12).fill(.5),gaps:Array(12).fill(0),outside:Array(12).fill(0)}));
  for(const width of [375,1000]) {
    const ui=await player({cases:[c],width});
    assert.equal(ui.rectangles.length,24);
    for(const rect of ui.rectangles) {
      assert.ok(rect.w>0 && rect.h>0 && rect.x>=0 && rect.x+rect.w<=width);
      assert.ok(rect.y>=0 && rect.y+rect.h<=ui.element('map').height);
    }
    assert.ok(ui.element('map').height>=280,'both players have separate agreement and offer bands');
    assert.match(ui.element('protocol').textContent,/only after both parties agree/);
    ui.click('end');
    assert.equal(ui.rectangles.length,24);
  }
});

test('Play stops at selected case; replay and step buttons never cross cases', async () => {
  const ui = await player();
  ui.click('play'); ui.tick(1100); ui.tick(1163); ui.tick(1200); ui.tick(1263);
  assert.equal(ui.element('play').textContent, 'Play');
  assert.equal(Number(ui.element('case').value), 0);
  assert.equal(Number(ui.element('scrub').value), 3);
  ui.tick(10000); ui.click('next');
  assert.equal(Number(ui.element('case').value), 0);
  ui.click('play');
  assert.equal(Number(ui.element('scrub').value), 0);
  ui.select(1);
  assert.equal(ui.element('play').textContent, 'Play');
  assert.equal(Number(ui.element('scrub').max), 4);
  ui.click('previous');
  assert.equal(Number(ui.element('case').value), 1);
  assert.equal(Number(ui.element('scrub').value), 0);
});

test('Blue fills interpolate; readouts and hover stay at the destination pivot', async () => {
  const ui = await player(); const initial = ui.firstFill, initialHint = ui.element('detail').textContent;
  const originalData = JSON.stringify(ui.cases);
  ui.click('play'); ui.tick(1100);
  assert.equal(ui.firstFill, initial);
  assert.match(ui.element('numbers').textContent, /Pivot 2 /);
  assert.equal(ui.element('detail').textContent, initialHint);
  assert.doesNotMatch(ui.element('detail').textContent, /Color transition/);
  ui.tick(1131.25); const middle = ui.firstFill;
  assert.notEqual(middle, initial);
  ui.element('map').onpointermove({ clientX: 70, clientY: 35 });
  assert.match(ui.element('tip').textContent, /Probability: 0.8000/);
  ui.tick(1163); const endpoint = ui.firstFill;
  assert.notEqual(endpoint, middle);
  assert.doesNotMatch(ui.element('detail').textContent, /Color transition/);
  assert.equal(JSON.stringify(ui.cases), originalData);
});

test('Scrubbing is exact and keeps selected value even while playing', async () => {
  const ui = await player();
  ui.click('play'); ui.tick(1100); ui.scrub(1);
  assert.equal(Number(ui.element('scrub').value), 1);
  assert.equal(ui.element('play').textContent, 'Play');
  assert.doesNotMatch(ui.element('detail').textContent, /Color transition/);
  ui.select(1); ui.scrub(3);
  assert.equal(Number(ui.element('scrub').value), 3);
  assert.equal(Number(ui.element('case').value), 1);
});

test('Pause and PNG export snap to the recorded destination, not a faded frame', async () => {
  const ui = await player();
  ui.click('play'); ui.tick(1100); ui.tick(1131.25); const middle = ui.firstFill;
  ui.click('play');
  assert.notEqual(ui.firstFill, middle);
  assert.doesNotMatch(ui.element('detail').textContent, /Color transition/);
  ui.click('next'); const oldFill = ui.firstFill;
  ui.click('png');
  assert.notEqual(ui.firstFill, oldFill);
  assert.equal(ui.download, 'American-pivot-3.png');
  assert.equal(ui.element('play').textContent, 'Play');
});

test('Reduced motion and smoothing toggle disable fades; steps retain unchanged pivots', async () => {
  const ui = await player({ reduced: true });
  assert.equal(ui.element('smooth').disabled, true);
  ui.click('next'); assert.equal(Number(ui.element('scrub').value), 1);
  ui.click('next'); assert.equal(Number(ui.element('scrub').value), 2);
  assert.doesNotMatch(ui.element('detail').textContent, /Color transition/);
  ui.media.matches = false; ui.media.change();
  assert.equal(ui.element('smooth').disabled, false);
  ui.element('smooth').checked = false; ui.element('smooth').onchange();
  ui.click('next');
  assert.doesNotMatch(ui.element('detail').textContent, /Color transition/);
  ui.media.matches = true; ui.media.change();
  assert.equal(ui.element('smooth').checked, false);
});

test('Single-case player has the same local controls without a case menu', async () => {
  const ui = await player({ cases: [fixture('American', 4)] });
  assert.equal(ui.element('case').parentElement.hidden, true);
  ui.click('end'); ui.click('play');
  assert.equal(Number(ui.element('scrub').value), 0);
  assert.equal(Number(ui.element('scrub').max), 3);
});

test('Unreached destination rows stay blank during fades; reached zero probability is faint blue', async () => {
  const c = fixture('American', 4);
  c.frames[2].r[0] = 0;
  c.frames[2].fallback = [0];
  const ui = await player({ cases: [c] });
  const original = JSON.stringify(c);
  ui.click('play'); ui.tick(1100);
  assert.deepEqual(ui.fills.slice(0, 2), ['#ffffff', '#ffffff']);
  assert.equal(ui.markers, 1, 'only the reached defendant may have a gain corner');
  ui.tick(1131.25);
  assert.deepEqual(ui.fills.slice(0, 2), ['#ffffff', '#ffffff']);
  ui.element('map').onpointermove({ clientX: 70, clientY: 35 });
  assert.match(ui.element('tip').textContent, /Unreached information set/);
  assert.match(ui.element('tip').textContent, /Stored off-path probability: 0.8000/);
  ui.click('end');
  assert.equal(ui.fills[1], 'rgb(225,236,244)');
  assert.equal(ui.fills[0], 'rgb(35,79,112)');
  assert.equal(JSON.stringify(c), original);
});

test('Unreached rows are blank even with defined positive off-path advantages', async () => {
  const c = fixture('American', 4);
  c.frames[3].r = [1e-11, 0];
  c.frames[3].fallback = [0, 1];
  const ui = await player({ cases: [c] });
  ui.click('end');
  assert.deepEqual(ui.fills, Array(4).fill('#ffffff'));
  assert.equal(ui.markers, 0);
});
