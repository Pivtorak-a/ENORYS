/** Authoritative survival world. Cognition never receives this object. */
import { rng } from '../../src/engine.js';
import { NEEDS, ITEMS, RECIPES, clamp, zeros, initialNeeds, canPay, itemDelta } from './domain.mjs';
import { availableUnreserved } from './planner.mjs';

const indexOf=(w,id)=>w.people.findIndex(p=>p.id===id);
export function createWorld({seed=42,agents=24,days=16,turnsPerDay=3,resources=1,
  visibility=.35,misidentification=.08,cold=.5,rumors=true}={}) {
  if(!Number.isInteger(seed)||seed<0||seed>4294967295||!Number.isInteger(agents)||agents<12||agents>500||
    !Number.isInteger(days)||days<1||days>100||!Number.isInteger(turnsPerDay)||turnsPerDay<1||turnsPerDay>10||
    ![resources,visibility,misidentification,cold].every(Number.isFinite)||resources<0||resources>3||
    visibility<0||visibility>1||misidentification<0||misidentification>1||cold<0||cold>1)throw new RangeError('Bad world configuration');
  const random=rng(seed);
  const people=Array.from({length:agents},(_,i)=>{
    const inv=zeros();inv.food=1+Number(random()>.65);inv.water=1+Number(random()>.65);
    inv.wood=Number(random()>.5);inv.soap=Number(random()>.8);
    return {id:`A${String(i+1).padStart(3,'0')}`,zone:i%4,group:i%3,alive:true,
      inv,needs:initialNeeds(random),health:95,injury:0,infection:0,
      traits:{greed:random(),generosity:random(),norm:random(),courage:random(),curiosity:random(),sociability:random()},
      plan:null, evidence:new Map(),trust:{},debts:{},requests:[],inbox:[],contacts:0,
      reported:new Set(),acted:new Set(),lastEventTick:-1,awake:true};
  });
  const depot=zeros(),sources=zeros();
  for(const [k,v] of Object.entries({food:Math.ceil(agents*.2),water:Math.ceil(agents*.25),
    bandage:2,medicine:2,soap:2,latrine:1}))depot[k]=Math.round(v*resources);
  for(const [k,v] of Object.entries({dirty_water:days*agents*.72,raw_food:days*agents*.62,wood:days*agents*.9,
    medicine:agents*.1,bandage:agents*.2,soap:agents*.3,coat:agents*.25,blanket:agents*.25,
    tool:agents*.25,bed:agents*.20}))sources[k]=Math.round(v*resources);
  const world={seed,people,depot,sources,agents,days,turnsPerDay,visibility,misidentification,cold,rumors,
    tick:0,day:1,events:[],decisions:[],nextEventId:1,guards:0,ledger:{consumed:zeros(),converted:zeros()},
    stats:{actions:{},rejected:0,eyeWitnesses:0,falseAccusations:0,accurateAccusations:0,
      retractions:0,delivered:0,plansCompleted:0,plansInterrupted:0,plansReplanned:0,
      auto:0,deliberate:0,plannerCalls:0,candidateChecks:0,dead:0},initialTotals:zeros()};
  for(const k of Object.keys(ITEMS))world.initialTotals[k]=countItem(world,k);
  return world;
}
export function countItem(w,k){return w.sources[k]+w.depot[k]+w.people.reduce((s,a)=>s+a.inv[k],0);}
export function conservation(w){return Object.fromEntries(Object.keys(ITEMS).map(k=>[
  k,countItem(w,k)-w.initialTotals[k]-w.ledger.converted[k]+w.ledger.consumed[k]]));}
export function visibleView(w,a){
  const peers=w.people.filter(p=>p.id!==a.id&&p.alive&&p.zone===a.zone);
  return Object.freeze({weather:w.cold,day:w.day,tick:w.tick,depot:Object.freeze({...w.depot}),
    nearby:peers.map(p=>({id:p.id,group:p.group})),
    messages:a.inbox.map(m=>({...m})),requests:a.requests.map(m=>({...m})),
    sourcesKnown:Object.freeze({raw_food:w.sources.raw_food>0,dirty_water:w.sources.dirty_water>0,
      wood:w.sources.wood>0}),guarded:w.guards>w.tick});
}
const randomKey=(w,agentId,channel)=>rng((w.seed ^ Math.imul(agentId,0x9e3779b1) ^ Math.imul(w.tick+1,0x85ebca6b) ^ channel)>>>0)();
function send(w,to,packet){const a=w.people[indexOf(w,to)];if(!a||!a.alive)return false;
  a.inbox.push({...packet});if(a.inbox.length>80)a.inbox.shift();a.lastEventTick=w.tick;w.stats.delivered++;return true;}
