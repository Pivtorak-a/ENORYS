/** ENORYS Experiment 003 — multi-need, multi-item autonomous survival simulation.
 * A bounded, deterministic research toy. Not a model of real human needs.
 * All world mutations pass through the World Authority (apply / nextDay).
 * Agents receive only their local needs, inventory, delivered memories and public observations.
 */
import { rng } from '../src/engine.js';

export const ITEMS = Object.freeze({
  food:          {type:'consumable',need:'hunger',description:'Ready-to-eat ration'},
  water:         {type:'consumable',need:'thirst',description:'Safe drinking water'},
  raw_food:      {type:'material',description:'Unprepared food'},
  dirty_water:   {type:'material',description:'Unsafe water requiring boiling'},
  wood:          {type:'material',description:'Fuel or shelter material'},
  medicine:      {type:'consumable',need:'health',description:'Treats infection or injury'},
  bandage:       {type:'consumable',need:'health',description:'Stops bleeding'},
  soap:          {type:'consumable',need:'hygiene',description:'Washing supply'},
  blanket:       {type:'durable',need:'cold',description:'Passive insulation'},
  coat:          {type:'durable',need:'cold',description:'Protective clothing'},
  tool:          {type:'durable',description:'Improves gathering, wears out'},
  bed:           {type:'durable',need:'fatigue',description:'Improves sleep'},
  shelter:       {type:'structure',need:'safety',description:'Passive safety and insulation'},
  latrine:       {type:'structure',need:'bladder',description:'Communal sanitation facility'},
});
export const NEEDS = Object.freeze(['hunger','thirst','fatigue','cold','hygiene','bladder','safety','belonging','morale']);
export const ACTIONS = Object.freeze(['eat','drink','eat_raw','drink_dirty','sleep','relieve','warm','wash','treat','bandage','socialize','gather_food','collect_water',
  'gather_wood','scavenge','cook','boil','build_shelter','build_latrine','ask','give','take','tell','investigate','accuse','defend','repay','guard','wait']);
const clamp = (x,lo=0,hi=100) => Math.max(lo, Math.min(hi,x));
const round = x => Math.round(x*1000)/1000;
const defaultMap = () => Object.fromEntries(Object.keys(ITEMS).map(k=>[k,0]));
const roster=['Ada','Borys','Celia','Danylo','Emma','Fedir','Hanna','Ihor','Iryna','Kyrylo','Lada','Marko',
  'Nina','Oleh','Petro','Rita','Sava','Tania','Ulana','Vira','Yaroslav','Zlata','Lev','Marta','Taras','Sofiia','Roman','Oksana','Artem','Mila'];
