/** ENORYS public demo: deterministic, dependency-free toy cognitive simulation.
 * Not the planned C# cognitive PoC and not a validated psychology model.
 */
const clamp = (x, lo = 0, hi = 1) => Math.max(lo, Math.min(hi, x));

export function rng(seed) {
  let state = seed >>> 0;
  return () => {
    state = (state + 0x6d2b79f5) >>> 0;
    let t = Math.imul(state ^ (state >>> 15), 1 | state);
    t ^= t + Math.imul(t ^ (t >>> 7), 61 | t);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

export function createWorld({ seed = 42, agents = 24, witnessRate = 0.5 } = {}) {
  if (!Number.isInteger(seed) || seed < 0 || seed > 0xffffffff) throw new RangeError('seed must be a uint32');
  if (!Number.isInteger(agents) || agents < 6 || agents > 60) throw new RangeError('agents must be 6..60');
  if (!Number.isFinite(witnessRate) || witnessRate < 0 || witnessRate > 1) throw new RangeError('witnessRate must be 0..1');
  const random = rng(seed);
  const people = Array.from({ length: agents }, (_, i) => ({
    id: `A${String(i + 1).padStart(2, '0')}`,
    place: i % 3,
    supplies: Math.floor(random() * 7),
    traits: { greed: random(), generosity: random(), norm: random(), sociability: random() },
    memories: [], relationships: {}, reported: [],
  }));
  const cache = agents;
  return {
    seed, witnessRate, tick: 0, cache, people, log: [], decisions: [], random,
    initialTotal: cache + people.reduce((sum, a) => sum + a.supplies, 0),
    stats: { takes: 0, donations: 0, reports: 0, witnessedTakes: 0 },
  };
}

function observed(actor, subject, event, source, confidence, parentId = null, rootId = event.id) {
  if (actor.id === subject || actor.memories.some(m => m.eventId === event.id && m.source === source)) return;
  const memory = { eventId: event.id, kind: event.kind, subject, source, confidence, parentId, rootId, tick: event.tick };
  actor.memories.push(memory);
  if (actor.memories.length > 60) actor.memories.shift();
  const old = actor.relationships[subject] ?? 0;
  const delta = event.kind === 'take' ? -0.25 * confidence : event.kind === 'donate' ? 0.15 * confidence : 0;
  actor.relationships[subject] = clamp(old + delta, -1, 1);
}

/** Deliberation may read only the actor's state and explicit local perceptions. */
export function decide(actor, perception) {
  const need = clamp((3 - actor.supplies) / 3);
  const surplus = clamp((actor.supplies - 3) / 3);
  const evidence = actor.memories.find(m => m.kind === 'take' && m.subject !== actor.id &&
    perception.neighbors.some(n => n.id !== m.subject && !actor.reported.includes(`${m.eventId}:${n.id}`)));
  const recipient = evidence && perception.neighbors.find(n => n.id !== evidence.subject &&
    !actor.reported.includes(`${evidence.eventId}:${n.id}`));
  const candidates = [
    { action: 'wait', terms: { baseline: 0.75 } },
    ...(perception.cache > 0 ? [{ action: 'take', terms: {
      need: need * 2.0, greed: actor.traits.greed * 1.1, norm: -actor.traits.norm * 1.35,
      visibility: -perception.neighbors.length * 0.08,
    }}] : []),
    ...(actor.supplies > 3 ? [{ action: 'donate', terms: {
      surplus: surplus * 0.9, generosity: actor.traits.generosity * 1.3, norm: actor.traits.norm * 0.75,
    }}] : []),
    ...(evidence && recipient ? [{ action: 'tell', terms: {
      norms: actor.traits.norm * 0.9, social: actor.traits.sociability * 1.1,
      salience: evidence.confidence * 0.6,
    }, evidence, recipient: recipient.id }] : []),
  ];
  const scored = candidates.map(c => ({ ...c, score: Object.values(c.terms).reduce((a,b) => a + b, 0) }));
  const chosen = scored.reduce((best, current) => current.score > best.score ? current : best);
  return { chosen, scored, perception: { ...perception, neighbors: perception.neighbors.map(n => n.id) } };
}

function apply(world, actor, decision) {
  const { action, evidence, recipient } = decision;
  const event = { id: `E${world.tick + 1}`, tick: world.tick + 1, kind: action, actor: actor.id, place: actor.place };
  if (action === 'take') {
    if (world.cache <= 0) throw new Error('world adapter rejected empty cache');
    world.cache--; actor.supplies++; world.stats.takes++;
  } else if (action === 'donate') {
    if (actor.supplies <= 3) throw new Error('world adapter rejected donation');
    actor.supplies--; world.cache++; world.stats.donations++;
  } else if (action === 'tell') {
    if (!evidence || !recipient || !actor.memories.includes(evidence)) throw new Error('world adapter rejected unsupported report');
    if (actor.reported.includes(`${evidence.eventId}:${recipient}`)) throw new Error('duplicate report');
    actor.reported.push(`${evidence.eventId}:${recipient}`);
    event.recipient = recipient;
    event.claim = { kind: evidence.kind, subject: evidence.subject, parentId: evidence.eventId, rootId: evidence.rootId };
    world.stats.reports++;
  }
  world.log.push(event);
  return event;
}

export function step(world) {
  const actor = world.people[world.tick % world.people.length];
  const neighbors = world.people.filter(a => a.place === actor.place && a.id !== actor.id);
  const perception = { cache: world.cache, neighbors: neighbors.map(n => ({ id: n.id })) };
  const trace = decide(actor, perception);
  const event = apply(world, actor, trace.chosen);
  if (event.kind === 'take' || event.kind === 'donate') {
    for (const witness of neighbors) {
      if (world.random() < world.witnessRate) {
        observed(witness, actor.id, event, 'eyewitness', 1);
        if (event.kind === 'take') world.stats.witnessedTakes++;
      }
    }
  } else if (event.kind === 'tell') {
    const listener = world.people.find(a => a.id === event.recipient);
    if (!listener || listener.place !== actor.place) throw new Error('listener not present');
    // Hearsay is a claim about an earlier event; do not grant its receiver direct observation.
    observed(listener, event.claim.subject,
      { id: event.id, tick: event.tick, kind: event.claim.kind }, 'hearsay', 0.55, event.claim.parentId, event.claim.rootId);
  }
  world.decisions.push({ tick: event.tick, actor: actor.id, action: event.kind,
    score: trace.chosen.score, terms: trace.chosen.terms,
    candidates: trace.scored.map(c => ({ action: c.action, score: c.score, terms: c.terms })),
    visible: trace.perception });
  world.tick++;
  return event;
}

export function run(options = {}, ticks = 120) {
  if (!Number.isInteger(ticks) || ticks < 0 || ticks > 10000) throw new RangeError('ticks must be 0..10000');
  const world = createWorld(options);
  for (let i = 0; i < ticks; i++) step(world);
  return world;
}

export function snapshot(world) {
  return {
    seed: world.seed, witnessRate: world.witnessRate, tick: world.tick, cache: world.cache,
    stats: world.stats, initialTotal: world.initialTotal,
    people: world.people, log: world.log, decisions: world.decisions,
  };
}
