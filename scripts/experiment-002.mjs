/** ENORYS Experiment 002 — multi-turn social causality sandbox.
 * Research/education only: not a human behavior model or completed .NET runtime.
 * World is authoritative; cognition sees only agent state and filtered evidence.
 */
import { rng } from '../src/engine.js';

const clamp = (n, lo = 0, hi = 1) => Math.max(lo, Math.min(hi, n));
const logit = p => Math.log(p / (1 - p));
const logistic = v => 1 / (1 + Math.exp(-v));
const round = v => Number(v.toFixed(4));
const prior = 0.12;
const MAX_MEMORIES = 100;

const roster = ['Ada','Borys','Celia','Danylo','Emma','Fedir','Hanna','Ihor','Iryna','Kyrylo',
  'Lada','Marko','Nina','Oleh','Petro','Rita','Sava','Tania','Ulana','Vira',
  'Yaroslav','Zlata','Lev','Marta','Taras','Sofiia','Roman','Oksana','Artem','Mila'];

export function makeWorld({ seed = 42, agents = 24, visibility = 0.30, gossip = true,
  foraging = true, misidentification = 0.10, initialDepot = 20 } = {}) {
  if (!Number.isInteger(seed) || seed < 0 || seed > 4294967295 ||
      !Number.isInteger(agents) || agents < 12 || agents > 30 ||
      !Number.isFinite(visibility) || visibility < 0 || visibility > 1 ||
      !Number.isFinite(misidentification) || misidentification < 0 || misidentification > 1 ||
      !Number.isInteger(initialDepot) || initialDepot < 0 || initialDepot > 200)
    throw new RangeError('Invalid simulation configuration');
  const r = rng(seed);
  const people = Array.from({ length: agents }, (_, i) => ({
    id: `A${String(i+1).padStart(2,'0')}`, name: roster[i], stock: 1 + Math.floor(r() * 4),
    traits: { norm: .2+.8*r(), greed: r(), courage: r(), curiosity: r(), sociability: r(),
      generosity:r(), resilience:r() },
    mood: { grievance: 0, shame: 0 }, starvation: 0,
    received: [], known: {}, // independent-root evidence, grouped by incident
    trust: Object.create(null), used: new Set(), warned: new Set(),
    ownTakes: [], ownHistory: new Set(), accusations: [], restitution: 0,
  }));
  const world = {
    seed, agents, visibility, gossip, foraging, misidentification,
    people, depot: initialDepot, tick: 0, events: [], incidents: [], decisions: [],
    guardUntil: -1, produced: 0, consumed: 0, initialStock: initialDepot+people.reduce((s,a)=>s+a.stock,0),
    stats: { take:0, forage:0, share:0, tell:0, investigate:0, accuse:0, defend:0,
      repay:0, guard:0, wait:0, witnessed:0, falseIdentification:0, falseAccusations:0,
      accurateAccusations:0, repeatedClaimRejected:0 },
    identifiers: 0,
  };
  return world;
}

function draw(world, actor, channel, other = 0) {
  // A channel has a dedicated stream: unrelated gossip/detection doesn't shift traits.
  let seed = (world.seed ^ (actor*0x9e3779b1) ^ (world.tick*0x85ebca6b) ^
    (channel*0xc2b2ae35) ^ (other*0x27d4eb2f)) >>> 0;
  return rng(seed)();
}
function friends(world, actor) {
  const p=world.people, idx=p.indexOf(actor);
  return [1,2,3,5].map(k=>p[(idx+k)%p.length]);
}
const eventID = world => `E${String(++world.identifiers).padStart(4,'0')}`;
const incidentFor = (world,id) => world.incidents.find(v => v.id === id);

/** Ingests a report, not a world event. Shares original source root across forwards. */
function learn(world, recipient, observation) {
  const { incidentId, suspect, root, source } = observation;
  if (!root || !incidentId || !world.people.some(p=>p.id===suspect)) throw new Error('Bad observation');
  if (!recipient.known[incidentId]) recipient.known[incidentId]=[];
  const memory = recipient.known[incidentId];
  if (memory.some(e=>e.root===root)) {world.stats.repeatedClaimRejected++;return false;}
  if (memory.length >= MAX_MEMORIES) return false;
  const item = { incidentId, suspect, root, source,
    weight: observation.weight, by: observation.by, tick: world.tick };
  memory.push(item); recipient.received.push(item);
  // Attitude depends on *received information*, not truth. Each root counts at most once.
  const old=recipient.trust[suspect] ?? 0;
  recipient.trust[suspect]=clamp(old-.075*observation.weight,-1,1);
  return true;
}

