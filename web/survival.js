import { run, summary, ITEMS, NEEDS } from '../scripts/experiment-003.mjs';
const $ = id=>document.getElementById(id);
const node=(tag,cl='',value='')=>{const x=document.createElement(tag);x.className=cl;x.textContent=value;return x;};
const fmt=v=>Number(v).toFixed(1);
const needNames={hunger:'Голод',thirst:'Спрага',fatigue:'Втома',cold:'Холод',hygiene:'Гігієна',bladder:'Туалет',
  safety:'Небезпека',belonging:'Самотність',morale:'Поганий настрій'};
const itemNames={food:'Готова їжа',water:'Чиста вода',raw_food:'Сира їжа',dirty_water:'Нечиста вода',
  wood:'Дрова',medicine:'Ліки',bandage:'Бинти',soap:'Мило',blanket:'Ковдри',coat:'Куртки',
  tool:'Інструменти',bed:'Ліжка',shelter:'Укриття',latrine:'Вбиральня'};
const storyKinds=new Set(['take','accuse','defend','repay','tell','investigate','guard','give','ask','build_shelter','treat','bandage','drink_dirty','eat_raw']);
const survivalKinds=new Set(['eat','drink','eat_raw','drink_dirty','sleep','relieve','warm','wash','treat','bandage','gather_food',
  'collect_water','gather_wood','scavenge','cook','boil','build_shelter','build_latrine']);
const socialKinds=new Set(['socialize','give','ask','take','tell','investigate','accuse','defend','repay','guard']);
let world=null,currentEvent=null;
function options(){return {seed:Number($('seed').value),days:Number($('days').value),resources:Number($('resources').value)/100,
  weatherSeverity:Number($('weather').value)/100,visibility:Number($('visibility').value)/100,gossip:$('gossip').checked};}