export function execute(w,proposal){
  const ai=indexOf(w,proposal.actor),a=w.people[ai];
  if(!a||!a.alive||!proposal||typeof proposal.kind!=='string')throw new TypeError('Invalid proposal');
  const item=proposal.item,kind=proposal.kind,inv=a.inv;
  const localTarget=()=>{const p=w.people[indexOf(w,proposal.target)];return p?.alive&&p.zone===a.zone;};
  const validation=()=>{
    if(kind in RECIPES)return canPay(inv,RECIPES[kind])&&(kind!=='build_latrine'||w.depot.latrine===0);
    if(['eat','drink','wash','warm','treat','bandage'].includes(kind))return inv[{eat:'food',drink:'water',wash:'soap',warm:'wood',treat:'medicine',bandage:'bandage'}[kind]]>0 && (kind!=='wash'||inv.water>0);
    if(['gather_food','collect_water','gather_wood'].includes(kind))return w.sources[{gather_food:'raw_food',collect_water:'dirty_water',gather_wood:'wood'}[kind]]>0;
    if(kind==='scavenge')return Object.values(w.sources).some(x=>x>0);
    if(kind==='take')return ['food','water','wood'].includes(item)&&w.depot[item]>0;
    if(kind==='give'||kind==='repay')return item in ITEMS&&inv[item]>0&&localTarget()&&a.id!==proposal.target&&
      (kind!=='give'||availableUnreserved(a,item)>0)&&
      (kind!=='repay'||(a.debts[proposal.target+':'+item]||0)>0)&&
      (kind!=='give'||a.requests.some(r=>r.from===proposal.target&&r.item===item));
    if(kind==='ask')return item in ITEMS && localTarget() && a.id!==proposal.target;
    if(kind==='tell')return localTarget()&&a.id!==proposal.target&&!!proposal.root&&Number.isFinite(proposal.lr)&&
      [...a.evidence.values()].some(x=>x.root===proposal.root&&x.active&&x.incident===proposal.incident&&x.suspect===proposal.suspect);
    if(kind==='retract')return localTarget()&&a.id!==proposal.target&&!!proposal.root&&
      (a.sentReports||[]).some(x=>x.root===proposal.root&&x.to===proposal.target);
    if(kind==='investigate'||kind==='accuse')return !!proposal.incident&&
      [...a.evidence.values()].some(x=>x.incident===proposal.incident&&x.active)&&
      (kind!=='accuse'||(indexOf(w,proposal.target)>=0&&
        [...a.evidence.values()].some(x=>x.incident===proposal.incident&&x.suspect===proposal.target&&x.active)));
    return ['sleep','relieve','socialize','guard','wait'].includes(kind);
  };
  // Validate entire proposal before any effect: rejection must not mutate resource state.
  if(!validation()){w.stats.rejected++;return {accepted:false,reason:'failed_precondition',proposal};}
  const e={id:`E${String(w.nextEventId++).padStart(6,'0')}`,tick:w.tick,day:w.day,actor:a.id,kind,
    item:item||null,target:proposal.target||null,root:proposal.root||null};
  const consume=(k,n=1)=>{inv[k]-=n;w.ledger.consumed[k]+=n;};
  if(kind in RECIPES){
    const delta=itemDelta(RECIPES[kind]);
    for(const [k,n] of Object.entries(delta)){inv[k]+=n;w.ledger.converted[k]+=n;}
    if(kind==='build_latrine'){inv.latrine--;w.depot.latrine++;}
    if(kind==='build_shelter')a.needs.safety=clamp(a.needs.safety-20);
  }else if(kind==='eat'){consume('food');a.needs.hunger=clamp(a.needs.hunger-57);}
  else if(kind==='drink'){consume('water');a.needs.thirst=clamp(a.needs.thirst-65);}
  else if(kind==='sleep'){a.needs.fatigue=clamp(a.needs.fatigue-(a.inv.bed?63:48));}
  else if(kind==='relieve'){a.needs.bladder=clamp(a.needs.bladder-70);if(w.depot.latrine===0)a.needs.hygiene=clamp(a.needs.hygiene+12);}
  else if(kind==='wash'){consume('soap');consume('water');a.needs.hygiene=clamp(a.needs.hygiene-55);}
  else if(kind==='warm'){consume('wood');a.needs.cold=clamp(a.needs.cold-52);}
  else if(kind==='treat'){consume('medicine');a.infection=clamp(a.infection-35);a.health=clamp(a.health+5);}
  else if(kind==='bandage'){consume('bandage');a.injury=clamp(a.injury-30);}
  else if(kind==='socialize'){a.needs.belonging=clamp(a.needs.belonging-23);a.needs.morale=clamp(a.needs.morale-12);a.contacts++;}
  else if(['gather_food','collect_water','gather_wood'].includes(kind)){
    const source={gather_food:'raw_food',collect_water:'dirty_water',gather_wood:'wood'}[kind];
    w.sources[source]--;inv[source]++;
  }else if(kind==='scavenge'){
    const source=Object.keys(w.sources).find(k=>w.sources[k]>0);
    w.sources[source]--;inv[source]++;e.item=source;
  }else if(kind==='take'){
    w.depot[item]--;inv[item]++;e.incident=e.id;
    const peers=w.people.filter(p=>p.zone===a.zone&&p.id!==a.id&&p.alive);
    for(const [j,p] of peers.entries())if(randomKey(w,ai+j,47)<w.visibility){
      const mistaken=randomKey(w,ai+j,71)<w.misidentification;
      const wrong=w.people.find(x=>x.id!==a.id&&x.id!==p.id);
      const suspect=mistaken?wrong.id:a.id;
      send(w,p.id,{type:'evidence',incident:e.id,root:`${e.id}/${p.id}`,suspect,
        lr:Math.log(8),source:'eyewitness',from:p.id});w.stats.eyeWitnesses++;
    }
  }else if(kind==='ask'){
    send(w,proposal.target,{type:'request',from:a.id,item,amount:1,at:w.tick});
  }else if(kind==='give'||kind==='repay'){
    const b=w.people[indexOf(w,proposal.target)];inv[item]--;b.inv[item]++;
    if(kind==='give')a.requests=a.requests.filter(r=>!(r.from===b.id&&r.item===item));
    send(w,b.id,{type:kind==='give'?'gift':'repayment',from:a.id,item,at:w.tick});
    send(w,a.id,{type:kind==='give'?'gift_sent':'debt_paid',to:b.id,item,at:w.tick});
  }else if(kind==='tell'){
    send(w,proposal.target,{type:'evidence',incident:proposal.incident,root:proposal.root,
      suspect:proposal.suspect,lr:proposal.lr*.6,source:'hearsay',from:a.id});
  }else if(kind==='retract'){
    send(w,proposal.target,{type:'retract',root:proposal.root,from:a.id});w.stats.retractions++;
  }else if(kind==='investigate'){
    const old=w.events.find(event=>event.id===proposal.incident&&event.kind==='take');
    if(old){const suspect=randomKey(w,ai,77)<.20?w.people[(ai+2)%w.agents].id:old.actor;
      send(w,a.id,{type:'evidence',incident:old.id,root:`${old.id}/clue/${a.id}`,suspect,
        lr:Math.log(5),source:'trace',from:a.id});}
    a.acted.add('investigate:'+proposal.incident);
  }else if(kind==='accuse'){
    const old=w.events.find(event=>event.id===proposal.incident&&event.kind==='take');
    e.correct=!!old&&old.actor===proposal.target;
    w.stats[e.correct?'accurateAccusations':'falseAccusations']++;
    send(w,proposal.target,{type:'accused',from:a.id,incident:proposal.incident});
    a.acted.add('accuse:'+proposal.incident+':'+proposal.target);
  }else if(kind==='guard'){w.guards=w.tick+w.agents;a.needs.safety=clamp(a.needs.safety-12);}
  w.events.push(e);w.stats.actions[kind]=(w.stats.actions[kind]||0)+1;
  return {accepted:true,event:e};
}
export function advanceDay(w){
  w.day++;
  for(const a of w.people){if(!a.alive)continue;
    for(const [n,setting] of Object.entries(NEEDS))a.needs[n]=clamp(a.needs[n]+setting.daily);
    a.needs.cold=clamp(a.needs.cold+(w.cold>.7?11:4)-(a.inv.coat?5:0)-(a.inv.shelter?7:0));
    if(a.needs.hunger>85)a.health-=5;
    if(a.needs.thirst>85)a.health-=9;
    if(a.needs.cold>85)a.health-=4;
    if(a.infection>0)a.health-=a.infection*.03;
    if(a.needs.hygiene>85)a.infection=clamp(a.infection+3);
    a.health=clamp(a.health,0,100);
    if(a.health<=0){a.alive=false;w.stats.dead++;}
  }
}
export function worldSnapshot(w){return {
  seed:w.seed,tick:w.tick,day:w.day,stats:w.stats,
  people:w.people.map(p=>({id:p.id,alive:p.alive,inv:p.inv,needs:p.needs,health:p.health,
    trust:p.trust,debts:p.debts,evidence:[...p.evidence],plan:p.plan})),
  depot:w.depot,sources:w.sources,ledger:w.ledger,
  events:w.events,decisions:w.decisions,
};}