export function suspicion(agent, incidentId) {
  const evidence = agent.known[incidentId] || [];
  const tally = new Map();
  for(const e of evidence) {
    if (!tally.has(e.suspect)) tally.set(e.suspect, []);
    tally.get(e.suspect).push(e.weight);
  }
  // Discount extra *different* roots per target to limit source dependencies; conservative heuristic.
  return [...tally.entries()].map(([suspect,values]) => {
    values.sort((a,b)=>b-a);
    const logLR = values.reduce((sum,x,i)=>sum+x*(i===0?1:0.25),0);
    return {suspect, posterior:logistic(logit(prior)+logLR), roots:values.length};
  }).sort((a,b)=>b.posterior-a.posterior||a.suspect.localeCompare(b.suspect));
}
function lead(actor) {
  const all=[];
  for(const incidentId of Object.keys(actor.known)) {
    for(const candidate of suspicion(actor,incidentId).filter(x=>x.suspect!==actor.id)) all.push({...candidate, incidentId});
  }
  return all.sort((a,b)=>b.posterior-a.posterior || a.incidentId.localeCompare(b.incidentId))[0] || null;
}

/** Decision function: only own state, public depot value, and private evidence. */
export function decide(world, actor) {
  // We deliberately do not read incident.actor or authoritative action history here.
  const t=actor.traits, hunger=clamp((3-actor.stock)/3);
  const known=lead(actor), guardActive=world.tick < world.guardUntil;
  const candidates=[{kind:'wait',terms:{baseline:0.25}},
    ...(world.foraging?[{kind:'forage',terms:{food:0.48+hunger*.92,work:.32*t.resilience}}]:[]),
    ...(world.depot >= 2 ? [{kind:'take', terms:{need:0.35+hunger*2.1,greed:.7*t.greed,
      moral:-.92*t.norm,deterrence:guardActive?-.85:0}}]:[]),
    ...(actor.stock>=5?[{kind:'share',terms:{care:1.0*t.generosity, surplus:.5,
      norms:.4*t.norm}}]:[]),
    ...(world.tick>=world.agents && known?.posterior>=.4 &&
      !actor.used.has(`investigate:${known.incidentId}`)?[{kind:'investigate',incidentId:known.incidentId,
        terms:{doubt:known.posterior*.85,inquiry:t.curiosity*.85,norm:.25*t.norm}}]:[]),
    ...(world.tick>=world.agents && known && known.posterior>=.49 && !actor.used.has(`accuse:${known.incidentId}:${known.suspect}`)
      ?[{kind:'accuse',target:known.suspect,incidentId:known.incidentId,
        terms:{conviction:known.posterior*1.7,courage:t.courage*.55,
          norms:.4*t.norm,resentment:actor.mood.grievance*.4}}]:[]),
    ...(world.gossip && known && known.posterior>=.38 ? friends(world,actor)
      .filter(friend=>friend.id!==known.suspect && !actor.warned.has(`${known.incidentId}:${friend.id}`))
      .slice(0,1).map(friend=>({kind:'tell',recipient:friend.id,incidentId:known.incidentId,
        suspect:known.suspect,terms:{warning:known.posterior*.9,social:t.sociability*.8,
          norms:.4*t.norm}})):[]),
    ...(world.tick>=world.agents && known?.posterior>=.4 && world.tick>=world.guardUntil
      ?[{kind:'guard',terms:{fear:known.posterior*1.15,duty:t.norm*.78}}]:[]),
    ...(actor.ownTakes.length && actor.stock>=2 &&
      actor.accusations.some(a=>actor.ownTakes.includes(a.incidentId)) ? [{kind:'repay',terms:{shame:1.1+.75*t.norm,
        pressure:Math.min(1,actor.accusations.length*.35),resources:actor.stock>=4?.25:0}}] : []),
    ...(actor.accusations.some(a=>!actor.ownHistory.has(a.incidentId) && !actor.used.has(`defend:${a.incidentId}`))
       ?[{kind:'defend', incidentId:actor.accusations.find(a=>!actor.ownHistory.has(a.incidentId) && !actor.used.has(`defend:${a.incidentId}`)).incidentId,
         terms:{dignity:1.2,alarm:Math.min(.8,actor.accusations.length*.25),courage:t.courage*.6}}]:[]),
  ];
  const scored=candidates.map(a=>({...a,score:Object.values(a.terms).reduce((s,v)=>s+v,0)}));
  // One-turn decision; stable deterministic argmax with no name/story special cases.
  const pick=scored.reduce((best,option)=>option.score>best.score?option:best);
  return {chosen:pick,candidates:scored.map(a=>({kind:a.kind,score:round(a.score),terms:a.terms})),
    perceived:{depot:world.depot,guardActive,known:known&&{...known}}};
}

