import test from 'node:test';
import assert from 'node:assert/strict';
import { once, study, posterior } from '../scripts/experiment-001.mjs';

const near = (a, b, tol = 1e-10) => assert.ok(Math.abs(a - b) <= tol, String(a) + ' vs ' + String(b));

test('prior and posterior are analytical Bayes odds', () => {
  near(posterior(.2, 0), .2);
  near(posterior(.2, Math.log(10)), 5 / 7);
  near(posterior(.2, .6 * Math.log(3)), .3258255064974997);
});

test('all evaluated agents are accounted for exactly', () => {
  for (const p of [0, .35, 1]) {
    const { stats, traces } = once({ p });
    assert.equal(traces.length, 38);
    assert.equal(stats.direct + stats.heard + stats.uninformed, 38);
    assert.equal(stats.ignore + stats.investigate + stats.confront, 38);
  }
});

test('same initial state and seed reproduces every decision', () => {
  assert.deepEqual(once({ seed: 42 }), once({ seed: 42 }));
});

test('hidden ground truth never leaks into private decisions', () => {
  const allowed = once({ seed: 42, permission: true });
  const forbidden = once({ seed: 42, permission: false });
  assert.deepEqual(allowed.stats, forbidden.stats);
  assert.deepEqual(allowed.traces, forbidden.traces);
  assert.notEqual(allowed.world.permission, forbidden.world.permission);
});

test('no eyewitnesses means no evidence and no accusations', () => {
  const { stats } = once({ p: 0 });
  assert.equal(stats.direct, 0);
  assert.equal(stats.heard, 0);
  assert.equal(stats.confront, 0);
});

test('all 38 eligible agents witness when probability is one', () => {
  const { stats } = once({ p: 1 });
  assert.equal(stats.direct, 38);
  assert.equal(stats.heard, 0);
});

test('root IDs cannot be double counted by forwarded messages', () => {
  const { traces } = once({ seed: 42, p: .35 });
  for (const agent of traces)
    assert.equal(new Set(agent.evidenceRoots).size, agent.evidenceRoots.length);
});

test('hearsay changes decisions but never creates an eyewitness', () => {
  const without = once({ seed: 42, p: .35, rumors: false });
  const withRumors = once({ seed: 42, p: .35, rumors: true });
  assert.equal(withRumors.stats.direct, without.stats.direct);
  assert.equal(without.stats.heard, 0);
  assert.ok(withRumors.stats.heard > 0);
  assert.ok(withRumors.stats.confront > without.stats.confront);
});

test('visibility counterfactual reuses random draws', () => {
  const low = once({ seed: 42, p: .2 });
  const high = once({ seed: 42, p: .8 });
  assert.ok(high.stats.direct >= low.stats.direct);
});

test('Monte Carlo eyewitness mean agrees with E[W]=38p', () => {
  const runs = 1000;
  for (const row of study(runs)) {
    const se = Math.sqrt(38 * row.p * (1 - row.p) / runs);
    assert.ok(Math.abs(row.direct - row.expectedDirect) <= 5 * se + .05);
  }
});

test('40 agents can reach different outcomes from the same event', () => {
  const { stats, traces } = once({ seed: 42, p: .35 });
  assert.deepEqual({ direct: stats.direct, heard: stats.heard, uninformed: stats.uninformed },
    { direct: 16, heard: 14, uninformed: 8 });
  assert.deepEqual({ ignore: stats.ignore, investigate: stats.investigate, confront: stats.confront },
    { ignore: 17, investigate: 6, confront: 15 });
  assert.equal(traces.find(x => x.id === 'A10').decision, 'ignore');
  assert.equal(traces.find(x => x.id === 'A04').decision, 'confront');
});
