import test from 'node:test';
import assert from 'node:assert/strict';
import { createWorld, step, run, decide, summary, timeline, ITEMS, NEEDS, ACTIONS } from '../scripts/experiment-003.mjs';
const allzero=(w)=>Object.values(summary(w).discrepancy).every(x=>x===0);

test('all catalog items and essential survival dimensions are modeled',()=>{
 assert.equal(Object.keys(ITEMS).length,14);
 assert.ok(NEEDS.includes('hunger')&&NEEDS.includes('thirst')&&NEEDS.includes('fatigue'));
 assert.ok(NEEDS.includes('cold')&&NEEDS.includes('hygiene')&&NEEDS.includes('bladder')&&NEEDS.includes('safety'));
 assert.ok(NEEDS.includes('belonging')&&NEEDS.includes('morale'));
 const w=createWorld();
 assert.ok(w.people.every(p=>Number.isFinite(p.health)&&p.inv&&p.needs));
 assert.ok(ACTIONS.length>=20);
});
test('identical conditions produce identical stories and statistics',()=>{
 const a=run({seed:42,days:12}),b=run({seed:42,days:12});
 assert.deepEqual(a.events,b.events);assert.deepEqual(a.decisions,b.decisions);
 assert.deepEqual(summary(a),summary(b));
});
test('conservation holds for every resource including crafting and consumption',()=>{
 for(let seed=1;seed<=50;seed++) for(const resources of [.35,1,2]){
  const w=run({seed,days:12,resources});
  assert.ok(allzero(w),JSON.stringify({seed,resources,discrepancy:summary(w).discrepancy}));
  for(const kind of Object.keys(ITEMS)){
   assert.ok(w.depot[kind]>=0 && w.sources[kind]>=0);
   assert.ok(w.people.every(p=>Number.isInteger(p.inv[kind])&&p.inv[kind]>=0));
  }
 }
});
test('all needs/health remain bounded during storms and scarcity',()=>{
 for(let seed=1;seed<=75;seed++){
  const w=run({seed,days:22,resources:.22,weatherSeverity:1});
  for(const a of w.people){
   assert.ok(a.health>=0&&a.health<=100);
   assert.ok(a.infection>=0&&a.infection<=100);
   assert.ok(a.wound>=0&&a.wound<=100);
   for(const v of Object.values(a.needs))assert.ok(v>=0&&v<=100);
  }
  assert.ok(allzero(w));
 }
});
test('no actions access hidden attacker identity while deliberating',()=>{
 const w=run({seed:42,days:3});
 const before=w.people.map(p=>decide(p, {depot:{...w.depot},weather:w.weather,guarded:false,
  neighbors:[],requests:p.receivedRequests.map(x=>({...x}))}));
 for(const incident of w.incidents) incident.actor='DIAGNOSTIC_CHANGED';
 const after=w.people.map(p=>decide(p, {depot:{...w.depot},weather:w.weather,guarded:false,
  neighbors:[],requests:p.receivedRequests.map(x=>({...x}))}));
 assert.deepEqual(before,after);
});
test('unseen world source inventory cannot change a private decision',()=>{
 const w=createWorld();const a=w.people[0],ctx={depot:{...w.depot},weather:w.weather,guarded:false,neighbors:[],requests:[]};
 const before=decide(a,ctx);
 w.sources.medicine=0;w.sources.dirty_water=0;w.sources.wood=0;
 assert.deepEqual(before,decide(a,ctx));
});
test('decision is pure and preserves personal inventory and needs',()=>{
 const w=createWorld(),a=w.people[0];
 const before=structuredClone({inv:a.inv,needs:a.needs,health:a.health,beliefs:a.beliefs});
 const view={depot:{...w.depot},weather:w.weather,guarded:false,neighbors:[],requests:[]};
 decide(a,view);
 assert.deepEqual(before,{inv:a.inv,needs:a.needs,health:a.health,beliefs:a.beliefs});
});
test('medical items explicitly respond to injuries and infection',()=>{
 const w=createWorld(),p=w.people[0],view={depot:{...w.depot},weather:'mild',guarded:false,neighbors:[],requests:[]};
 p.needs={hunger:15,thirst:15,fatigue:5,cold:0,hygiene:10,safety:0,belonging:5,morale:10};
 p.health=45;p.infection=98;p.inv.medicine=1;
 assert.equal(decide(p,view).chosen.kind,'treat');
 p.inv.medicine=0;p.infection=0;p.wound=100;p.inv.bandage=1;p.health=75;
 assert.equal(decide(p,view).chosen.kind,'bandage');
});
test('clean drinking, eating and sleep counter their respective deficits',()=>{
 const w=createWorld({agents:12,days:2}),p=w.people[0];
 p.inv.water=2;p.needs.thirst=100;
 let action=step(w);
 assert.equal(action.kind,'drink');assert.ok(p.needs.thirst<50);assert.equal(p.inv.water,1);
 p.needs.thirst=0;p.needs.hunger=100;p.inv.food=1;
 // Fast-forward until this agent is scheduled again; other actors execute independently.
 for(let i=0;i<11;i++)step(w);
 action=step(w);assert.equal(action.kind,'eat');assert.ok(p.needs.hunger<60);
});
test('needs and weather evolve only at day boundaries',()=>{
 const w=createWorld({agents:12,turnsPerDay:1,days:3,weatherSeverity:1});
 const p=w.people[0];p.needs.hunger=20;p.needs.thirst=20;p.needs.hygiene=20;
 // Disable early consumption by reducing this agent's supplies/available actions.
 for(let i=0;i<12;i++)step(w);
 const before=w.people[1].needs.hygiene;step(w);
 assert.equal(w.people[1].needs.hygiene,before+8);
});
test('repeated hearsay roots never become independent evidence',()=>{
 const w=run({days:16,seed:42,visibility:.8});
 for(const p of w.people)for(const items of Object.values(p.beliefs)){
  assert.equal(new Set(items.map(x=>x.root)).size,items.length);
  assert.ok(items.every(x=>x.kind==='sight'||x.kind==='hearsay'||x.kind==='physical trace'||x.kind==='public accusation'));
 }
});
test('changing visibility changes events but does not change initial traits',()=>{
 const a=run({seed:42,days:12,visibility:0,gossip:false});
 const b=run({seed:42,days:12,visibility:.8,gossip:true});
 assert.deepEqual(a.people.map(p=>p.traits),b.people.map(p=>p.traits));
 assert.equal(a.stats.eyewitness,0);
 assert.ok(b.stats.eyewitness>0);
 assert.equal(a.stats.accuse,0);
 assert.ok(b.stats.accuse>0);
});
test('scarcity and colder weather produce measurable changes, not new rules',()=>{
 const normal=summary(run({seed:42,days:12,resources:1,weatherSeverity:.6}));
 const scarce=summary(run({seed:42,days:12,resources:.35,weatherSeverity:.6}));
 const winter=summary(run({seed:42,days:12,resources:1,weatherSeverity:1}));
 assert.notDeepEqual(normal.meanNeeds,scarce.meanNeeds);
 assert.notDeepEqual(normal.meanNeeds,winter.meanNeeds);
 assert.equal(normal.balanced,true);assert.equal(scarce.balanced,true);assert.equal(winter.balanced,true);
});
test('actual event streams contain survival, crafting and social actions',()=>{
 const w=run({seed:42,days:12});
 for(const kind of ['eat','drink','sleep','relieve','gather_food','collect_water','cook','boil','scavenge','take','accuse','socialize','ask','give','build_shelter','wash'])
  assert.ok(w.stats[kind]>0,'Missing '+kind);
 assert.ok(summary(w).uniqueActions>=16);
 assert.ok(timeline(w).every(e=>w.events.some(src=>src.id===e.id)));
});
test('input validation and stopped world prohibit extra ticks',()=>{
 assert.throws(()=>createWorld({seed:-1}),RangeError);
 assert.throws(()=>createWorld({agents:2}),RangeError);
 assert.throws(()=>createWorld({resources:-.01}),RangeError);
 assert.throws(()=>createWorld({weatherSeverity:Infinity}),RangeError);
 assert.throws(()=>createWorld({turnsPerDay:0}),RangeError);
 const w=run({days:1,agents:12});assert.throws(()=>step(w),RangeError);
});