const dRand = (world, actor, channel, extra=0) => {
  const seed=(world.seed ^ Math.imul(actor+1,0x9e3779b1) ^ Math.imul(world.tick+1,0x85ebca6b) ^
    Math.imul(channel+1,0xc2b2ae35) ^ Math.imul(extra+1,0x27d4eb2f))>>>0;
  return rng(seed)();
};
function checkConfig({seed,agents,visibility,misidentification,weatherSeverity,resources,days,turnsPerDay}) {
  if (!Number.isInteger(seed) || seed<0 || seed>4294967295 ||
      !Number.isInteger(agents) || agents<12 || agents>30 ||
      !Number.isFinite(visibility) || visibility<0 || visibility>1 ||
      !Number.isFinite(misidentification) || misidentification<0 || misidentification>1 ||
      !Number.isFinite(weatherSeverity) || weatherSeverity<0 || weatherSeverity>1 ||
      !Number.isFinite(resources) || resources<0 || resources>3 ||
      !Number.isInteger(days) || days<1 || days>40 ||
      !Number.isInteger(turnsPerDay) || turnsPerDay<1 || turnsPerDay>5)
    throw new RangeError('Invalid simulation parameters');
}
export function createWorld({seed=42,agents=24,visibility=.3,misidentification=.1,gossip=true,
  weatherSeverity=.6,resources=1,days=12,turnsPerDay=3}={}) {
  checkConfig({seed,agents,visibility,misidentification,weatherSeverity,resources,days,turnsPerDay});
  const r=rng(seed), people=Array.from({length:agents},(_,i)=>{
    const inv=defaultMap();
    inv.food=1+Math.floor(r()*2);inv.water=1+Math.floor(r()*2);
    inv.wood=+(r()<.25);inv.blanket=+(r()<.35);inv.coat=+(r()<.25);
    inv.tool=+(r()<.3);inv.bed=+(r()<.18);inv.bandage=+(r()<.1);
    return {id:`A${String(i+1).padStart(2,'0')}`,name:roster[i],inv,
      needs:{hunger:18+Math.floor(r()*23),thirst:19+Math.floor(r()*23),fatigue:15+Math.floor(r()*25),
        cold:20+Math.floor(r()*18),hygiene:10+Math.floor(r()*20),bladder:12+Math.floor(r()*15),safety:20+Math.floor(r()*20),
        belonging:20+Math.floor(r()*25),morale:15+Math.floor(r()*23)},
      health:85+Math.floor(r()*16),wound:0,infection:0,toolWear:0,
      traits:{greed:r(),generosity:r(),norm:r(),resourcefulness:r(),sociability:r(),courage:r(),curiosity:r()},
      trust:{},beliefs:{},reported:new Set(),acted:new Set(),accusations:[],ownTakes:[],
      receivedRequests:[], socialContacts:0, alive:true};
  });
  const depot=defaultMap(), sources=defaultMap();
  for(const [k,v] of Object.entries({food:7,water:10,wood:7,medicine:2,soap:2,bandage:2,blanket:1,tool:1,latrine:1}))
    depot[k]=Math.max(0,Math.round(v*resources));
  for(const [k,v] of Object.entries({raw_food:300,dirty_water:450,wood:190,medicine:13,soap:18,bandage:14,
    coat:6,blanket:11,tool:10,bed:8})) sources[k]=Math.max(0,Math.round(v*resources));
  const world={seed,agents,visibility,misidentification,gossip,weatherSeverity,resources,days,turnsPerDay,
    people,depot,sources,tick:0,day:1,events:[],decisions:[],incidents:[],requests:[],publicGuardUntil:0,
    ledger:{consumed:defaultMap(),converted:defaultMap()},stats:{},identifiers:0,weather:'mild',
    initialTotals:defaultMap()};
  for(const item of Object.keys(ITEMS))world.initialTotals[item]=world.depot[item]+world.sources[item]+
    people.reduce((s,p)=>s+p.inv[item],0);
  for(const kind of ACTIONS)world.stats[kind]=0;
  Object.assign(world.stats,{falseAccusations:0,accurateAccusations:0,wrongIdentification:0,eyewitness:0,
    rejected:0,rumorsRejected:0,dead:0,illnessEvents:0});
  return world;
}
function context(world,agent) {
  // Publicly posted depot list, the weather, and explicit delivered requests.
  // NO access to world.incidents, action log, true attacker or source reserves.
  const local=world.people.indexOf(agent),neighbors=[1,2,3,5].map(k=>world.people[(local+k)%world.agents]);
  return {depot:{...world.depot},weather:world.weather,guarded:world.tick<world.publicGuardUntil,
    neighbors:neighbors.filter(x=>x.alive).map(x=>({id:x.id,name:x.name})),
    requests:agent.receivedRequests.map(x=>({...x}))};
}
function strongestBelief(agent) {
  const results=[];
  for(const [incident,memories] of Object.entries(agent.beliefs)){
    const grouped=new Map();
    for(const m of memories){if(m.suspect===agent.id)continue;
      grouped.set(m.suspect,[...(grouped.get(m.suspect)||[]),m.weight]);}
    for(const [suspect,weights] of grouped){weights.sort((a,b)=>b-a);
      const ll=Math.log(.12/.88)+weights.reduce((s,v,i)=>s+v*(i===0?1:.22),0);
      const posterior=1/(1+Math.exp(-ll));results.push({incident,suspect,posterior,roots:weights.length});}
  }
  return results.sort((x,y)=>y.posterior-x.posterior||x.incident.localeCompare(y.incident))[0]||null;
}
function score(kind,terms,extra={}) {return {kind,...extra,terms,score:Object.values(terms).reduce((s,v)=>s+v,0)};}
/** Only agent-local state and a deliberately filtered public view are used. */
export function decide(agent,view) {
  if(!agent.alive)return {chosen:score('wait',{dead:0}),candidates:[],visible:view};
  const n=agent.needs,t=agent.traits,I=agent.inv,u=k=>n[k]/100;
  const lack=k=>1-Math.min(1,I[k]);
  const maxNeed=Math.max(u('hunger'),u('thirst'));
  const candidates=[score('wait',{baseline:0.08})];
  if(I.food>0)candidates.push(score('eat',{hunger:3.8*u('hunger'),quality:.2}));
  if(I.water>0)candidates.push(score('drink',{thirst:4.4*u('thirst'),quality:.2}));
  if(I.food===0&&I.raw_food>0&&u('hunger')>.68)candidates.push(score('eat_raw',{urgency:3.5*u('hunger'),risk:-.6}));
  if(I.water===0&&I.dirty_water>0&&u('thirst')>.68)candidates.push(score('drink_dirty',{urgency:3.95*u('thirst'),risk:-.8}));
  candidates.push(score('sleep',{fatigue:3.1*u('fatigue'),comfort:.35+.22*Math.min(1,I.bed)+.1*Math.min(1,I.shelter)}));
  candidates.push(score('relieve',{urgency:3.6*u('bladder'),sanitation:view.depot.latrine>0?.18:-.05}));
  if(I.wood>0)candidates.push(score('warm',{cold:3.6*u('cold'),safety:.15*u('safety')}));
  if(I.soap>0&&I.water>0)candidates.push(score('wash',{hygiene:3.5*u('hygiene'),health:.3}));
  if(I.medicine>0&&(agent.infection>0||agent.health<75))candidates.push(score('treat',{
    illness:2.9*agent.infection/100,health:2.2*(100-agent.health)/100,urgency:.6}));
  if(I.bandage>0&&agent.wound>0)candidates.push(score('bandage',{wound:3*agent.wound/100,heal:.6}));
  candidates.push(score('socialize',{belonging:2.2*u('belonging'),morale:.7*u('morale'),temperament:.3*t.sociability}));
  if(I.raw_food<2&&I.food<2)candidates.push(score('gather_food',{need:.95+1.15*u('hunger')+.2*lack('raw_food'),skill:.32*t.resourcefulness}));
  if(I.dirty_water<2&&I.water<2)candidates.push(score('collect_water',{need:.9+1.25*u('thirst')+.2*lack('dirty_water'),skill:.24*t.resourcefulness}));
  if(I.wood<3)candidates.push(score('gather_wood',{need:.56+.8*u('cold')+.72*(I.dirty_water>0&&I.wood===0?1:0),
    shelter:.4*(I.shelter===0?1:0),skill:.20*t.resourcefulness}));
  const lackingSupplies=(agent.infection>15&&I.medicine===0?2*agent.infection/100:0)+
    (agent.wound>10&&I.bandage===0?2*agent.wound/100:0)+
    (I.soap===0?1.4*u('hygiene'):0)+(I.coat===0?.7*u('cold'):0);
  candidates.push(score('scavenge',{baseline:.58,supplies:lackingSupplies,utility:.14*t.resourcefulness}));
  if(I.raw_food>0)candidates.push(score('cook',{urgency:1.45+1.45*u('hunger'),scarcity:.45*lack('food')}));
  if(I.dirty_water>0&&I.wood>0)candidates.push(score('boil',{urgency:1.5+1.7*u('thirst'),scarcity:.4*lack('water')}));
  if(I.wood>=3&&I.shelter===0)candidates.push(score('build_shelter',{cold:1.5*u('cold'),safety:1.4*u('safety'),longTerm:1.15}));
  if(I.wood>=2&&view.depot.latrine===0)candidates.push(score('build_latrine',
    {need:1.2*u('bladder'),hygiene:1.0*u('hygiene'),community:.6*t.norm,longTerm:.65}));
  const lacking=[['water',u('thirst')],['food',u('hunger')],['wood',u('cold')],['medicine',agent.infection/100],
    ['bandage',agent.wound/100]].filter(([item,need])=>I[item]===0&&need>.42).sort((a,b)=>b[1]-a[1]);
  if(lacking.length){const [item,need]=lacking[0];
    if(!agent.acted.has('ask:'+item+':'+Math.floor(agent.socialContacts/5)))
      candidates.push(score('ask',{need:1.1+2.1*need,social:.4*t.sociability},{item}));}
  // Donate only when a request arrived through communication, not because neighbors' private state is visible.
  const eligible=view.requests.filter(x=>I[x.item]>0&&x.from!==agent.id);
  if(eligible.length){const req=[...eligible].sort((a,b)=>b.priority-a.priority)[0];
    candidates.push(score('give',{altruism:1.1+1.5*t.generosity,urgency:1.2*req.priority,
      reserve:-(I[req.item]<=1?.8:.04)},{item:req.item,recipient:req.from}));}
  if(maxNeed>.35){const item=u('thirst')>u('hunger')?'water':'food';
    if(view.depot[item]>0)candidates.push(score('take',{need:.4+2.35*maxNeed,greed:.75*t.greed,
      ethics:-.95*t.norm,guard:view.guarded?-.95:0},{item}));}
  const belief=strongestBelief(agent);
  if(belief){
    const known=agent.beliefs[belief.incident] || [];
    if(belief.posterior>.35&& !agent.acted.has('investigate:'+belief.incident))
      candidates.push(score('investigate',{suspicion:1.15*belief.posterior,curiosity:1.05*t.curiosity,
        norm:.4*t.norm},{incident:belief.incident}));
    if(belief.posterior>.52&&!agent.acted.has('accuse:'+belief.incident+':'+belief.suspect))
      candidates.push(score('accuse',{suspicion:1.9*belief.posterior,courage:.7*t.courage,
        norm:.45*t.norm},{incident:belief.incident,target:belief.suspect}));
    const recipient=view.neighbors.find(p=>p.id!==belief.suspect&&!agent.reported.has(belief.incident+':'+p.id));
    if(recipient)candidates.push(score('tell',{concern:1.0*belief.posterior,social:.95*t.sociability,
      norm:.35*t.norm},{incident:belief.incident,target:belief.suspect,recipient:recipient.id}));
    if(known.length&&u('safety')>.4&&!view.guarded)candidates.push(score('guard',{
      safety:.9*u('safety'),norm:.9*t.norm,concern:.55*belief.posterior}));
  }
  const pending=agent.accusations.find(x=>!agent.acted.has('defend:'+x.incident)&&!agent.ownTakes.includes(x.incident));
  if(pending)candidates.push(score('defend',{reputation:1.7,courage:.65*t.courage},{incident:pending.incident}));
  if(agent.ownTakes.length&&(I.food>0||I.water>0)&&agent.accusations.some(x=>agent.ownTakes.includes(x.incident)))
    candidates.push(score('repay',{shame:1.45,norm:.8*t.norm,pressure:.5},{incident:agent.ownTakes[0]}));
  const chosen=candidates.reduce((best,cur)=>cur.score>best.score?cur:best);
  return {chosen,candidates,visible:{weather:view.weather,guarded:view.guarded,
    depot:view.depot,requests:view.requests.map(x=>({...x})),belief}};
}
function total(world,item) {
  return world.sources[item]+world.depot[item]+world.people.reduce((s,p)=>s+p.inv[item],0);
}
function consume(world,actor,item,n=1) {
  if(actor.inv[item]<n)throw Error('Invalid consumption');
  actor.inv[item]-=n;world.ledger.consumed[item]+=n;
}
function convert(world,agent,from,to,n=1) {
  if(agent.inv[from]<n)throw Error('Invalid conversion');
  agent.inv[from]-=n;agent.inv[to]+=n;
  world.ledger.converted[from]-=n;world.ledger.converted[to]+=n;
}
function deliver(world,recipient,obs) {
  const {incident,suspect,root,weight}=obs;
  if(!incident||!root||!world.people.some(p=>p.id===suspect)||!Number.isFinite(weight))throw Error('Invalid report');
  const existing=recipient.beliefs[incident] || (recipient.beliefs[incident]=[]);
  if(existing.some(x=>x.root===root)){world.stats.rumorsRejected++;return false;}
  if(existing.length>=120)return false;
  existing.push({incident,suspect,root,weight,kind:obs.kind,from:obs.from,tick:world.tick});
  recipient.trust[suspect]=clamp((recipient.trust[suspect]??0)-weight*3,-100,100);
  return true;
}
function neighbors(world,actor) {
  const idx=world.people.indexOf(actor);
  return [1,2,3,5].map(k=>world.people[(idx+k)%world.agents]).filter(p=>p.alive);
}
function perceiveIncident(world,event,actor) {
  for(let i=0;i<world.people.length;i++) {
    const witness=world.people[i];if(witness===actor||!witness.alive)continue;
    const p=clamp(world.visibility+(world.tick<world.publicGuardUntil?.15:0),0,1);
    if(dRand(world,i,14)<p){
      const mistaken=dRand(world,i,15)<world.misidentification;
      const suspects=world.people.filter(x=>x.id!==actor.id&&x!==witness);
      const suspect=mistaken?suspects[Math.floor(dRand(world,i,16)*suspects.length)].id:actor.id;
      if(mistaken)world.stats.wrongIdentification++;
      world.stats.eyewitness++;
      deliver(world,witness,{incident:event.id,suspect,root:event.id+'/sight/'+witness.id,
        weight:Math.log(8),kind:'sight',from:witness.id});
    }
  }
}
function toolWear(world,actor) {
  if(actor.inv.tool>0) {
    actor.toolWear++;
    if(actor.toolWear>=14){actor.toolWear=0;consume(world,actor,'tool');}
  }
}
function chooseSourceItem(world,agent) {
  const n=agent.needs;
  const desires={medicine:3*agent.infection/100+(agent.health<70?.9:0),bandage:3*agent.wound/100,
    coat:1.1*n.cold/100*(agent.inv.coat===0?1:0),blanket:1.0*n.cold/100*(agent.inv.blanket===0?1:0),
    tool:agent.inv.tool===0?.9:0,soap:1.1*n.hygiene/100,bed:agent.inv.bed===0?.45+n.fatigue/200:0};
  return Object.entries(desires).sort((a,b)=>b[1]-a[1])[0]?.[0]||'tool';
}
function apply(world,actor,proposal) {
  const kind=proposal.kind, e={id:`E${String(++world.identifiers).padStart(5,'0')}`,tick:world.tick,
    day:world.day,actor:actor.id,kind,details:'',item:proposal.item};
  const need=actor.needs, inv=actor.inv;
  const spent=(amount=0)=>{need.fatigue=clamp(need.fatigue+amount);need.hunger=clamp(need.hunger+amount*.3);};
  // Invalid proposals are rejected, not silently applied or granted world access.
  const reject=why=>{e.kind='rejected';e.details=why;world.stats.rejected++;};
  if(kind==='eat'&&inv.food>0){consume(world,actor,'food');need.hunger=clamp(need.hunger-49);e.details='ate one ration';}
  else if(kind==='drink'&&inv.water>0){consume(world,actor,'water');need.thirst=clamp(need.thirst-63);e.details='drank clean water';}
  else if(kind==='eat_raw'&&inv.raw_food>0){consume(world,actor,'raw_food');need.hunger=clamp(need.hunger-27);
    if(dRand(world,world.people.indexOf(actor),52)<.32){actor.infection=clamp(actor.infection+22);world.stats.illnessEvents++;}
    e.details='ate raw food without cooking, risking illness';}
  else if(kind==='drink_dirty'&&inv.dirty_water>0){consume(world,actor,'dirty_water');need.thirst=clamp(need.thirst-37);
    if(dRand(world,world.people.indexOf(actor),53)<.6){actor.infection=clamp(actor.infection+26);world.stats.illnessEvents++;}
    e.details='drank untreated water, risking infection';}
  else if(kind==='sleep'){need.fatigue=clamp(need.fatigue-47-13*(inv.bed>0)-5*(inv.shelter>0));
    need.safety=clamp(need.safety-(inv.shelter>0?12:3));e.details='slept'+(inv.bed?' in a bed':' without a bed');}
  else if(kind==='relieve'){need.bladder=clamp(need.bladder-75);
    if(world.depot.latrine>0){need.hygiene=clamp(need.hygiene+2);e.details='used the camp latrine';}
    else {need.hygiene=clamp(need.hygiene+13);
      if(dRand(world,world.people.indexOf(actor),59)<.1){actor.infection=clamp(actor.infection+8);world.stats.illnessEvents++;}
      e.details='relieved outdoors without proper sanitation';}
  }
  else if(kind==='warm'&&inv.wood>0){consume(world,actor,'wood');need.cold=clamp(need.cold-49);need.safety=clamp(need.safety-4);e.details='built a fire with one unit of wood';}
  else if(kind==='wash'&&inv.soap>0&&inv.water>0){consume(world,actor,'soap');consume(world,actor,'water');need.hygiene=clamp(need.hygiene-65);actor.infection=clamp(actor.infection-5);e.details='washed using soap and clean water';}
  else if(kind==='treat'&&inv.medicine>0){consume(world,actor,'medicine');actor.infection=clamp(actor.infection-46);actor.health=clamp(actor.health+19);e.details='used medicine to treat illness';}
  else if(kind==='bandage'&&inv.bandage>0){consume(world,actor,'bandage');actor.wound=clamp(actor.wound-65);actor.health=clamp(actor.health+4);e.details='bandaged an injury';}
  else if(kind==='socialize'){need.belonging=clamp(need.belonging-38);need.morale=clamp(need.morale-13);actor.socialContacts++;e.details='spent time talking with others';}
  else if(kind==='gather_food'||kind==='collect_water'||kind==='gather_wood'){
    const item=kind==='gather_food'?'raw_food':kind==='collect_water'?'dirty_water':'wood';
    let amount=1+(inv.tool>0?1:0);amount=Math.min(world.sources[item],amount);
    world.sources[item]-=amount;inv[item]+=amount;spent(7);toolWear(world,actor);
    if(dRand(world,world.people.indexOf(actor),56)<.018+(.05*(need.fatigue>75))){actor.wound=clamp(actor.wound+35);}
    e.item=item;e.amount=amount;e.details=amount?`collected ${amount} ${item}`:`found no more ${item} in the area`;
  }
  else if(kind==='scavenge'){
    const item=chooseSourceItem(world,actor);let wanted=item;
    if(world.sources[wanted]<=0){wanted=Object.keys(world.sources).find(k=>world.sources[k]>0&&
      ['medicine','bandage','coat','blanket','tool','soap','bed'].includes(k))||wanted;}
    const amount=world.sources[wanted]>0?1:0;
    world.sources[wanted]-=amount;inv[wanted]+=amount;spent(8);toolWear(world,actor);
    if(dRand(world,world.people.indexOf(actor),57)<.04){actor.wound=clamp(actor.wound+33);}
    e.item=wanted;e.amount=amount;e.details=amount?`scavenged one ${wanted}`:'the ruins had nothing useful left';
  }
  else if(kind==='cook'&&inv.raw_food>0){convert(world,actor,'raw_food','food');spent(2);e.details='prepared raw food into a ration';}
  else if(kind==='boil'&&inv.dirty_water>0&&inv.wood>0){convert(world,actor,'dirty_water','water');consume(world,actor,'wood');spent(1);e.details='boiled unsafe water into clean water';}
  else if(kind==='build_shelter'&&inv.wood>=3&&inv.shelter===0){inv.wood-=3;inv.shelter++;
    world.ledger.converted.wood-=3;world.ledger.converted.shelter++;spent(11);
    need.safety=clamp(need.safety-22);e.details='built a shelter from three units of wood';}
  else if(kind==='build_latrine'&&inv.wood>=2&&world.depot.latrine===0){
    inv.wood-=2;world.depot.latrine++;world.ledger.converted.wood-=2;world.ledger.converted.latrine++;
    spent(8);e.details='built a communal latrine from two units of wood';
  }
  else if(kind==='ask'){
    actor.acted.add('ask:'+proposal.item+':'+Math.floor(actor.socialContacts/5));
    const req={id:e.id,from:actor.id,item:proposal.item,priority:proposal.item==='water'?need.thirst/100:
      proposal.item==='food'?need.hunger/100:proposal.item==='wood'?need.cold/100:actor.infection/100,tick:world.tick};
    world.requests.push(req);for(const target of neighbors(world,actor)){target.receivedRequests.push({...req});
      if(target.receivedRequests.length>20)target.receivedRequests.shift();}
    e.details=`asked nearby agents for ${proposal.item}`;
  }
  else if(kind==='give'){
    const receiver=world.people.find(x=>x.id===proposal.recipient),item=proposal.item;
    if(!receiver||inv[item]<=0||!actor.receivedRequests.some(x=>x.from===receiver.id&&x.item===item))reject('invalid gift proposal');
    else {inv[item]--;receiver.inv[item]++;actor.receivedRequests=actor.receivedRequests.filter(x=>x.from!==receiver.id||x.item!==item);
      receiver.trust[actor.id]=clamp((receiver.trust[actor.id]??0)+10,-100,100);
      need.belonging=clamp(need.belonging-8);e.to=receiver.id;e.details=`gave one ${item} to ${receiver.name}`;}
  }
  else if(kind==='take'){
    const item=proposal.item;
    if(!item||world.depot[item]<=0)reject('public depot was empty');
    else {world.depot[item]--;inv[item]++;world.incidents.push({id:e.id,actor:actor.id,item,tick:world.tick});
      actor.ownTakes.push({incident:e.id,item});need.safety=clamp(need.safety+4);
      e.details=`removed one ${item} from the common depot without permission`;
      perceiveIncident(world,e,actor);}
  }
  else if(kind==='tell'){
    const listener=world.people.find(x=>x.id===proposal.recipient);
    const basis=(actor.beliefs[proposal.incident]||[]).find(x=>x.suspect===proposal.target);
    if(!listener||!basis)reject('no personal report to share');
    else {deliver(world,listener,{incident:basis.incident,suspect:basis.suspect,root:basis.root,
      weight:basis.weight*.55,kind:'hearsay',from:actor.id});
      actor.reported.add(proposal.incident+':'+listener.id);e.to=listener.id;e.incident=proposal.incident;
      e.details=`told ${listener.name} a report about ${basis.suspect}`;}
  }
  else if(kind==='investigate'){
    const incident=world.incidents.find(x=>x.id===proposal.incident);
    if(!incident)reject('no trace available');
    else {actor.acted.add('investigate:'+proposal.incident);
      const i=world.people.indexOf(actor),mistake=dRand(world,i,27)<.2;
      const others=world.people.filter(x=>x.id!==incident.actor);
      const suspect=mistake?others[Math.floor(dRand(world,i,28)*others.length)].id:incident.actor;
      deliver(world,actor,{incident:incident.id,suspect,root:incident.id+'/clue/'+actor.id,
        weight:Math.log(5),kind:'physical trace',from:actor.id});
      e.incident=incident.id;e.details=`found a ${mistake?'misleading ':' '}trace implicating ${suspect}`;}
  }
  else if(kind==='accuse'){
    const inc=world.incidents.find(x=>x.id===proposal.incident),target=world.people.find(x=>x.id===proposal.target);
    const basis=(actor.beliefs[proposal.incident]||[]).find(x=>x.suspect===proposal.target);
    if(!inc||!basis||!target)reject('unsupported accusation');
    else {actor.acted.add('accuse:'+proposal.incident+':'+proposal.target);e.target=target.id;e.incident=inc.id;
      e.correct=inc.actor===target.id;world.stats[e.correct?'accurateAccusations':'falseAccusations']++;
      target.accusations.push({incident:inc.id,from:actor.id});
      // Only original source claim, not objective truth, delivered to neighbors.
      for(const n of neighbors(world,actor))if(n!==target)deliver(world,n,{incident:inc.id,suspect:basis.suspect,
        root:basis.root,weight:basis.weight*.45,kind:'public accusation',from:actor.id});
      e.details=`accused ${target.name} of taking from the depot`;}
  }
  else if(kind==='defend'){
    actor.acted.add('defend:'+proposal.incident);e.incident=proposal.incident;e.details='publicly rejected an accusation';
  }
  else if(kind==='repay'){
    const debt=actor.ownTakes.find(x=>x.incident===proposal.incident);
    if(!debt||inv[debt.item]<=0)reject('unable to make restitution');
    else {inv[debt.item]--;world.depot[debt.item]++;actor.ownTakes=actor.ownTakes.filter(x=>x!==debt);
      e.incident=proposal.incident;e.item=debt.item;e.details=`returned one ${debt.item} to the depot`;}
  }
  else if(kind==='guard'){
    world.publicGuardUntil=world.tick+world.agents*world.turnsPerDay;
    need.safety=clamp(need.safety-11);e.details='organized watch at the common depot';
  }
  else { e.details='waited or had no useful action'; }
  if(e.kind!=='rejected')world.stats[kind]++;
  world.events.push(e);return e;
}
function nextDay(world) {
  const day=world.day+1;
  // Exogenous weather is determined by seed and day; it is not a scripted plot beat.
  const phase=(day*0.61803398875+world.seed*.001)%1;
  const temperature=(Math.sin(phase*2*Math.PI)*.4+.5)*world.weatherSeverity;
  world.weather=temperature>.55?'freezing':temperature>.32?'cold':'mild';
  for(const agent of world.people){
    if(!agent.alive)continue;
    const n=agent.needs;
    n.hunger=clamp(n.hunger+11);n.thirst=clamp(n.thirst+18);n.fatigue=clamp(n.fatigue+11);
    n.hygiene=clamp(n.hygiene+8);n.bladder=clamp(n.bladder+25);
    if(n.bladder>93)n.hygiene=clamp(n.hygiene+11);
    n.cold=clamp(n.cold+(world.weather==='freezing'?20:world.weather==='cold'?12:5)-
      (agent.inv.coat>0?10:0)-(agent.inv.blanket>0?5:0)-(agent.inv.shelter>0?9:0));
    n.safety=clamp(n.safety+5-(agent.inv.shelter>0?7:0)-(world.tick<world.publicGuardUntil?6:0));
    n.belonging=clamp(n.belonging+10-(agent.socialContacts>0?7:0));
    n.morale=clamp(n.morale+5+(n.hunger>80?7:0)+(n.thirst>80?8:0)+(agent.health<50?5:0));
    agent.socialContacts=0;
    if(n.hygiene>74&&dRand(world,world.people.indexOf(agent),40)<.3){agent.infection=clamp(agent.infection+13);world.stats.illnessEvents++;}
    let damage=agent.wound*.065+agent.infection*.04;
    if(n.hunger>85)damage+=6;
    if(n.thirst>85)damage+=11;
    if(n.cold>85)damage+=5;
    agent.health=clamp(agent.health-damage+(damage===0&&n.hunger<50&&n.thirst<50?1:0));
    agent.health=round(agent.health);
    if(agent.health<=0){agent.alive=false;world.stats.dead++;}
  }
  world.day=day;
}
export function step(world){
  if(world.tick>=world.days*world.agents*world.turnsPerDay)throw new RangeError('Simulation completed');
  if(world.tick>0&&world.tick%(world.agents*world.turnsPerDay)===0)nextDay(world);
  const actor=world.people[world.tick%world.agents];
  const view=context(world,actor),proposed=decide(actor,view);
  const event=apply(world,actor,proposed.chosen);
  world.decisions.push({eventId:event.id,actor:actor.id,kind:event.kind,score:round(proposed.chosen.score),
    candidates:proposed.candidates.map(x=>({kind:x.kind,score:round(x.score),terms:x.terms})),
    visible:proposed.visible,personal:{needs:{...actor.needs},health:actor.health,
      infection:actor.infection,wound:actor.wound,inventory:{...actor.inv}}});
  world.tick++;return event;
}
export function run(config={}){
  const world=createWorld(config);
  while(world.tick<world.days*world.agents*world.turnsPerDay)step(world);
  return world;
}
export function summary(world){
  const discrepancy=Object.fromEntries(Object.keys(ITEMS).map(item=>[
    item,total(world,item)-world.initialTotals[item]-world.ledger.converted[item]+world.ledger.consumed[item]]));
  const agentsAlive=world.people.filter(x=>x.alive),needs={};
  for(const k of NEEDS)needs[k]=round(world.people.reduce((s,x)=>s+x.needs[k],0)/world.agents);
  return {seed:world.seed,days:world.day,agents:world.agents,turns:world.tick,
    alive:agentsAlive.length,dead:world.stats.dead,meanHealth:round(world.people.reduce((s,x)=>s+x.health,0)/world.agents),
    meanNeeds:needs,inventoryTotals:Object.fromEntries(Object.keys(ITEMS).map(item=>[item,total(world,item)])),
    discrepancy,balanced:Object.values(discrepancy).every(x=>x===0),uniqueActions:new Set(world.events.map(x=>x.kind)).size,
    ...world.stats};
}
export function timeline(world,{limit=70}={}) {
  const important=new Set(['take','tell','investigate','accuse','defend','repay','guard','ask','give','build_shelter',
    'treat','bandage','relieve','build_latrine','scavenge','warm','boil','cook','rejected']);
  return world.events.filter(x=>important.has(x.kind)).slice(0,limit).map(e=>({
    ...e,name:world.people.find(x=>x.id===e.actor)?.name||e.actor}));
}
if(typeof process!=='undefined'&&import.meta.url===`file://${process.argv[1]}`){
  const seed=Number(process.argv[2]||42),days=Number(process.argv[3]||12);
  for(const condition of [{},{resources:.45},{weatherSeverity:1},{visibility:0,gossip:false}]){
    const world=run({seed,days,...condition});const s=summary(world);
    console.log(JSON.stringify({condition,seed,days,alive:s.alive,health:s.meanHealth,
      needs:s.meanNeeds,actions:Object.fromEntries(ACTIONS.map(k=>[k,s[k]])),balanced:s.balanced,
      falseAccusations:s.falseAccusations,accurateAccusations:s.accurateAccusations},null,2));
    if(!Object.keys(condition).length)console.log('CHRONICLE',JSON.stringify(timeline(world,{limit:40}),null,2));
  }
}