function observeTake(world, event, actor) {
  const viewers=world.people.filter(p=>p!==actor);
  for(const viewer of viewers){
    const obsIndex=world.people.indexOf(viewer);
    const seen=draw(world,obsIndex,1)<clamp(world.visibility+(world.tick<world.guardUntil?.2:0));
    if (!seen) continue;
    world.stats.witnessed++;
    const mistaken=draw(world,obsIndex,2)<world.misidentification;
    const all=world.people.filter(p=>p.id!==actor.id&&p!==viewer);
    const suspect=mistaken?all[Math.floor(draw(world,obsIndex,3)*all.length)].id:actor.id;
    if (mistaken) world.stats.falseIdentification++;
    learn(world,viewer,{incidentId:event.id,suspect,root:`${event.id}/sight/${viewer.id}`,
      source:'sight',weight:Math.log(8),by:viewer.id});
  }
}
function observePublic(world, event, actor, subject) {
  // Direct observers are notified about *public speech or repayment*. They do not
  // acquire global knowledge of the theft. Speech reuses claim's original root.
  const audience = [...new Set([...friends(world,actor), ...world.people.filter(p=>p.id===event.target)])];
  for(const viewer of audience){
    if(viewer.id===actor.id)continue;
    if (event.kind==='accuse') {
      if (viewer.id===event.target) viewer.accusations.push({by:actor.id,target:event.target,incidentId:event.incidentId});
      if (subject && viewer.id!==subject.id) {
        learn(world,viewer,{incidentId:subject.incidentId,suspect:subject.suspect,root:subject.root,
          source:'public claim',weight:subject.weight*.55,by:actor.id});
      }
    }
    if(event.kind==='repay') {
      // Credible public return repairs social trust but does not reveal secret history.
      viewer.trust[actor.id]=clamp((viewer.trust[actor.id]??0)+.15,-1,1);
    }
  }
}

/** All world mutation and authorized observation delivery is here, not in decide. */
function execute(world, actor, proposal) {
  const action=proposal.kind, e={id:eventID(world),tick:world.tick,day:Math.floor(world.tick/world.agents)+1,
    actor:actor.id,kind:action,detail:''};
  if(action==='forage'){
    actor.stock+=2;world.produced+=2;e.detail='brought back 2 rations from outside';
  } else if(action==='take') {
    if(world.depot<2)throw new Error('Unauthorized depleted depot');
    actor.stock+=2;world.depot-=2;actor.ownTakes.push(e.id);actor.ownHistory.add(e.id);
    world.incidents.push({id:e.id,truthActor:actor.id,tick:world.tick,amount:2});
    e.detail='removed 2 rations from the rationed depot';
    observeTake(world,e,actor);
  } else if(action==='share') {
    if(actor.stock<5)throw new Error('Invalid gift');
    actor.stock--;world.depot++;e.detail='donated one ration to the depot';
  } else if(action==='tell') {
    const listener=world.people.find(p=>p.id===proposal.recipient);
    const basis=(actor.known[proposal.incidentId]||[]).find(x=>x.suspect===proposal.suspect);
    if(!listener||!basis)throw new Error('Evidence-free rumor');
    learn(world,listener,{incidentId:proposal.incidentId,suspect:basis.suspect,
      root:basis.root,source:'hearsay',weight:basis.weight*.55,by:actor.id});
    actor.warned.add(`${proposal.incidentId}:${listener.id}`);
    e.to=listener.id;e.incidentId=proposal.incidentId;e.detail=`warned ${listener.name} about ${basis.suspect}`;
  } else if(action==='investigate') {
    if(actor.used.has(`investigate:${proposal.incidentId}`))throw new Error('Duplicated investigation');
    const incident=incidentFor(world,proposal.incidentId);
    if (!incident)throw new Error('Invalid incident');
    actor.used.add(`investigate:${proposal.incidentId}`);
    const mistaken=draw(world,world.people.indexOf(actor),4)<.18;
    const alternatives=world.people.filter(p=>p.id!==incident.truthActor);
    const suspect=mistaken?alternatives[Math.floor(draw(world,world.people.indexOf(actor),5)*alternatives.length)].id:incident.truthActor;
    // A fallible physical clue exists in-world. Agent gets a noisy cue, never incident.truthActor.
    learn(world,actor,{incidentId:incident.id,suspect,
      root:`${incident.id}/physical/${actor.id}`,source:'physical clue',weight:Math.log(6),by:actor.id});
    e.incidentId=incident.id;e.detail=`found a clue implicating ${suspect}${mistaken?' (misleading)':''}`;
  } else if(action==='accuse') {
    if(actor.used.has(`accuse:${proposal.incidentId}:${proposal.target}`)) throw new Error('Repeated accusation');
    const basis=(actor.known[proposal.incidentId]||[]).find(x=>x.suspect===proposal.target);
    if(!basis)throw new Error('Accusation without source');
    const incident=incidentFor(world,proposal.incidentId);
    actor.used.add(`accuse:${proposal.incidentId}:${proposal.target}`);
    e.target=proposal.target;e.incidentId=proposal.incidentId;
    e.correct=incident.truthActor===proposal.target;
    world.stats[e.correct?'accurateAccusations':'falseAccusations']++;
    e.detail=`publicly accused ${proposal.target} over ${proposal.incidentId}`;
    observePublic(world,e,actor,basis);
  } else if(action==='repay') {
    if(actor.stock<2||!actor.ownTakes.length)throw new Error('Impossible restitution');
    actor.stock-=2;world.depot+=2;e.incidentId=actor.ownTakes.shift();actor.restitution++;
    e.detail='returned 2 rations to the depot after social pressure';
    observePublic(world,e,actor);
  } else if(action==='guard') {
    world.guardUntil=world.tick+world.agents*2;
    e.detail='organized guarding of the communal supplies';
  } else if(action==='defend') {
    actor.used.add(`defend:${proposal.incidentId}`); e.incidentId=proposal.incidentId;
    e.detail=`publicly disputed an accusation over ${proposal.incidentId}`;
  } else {
    e.detail='rested rather than acting';
  }
  world.stats[action]++;
  world.events.push(e);
  return e;
}

