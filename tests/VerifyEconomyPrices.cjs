const fs=require('node:fs'),path=require('node:path'),{spawn}=require('node:child_process');
const {chromium}=require('C:/Users/kevle/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const check=(ok,message)=>{if(!ok)throw Error(message)};
(async()=>{
 const root=path.resolve('artifacts/enchantment-pricing'),config=fs.mkdtempSync(path.join(root,'browser-config-'));
 for(const file of fs.readdirSync('BaldursGateStyleOblivion').filter(f=>f.endsWith('.json')))fs.copyFileSync(path.join('BaldursGateStyleOblivion',file),path.join(config,file));
 const server=spawn('dotnet',[path.join(root,'editor/ActorResearch.dll'),'edit','--no-open','--port','5089','--reports',path.join(root,'candidate/Reports'),'--config',path.join(config,'actor-classification.json')]);let browser,log='';server.stdout.on('data',d=>log+=d);server.stderr.on('data',d=>log+=d);
 try{
  for(let i=0;i<100;i++){try{if((await fetch('http://127.0.0.1:5089/economy')).ok)break}catch{}await new Promise(r=>setTimeout(r,100))}
  browser=await chromium.launch({executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',headless:true});const page=await browser.newPage({viewport:{width:1550,height:1000}}),errors=[];page.on('pageerror',e=>errors.push(e.message));await page.goto('http://127.0.0.1:5089/economy');
  await page.waitForFunction(()=>typeof preview!=='undefined'&&preview?.Prices?.length>0,null,{timeout:30000});
  const balance=await page.evaluate(async()=>{
   const results=[];
   for(const category of ['Bandit','Daedric'])for(const tier of [1,2,3,4,5]){
    const response=await fetch('/api/economy/preview',{method:'POST',headers:{'Content-Type':'application/json','X-Actor-Editor-Token':TOKEN},body:JSON.stringify({Settings:settings,Scenario:{Tier:tier,Level:5,Class:'Warrior',Category:category,Outing:'Short outing',Target:'Steel kit'},CraftedValueMultiplier:data.CraftedValueMultiplier,IngredientFloor:data.IngredientFloor})});
    if(!response.ok)throw Error(await response.text());const value=await response.json();results.push({category,tier,result:value.Proposed,crafting:value.Crafting});
   }
   return results;
  });fs.writeFileSync(path.join(root,'balance-check.json'),JSON.stringify(balance,null,2));
  const get=key=>page.evaluate(key=>preview.Prices.find(p=>p.Key===key),key);
  check((await get('0C89CB:Oblivion.esm')).Value===210,'Rumare inventory total');check((await get('0228E2:Oblivion.esm')).Value===25,'Wolf manual value preserved');
  await page.getByRole('button',{name:'Item prices',exact:true}).click();await page.locator('#search').fill('SQ09RingReward');check((await page.locator('#items').innerText()).includes('Written base 150 + native magic 60'),'Native breakdown visible');
  await page.locator('#items button[data-edit]').click();await page.locator('#overrideValue').fill('244');await page.locator('#applyOverride').click();await page.waitForFunction(()=>preview.Prices.find(p=>p.Key==='0C89CB:Oblivion.esm').BaseValue===184);
  await page.locator('#overrideValue').fill('25');await page.locator('#applyOverride').click();await page.waitForFunction(()=>preview.Prices.find(p=>p.Key==='0C89CB:Oblivion.esm').BelowMinimum);check((await get('0C89CB:Oblivion.esm')).Value===61,'Unattainable total retains smallest positive native value');
  await page.locator('#overrideValue').fill('244');await page.locator('#applyOverride').click();await page.waitForFunction(()=>preview.Prices.find(p=>p.Key==='0C89CB:Oblivion.esm').Value===244);
  await page.locator('#save').click();await page.waitForFunction(()=>document.querySelector('#status').textContent.startsWith('Saved.'));const saved=JSON.parse(fs.readFileSync(path.join(config,'economy.json')));check(saved.ItemOverrides['0C89CB:Oblivion.esm'].Value===244,'Exact total persisted');check(saved.ItemOverrides['0228E2:Oblivion.esm'].Value===25,'Other choices survive save');
  await page.getByRole('button',{name:'Prices & services',exact:true}).click();await page.locator('#apparelValue').fill('0.4');await page.locator('#apparelValue').dispatchEvent('change');await page.waitForFunction(()=>preview.Prices.find(p=>p.Key==='0C89CB:Oblivion.esm').BaseValue===124);check((await get('0C89CB:Oblivion.esm')).Value===244,'Exact total maintained as native contribution changes');
  await page.getByRole('button',{name:'Item prices',exact:true}).click();await page.screenshot({path:path.join(root,'editor-prices.png')});
  check(!errors.length,errors.join('\n'));console.log('Economy browser passed: native totals, price breakdown, exact-total overrides, minimum warning, factor preview, saving and preservation of other choices.');
 }catch(e){console.error(log);throw e}finally{await browser?.close();server.kill()}
})().catch(e=>{console.error(e);process.exitCode=1});
