/** Cognitive level of detail: exact lower-bound gate for the cached planned action.
 * A deliberate call remains mandatory whenever a competitor could outrank it.
 * The gate duplicates only the cheap scalar bounds, not full candidate allocation.
 */
import {planStep} from './planner.mjs';
export function chooseMode(agent,view,{cognitiveLOD=true,socialEnabled=true}={}){
  const fallback=(reason)=>({mode:'deliberate',reason});
  if(!cognitiveLOD||!agent.alive)return fallback('forced or inactive');
  if(!agent.plan)return fallback('no active plan');
  if(agent.lastEventTick===view.tick)return fallback('new observation');
  if(agent.infection>45||agent.injury>50||agent.health<45)return fallback('urgent health');
  const step=planStep(agent,view);if(!step)return fallback('invalid prerequisites');
  const n=agent.needs,t=agent.traits,I=agent.inv,u=k=>n[k]/100;
  const urgency=u(agent.plan.goal==='hydrate'?'thirst':'hunger');
  const planned=1.55+1.85*urgency;
  if((agent.plan.goal==='hydrate'&&n.hunger>80)||
     (agent.plan.goal==='feed'&&n.thirst>80))return fallback('competing critical goal');
  // A conservative upper-bound test: if any possible alternative may win,
  // execute the full policy, including social considerations and all explanations.
  const competes=(kind,v)=>kind!==step.kind&&v>=planned-1e-12;
  const alternatives=[['sleep',.55+2.8*u('fatigue')],['relieve',.2+3.4*u('bladder')],
    ['socialize',.3+1.3*u('belonging')+.7*u('morale')]];
  if(I.water>0)alternatives.push(['drink',.35+4*u('thirst')]);
  if(I.food>0)alternatives.push(['eat',.35+3.75*u('hunger')]);
  if(I.wood>0)alternatives.push(['warm',.4+2.6*u('cold')]);
  if(I.soap>0&&I.water>0)alternatives.push(['wash',.1+2.5*u('hygiene')]);
  if(I.medicine>0&&agent.infection>0)alternatives.push(['treat',2+agent.infection/25]);
  if(I.bandage>0&&agent.injury>0)alternatives.push(['bandage',2+agent.injury/25]);
  if(I.dirty_water>0&&I.wood>0)alternatives.push(['boil',1.4+1.6*u('thirst')]);
  if(I.raw_food>0)alternatives.push(['cook',1.2+1.4*u('hunger')]);
  if(view.sourcesKnown.dirty_water&&I.water+I.dirty_water===0)
    alternatives.push(['collect_water',1+2*u('thirst')]);
  if(view.sourcesKnown.raw_food&&I.food+I.raw_food===0)
    alternatives.push(['gather_food',1+1.8*u('hunger')]);
  if(view.sourcesKnown.wood&&I.wood===0)
    alternatives.push(['gather_wood',.55+1.1*u('cold')+.4*Number(I.dirty_water>0)]);
  if(I.wood>=3&&!I.shelter)
    alternatives.push(['build_shelter',.3+1.6*u('cold')+1.2*u('safety')]);
  if(I.wood>=2&&!view.depot.latrine)
    alternatives.push(['build_latrine',.1+1.3*u('hygiene')+.8*u('bladder')]);
  if(view.depot.water>0&&u('thirst')>.48)
    alternatives.push(['take',1.8+2.3*u('thirst')+.45*t.greed-.8*t.norm]);
  if(view.depot.food>0&&u('hunger')>.53)
    alternatives.push(['take',1.6+2.2*u('hunger')+.45*t.greed-.75*t.norm]);
  if(alternatives.some(([kind,score])=>competes(kind,score)))return fallback('potentially superior routine action');
  if(socialEnabled){
    if(agent.pendingRetractions?.length)return fallback('pending correction');
    if([...agent.evidence.values()].some(e=>e.active))return fallback('possible social evidence');
    if(agent.requests.some(x=>I[x.item]>0))return fallback('aid request');
    if(Object.entries(agent.debts).some(([key,d])=>d>0&&I[key.split(':')[1]]>0))return fallback('debt');
    if((I.water===0&&u('thirst')>.56 || I.food===0&&u('hunger')>.6)&&view.nearby.length)
      return fallback('possible request for help');
  }
  return {mode:'automatic',reason:'cached plan wins conservative utility gate',step};
}
export function automaticDecision(agent,view,mode){
  if(mode.mode!=='automatic'||!mode.step)throw new RangeError('Invalid automatic plan');
  return {proposal:{actor:agent.id,...mode.step},candidates:[],mode:'automatic',reason:mode.reason};
}
