/** Deterministic runtime: one actor receives one private packet per scheduled turn. */
import {createWorld,visibleView,execute,advanceDay,conservation,worldSnapshot} from './world.mjs';
import {deliberate} from './cognition.mjs';
import {processInbox,recordAction} from './memory.mjs';
import {chooseMode,automaticDecision} from './lod.mjs';
export function tick(world,opts={}){
  if(world.tick>=world.days*world.agents*world.turnsPerDay)throw new RangeError('Simulation completed');
  if(world.tick>0 && world.tick%(world.agents*world.turnsPerDay)===0)advanceDay(world);
  const actor=world.people[world.tick%world.agents];
  if(!actor.alive){
    world.decisions.push({tick:world.tick,actor:actor.id,mode:'inactive',selected:null,
      reason:'dead agent performs no actions',candidates:0,accepted:true,plan:null});
    world.tick++;return {accepted:true,inactive:true};
  }
  processInbox(actor,world.tick);
  const packet=visibleView(world,actor);
  const mode=chooseMode(actor,packet,opts);
  const decision=mode.mode==='automatic'?automaticDecision(actor,packet,mode):deliberate(actor,packet,opts);
  if(mode.mode==='automatic') {
    world.stats.auto++;
    if(opts.auditLOD){
      const shadow=deliberate(structuredClone(actor),packet,opts);
      if(shadow.proposal.kind!==decision.proposal.kind){
        world.stats.autoDivergences??=[];
        if(world.stats.autoDivergences.length<30)world.stats.autoDivergences.push({tick:world.tick,actor:actor.id,fast:decision.proposal.kind,full:shadow.proposal.kind,needs:{...actor.needs},candidates:shadow.candidates.slice(0,20).sort((a,b)=>b.score-a.score).slice(0,3)});
      }
    }
  }
  else {world.stats.deliberate++;world.stats.plannerCalls++;}
  world.stats.candidateChecks+=decision.candidates.length;
  const result=execute(world,decision.proposal);
  if(result.accepted){
    if(actor.plan && result.event.kind===actor.plan.steps[actor.plan.index]?.kind){
      actor.plan.index++;if(actor.plan.index>=actor.plan.steps.length){
        actor.plan=null;world.stats.plansCompleted++;}
    }
    recordAction(actor,decision.proposal,packet,world.tick);
  }else if(actor.plan){actor.plan=null;world.stats.plansReplanned++;}
  world.decisions.push({tick:world.tick,actor:actor.id,mode:decision.mode,
    selected:decision.proposal.kind,reason:decision.reason,candidates:decision.candidates.length,
    accepted:result.accepted,plan:actor.plan?.goal??null});
  world.tick++;return result;
}
export function run(opts={}){
  const {plannerEnabled=true,socialEnabled=true,cognitiveLOD=true,auditLOD=false,...worldConfig}=opts;
  const world=createWorld(worldConfig);
  while(world.tick<world.days*world.agents*world.turnsPerDay)
    tick(world,{plannerEnabled,socialEnabled,cognitiveLOD:plannerEnabled&&cognitiveLOD,auditLOD});
  return world;
}
export function summary(w){
  const discrepancies=conservation(w);
  const alive=w.people.filter(p=>p.alive).length;
  const needs=Object.fromEntries(Object.keys(w.people[0].needs).map(k=>[k,
    w.people.reduce((s,a)=>s+a.needs[k],0)/w.people.length]));
  const eventsByKind=Object.fromEntries(Object.entries(w.stats.actions).sort(([a],[b])=>a.localeCompare(b)));
  return {seed:w.seed,days:w.day,agents:w.agents,turns:w.tick,alive,health:w.people.reduce((s,a)=>s+a.health,0)/w.agents,
    meanNeeds:needs,actions:eventsByKind,balanced:Object.values(discrepancies).every(x=>x===0),
    discrepancies,stats:w.stats};
}
export {createWorld,worldSnapshot};
