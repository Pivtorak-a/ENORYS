import test from 'node:test';
import assert from 'node:assert/strict';
import {makeWorld,run,step,decide,summary,suspicion,narrative} from '../scripts/experiment-002.mjs';

test('same seed/config creates identical multi-day event and decision traces',()=>{
 const a=run({seed:42,days:12}),b=run({seed:42,days:12});
 assert.deepEqual(a.events,b.events);assert.deepEqual(a.decisions,b.decisions);
 assert.deepEqual(summary(a),summary(b));
});
test('private cognition never reads the true actor of unseen incidents',()=>{
 const a=run({seed:42,days:2}),b=run({seed:42,days:2});
 for(const incident of b.incidents)incident.truthActor='FAKE-ONLY-WORLD-DIAGNOSTICS';
 // Only private deliberation is tested, not an investigation that would sample the world.
 for(let i=0;i<a.people.length;i++){
   assert.deepEqual(decide(a,a.people[i]),decide(b,b.people[i]));
 }
});
test('every decision utility equals visible weighted contributions',()=>{
 const world=run({seed:42,days:12});
 for(const d of world.decisions){
  const chosen=d.candidates.find(x=>x.kind===d.kind);
  assert.ok(chosen);
  assert.ok(Math.abs(chosen.score-d.score)<1e-10);
  for(const c of d.candidates)
    assert.ok(Math.abs(c.score-Object.values(c.terms).reduce((x,y)=>x+y,0))<.00006);
 }
});
test('proposing an action is side-effect-free',()=>{
 const world=run({seed:42,days:3});
 const stringify=w=>JSON.stringify(w,(_key,value)=>value instanceof Set?[...value]:value);
 const before=stringify(world);
 for(const agent of world.people) decide(world,agent);
 assert.equal(stringify(world),before);
});
test('quantities conserved with only explicit production and consumption',()=>{
 for(let seed=1;seed<=80;seed++){
  const w=run({seed,days:12});const s=summary(w);
  assert.equal(s.stockUnbalanced,0);
  assert.ok(w.depot>=0 && w.people.every(p=>p.stock>=0));
  assert.equal(s.totalStock,s.expectedStock);
  assert.equal(s.take,w.incidents.length);
 }
});
test('zero visibility produces no spontaneous rumors, accusations or investigations',()=>{
 const w=run({seed:42,days:12,visibility:0});const s=summary(w);
 assert.equal(s.witnessed,0);assert.equal(s.tell,0);assert.equal(s.accuse,0);
 assert.equal(s.investigate,0);
 assert.equal(s.uniqueEvidence,0);
});
test('no sensor mistakes means no false accusations in the controlled world',()=>{
 for(let seed=1;seed<=30;seed++){
  const s=summary(run({seed,days:10,misidentification:0}));
  assert.equal(s.falseIdentification,0);assert.equal(s.falseAccusations,0);
 }
});
test('source roots remain unique in every agent/incident',()=>{
 const w=run({seed:42,days:12});
 for(const agent of w.people)for(const [incidentId,evidence] of Object.entries(agent.known)){
  const roots=evidence.map(e=>e.root);
  assert.equal(new Set(roots).size,roots.length);
  for(const e of evidence){assert.equal(e.incidentId,incidentId);assert.ok(e.root.startsWith(`${incidentId}/`));}
 }
});
test('each accusation has its own perceived source before action',()=>{
 const w=run({seed:42,days:12});
 for(const e of w.events.filter(x=>x.kind==='accuse')){
   const d=w.decisions.find(x=>x.eventId===e.id);
   assert.equal(d.perceived.known.incidentId,e.incidentId);
   assert.equal(d.perceived.known.suspect,e.target);
 }
});
test('story contains unscripted false accusation, denial and restitution for seed 42',()=>{
 const w=run({seed:42,days:12});const s=summary(w);
 assert.ok(s.falseAccusations>0);assert.ok(s.accurateAccusations>0);
 assert.ok(s.defend>0 && s.repay>0 && s.guard>0 && s.investigate>0);
 const wrong=w.events.find(x=>x.kind==='accuse'&&!x.correct);
 assert.ok(w.events.some(e=>e.kind==='defend'&&e.actor===wrong.target&&e.incidentId===wrong.incidentId&&e.tick>wrong.tick));
 assert.ok(w.events.some(e=>e.kind==='repay'&&e.incidentId===wrong.incidentId));
});
test('gossip toggle changes downstream history but not initial agent traits',()=>{
 const a=run({seed:42,days:12,gossip:true}),b=run({seed:42,days:12,gossip:false});
 assert.deepEqual(a.people.map(p=>p.traits),b.people.map(p=>p.traits));
 assert.equal(summary(b).tell,0);assert.ok(summary(a).tell>0);
 assert.notDeepEqual(a.events,b.events);
});
test('changing visibility changes actions through observations, not script identity',()=>{
 const a=run({seed:42,days:12,visibility:0});const b=run({seed:42,days:12,visibility:.3});
 assert.deepEqual(a.people.map(p=>p.traits),b.people.map(p=>p.traits));
 assert.ok(summary(b).accuse>summary(a).accuse);
});
test('agent cannot accuse themselves, even after repeated social reports',()=>{
 const w=run({seed:42,days:12});
 assert.ok(w.events.filter(e=>e.kind==='accuse').every(e=>e.target!==e.actor));
});
test('evidence-only reports do not create unrelated new world thefts',()=>{
 const w=run({seed:42,days:6});
 assert.equal(w.events.filter(e=>e.kind==='take').length,w.incidents.length);
 assert.ok(w.people.some(p=>p.received.some(e=>e.source==='hearsay')));
});
test('parameters validated and timeline grows exactly with active agents',()=>{
 assert.throws(()=>makeWorld({agents:10}),RangeError);
 assert.throws(()=>makeWorld({visibility:1.2}),RangeError);
 assert.throws(()=>makeWorld({misidentification:NaN}),RangeError);
 assert.throws(()=>run({days:41}),RangeError);
 const w=makeWorld({seed:77});
 for(let i=0;i<25;i++)step(w);
 assert.equal(w.tick,25);assert.equal(w.events.length,25);
 assert.equal(w.decisions.length,25);
});
test('narrative is a human-readable projection of actual world events',()=>{
 const w=run({seed:42,days:6});const rows=narrative(w,{maxEvents:50});
 assert.ok(rows.some(row=>row.includes('FALSE ACCUSATION')));
 assert.ok(rows.some(row=>row.includes('returned 2 rations')));
});
