import test from 'node:test';import assert from 'node:assert/strict';
import {createWorld,execute,visibleView,conservation} from '../src/v04/world.mjs';
import {ingestEvidence,strongestSuspicion,retractEvidence,processInbox,recordAction} from '../src/v04/memory.mjs';
import {deliberate} from '../src/v04/cognition.mjs';
test('phase3: forwarded claim shares root and cannot multiply confidence',()=>{
 const agent=createWorld().people[2];const evidence={incident:'E10',root:'E10/A01',suspect:'A001',lr:Math.log(8),source:'eyewitness',from:'A001'};
 assert.equal(ingestEvidence(agent,evidence,10),true);
 const initial=strongestSuspicion(agent,10).posterior;
 for(let i=0;i<40;i++)ingestEvidence(agent,{...evidence,source:'hearsay',lr:2,from:`A${i}`},11);
 assert.equal(agent.evidence.size,1);assert.equal(strongestSuspicion(agent,10).posterior,initial);
});
test('phase3: authentic retraction removes evidence and alters posterior',()=>{
 const agent=createWorld().people[2];
 ingestEvidence(agent,{incident:'E7',root:'R7',suspect:'A004',lr:Math.log(8),source:'hearsay',from:'A001'},2);
 assert.ok(strongestSuspicion(agent,2).posterior>.12);
 assert.equal(retractEvidence(agent,'R7','A009'),false);
 assert.equal(retractEvidence(agent,'R7','A001'),true);
 assert.equal(strongestSuspicion(agent,2),null);
});
test('phase3: gift creates obligation, and repayment clears it with conservation',()=>{
 const world=createWorld();const giver=world.people[0],receiver=world.people[4];
 giver.inv.water=2;world.initialTotals.water++;
 const initial=Object.values(conservation(world)).every(v=>v===0);assert.equal(initial,true);
 execute(world,{actor:receiver.id,kind:'ask',target:giver.id,item:'water'});
 processInbox(giver,world.tick);
 const packet=visibleView(world,giver);const gift=deliberate(giver,packet,{plannerEnabled:false});
 assert.ok(gift.candidates.some(c=>c.kind==='give'));
 assert.equal(execute(world,{actor:giver.id,kind:'give',target:receiver.id,item:'water'}).accepted,true);
 processInbox(receiver,world.tick);
 assert.equal(receiver.debts[giver.id+':water'],1);
 assert.ok(receiver.trust[giver.id]>0);
 assert.equal(execute(world,{actor:receiver.id,kind:'repay',target:giver.id,item:'water'}).accepted,true);
 recordAction(receiver,{kind:'repay',target:giver.id,item:'water'},packet,world.tick);
 assert.equal(receiver.debts[giver.id+':water'],0);
 assert.ok(Object.values(conservation(world)).every(v=>v===0));
});
test('phase3: unsupported report, investigation, accusation and retraction rejected atomically',()=>{
 const w=createWorld();const a=w.people[0],b=w.people[1];const inv=JSON.stringify(a.inv);
 for(const kind of ['tell','investigate','accuse','retract']){
  assert.equal(execute(w,{actor:a.id,kind,incident:'EUNKNOWN',root:'F',suspect:b.id,target:b.id,lr:5}).accepted,false,kind);
 }
 assert.equal(JSON.stringify(a.inv),inv);
 assert.equal(w.events.length,0);
});
test('phase3: only authenticated forwarding author may retract a report',()=>{
 const w=createWorld();const a=w.people[0],b=w.people[4];
 ingestEvidence(a,{incident:'EOLD',root:'R11',suspect:'A009',lr:Math.log(10),source:'eyewitness',from:a.id},0);
 assert.equal(execute(w,{actor:a.id,kind:'tell',target:b.id,incident:'EOLD',root:'R11',suspect:'A009',lr:2}).accepted,true);
 recordAction(a,{kind:'tell',target:b.id,root:'R11'},visibleView(w,a),0);
 processInbox(b,0);assert.equal(b.evidence.has('R11'),true);
 assert.equal(execute(w,{actor:b.id,kind:'retract',target:a.id,root:'R11'}).accepted,false);
 assert.equal(execute(w,{actor:a.id,kind:'retract',target:b.id,root:'R11'}).accepted,true);
 processInbox(b,0);assert.equal(b.evidence.get('R11').active,false);
});
