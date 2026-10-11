// ENORYS Experiment 001: one event, 40 agents, private beliefs and counterfactuals.
// Research demonstration only: not human psychology or the finished C# engine.
import { rng } from '../src/engine.js';

const sigmoid = x => 1 / (1 + Math.exp(-x));
const posterior = (p, logLR) => sigmoid(Math.log(p / (1 - p)) + logLR);

function once({ seed = 42, n = 40, p = .35, rumors = true, permission = false } = {}) {
  if (!Number.isInteger(n) || n < 20 || n > 60 || p < 0 || p > 1) throw new RangeError('Invalid experiment inputs');
  const r = rng(seed);
  const sensor = rng((seed ^ 0xA6C3E51D) >>> 0);
  const communication = rng((seed ^ 0x37F18DB2) >>> 0);
  const people = Array.from({ length: n }, (_, i) => ({
    id: 'A' + String(i + 1).padStart(2, '0'),
    norm: .25 + .7 * r(), courage: r(), curiosity: r(), social: r(),
    memory: new Map()
  }));
  // Objective truth exists only here. Decisions receive private memories instead.
  const world = { event: 'E1', actor: people[0].id, owner: people[1].id, permission };
  const observers = people.slice(2);
  for (const a of observers) {
    if (sensor() < p) {
      const root = 'O:' + world.event + ':' + a.id;
      a.memory.set(root, { source: 'sight', lr: Math.log(10) });
    }
  }

  // Freeze direct witnesses before forwarding; rumors cannot spontaneously become eyewitnesses.
  const witnesses = observers.filter(a => a.memory.has('O:' + world.event + ':' + a.id));
  let messages = 0;
  if (rumors) for (const a of witnesses) {
    const i = observers.indexOf(a);
    if (communication() >= .30 + .65 * a.social) continue;
    const root = 'O:' + world.event + ':' + a.id;
    for (let hop = 1; hop <= 2; hop++) {
      const target = observers[(i + hop) % observers.length];
      if (!target.memory.has(root))
        target.memory.set(root, { source: 'rumor', lr: .6 * Math.log(3) });
      messages++;
    }
  }

  const stats = { direct: 0, heard: 0, uninformed: 0, ignore: 0, investigate: 0,
    confront: 0, meanBelief: 0, messages };
  const traces = [];
  for (const a of observers) {
    const ev = [...a.memory.entries()].sort((x, y) =>
      Number(y[1].source === 'sight') - Number(x[1].source === 'sight') ||
      x[0].localeCompare(y[0]));
    // Different roots after the first are downweighted to 25% (heuristic correlation control).
    const logLR = ev.reduce((sum, [, e], i) => sum + e.lr * (i === 0 ? 1 : .25), 0);
    const belief = posterior(.2, logLR);
    const severity = .8 * belief * a.norm;
    const choices = { ignore: .5 };
    if (ev.length) {
      choices.investigate = 1.3 * severity + .4 * a.curiosity;
      choices.confront = 2 * severity + .35 * a.courage - .1;
    }
    let selected = 'ignore';
    for (const [name, score] of Object.entries(choices))
      if (score > choices[selected]) selected = name;
    stats[selected]++;
    stats.meanBelief += belief;
    const direct = ev.some(([, e]) => e.source === 'sight');
    stats[direct ? 'direct' : ev.length ? 'heard' : 'uninformed']++;
    traces.push({ id: a.id, direct, evidenceRoots: ev.map(([root]) => root),
      belief, decision: selected });
  }
  stats.meanBelief /= observers.length;
  return { world, stats, traces };
}

function study(runs = 1000) {
  if (!Number.isInteger(runs) || runs < 1 || runs > 10000) throw new RangeError('runs');
  return [0, .35, 1].map(p => {
    const sums = { direct: 0, heard: 0, uninformed: 0, ignore: 0,
      investigate: 0, confront: 0, meanBelief: 0 };
    for (let seed = 1; seed <= runs; seed++) {
      const { stats } = once({ seed, p });
      for (const key of Object.keys(sums)) sums[key] += stats[key] / runs;
    }
    return { p, expectedDirect: 38 * p, ...sums };
  });
}

if (import.meta.url === 'file://' + process.argv[1]) {
  console.log(JSON.stringify({
    single: [0, .35, 1].map(p => ({ p, ...once({ p }).stats })),
    monteCarlo: study(),
    noRumor: once({ rumors: false }).stats
  }, null, 2));
}
export { once, study, posterior };
