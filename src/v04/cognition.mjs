/** Agent cognition. This API takes only own state and a filtered observation packet. */
import {SURVIVAL_GOALS,makePlan,planStep,availableUnreserved} from './planner.mjs';
import {strongestSuspicion} from './memory.mjs';
const score=(kind,value,reason,extra={})=>({kind,score:value,reason,...extra});
export function deliberate(agent,view,{plannerEnabled=true,socialEnabled=true}={}){
  if(!agent.alive)return {proposal:{actor:agent.id,kind:'wait'},candidates:[],mode:'automatic',reason:'dead'};
  const n=agent.needs,I=agent.inv,t=agent.traits, u=k=>n[k]/100;
  const candidates=[score('wait',.01,'baseline'),
    score('sleep',.55+2.8*u('fatigue'),'rest'),
    score('relieve',.2+3.4*u('bladder'),'sanitation'),
    score('socialize',.3+1.3*u('belonging')+.7*u('morale'),'affiliation')];
  if(I.water>0)candidates.push(score('drink',.35+4*u('thirst'),'hydration'));
  if(I.food>0)candidates.push(score('eat',.35+3.75*u('hunger'),'nutrition'));
  if(I.wood>0)candidates.push(score('warm',.4+2.6*u('cold'),'temperature'));
  if(I.soap>0&&I.water>0)candidates.push(score('wash',.1+2.5*u('hygiene'),'sanitation'));
  if(I.medicine>0&&agent.infection>0)candidates.push(score('treat',2+agent.infection/25,'infection'));
  if(I.bandage>0&&agent.injury>0)candidates.push(score('bandage',2+agent.injury/25,'wound'));
  if(I.dirty_water>0&&I.wood>0)candidates.push(score('boil',1.4+1.6*u('thirst'),'prepare water'));
  if(I.raw_food>0)candidates.push(score('cook',1.2+1.4*u('hunger'),'prepare food'));
  if(view.sourcesKnown.dirty_water&&I.water+I.dirty_water===0)candidates.push(score('collect_water',1.0+2*u('thirst'),'collect source'));
  if(view.sourcesKnown.raw_food&&I.food+I.raw_food===0)candidates.push(score('gather_food',1+1.8*u('hunger'),'collect source'));
  if(view.sourcesKnown.wood&&I.wood===0)candidates.push(score('gather_wood',.55+1.1*u('cold')+.4*Number(I.dirty_water>0),'collect fuel'));
  if(I.wood>=3&&!I.shelter)candidates.push(score('build_shelter',.3+1.6*u('cold')+1.2*u('safety'),'shelter'));
  if(I.wood>=2&&!view.depot.latrine)candidates.push(score('build_latrine',.1+1.3*u('hygiene')+.8*u('bladder'),'sanitation infrastructure'));
  if(view.depot.water>0&&u('thirst')>.48)candidates.push(score('take',1.8+2.3*u('thirst')+.45*t.greed-.8*t.norm,'common supplies',{item:'water'}));
  if(view.depot.food>0&&u('hunger')>.53)candidates.push(score('take',1.6+2.2*u('hunger')+.45*t.greed-.75*t.norm,'common supplies',{item:'food'}));
  if(plannerEnabled){
    if(!agent.plan){
      for(const goal of SURVIVAL_GOALS){
        if(agent.needs[goal.need]<goal.threshold||I[goal.item]>0)continue;
        const p=makePlan(agent,view,goal);
        if(p){agent.plan=p;break;}
      }
    }
    const next=planStep(agent,view);
    if(next){const urge=agent.plan.goal==='hydrate'?u('thirst'):u('hunger');
      candidates.push(score(next.kind,1.55+1.85*urge,'planned prerequisite', {planGoal:next.planGoal}));}
  }
  if(socialEnabled){
    const help=view.requests.find(m=>m.type==='request'&&availableUnreserved(agent,m.item)>0&&
      view.nearby.some(p=>p.id===m.from)&&
      ((m.item==='water'?u('thirst'):m.item==='food'?u('hunger'):0)<.55||I[m.item]>1));
    if(help)candidates.push(score('give',2.25+1.8*t.generosity,'answer public request',{target:help.from,item:help.item}));
    const debt=Object.entries(agent.debts).find(([key,amount])=>amount>0&&
      I[key.split(':')[1]]>0 && view.nearby.some(p=>p.id===key.split(':')[0]));
    if(debt){const [key]=debt,[target,item]=key.split(':');
      candidates.push(score('repay',2.1+1.4*t.norm,'repay acknowledged debt',{target,item}));}
    const needItem=I.water===0&&u('thirst')>.56?'water':I.food===0&&u('hunger')>.6?'food':null;
    if(needItem&&view.nearby.length&& !agent.acted.has(`ask:${needItem}:${view.day}`))
      candidates.push(score('ask',1.8+1.6*u(needItem==='water'?'thirst':'hunger')+.5*t.sociability,
        'seek assistance',{target:view.nearby[0].id,item:needItem}));
    const suspicion=strongestSuspicion(agent);
    if(suspicion && suspicion.posterior>.4){
      if(!agent.acted.has('investigate:'+suspicion.incident))
        candidates.push(score('investigate',1.15+1.2*suspicion.posterior+.5*t.curiosity,
          'uncertain suspected event',{incident:suspicion.incident}));
      if(suspicion.posterior>.56&&!agent.acted.has(`accuse:${suspicion.incident}:${suspicion.suspect}`))
        candidates.push(score('accuse',.85+1.6*suspicion.posterior+.35*t.norm,
          'suspect and social norm',{incident:suspicion.incident,target:suspicion.suspect}));
      const root=suspicion.root;
      const recipient=view.nearby.find(x=>x.id!==suspicion.suspect&&!agent.reported.has(root+':'+x.id));
      if(recipient&&view.tick>1)candidates.push(score('tell',.8+.9*t.sociability+.5*suspicion.posterior,
        'propagate claim',{incident:suspicion.incident,root,suspect:suspicion.suspect,
          lr:suspicion.lr,target:recipient.id}));
    }
    const correction=agent.pendingRetractions?.find(x=>view.nearby.some(p=>p.id===x.to)&&
      !agent.reported.has(`retract:${x.root}:${x.to}`));
    if(correction)candidates.push(score('retract',3.2,'correct previous testimony',
      {root:correction.root,target:correction.to}));
  }
  const chosen=candidates.reduce((a,b)=>b.score>a.score?b:a);
  return {proposal:{actor:agent.id,kind:chosen.kind,...Object.fromEntries(
    Object.entries(chosen).filter(([k])=>!['kind','score','reason'].includes(k)))},
    candidates,mode:'deliberate',reason:chosen.reason};
}
