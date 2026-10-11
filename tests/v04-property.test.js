import test from 'node:test';import assert from 'node:assert/strict';
import {run,summary,worldSnapshot,createWorld,tick} from '../src/v04/runtime.mjs';
import {visibleView,execute} from '../src/v04/world.mjs';
import {deliberate} from '../src/v04/cognition.mjs';
import {availableUnreserved,makePlan,SURVIVAL_GOALS} from '../src/v04/planner.mjs';

test('property: 100 seeds under scarcity and changing witness levels conserve all 14 items',()=>{
 for(let seed=101;seed<=200;seed++){
  const w=run({seed,agents:24,days:8,resources:(seed%4)*.3+.1,visibility:(seed%5)*.2,
    misidentification:(seed%3)*.2,cold:(seed%3)*.4});
  const s=summary(w);assert.ok(s.balanced,`seed ${seed}: ${JSON.stringify(s.discrepancies)}`);
  assert.ok(w.people.every(p=>Object.values(p.inv).every(v=>Number.isInteger(v)&&v>=0)),`negative item seed ${seed}`);
  assert.ok(w.people.every(p=>Object.values(p.needs).every(x=>Number.isFinite(x)&&x>=0&&x<=100)),`need seed ${seed}`);
  assert.ok(w.people.every(p=>p.health>=0&&p.health<=100));
  assert.equal(w.tick,24*8*3);
 }
});
test('property: no hidden fact in perception and cognition function can receive only local actor + packet',()=>{
 const w=createWorld();const a=w.people[0], packet=visibleView(w,a);
 const history=w.events;
 const proposalA=deliberate(structuredClone(a),packet,{plannerEnabled:false});
 w.events.push({id:'SECRET',actor:'A999',kind:'take',privateEvent:true});
 const proposalB=deliberate(structuredClone(a),visibleView(w,a),{plannerEnabled:false});
 assert.deepEqual(proposalA,proposalB);
 assert.equal(w.events.length,1);
});
test('regression: dead agents produce no illegal proposals',()=>{
 const w=createWorld({seed:7,resources:0,days:40,cold:1});
 let encounters=0;
 while(w.tick<w.days*w.agents*w.turnsPerDay){const result=tick(w);if(result.inactive)encounters++;}
 assert.ok(encounters>0);assert.equal(summary(w).balanced,true);
});
test('regression: dynamically acquired crafting inputs cannot be given away',()=>{
 const w=createWorld();const a=w.people[0];a.inv.water=0;a.inv.wood=0;a.inv.dirty_water=0;
 const p=makePlan(a,visibleView(w,a),SURVIVAL_GOALS[0]);a.plan=p;
 a.inv.dirty_water=1;a.inv.wood=1;
 assert.equal(availableUnreserved(a,'wood'),0);
 assert.equal(availableUnreserved(a,'dirty_water'),0);
});
test('regression: remote social transactions and fabricated rumors rejected',()=>{
 const w=createWorld();const a=w.people[0],remote=w.people[1];a.inv.water=2;
 assert.equal(execute(w,{actor:a.id,kind:'ask',target:remote.id,item:'water'}).accepted,false);
 assert.equal(execute(w,{actor:a.id,kind:'tell',target:remote.id,incident:'F1',root:'R1',suspect:'A002',lr:10}).accepted,false);
});
