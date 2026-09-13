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

async function player({ reduced = false, cases = [fixture('American', 4), fixture('British', 5)] } = {}) {
  let now = 0, raf, fills = [], download;
  const context = new Proxy({ fillRect(x, y, w, h) {
    if (this.fillStyle === '#ffffff') fills = [];
    else fills.push(this.fillStyle);
  } }, { get: (target, key) => key in target ? target[key] : () => {} });
  const elements = new Map();
  function element(id) {
    if (!elements.has(id)) elements.set(id, {
      value: '', textContent: '', checked: false, disabled: false, style: {}, attributes: {},
      parentElement: { clientWidth: 1000 }, offsetWidth: 150, offsetHeight: 100,
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
    get firstFill() { return fills[0]; }, get download() { return download; }
  };
}

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
  const ui = await player(); const initial = ui.firstFill;
  const originalData = JSON.stringify(ui.cases);
  ui.click('play'); ui.tick(1100);
  assert.equal(ui.firstFill, initial);
  assert.match(ui.element('numbers').textContent, /Pivot 2 /);
  assert.match(ui.element('detail').textContent, /Color transition 0 → 2/);
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
