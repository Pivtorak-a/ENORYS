import {run,summary} from '../src/v04/runtime.mjs';
const $=id=>document.getElementById(id);
const make=(tag,cls='',value='')=>{const el=document.createElement(tag);el.className=cls;el.textContent=String(value);return el;};
const num=(x,p=1)=>Number(x).toFixed(p);
let world=null,lastSettings=null;
const actionDescription={take:'took supplies without consent from the shared depot',ask:'asked a neighbor for supplies',give:'gave a requested item',repay:'returned a borrowed resource',tell:'shared a suspicious report',accuse:'accused another character',investigate:'investigated a reported incident',retract:'retracted an earlier claim',boil:'purified water',cook:'prepared food',build_shelter:'built shelter',build_latrine:'built a latrine',treat:'treated an infection',bandage:'dressed a wound',guard:'organized community watch',socialize:'spent time socializing'};
function settings(){return {seed:Number($('seed').value),agents:Number($('agents').value),days:Number($('days').value),
 resources:Number($('resources').value)/100,visibility:Number($('visibility').value)/100,
 misidentification:.12,plannerEnabled:$('planning').checked,cognitiveLOD:$('lod').checked,
 socialEnabled:$('social').checked};}
const row=(name,value)=>{const div=make('div','info-row');div.append(make('span','',name),make('strong','',value));return div;};
function renderAgent(){if(!world)return;const a=world.people.find(p=>p.id===$('agent-picker').value)||world.people[0];const section=$('agent-inspector');section.replaceChildren();
 const left=make('div','person-attributes');const right=make('div','person-attributes');
 left.append(make('h3','','Body, supplies and plan'),row('Health',num(a.health)),row('Alive',a.alive?'Yes':'No'),row('Zone',a.zone),row('Social group',a.group),row('Current plan',a.plan?.goal||'none'));
 for(const [k,v] of Object.entries(a.inv).filter(([,n])=>n>0))left.append(row(k,v));
 right.append(make('h3','','Private knowledge and relationships'),row('Independent evidence roots',a.evidence.size));
 for(const e of [...a.evidence.values()].slice(-10))right.append(make('p','',`${e.active?'•':'×'} ${e.incident}: ${e.suspect} — ${e.source}, root ${e.root}`));
 for(const [k,v] of Object.entries(a.trust))right.append(row('Trust · '+k,num(v)));
 for(const [k,v] of Object.entries(a.debts).filter(([,v])=>v>0))right.append(row('Owes · '+k,v));
 if(!a.evidence.size&&!Object.keys(a.trust).length)right.append(make('p','empty','No delivered reports or social transactions.'));
 section.append(left,right);
}
function render(){const s=summary(world),a=s.actions;
 $('alive').textContent=`${s.alive}/${s.agents}`;$('health').textContent=num(s.health);
 $('plans').textContent=s.stats.plansCompleted;$('automatic').textContent=s.stats.auto;
 $('actions').textContent=world.events.length;
 const needs=$('needs-graph');needs.replaceChildren();
 for(const [k,v] of Object.entries(s.meanNeeds)){
  const bar=make('div','need-line');const track=make('div','need-track'), fill=make('div','need-fill'+(v>70?' high':''));
  fill.style.width=Math.min(100,v)+'%';track.append(fill);bar.append(make('span','',k),track,make('span','',num(v)));needs.append(bar);
 }
 const social=$('social-stats');social.replaceChildren();
 for(const [k,v] of Object.entries({Taking:a.take||0,Requests:a.ask||0,Gifts:a.give||0,
  Repayments:a.repay||0,Investigations:a.investigate||0,Accusations:a.accuse||0,
  'False accusations':s.stats.falseAccusations,Reports:a.tell||0,Retractions:a.retract||0}))social.append(row(k,v));
 const feed=$('chronicle');feed.replaceChildren();
 const important=world.events.filter(e=>actionDescription[e.kind]).slice(0,90);
 for(const e of important){const entry=make('div','chronicle-entry');
  entry.append(make('strong','',`Day ${e.day} · ${e.actor}`),make('div','',actionDescription[e.kind]),
    make('div','sub',[e.item,e.target,e.incident].filter(Boolean).join(' · ')));feed.append(entry);
 }
 if(!important.length)feed.append(make('p','empty','No noteworthy social events were observed.'));
 const selector=$('agent-picker'),selected=selector.value;
 selector.replaceChildren(...world.people.map(a=>{const el=make('option','',a.id);el.value=a.id;return el;}));
 selector.value=world.people.some(a=>a.id===selected)?selected:world.people[0].id;
 renderAgent();$('run-status').textContent=`Completed ${world.tick.toLocaleString()} decisions. Conservation ledger: ${s.balanced?'PASS':'FAIL'}. Rejected actions: ${s.stats.rejected}.`;
}
function start(){try{const config=settings();lastSettings=config;$('run-status').textContent='Running…';
 world=run(config);$('comparison').replaceChildren();render();}
 catch(error){$('run-status').textContent=`Error: ${error.message}`;}}
$('resources').addEventListener('input',()=>{$('resources-label').textContent=$('resources').value+'%';});
$('visibility').addEventListener('input',()=>{$('visibility-label').textContent=$('visibility').value+'%';});
$('agent-picker').addEventListener('change',renderAgent);
$('start').addEventListener('click',start);
$('compare').addEventListener('click',()=>{try{if(!world)start();const original=summary(world);
 const alternate=summary(run({...lastSettings,plannerEnabled:false,cognitiveLOD:false}));
 const c=$('comparison');c.replaceChildren();c.append(make('h3','','Same seed, different planning policy'),
   make('div','compare-row',`With planning: mean health ${num(original.health)}; thirst ${num(original.meanNeeds.thirst)}; completed plans ${original.stats.plansCompleted}`),
   make('div','compare-row',`Reactive: mean health ${num(alternate.health)}; thirst ${num(alternate.meanNeeds.thirst)}; completed plans ${alternate.stats.plansCompleted}`));}
 catch(error){$('run-status').textContent=error.message;}});
start();
