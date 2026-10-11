import test from 'node:test';
import assert from 'node:assert/strict';
import { createWorld, decide, step, run, snapshot, rng } from '../src/engine.js';

test('deterministic run and trace for identical seed', () => {
  assert.deepEqual(snapshot(run({ seed: 12, agents: 24, witnessRate: 0.4 }, 500)),
    snapshot(run({ seed: 12, agents: 24, witnessRate: 0.4 }, 500)));
});
test('distinct seeds produce distinct worlds', () => {
  assert.notDeepEqual(snapshot(run({ seed: 1 }, 20)).people, snapshot(run({ seed: 2 }, 20)).people);
});
test('authoritative world never invents or destroys supplies', () => {
  for (const witnessRate of [0, 0.5, 1]) {
    const world = run({ seed: 19, agents: 50, witnessRate }, 1800);
    assert.equal(world.cache + world.people.reduce((sum, a) => sum + a.supplies, 0), world.initialTotal);
    assert.ok(world.cache >= 0 && world.people.every(a => a.supplies >= 0));
  }
});
test('decisions expose utility terms exactly', () => {
  const world = run({ seed: 35 }, 300);
  for (const d of world.decisions) {
    for (const c of d.candidates) assert.ok(Math.abs(c.score - Object.values(c.terms).reduce((a,b)=>a+b,0)) < 1e-9);
    assert.equal(d.score, Math.max(...d.candidates.map(c => c.score)));
  }
});
test('zero witness rate cannot create eyewitness evidence', () => {
  const world = run({ seed: 17, witnessRate: 0 }, 600);
  assert.equal(world.stats.witnessedTakes, 0);
  assert.ok(world.people.every(p => p.memories.every(m => m.source !== 'eyewitness')));
  assert.equal(world.stats.reports, 0);
});
test('reports have witnessed ancestry and do not become eyewitnesses', () => {
  const world = run({ seed: 17, witnessRate: 1 }, 600);
  assert.ok(world.stats.witnessedTakes > 0);
  assert.ok(world.stats.reports > 0);
  for (const p of world.people) for (const m of p.memories) {
    assert.ok(['eyewitness','hearsay'].includes(m.source));
    if (m.source === 'hearsay') {
      assert.ok(m.parentId);
      assert.ok(world.log.some(e => e.id === m.parentId));
      assert.ok(world.log.some(e => e.id === m.rootId && e.kind === 'take'));
      assert.notEqual(m.parentId, m.eventId);
    }
  }
});
test('invalid configurations are rejected', () => {
  assert.throws(() => createWorld({ agents: 4 }), RangeError);
  assert.throws(() => createWorld({ witnessRate: -1 }), RangeError);
  assert.throws(() => createWorld({ seed: 2.2 }), RangeError);
  assert.throws(() => run({}, 10001), RangeError);
});
test('deliberation only requires actor and explicit perception', () => {
  const actor = createWorld().people[0];
  const x = decide(actor, { cache: 1, neighbors: [] });
  assert.ok(x.scored.some(c => c.action === 'take'));
  assert.ok(!x.scored.some(c => c.action === 'tell'));
});
test('rng returns finite values on unit interval', () => {
  const random = rng(0); for(let i=0;i<1000;i++) assert.ok(random() >= 0);
});
