import test from 'node:test';import assert from 'node:assert/strict';
import {createWorld,visibleView,execute} from '../src/v04/world.mjs';
import {makePlan,planStep,availableUnreserved,SURVIVAL_GOALS} from '../src/v04/planner.mjs';
test('phase2: generic planner resolves water through gather, wood, boil, drink',()=>{
 const w=createWorld();const a=w.people[0];a.inv.water=0;a.inv.wood=0;a.inv.dirty_water=0;
 const p=makePlan(a,visibleView(w,a),SURVIVAL_GOALS[0]);assert.ok(p);
 assert.deepEqual(p.steps.map(x=>x.kind),['collect_water','gather_wood','boil','drink']);
});
test('phase2: resulting action plan executes through World Authority only',()=>{
 const w=createWorld();const a=w.people[0];a.inv.water=0;a.inv.wood=0;a.inv.dirty_water=0;
 w.initialTotals.water-=1;w.initialTotals.wood-=w.people[0].inv.wood; // initial water removed deliberately
 const p=makePlan(a,visibleView(w,a),SURVIVAL_GOALS[0]);assert.ok(p);a.plan=p;
 for(const kind of p.steps.map(s=>s.kind)){
  const r=execute(w,{actor:a.id,kind});assert.equal(r.accepted,true,kind);
  p.index++;
 }
 assert.equal(a.inv.water,0);assert.ok(a.needs.thirst<=35);
});
test('phase2: reservations protect inputs from donation',()=>{
 const w=createWorld();const a=w.people[0];a.inv.water=0;a.inv.dirty_water=1;a.inv.wood=1;
 a.plan=makePlan(a,visibleView(w,a),SURVIVAL_GOALS[0]);
 assert.equal(availableUnreserved(a,'wood'),0);assert.equal(availableUnreserved(a,'dirty_water'),0);
 assert.ok(a.plan.steps.some(x=>x.kind==='boil'));
});
test('phase2: cannot fabricate resources with empty known sources',()=>{
 const w=createWorld({resources:0});const a=w.people[0];a.inv.water=0;a.inv.dirty_water=0;a.inv.wood=0;
 assert.equal(makePlan(a,visibleView(w,a),SURVIVAL_GOALS[0]),null);
});
