import test from 'node:test';import assert from 'node:assert/strict';
import {createWorld,visibleView} from '../src/v04/world.mjs';
import {makePlan,SURVIVAL_GOALS} from '../src/v04/planner.mjs';
import {chooseMode} from '../src/v04/lod.mjs';
import {run,summary,worldSnapshot} from '../src/v04/runtime.mjs';
test('phase4: valid continuing plan uses automatic bounded path',()=>{
 const w=createWorld();const a=w.people[0];a.inv.water=0;a.inv.wood=0;a.inv.dirty_water=0;
 a.plan=makePlan(a,visibleView(w,a),SURVIVAL_GOALS[0]);
 assert.equal(chooseMode(a,visibleView(w,a)).mode,'automatic');
});
test('phase4: newly arrived observation forces deliberate review',()=>{
 const w=createWorld();const a=w.people[0];a.inv.water=0;a.inv.dirty_water=0;a.inv.wood=0;
 a.plan=makePlan(a,visibleView(w,a),SURVIVAL_GOALS[0]);a.lastEventTick=w.tick;
 assert.equal(chooseMode(a,visibleView(w,a)).mode,'deliberate');
});
test('phase4: competing critical needs preempt cached plan',()=>{
 const w=createWorld();const a=w.people[0];a.inv.water=0;a.inv.dirty_water=0;a.inv.wood=0;
 a.plan=makePlan(a,visibleView(w,a),SURVIVAL_GOALS[0]);a.needs.hunger=89;
 assert.equal(chooseMode(a,visibleView(w,a)).mode,'deliberate');
});
test('phase4: invalidated prerequisites require replanning',()=>{
 const w=createWorld();const a=w.people[0];a.inv.water=0;a.inv.dirty_water=0;a.inv.wood=0;
 a.plan=makePlan(a,visibleView(w,a),SURVIVAL_GOALS[0]);w.sources.dirty_water=0;
 assert.equal(chooseMode(a,visibleView(w,a)).mode,'deliberate');
});
test('phase4: LOD reduces full candidate evaluations across same-seed runs',()=>{
 const opts={seed:42,days:10,agents:24};const low=run({...opts,cognitiveLOD:true});
 const high=run({...opts,cognitiveLOD:false});
 assert.equal(summary(low).balanced,true);assert.equal(summary(high).balanced,true);
 assert.ok(low.stats.auto>0);assert.equal(high.stats.auto,0);
 assert.ok(low.stats.candidateChecks<high.stats.candidateChecks);
 assert.equal(low.tick,high.tick);
});
test('phase4: replay is exact on fixed platform, for 50 NPC',()=>{
 const o={seed:37,agents:50,days:5,resources:.65};
 assert.deepEqual(worldSnapshot(run(o)),worldSnapshot(run(o)));
});
test('phase4: 100 and 500 agents remain finite and balanced',()=>{
 for(const agents of [100,500]){
  const s=summary(run({seed:12,agents,days:3,resources:.6}));
  assert.equal(s.balanced,true);assert.ok(Number.isFinite(s.health));
  assert.equal(s.turns,agents*3*3);
 }
});
test('phase4: 30 held-out seeds: every automatic action equals full deliberate choice',()=>{
  let auto=0;
  for(let seed=1101;seed<=1130;seed++){
    const w=run({seed,days:12,resources:.55,auditLOD:true,agents:24});
    assert.equal(w.stats.autoDivergences?.length||0,0,`incorrect cached action at seed ${seed}`);
    auto+=w.stats.auto;
  }
  assert.ok(auto>0);
});
