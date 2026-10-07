// Apply researched identity/tier choices; numeric formulas remain in EnhancementBalance.
const fs = require('node:fs');
const read = p => JSON.parse(fs.readFileSync(p));
const save = (p,v) => fs.writeFileSync(p,JSON.stringify(v,null,2)+'\n');
const report = read('artifacts/phase0-data/Reports/BaldursGateStyleOblivion.enhancements.json');
const research = read('docs/UniqueItemResearch.json');
const enchant = read('BaldursGateStyleOblivion/enchantments.json');
const rewards = read('BaldursGateStyleOblivion/rewards.json');
const oldAudit = read('docs/UniqueItemTranslation.json');
const choices = new Map(research.Items.map(i=>[i.Name,i]));
const exceptions = new Map(oldAudit.Families.filter(f=>f.Preserved).map(f=>[f.Name,f.Reason]));
function weaponClass(item) {
  const p=item.BeforePhysical,id=item.EditorID+' '+item.Name;
  if(p.NativeType==='Staff')return null;
  if(p.NativeType==='Bow')return 'Bow';
  if(p.NativeType==='BladeOneHand')return /Razor|Dagger|Blade of Woe|Sufferthorn|Witsplinter/i.test(id)?'Dagger':/Chillrend|Shortsword/i.test(id)?'Shortsword':'Longsword';
  if(p.NativeType==='BladeTwoHand')return 'Claymore';
  if(p.NativeType==='BluntOneHand')return /axe/i.test(id)?'Waraxe':'Mace';
  if(p.NativeType==='BluntTwoHand')return /axe/i.test(id)?'Battleaxe':'Warhammer';
  return null;
}
function identity(item) {return [item.BeforePhysical?.Kind,weaponClass(item),item.Slots,item.Before.map(e=>e.Code+':'+e.ActorValue).join('|')].join(':');}
function score(item) {return (item.BeforePhysical?.Damage||0)+(item.BeforePhysical?.Armor||0)+item.Before.filter(e=>!e.Scripted).reduce((n,e)=>n+e.Magnitude*Math.max(1,e.Duration),0);}
const families=[];
for(const [name,choice] of choices) {
  const rows=report.Items.filter(i=>i.Artifact&&i.Name===name);
  if(!rows.length)throw Error('Researched family missing: '+name);
  if(choice.Tier===null)throw Error('Finish lore research before applying: '+name);
  const groups=Map.groupBy(rows,identity), references=[];
  for(const group of groups.values()) {
    const reference=group.toSorted((a,b)=>score(b)-score(a)||b.FormKey.localeCompare(a.FormKey))[0];references.push(reference.FormKey);
    const capacity=Math.max(100,...group.map(i=>i.Charge||0));
    for(const item of group) {
      const rule=enchant.Artifacts[item.FormKey],p=item.BeforePhysical;
      const preserve=exceptions.has(name);
      rule.Tier=choice.Tier;rule.Material=p?.Kind!=='Weapon'&&!p?.Heavy&&['Daedric','Ebony','Madness'].includes(choice.Material)?'Glass':choice.Material;rule.Preserve=preserve;
      rule.Sources=choice.Sources;rule.PhysicalPower=choice.ExceptionalPhysical?1.1:1;rule.EnchantmentPower=1;
      rule.Class=weaponClass(item)||undefined;
      rule.NormalizePhysical=p?.Kind!=='Clothing'&&p?.NativeType!=='Staff'&&!(p?.Kind!=='Weapon'&&p?.Armor===0);
      rule.BalanceScriptedEquipment=!preserve&&item.Before.some(e=>e.Scripted);
      rule.ChargedHits=choice.Tier>=7?45:35;
      rule.Reason=choice.Reason+(preserve?' Preserved: '+exceptions.get(name):' Standard physical baseline; tier-bounded native effects.');
      if(!preserve) {
        rule.Effects=item.Before.map((effect,index)=>effect.Scripted?effect:reference.Before[index]);
        if(name==='Gray Cowl of Nocturnal')rule.Effects=rule.Effects.map(e=>e.Code==='FTHR'?{...e,Magnitude:100}:e);
        rule.Value=reference.BeforePhysical?.Value;
        if(p?.NativeType==='Staff')rule.Damage=reference.BeforePhysical.Damage;else delete rule.Damage;
        if(p?.Kind!=='Weapon'&&p?.Armor===0)rule.Armor=0;else delete rule.Armor;
        if(p?.Kind==='Weapon')rule.ChargeCapacity=capacity;
      }
      const meta=rewards.Artifacts[item.FormKey];if(meta){meta.ArtifactTier=choice.Tier;meta.Reason=rule.Reason;}
    }
  }
  families.push({Name:name,Tier:choice.Tier,Material:choice.Material,Records:rows.map(i=>i.FormKey),References:references,Preserved:exceptions.has(name),Reason:choice.Reason,Sources:choice.Sources});
}
save('BaldursGateStyleOblivion/enchantments.json',enchant);save('BaldursGateStyleOblivion/rewards.json',rewards);
save('docs/UniqueItemTranslation.json',{Model:research.Model,Families:families,NonEquipment:oldAudit.NonEquipment});
console.log('Applied '+families.length+' families / '+families.reduce((n,f)=>n+f.Records.length,0)+' equipment records.');
