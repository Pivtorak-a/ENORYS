import test from 'node:test';import assert from 'node:assert/strict';
import {ITEMS, NEEDS, RECIPES,validateDomain,validateItem,canPay,itemDelta,zeros} from '../src/v04/domain.mjs';
test('phase1: schema is validated and matches existing 9 needs and 14 resources',()=>{
 assert.equal(validateDomain(),true);assert.equal(Object.keys(NEEDS).length,9);assert.equal(Object.keys(ITEMS).length,14);
});
test('phase1: every recipe has an exact, conservative typed delta',()=>{
 for(const recipe of Object.values(RECIPES)){
  const delta=itemDelta(recipe);assert.equal(Object.keys(delta).length,14);
  for(const k of Object.keys(ITEMS))assert.equal(delta[k],(recipe.outputs[k]||0)-(recipe.inputs[k]||0));
 }
});
test('phase1: invalid items and negative quantities never pass',()=>{
 assert.throws(()=>validateItem('water',-1),RangeError);assert.throws(()=>validateItem('fake',1),RangeError);
 const z=zeros();assert.equal(canPay(z,RECIPES.boil),false);z.wood=1;z.dirty_water=1;assert.equal(canPay(z,RECIPES.boil),true);
});
