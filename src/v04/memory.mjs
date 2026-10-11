/** Private episodic evidence, anti-echo provenance, trust and social debts. */
const clamp=(v,a=-100,b=100)=>Math.max(a,Math.min(b,v));
export function ingestEvidence(agent,message,tick){
  if(!message.root||!message.incident||!message.suspect||!Number.isFinite(message.lr))return false;
  const existing=agent.evidence.get(message.root);
  if(existing){ // Forwarded copies of same original observation are not independent evidence.
    return false;
  }
  const trust=agent.trust[message.from]??0;
  const reliability=message.source==='eyewitness'?1:
    message.source==='trace'?.85:clamp(.58+trust/250,.2,.92);
  agent.evidence.set(message.root,{root:message.root,incident:message.incident,
    suspect:message.suspect,lr:message.lr*reliability,source:message.source,
    from:message.from,tick,active:true});
  return true;
}
export function retractEvidence(agent,root,source){
  const e=agent.evidence.get(root);
  if(!e||e.from!==source||!e.active)return false;
  e.active=false;agent.trust[source]=clamp((agent.trust[source]??0)-4);
  return true;
}
export function strongestSuspicion(agent,tick=0){
  const evidence=[...agent.evidence.values()].filter(e=>e.active);
  const grouped=new Map();
  for(const e of evidence){if(e.suspect===agent.id)continue;
    const key=e.incident+':'+e.suspect;
    const a=grouped.get(key)||[];a.push(e);grouped.set(key,a);
  }
  const choices=[];
  for(const [key,records] of grouped){
    records.sort((a,b)=>b.lr-a.lr||a.root.localeCompare(b.root));
    let logit=Math.log(.12/.88);
    for(let i=0;i<records.length;i++){
      const age=Math.max(0,tick-records[i].tick);
      logit+=records[i].lr*Math.exp(-age/800)*(i===0?1:.25);
    }
    const posterior=1/(1+Math.exp(-logit));
    choices.push({incident:records[0].incident,suspect:records[0].suspect,
      posterior,root:records[0].root,lr:records[0].lr,independentRoots:records.length});
  }
  return choices.sort((a,b)=>b.posterior-a.posterior||a.incident.localeCompare(b.incident))[0]||null;
}
export function processInbox(agent,tick){
  if(!agent.alive){agent.inbox=[];return 0;}
  const packets=agent.inbox.splice(0);let count=0;
  for(const packet of packets){
    if(packet.type==='evidence'){
      const accepted=ingestEvidence(agent,packet,tick);
      if(accepted){
        // If new direct evidence contradicts a forwarded claim, schedule a correction.
        if(packet.source==='trace'||packet.source==='eyewitness'){
          for(const sent of (agent.sentReports||[])){
            const old=agent.evidence.get(sent.root);
            if(old&&old.incident===packet.incident&&old.suspect!==packet.suspect){
              agent.pendingRetractions??=[];
              if(!agent.pendingRetractions.some(x=>x.root===sent.root&&x.to===sent.to))
                agent.pendingRetractions.push({root:sent.root,to:sent.to});
            }
          }
        }
        count++;
      }
    }else if(packet.type==='retract'){if(retractEvidence(agent,packet.root,packet.from))count++;}
    else if(packet.type==='gift'){
      const key=packet.from+':'+packet.item;
      agent.debts[key]=(agent.debts[key]||0)+1;
      agent.trust[packet.from]=clamp((agent.trust[packet.from]??0)+8);
      count++;
    }else if(packet.type==='repayment'){
      agent.trust[packet.from]=clamp((agent.trust[packet.from]??0)+4);count++;
    }else if(packet.type==='accused'){
      agent.trust[packet.from]=clamp((agent.trust[packet.from]??0)-6);count++;
    }else if(packet.type==='request'){
      agent.requests.push(packet);if(agent.requests.length>30)agent.requests.shift();count++;
    }
  }
  return count;
}
export function recordAction(agent,proposal,view,tick){
  const {kind,item,target,root}=proposal;
  if(kind==='ask')agent.acted.add(`ask:${item}:${view.day}`);
  if(kind==='tell'){
    agent.reported.add(root+':'+target);
    agent.sentReports??=[];
    agent.sentReports.push({root,to:target,tick});
  }
  if(kind==='retract')agent.reported.add(`retract:${root}:${target}`);
  if(kind==='repay'){
    const key=target+':'+item;
    agent.debts[key]=Math.max(0,(agent.debts[key]||0)-1);
  }
}
