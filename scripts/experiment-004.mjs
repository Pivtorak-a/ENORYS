/** ENORYS Experiment 004: deterministic, four-gate, headless research protocol. */
import {performance} from 'node:perf_hooks';
import {run,tick,createWorld,summary} from '../src/v04/runtime.mjs';
import {writeFileSync} from 'node:fs';
const avg=(xs,key)=>xs.reduce((sum,x)=>sum+key(x),0)/xs.length;
const round=(x,p=3)=>Number(x.toFixed(p));
const action=w=>k=>w.stats.actions[k]||0;
export function trials({count=40,startSeed=1000,agents=24,days=14,resources=.55}={}){
 const configs=[{name:'reactive',plannerEnabled:false,cognitiveLOD:false,socialEnabled:true},
  {name:'planned',plannerEnabled:true,cognitiveLOD:false,socialEnabled:true},
  {name:'planned_lod',plannerEnabled:true,cognitiveLOD:true,socialEnabled:true},
  {name:'planned_without_social',plannerEnabled:true,cognitiveLOD:true,socialEnabled:false}];
 const variants={};let examples={};
 for(const conf of configs){
  const runs=[];
  for(let i=0;i<count;i++){
   const w=run({seed:startSeed+i,agents,days,resources,visibility:.45,misidentification:.12,...conf});
   const s=summary(w);
   if(!s.balanced)throw Error('Item conservation failed for '+conf.name+' seed '+(startSeed+i));
   runs.push({health:s.health,alive:s.alive,hydration:s.meanNeeds.thirst,hunger:s.meanNeeds.hunger,
    candidates:s.stats.candidateChecks,automatic:s.stats.auto,deliberate:s.stats.deliberate,
    plans:s.stats.plansCompleted,peopleHelped:(s.actions.give||0),returns:(s.actions.repay||0),
    accusations:(s.actions.accuse||0),rumors:(s.actions.tell||0),thefts:(s.actions.take||0),
    rejected:s.stats.rejected,dead:s.stats.dead});
   if(i===0)examples[conf.name]={stats:s.stats,actions:s.actions};
  }
  variants[conf.name]=Object.fromEntries(Object.keys(runs[0]).map(k=>[k,round(avg(runs,r=>r[k]))]));
 }
 return {protocol:{count,startSeed,agents,days,resources},variants,examples};
}
export function benchmark({seed=312,agentsList=[50,100,500],days=5,repeats=4}={}){
 const rows=[];
 for(const agents of agentsList)for(const lod of [false,true]){
  const cases=[];
  for(let j=0;j<repeats;j++){
   const w=createWorld({seed:seed+j,agents,days,resources:.8});
   const timings=[];let t0=performance.now();
   while(w.tick<w.agents*w.days*w.turnsPerDay){
    const start=performance.now();tick(w,{plannerEnabled:true,socialEnabled:true,cognitiveLOD:lod});
    timings.push(performance.now()-start);
   }
   const elapsed=performance.now()-t0;
   const s=summary(w);if(!s.balanced)throw Error('benchmark resource violation');
   timings.sort((a,b)=>a-b);
   cases.push({totalMs:elapsed,tickP95Ms:timings[Math.floor(timings.length*.95)],
    candidateChecks:s.stats.candidateChecks,automatic:s.stats.auto,health:s.health});
  }
  rows.push({agents,lod,days,ticks:agents*days*3,meanTotalMs:round(avg(cases,r=>r.totalMs)),
   meanP95TickMs:round(avg(cases,r=>r.tickP95Ms),4),
   meanChecks:round(avg(cases,r=>r.candidateChecks)),
   automatic:round(avg(cases,r=>r.automatic)),health:round(avg(cases,r=>r.health))});
 }
 return {platform:{node:process.version,arch:process.arch,os:process.platform,cpus:'not pinned',
  note:'microbenchmark in shared sandbox; NOT real-game FPS or .NET performance'},repeats,rows};
}
export function story(seed=42){
 const w=run({seed,agents:24,days:18,resources:.5,visibility:.55,misidentification:.18});
 const kinds=new Set(['take','ask','give','repay','tell','accuse','investigate','retract','build_shelter','boil','cook','treat']);
 return {seed,summary:summary(w),events:w.events.filter(e=>kinds.has(e.kind)).slice(0,75),
  decisions:w.decisions.filter(d=>['take','give','repay','tell','accuse','investigate','retract'].includes(d.selected)).slice(0,45)};
}
if(import.meta.url===`file://${process.argv[1]}`){
 const data={generatedAt:'2026-10-12',trials:trials(),benchmarks:benchmark(),story:story()};
 const output=process.argv[2];if(output)writeFileSync(output,JSON.stringify(data,null,2)+'\n');
 console.log(JSON.stringify({results: data.trials.variants,benchmarks:data.benchmarks.rows,
   storyActions:data.story.summary.actions,storyHealth:data.story.summary.health},null,2));
}
