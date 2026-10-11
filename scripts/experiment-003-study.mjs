/** Batch sensitivity and model-integrity report for Experiment 003. */
import { run, summary } from './experiment-003.mjs';
const configs={baseline:{},scarce:{resources:.35},freezing:{weatherSeverity:1},isolated:{visibility:0,gossip:false}};
const trials=80, samples={};
for(const [name,overrides] of Object.entries(configs)){
  samples[name]=[];
  for(let seed=1;seed<=trials;seed++){
    const w=run({seed,agents:24,days:12,...overrides}),s=summary(w);
    if(!s.balanced)throw Error('Conservation failure: '+name+'/'+seed);
    samples[name].push(s);
  }
}
const fields=['alive','dead','meanHealth','uniqueActions','give','ask','take','accurateAccusations','falseAccusations',
  'eat','drink','sleep','wash','treat','bandage','build_shelter','build_latrine','relieve','scavenge','boil','cook','socialize'];
const mean=xs=>xs.reduce((a,b)=>a+b,0)/xs.length;
const statistics={};
for(const [name,s] of Object.entries(samples)){
  statistics[name]={};for(const f of fields)statistics[name][f]=Number(mean(s.map(x=>x[f])).toFixed(3));
  statistics[name].meanNeeds=Object.fromEntries(Object.keys(s[0].meanNeeds).map(k=>[k,Number(mean(s.map(x=>x.meanNeeds[k])).toFixed(3))]));
  statistics[name].balanced=s.every(x=>x.balanced);
}
const paired={};
for(const name of Object.keys(configs).filter(x=>x!=='baseline')){
  paired[name]={};for(const f of ['meanHealth','alive','give','falseAccusations','meanNeeds.thirst','meanNeeds.cold']){
    const get=s=>f.includes('.')?s.meanNeeds[f.split('.')[1]]:s[f];
    const diffs=samples[name].map((s,i)=>get(s)-get(samples.baseline[i]));
    const m=mean(diffs), variance=diffs.reduce((a,x)=>a+(x-m)**2,0)/(diffs.length-1),se=Math.sqrt(variance/diffs.length);
    paired[name][f]={delta:Number(m.toFixed(3)),ci95:[Number((m-1.96*se).toFixed(3)),Number((m+1.96*se).toFixed(3))]};
  }
}
const single={};for(const [name,overrides] of Object.entries(configs)){
  const s=summary(run({seed:42,days:12,...overrides}));single[name]={...Object.fromEntries(fields.map(k=>[k,s[k]])),
    meanNeeds:s.meanNeeds,balanced:s.balanced,turns:s.turns};
}
console.log(JSON.stringify({trials,actors:24,days:12,turnsPerDay:3,decisionsPerRun:864,single,statistics,paired},null,2));
