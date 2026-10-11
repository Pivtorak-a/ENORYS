import { createWorld, run, step } from '../src/engine.js';

const $ = id => document.getElementById(id);
let world = null;
const actionText = { wait: 'waited', take: 'took 1 supply from the common cache', donate: 'donated 1 supply', tell: 'shared a report' };
const num = n => Number(n).toFixed(2);

function el(tag, className = '', text = '') {
  const node = document.createElement(tag);
  node.className = className;
  node.textContent = text;
  return node;
}
function line(parent, label, value) {
  const item = el('div','info-row'); item.append(el('span','',label),el('strong','',String(value))); parent.append(item);
}
function config() {
  const seed = Number($('seed').value), agents = Number($('agents').value);
  const witnessRate = Number($('witness').value) / 100;
  return { seed, agents, witnessRate };
}
function render() {
  $('ticks').textContent = world.tick;
  $('cache').textContent = world.cache;
  $('takes').textContent = world.stats.takes;
  $('donations').textContent = world.stats.donations;
  $('reports').textContent = world.stats.reports;
  const events = $('events'); events.replaceChildren();
  for (const event of world.log.slice(-16).reverse()) {
    const row = el('div','event-row');
    row.append(el('span','tick',String(event.tick).padStart(3,'0')), el('span','tag '+event.kind,event.kind.toUpperCase()));
    const description = `${event.actor} ${actionText[event.kind]}${event.recipient ? ` to ${event.recipient} about ${event.claim.subject}` : ''}`;
    row.append(el('span','',description)); events.append(row);
  }
  if (!world.tick) events.append(el('p','empty','No events yet. Run the simulation to see what agents do.'));
  const inspector = $('decision'); inspector.replaceChildren();
  const last = world.decisions.at(-1);
  if (last) {
    inspector.append(el('h3','',`${last.actor} chose ${last.action.toUpperCase()}`));
    line(inspector, 'Decision score', num(last.score));
    line(inspector, 'Nearby agents', last.visible.neighbors.length);
    line(inspector, 'Visible cache', last.visible.cache);
    inspector.append(el('div','mini-title','Candidate utilities'));
    for (const c of [...last.candidates].sort((a,b) => b.score - a.score)) {
      const row = el('div','score');
      const cap = el('div','score-name',`${c.action.toUpperCase()} · ${num(c.score)}`);
      const bar = el('div','score-track'); const fill = el('div','score-fill'+(c.action===last.action?' chosen':'') );
      fill.style.width = `${Math.max(0,Math.min(100,c.score / Math.max(1,last.score) * 100))}%`;
      bar.append(fill); row.append(cap,bar);
      const terms = Object.entries(c.terms).map(([k,v]) => `${k}: ${num(v)}`).join(' · ');
      row.append(el('div','terms',terms)); inspector.append(row);
    }
  } else inspector.append(el('p','empty','A selected action and its utility terms will appear here.'));
  const select = $('selected-agent');
  if (select.options.length !== world.people.length) {
    const previous = select.value;
    select.replaceChildren(...world.people.map(p => { const option=el('option','',p.id); option.value=p.id; return option; }));
    if(world.people.some(p=>p.id===previous)) select.value=previous;
  }
  renderAgent();
}
function renderAgent() {
  const agent = world.people.find(p => p.id === $('selected-agent').value) || world.people[0];
  if (!agent) return;
  const section = $('agent-detail'); section.replaceChildren();
  line(section, 'Supplies', agent.supplies);
  line(section, 'Location', `Zone ${agent.place + 1}`);
  for (const [k,v] of Object.entries(agent.traits)) line(section, k, num(v));
  section.append(el('div','mini-title',`Memories (${agent.memories.length}, latest 6)`));
  for (const m of agent.memories.slice(-6).reverse()) {
    section.append(el('div','memory',`${m.subject} · ${m.kind} · ${m.source} (${Math.round(m.confidence*100)}%) · ${m.rootId}`));
  }
  if (!agent.memories.length) section.append(el('p','empty','No observed or reported events.'));
  section.append(el('div','mini-title','Relationships'));
  const relations = Object.entries(agent.relationships).sort((a,b) => a[1]-b[1]).slice(0,6);
  if(!relations.length) section.append(el('p','empty','No relationships updated yet.'));
  for (const [id,value] of relations) line(section,id,num(value));
}
function reset() {
  try { world = createWorld(config()); $('comparison').replaceChildren(); render(); }
  catch(error) { window.alert(error.message); }
}
$('witness').addEventListener('input', () => { $('witness-label').textContent = `${$('witness').value}%`; });
$('reset').addEventListener('click', reset);
$('step').addEventListener('click', () => { step(world); render(); });
$('run').addEventListener('click', () => { for(let i=0;i<100;i++) step(world); render(); });
$('selected-agent').addEventListener('change',renderAgent);
$('compare').addEventListener('click', () => {
  try {
    const opts = config(), a = run({ ...opts, witnessRate: 0 }, 300), b = run({ ...opts, witnessRate: 1 }, 300);
    const section = $('comparison'); section.replaceChildren();
    for (const [label,w] of [['0% witnesses',a],['100% witnesses',b]]) {
      const card = el('div','compare-card');card.append(el('h3','',label));
      line(card,'Takes',w.stats.takes); line(card,'Donations',w.stats.donations);
      line(card,'Reports',w.stats.reports); line(card,'Observed takes',w.stats.witnessedTakes);
      card.append(el('p','small','Same seed, initialization & number of decisions.'));section.append(card);
    }
  } catch(error) { window.alert(error.message); }
});
reset();