export function step(world){
  const actor=world.people[world.tick%world.people.length];
  if(world.tick && world.tick%world.people.length===0){
    for(const p of world.people){
      if(p.stock>0){p.stock--;world.consumed++}else p.starvation++;
    }
  }
  const proposal=decide(world,actor);
  const event=execute(world,actor,proposal.chosen);
  world.decisions.push({eventId:event.id,actor:actor.id,kind:event.kind,score:round(proposal.chosen.score),
    candidates:proposal.candidates,perceived:proposal.perceived});
  world.tick++;
  return event;
}
export function run({ days = 14, ...config } = {}){
  if(!Number.isInteger(days)||days<1||days>40)throw new RangeError('days 1..40');
  const world=makeWorld(config);
  for(let i=0;i<days*world.agents;i++)step(world);
  return world;
}
export function summary(world){
  const truth=new Map(world.incidents.map(x=>[x.id,x.truthActor]));
  const distinct=new Set(world.people.flatMap(p=>p.received.map(e=>`${p.id}|${e.incidentId}|${e.root}`)));
  const total=world.people.reduce((sum,p)=>sum+p.stock,0)+world.depot;
  return {
    seed:world.seed,days:world.tick/world.agents,agents:world.agents,visibility:world.visibility,gossip:world.gossip,
    incidents:world.incidents.length, totalStock:total,expectedStock:world.initialStock+world.produced-world.consumed,
    stockUnbalanced:total-(world.initialStock+world.produced-world.consumed),
    ...world.stats, uniqueEvidence:distinct.size,starvation:world.people.reduce((s,p)=>s+p.starvation,0),
    matchedAccusations:world.events.filter(e=>e.kind==='accuse'&&truth.get(e.incidentId)===e.target).length,
    socialDiversity:new Set(world.decisions.map(x=>x.kind)).size,
  };
}
export function narrative(world,{maxEvents=36}={}){
  const important=world.events.filter(x=>['take','tell','investigate','accuse','repay','guard','defend','share'].includes(x.kind));
  return important.slice(0,maxEvents).map(e=>{
    const name=world.people.find(x=>x.id===e.actor)?.name||e.actor;
    return `Day ${String(e.day).padStart(2,'0')} [${e.id}] ${name}: ${e.detail}${e.kind==='accuse'?e.correct?' [TRUE ACCUSATION]':' [FALSE ACCUSATION]':''}`;
  });
}
function headline(world){
  const s=summary(world);
  return {seed:s.seed,visibility:s.visibility,gossip:s.gossip,days:s.days,
    ...Object.fromEntries(['incidents','take','forage','share','tell','investigate','accuse','accurateAccusations',
      'falseAccusations','repay','guard','defend','starvation','uniqueEvidence','stockUnbalanced'].map(k=>[k,s[k]]))};
}
if(typeof process !== 'undefined' && import.meta.url===`file://${process.argv[1]}`){
  const seed=Number(process.argv[2]||42),days=Number(process.argv[3]||14);
  for(const variant of [{visibility:0.3,gossip:true},{visibility:0,gossip:true},{visibility:.75,gossip:true},{visibility:.3,gossip:false}]){
    const world=run({seed,days,...variant});
    console.log('\n### '+JSON.stringify(headline(world)));
    if(variant.visibility===.3&&variant.gossip)console.log(narrative(world,{maxEvents:55}).join('\n'));
  }
}
