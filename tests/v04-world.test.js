import test from 'node:test';import assert from 'node:assert/strict';
import {createWorld,execute,conservation,visibleView} from '../src/v04/world.mjs';
test('phase1: world action applies recipe exact resource ledger',()=>{
 const w=createWorld({seed:4});const a=w.people[0];a.inv.dirty_water=1;a.inv.wood=1;
 // An explicit fixture grants items; adjust fixture baseline accordingly.
 w.initialTotals.dirty_water++;w.initialTotals.wood++;
 const response=execute(w,{actor:a.id,kind:'boil'});
 assert.equal(response.accepted,true);assert.equal(a.inv.water,2);assert.equal(a.inv.dirty_water,0);
 assert.deepEqual(Object.values(conservation(w)).filter(n=>n!==0),[]);
});
test('phase1: invalid multi-item recipe cannot partially commit',()=>{
 const w=createWorld();const a=w.people[0];const before=JSON.stringify({inv:a.inv,ledger:w.ledger,events:w.events});
 const result=execute(w,{actor:a.id,kind:'build_shelter'});
 if(result.accepted){assert.equal(a.inv.shelter,1);} else {
  assert.equal(JSON.stringify({inv:a.inv,ledger:w.ledger,events:w.events}),before);
  assert.equal(result.reason,'failed_precondition');
 }
});
test('phase1: agent view omits true world event history, private peer inventories',()=>{
 const w=createWorld();const view=visibleView(w,w.people[0]);
 assert.equal(view.events,undefined);assert.equal(view.incidents,undefined);
 assert.ok(view.nearby.every(p=>!('inv' in p)));
});