function row(host,key,val){const e=node('div','s-row');e.append(node('span','',key),node('strong','',String(val)));host.append(e);}
function renderNeeds(){const host=$('needs');host.replaceChildren();
  const s=summary(world);
  for(const k of NEEDS){const line=node('div','need-line'),upper=node('div','s-row');
    upper.append(node('span','',needNames[k]),node('strong','',`${fmt(s.meanNeeds[k])} / 100`));line.append(upper);
    const bar=node('div','need-bar'),fill=node('div','need-fill'+(s.meanNeeds[k]>70?' danger':''));
    fill.style.width=s.meanNeeds[k]+'%';bar.append(fill);line.append(bar);host.append(line);}
}
function renderAgent(){const agent=world.people.find(p=>p.id===$('agent').value)||world.people[0];if(!agent)return;
  const stats=$('agent-state');stats.replaceChildren();
  row(stats,'Стан',agent.alive?'Живий':'Загинув');row(stats,'Здоров’я',fmt(agent.health));
  row(stats,'Інфекція',fmt(agent.infection));row(stats,'Рана',fmt(agent.wound));
  row(stats,'Відомих інцидентів',Object.keys(agent.beliefs).length);
  row(stats,'Довіра до інших (змінені зв’язки)',Object.keys(agent.trust).length);
  const inv=$('inventory');inv.replaceChildren();
  for(const [item,qty] of Object.entries(agent.inv)){const e=node('div','item'+(qty===0?' absent':''));
    e.append(node('span','',itemNames[item]),node('strong','',String(qty)));inv.append(e);}
}
function renderEvents(){const host=$('chronicle'),filter=$('filter').value;host.replaceChildren();
  const source=world.events.filter(e=>filter==='all'||(filter==='story'&&storyKinds.has(e.kind))||
    (filter==='survival'&&survivalKinds.has(e.kind))||(filter==='social'&&socialKinds.has(e.kind)));
  for(const event of source.slice(0,260)){
    const act=world.people.find(p=>p.id===event.actor);
    const button=node('button','chronicle-event'+(currentEvent===event.id?' active':''));button.type='button';
    const info=node('span','chronicle-content');info.append(node('strong','',act?.name||event.actor),
      node('small','',event.details));
    button.append(node('span','day-label',`ДЕНЬ ${event.day}\n${event.id}`),info);
    button.addEventListener('click',()=>{currentEvent=event.id;renderEvents();renderInspector(event);});host.append(button);
  }
  if(!source.length)host.append(node('p','empty','У цьому прогоні немає відповідних подій.'));
}
function renderInspector(event){const host=$('inspector');host.replaceChildren();
  if(!event){host.append(node('p','empty','Вибери подію з хронології.'));return;}
  const d=world.decisions.find(x=>x.eventId===event.id);if(!d)return;
  host.append(node('h3','',`${world.people.find(p=>p.id===d.actor)?.name}: ${d.kind}`));
  row(host,'Вибрана корисність',fmt(d.score));row(host,'Погода',d.visible.weather);
  row(host,'Охорона складу',d.visible.guarded?'Так':'Ні');
  if(d.visible.belief)row(host,'Головна суб’єктивна підозра',`${d.visible.belief.suspect} · ${Math.round(d.visible.belief.posterior*100)}%`);
  host.append(node('h3','',`Альтернативи · ${d.candidates.length}`));
  for(const item of [...d.candidates].sort((a,b)=>b.score-a.score).slice(0,11)){
    const e=node('div','candidate');row(e,item.kind,fmt(item.score));
    e.append(node('small','',Object.entries(item.terms).map(([k,v])=>`${k} ${fmt(v)}`).join(' · ')));host.append(e);
  }
  host.append(node('p','hint','Рішення базувалося на стані до дії. Стан NPC праворуч показує фінал усієї симуляції.'));
}
function renderComparison(){const base=summary(world),alt=summary(run({...options(),resources:Math.max(.1,options().resources*.5)}));
  const box=$('comparison');box.replaceChildren();
  const columns=node('div','comparison-grid');columns.append(node('span','','Показник'),node('span','','Поточний'),node('span','','Дефіцит'));
  for(const [label,a,b] of [['Живих',base.alive,alt.alive],['Сер. здоров’я',fmt(base.meanHealth),fmt(alt.meanHealth)],
    ['Спрага',fmt(base.meanNeeds.thirst),fmt(alt.meanNeeds.thirst)],['Голод',fmt(base.meanNeeds.hunger),fmt(alt.meanNeeds.hunger)],
    ['Передано предметів',base.give,alt.give],['Звинувачень',base.accuse,alt.accuse],
    ['Перевірка кількостей',base.balanced?'OK':'ERROR',alt.balanced?'OK':'ERROR']]){
      columns.append(node('span','',label),node('strong','',String(a)),node('strong','',String(b)));}
  box.append(columns);
}
function render(){const data=summary(world);
  for(const [key,val] of [['alive',data.alive+'/'+data.agents],['health',fmt(data.meanHealth)],
    ['actions',data.uniqueActions],['gifts',data.give],['wrong',data.falseAccusations],
    ['balance',data.balanced?'OK':'ERROR']])$(key).textContent=String(val);
  renderNeeds();const chosen=$('agent').value;$('agent').replaceChildren(...world.people.map(p=>{
    const o=node('option','',p.name+' ('+p.id+')');o.value=p.id;return o;}));
  if(world.people.some(p=>p.id===chosen))$('agent').value=chosen;
  renderAgent();currentEvent=null;renderEvents();renderInspector(null);$('comparison').replaceChildren();
}
$('controls').addEventListener('submit',e=>{e.preventDefault();$('error').hidden=true;
  try{world=run(options());render();}catch(error){$('error').textContent=String(error.message);$('error').hidden=false;}
});
for(const [id,value] of [['resources','resources-value'],['weather','weather-value'],['visibility','visibility-value']])
  $(id).addEventListener('input',()=>$(value).textContent=$(id).value+'%');
$('filter').addEventListener('change',renderEvents);$('agent').addEventListener('change',renderAgent);
$('compare').addEventListener('click',()=>{try{renderComparison()}catch(e){$('error').hidden=false;$('error').textContent=String(e.message)}});
world=run(options());render();
