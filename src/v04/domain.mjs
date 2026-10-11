/** Survival domain only: generic cognition must import predicates, not these item names. */
export const NEEDS = Object.freeze({
  hunger:{daily:12,weight:1.0},thirst:{daily:19,weight:1.35},
  fatigue:{daily:13,weight:.85},cold:{daily:11,weight:.9},
  hygiene:{daily:9,weight:.35},bladder:{daily:19,weight:.65},
  safety:{daily:5,weight:.55},belonging:{daily:8,weight:.3},morale:{daily:4,weight:.3},
});
export const ITEMS = Object.freeze({
  food:{type:'consumable'},water:{type:'consumable'},raw_food:{type:'material'},dirty_water:{type:'material'},
  wood:{type:'material'},medicine:{type:'consumable'},bandage:{type:'consumable'},soap:{type:'consumable'},
  blanket:{type:'durable'},coat:{type:'durable'},tool:{type:'durable'},bed:{type:'durable'},
  shelter:{type:'structure'},latrine:{type:'structure'},
});
export const RECIPES = Object.freeze({
  boil:{inputs:{dirty_water:1,wood:1},outputs:{water:1},effort:1},
  cook:{inputs:{raw_food:1},outputs:{food:1},effort:1},
  build_shelter:{inputs:{wood:3},outputs:{shelter:1},effort:2},
  build_latrine:{inputs:{wood:2},outputs:{latrine:1},effort:2},
});
export const clamp = (n,min=0,max=100)=>Math.max(min,Math.min(max,n));
export const zeros = () => Object.fromEntries(Object.keys(ITEMS).map(k=>[k,0]));
export const initialNeeds = r => Object.fromEntries(Object.keys(NEEDS).map(k=>[k,Math.floor(18+r()*18)]));
export function validateItem(item,amount=1){
  if(!(item in ITEMS)||!Number.isSafeInteger(amount)||amount<0) throw new RangeError('Unknown or invalid item quantity');
}
export function canPay(store,recipe){return Object.entries(recipe.inputs).every(([k,v])=>store[k]>=v);}
export function itemDelta(recipe){
  const result=zeros();for(const [k,v] of Object.entries(recipe.inputs)) result[k]-=v;
  for(const [k,v] of Object.entries(recipe.outputs))result[k]+=v;return result;
}
export function validateDomain(){
  for(const [name,recipe] of Object.entries(RECIPES)) {
    if(!Object.keys(recipe.inputs).length||!Object.keys(recipe.outputs).length)throw Error('Empty recipe '+name);
    for(const [k,v] of Object.entries({...recipe.inputs,...recipe.outputs})){
      validateItem(k,v);if(v===0)throw Error('Zero recipe quantity');
    }
  }
  for(const [n,v] of Object.entries(NEEDS)) if(!(v.daily>=0&&v.daily<=100&&Number.isFinite(v.weight))) throw Error('Invalid need '+n);
  return true;
}
