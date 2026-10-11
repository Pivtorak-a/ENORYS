import { run, summary } from '../scripts/experiment-002.mjs';
const byId = id => document.getElementById(id);
const element = (tag, className = '', label = '') => {
  const el = document.createElement(tag); el.className = className; el.textContent=label; return el;
};
const labelOf = (w,id) => w.people.find(a=>a.id===id)?.name || id || 'unknown';
const details = {
  take: 'took two rations from the restricted depot',
  forage:'foraged for two rations', share:'contributed one ration to the depot',
  tell:'passed on a claim', investigate:'examined traces of a past incident',
  accuse:'made a public accusation', defend:'denied an accusation',
  repay:'returned supplies to the community',guard:'organized depot watch',wait:'waited'
};
let world, selected;
const uiData = () => ({seed: Number(byId('seed').value),days:Number(byId('days').value),
  visibility:Number(byId('visibility').value)/100,
  misidentification:Number(byId('error-rate').value)/100,
  gossip:byId('gossip').checked});
function renderEvents(){
  const host=byId('timeline');host.replaceChildren();
  const incident=byId('incident').value, truth=byId('show-truth').checked;
  const shown=world.events.filter(e=>e.kind!=='wait'&&e.kind!=='forage'&&(
    incident==='all' || e.id===incident || e.incidentId===incident)).slice(0,170);
  byId('count').textContent=`${shown.length} highlighted events · ${world.agents} NPC`;
  for(const event of shown){
    const btn=element('button','story-event'+(selected===event.id?' selected':''));btn.type='button';
    btn.append(element('span','event-date',`DAY ${event.day}\n${event.id}`));
    const block=element('div','event-copy');
    block.append(element('span','kind-pill '+event.kind,event.kind),element('strong','',labelOf(world,event.actor)),
      element('span','',details[event.kind]||event.kind));
    if(event.target)block.append(element('div','',`Target: ${labelOf(world,event.target)}`));
    if(event.to)block.append(element('div','',`Recipient: ${labelOf(world,event.to)}`));
    if(event.incidentId)block.append(element('div','',`Incident ${event.incidentId}`));
    if(truth&&event.kind==='accuse')block.append(element('div','truth'+(event.correct?' true':''),event.correct?'Objective record: accusation correct':'Objective record: mistaken accusation'));
    btn.append(block);btn.addEventListener('click',()=>{selected=event.id;renderEvents();renderInspector(event);});host.append(btn);
  }
  if(!shown.length)host.append(element('p','empty','No qualifying events in this run. Try another seed or visibility.'));
}
function detail(root,name,value){const div=element('div','detail-line');div.append(element('span','',name),element('strong','',String(value)));root.append(div);}
function renderInspector(event){
  const host=byId('inspection');host.replaceChildren();if(!event){host.append(element('p','empty','Choose an event in the timeline.'));return;}
  const decision=world.decisions.find(d=>d.eventId===event.id);
  host.append(element('h3','',`${labelOf(world,event.actor)} · ${event.kind}`));
  detail(host,'Day and event',`${event.day} · ${event.id}`);
  detail(host,'Chosen action score',decision.score.toFixed(3));
  detail(host,'Depot visible to actor',decision.perceived.depot);
  if(decision.perceived.known){const k=decision.perceived.known;
    detail(host,'Strongest private suspicion',`${labelOf(world,k.suspect)} (${(k.posterior*100).toFixed(1)}%)`);
    detail(host,'Belief originated in',`${k.roots} evidence root(s)`);
  }else detail(host,'Known evidence','None');
  const title=element('h3','', 'Competing choices'); title.className='gap-top';host.append(title);
  for(const candidate of [...decision.candidates].sort((a,b)=>b.score-a.score)){
    const box=element('div','score-block');const heading=element('div','score-key');
    heading.append(element('strong','',candidate.kind.toUpperCase()),element('span',candidate.kind===event.kind?'score-pill':'',candidate.score.toFixed(3)));
    box.append(heading);
    box.append(element('div','score-terms',Object.entries(candidate.terms).map(([k,v])=>`${k} ${v.toFixed(2)}`).join(' · ')));
    host.append(box);
  }
}
function renderComparison(){
  const alternative=run({...uiData(),gossip:!world.gossip});const a=summary(world),b=summary(alternative);
  const box=byId('alternatives');box.replaceChildren();
  box.append(element('p','hint',`Current history: gossip ${world.gossip?'on':'off'}. Other history: gossip ${alternative.gossip?'on':'off'}. Same initial seed, different later actions.`));
  for(const [key,label] of [['take','Depot incidents'],['tell','Private reports'],['accuse','Accusations'],['falseAccusations','Wrong accusations'],['repay','Restitution'],['starvation','Starvation-days']]){
    const row=element('div','alt-row');row.append(element('span','',label),element('strong','',String(a[key])),element('strong','',String(b[key])));box.append(row);
  }
}
function generate(){
  byId('errors').hidden=true;
  try{
    world=run(uiData());const s=summary(world);
    for(const [k,v] of [['takes',s.take],['accuse',s.accuse],['wrong',s.falseAccusations],
      ['repay',s.repay],['starve',s.starvation]])byId(k).textContent=String(v);
    const menu=byId('incident'), previous=menu.value;menu.replaceChildren();
    const any=element('option','','All highlighted events');any.value='all';menu.append(any);
    for(const i of world.incidents){const opt=element('option','',`${i.id} · depot incident day ${Math.floor(i.tick/world.agents)+1}`);opt.value=i.id;menu.append(opt);}
    const wrong=world.events.find(e=>e.kind==='accuse'&&!e.correct);
    const suggestion=wrong?.incidentId||world.incidents[0]?.id||'all';
    menu.value=world.incidents.some(x=>x.id===previous)?previous:suggestion;
    selected=null;renderEvents();renderInspector(null);byId('alternatives').replaceChildren();
  }catch(error){byId('errors').textContent=String(error.message);byId('errors').hidden=false;}
}
for(const id of ['visibility','error-rate'])byId(id).addEventListener('input',()=>{
  byId(id==='visibility'?'visibility-number':'error-number').textContent=byId(id).value+'%';
});
byId('controls').addEventListener('submit',ev=>{ev.preventDefault();generate();});
byId('incident').addEventListener('change',()=>{selected=null;renderEvents();renderInspector(null)});
byId('show-truth').addEventListener('change',()=>renderEvents());
byId('compare').addEventListener('click',renderComparison);
generate();
