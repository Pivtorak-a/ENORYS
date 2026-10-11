/** Domain-agnostic bounded regression planner over passed recipes and source actions.
 * No world access, inventories are simulated virtually before action proposals. */
import {RECIPES} from './domain.mjs';
export const SOURCING = Object.freeze({dirty_water:'collect_water',raw_food:'gather_food',wood:'gather_wood'});
export const SURVIVAL_GOALS = Object.freeze([
  {name:'hydrate',need:'thirst',item:'water',use:'drink',threshold:42},
  {name:'feed',need:'hunger',item:'food',use:'eat',threshold:48},
]);
export function makePlan(agent,view,goal,{recipes=RECIPES,sourcing=SOURCING,maxDepth=8}={}){
  if(!goal||!(goal.item in agent.inv))throw new TypeError('Invalid domain goal');
  const virtual={...agent.inv},steps=[];
  const reserved={};
  const ensure=(item,amount,depth,seen)=>{
    if(virtual[item]>=amount)return true;
    if(depth>maxDepth||seen.has(item))return false;
    const lacking=amount-virtual[item];const nextSeen=new Set([...seen,item]);
    const producer=Object.entries(recipes).find(([,r])=>(r.outputs[item]||0)>0);
    if(producer){const [kind,recipe]=producer;
      for(let cycle=0;cycle<Math.ceil(lacking/recipe.outputs[item]);cycle++){
        for(const [input,count] of Object.entries(recipe.inputs))if(!ensure(input,count,depth+1,nextSeen))return false;
        if(steps.length>=maxDepth)return false;
        steps.push({kind});
        for(const [input,count] of Object.entries(recipe.inputs))virtual[input]-=count;
        for(const [output,count] of Object.entries(recipe.outputs))virtual[output]+=count;
      }
      return true;
    }
    const source=sourcing[item];
    if(!source||view.sourcesKnown[item]!==true)return false;
    if(steps.length+lacking>=maxDepth)return false;
    for(let i=0;i<lacking;i++){steps.push({kind:source});virtual[item]++;}
    return true;
  };
  if(!ensure(goal.item,1,0,new Set()))return null;
  steps.push({kind:goal.use});
  if(steps.length>maxDepth)return null;
  // Reserve available materials required by forthcoming crafting; never claim depot items.
  for(const step of steps){const recipe=recipes[step.kind];if(!recipe)continue;
    for(const [item,count] of Object.entries(recipe.inputs))
      reserved[item]=Math.min(agent.inv[item],Math.max(reserved[item]||0,count));
  }
  return {goal:goal.name,created:view.tick,steps,reserved,index:0,replans:0};
}
export function validNextStep(agent,view,plan){
  if(!plan||plan.index>=plan.steps.length)return false;
  const step=plan.steps[plan.index],recipe=RECIPES[step.kind];
  if(recipe)return Object.entries(recipe.inputs).every(([k,n])=>agent.inv[k]>=n);
  if(step.kind in {gather_food:1,collect_water:1,gather_wood:1}){
    const source=Object.entries(SOURCING).find(([,s])=>s===step.kind)?.[0];
    return !!view.sourcesKnown[source];
  }
  if(step.kind==='drink')return agent.inv.water>0;
  if(step.kind==='eat')return agent.inv.food>0;
  return true;
}
export function planStep(agent,view){
  const plan=agent.plan;if(!plan||plan.index>=plan.steps.length)return null;
  return validNextStep(agent,view,plan)?{...plan.steps[plan.index],planGoal:plan.goal}:null;
}
export function availableUnreserved(agent,item){
  if(!agent.plan)return agent.inv[item];
  // Reservation is dynamic: supplies obtained AFTER planning remain reserved for pending recipes.
  const pending=agent.plan.steps.slice(agent.plan.index);
  let demand=0;
  for(const step of pending){
    const recipe=RECIPES[step.kind];if(recipe)demand+=recipe.inputs[item]||0;
    if(step.kind==='drink'&&item==='water'||step.kind==='eat'&&item==='food')demand++;
  }
  const held=Math.min(agent.inv[item],demand);
  return Math.max(0,agent.inv[item]-held);
}
