const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const {spawn}=require('node:child_process');const net=require('node:net');
const check=(ok,message)=>{if(!ok)throw new Error(message)};
(async()=>{
 const directory=fs.mkdtempSync(path.resolve('artifacts/editor-test-'));
 const config=path.join(directory,'actor-classification.json');const key='016487:Oblivion.esm';
 fs.writeFileSync(config,JSON.stringify({Groups:{test:[{Id:'Test fallback',Priority:'Fallback',Evidence:'Always',Values:{PowerTier:2}}]},FormKeyOverrides:{[key]:{Name:'Mannimarco',PowerTier:7,Handling:'Named',Description:'Preserve these notes',Reason:'Test assignment',Sources:[],Model:'manual'}}}));
 const probe=net.createServer();await new Promise(resolve=>probe.listen(0,'127.0.0.1',resolve));const port=probe.address().port;await new Promise(resolve=>probe.close(resolve));
 const url=`http://127.0.0.1:${port}`;
 const server=spawn('dotnet',['tools/ActorResearch/bin/Debug/net10.0/ActorResearch.dll','edit','--config',config,'--port',String(port),'--no-open'],{stdio:['ignore','pipe','pipe']});
 let log='';server.stdout.on('data',data=>log+=data);server.stderr.on('data',data=>log+=data);
 try{
  let html;
  for(let attempt=0;attempt<100&&!html;attempt++){try{const response=await fetch(url);if(response.ok)html=await response.text()}catch{}if(!html)await new Promise(resolve=>setTimeout(resolve,100))}
  check(html,'Editor did not start: '+log);
  check(html.includes('<th>Level override</th>'),'Fixed level must have a visible table column');
  check(!html.includes('special-handling\">Fixed level override'),'Fixed level must not remain hidden in details');
  const script=html.match(/<script>([\s\S]*?)<\/script>/)[1];
  const elements=new Map();
  const element=id=>{if(!elements.has(id))elements.set(id,{value:id==='sort'?'group':id==='page-size'?'50':'',checked:false,textContent:'',innerHTML:'',listeners:{},append(){},addEventListener(type,handler){this.listeners[type]=handler}});return elements.get(id)};
  const nativeFetch=fetch;
  const context=vm.createContext({document:{getElementById:element,createElement:()=>({})},URL,setTimeout,clearTimeout,fetch:(route,options)=>nativeFetch(url+route,{...options,headers:{...options.headers,Origin:url}})});
  vm.runInContext(script,context);
  check(vm.runInContext('config',context)===config,'Refusing to edit a different configuration');
  const actor=()=>vm.runInContext(`actors.find(a=>a.FormKey==='${key}')`,context);
  const file=()=>JSON.parse(fs.readFileSync(config,'utf8'));
  element('search').value=key;vm.runInContext('render()',context);
  async function click(deleting,value='',handling='',level='',delevel=''){
   const row={querySelector:selector=>({value:({'.tier-edit':value,'.handling-edit':handling,'.fixed-level-edit':level,'.delevel-edit':delevel})[selector]})};
   const button={classList:{contains:kind=>kind===(deleting?'delete-actor':'save-actor')},dataset:{key},closest:()=>row};
   await element('rows').listeners.click({target:button});
   check(!button.disabled,'Save/delete control must become usable again');
  }
  await click(false,'8','ProtectedSpecial');
  check(file().FormKeyOverrides[key].PowerTier===8,'Save did not reach the JSON');
  check(actor().Tier===8&&actor().OverrideTier===8,'Saved tier did not update the existing browser row');
  check(actor().OverrideHandling==='ProtectedSpecial','Saved handling did not update');
  check(file().FormKeyOverrides[key].Description==='Preserve these notes','Save erased research notes');
  check(element('rows').innerHTML.includes('value="8"'),'Saved input still displays the old tier');
  const refreshedResponse=await nativeFetch(url);check(refreshedResponse.headers.get('cache-control')==='no-store','Editor must prevent stale cached pages');const refreshed=await refreshedResponse.text();const refreshedActors=JSON.parse(refreshed.match(/const actors=(.*?);const groups=/)[1]);
  check(refreshedActors.find(a=>a.FormKey===key).OverrideTier===8,'Save did not survive page refresh');
  await click(false,'','');
  check(!('PowerTier' in file().FormKeyOverrides[key]),'Clearing a tier did not remove it');
  check(actor().OverrideTier===null&&actor().OverrideHandling===null&&actor().Tier===2,'Null response fields did not clear stale overrides');
  await click(false,'0','Named');check(actor().Tier===0&&file().FormKeyOverrides[key].PowerTier===0,'Tier zero must save as an override');
  element('overrides').value='only';vm.runInContext('render()',context);
  await click(true);
  check(!file().FormKeyOverrides[key],'Delete did not remove the whole JSON entry');
  check(actor().HasOverride===false&&actor().OverrideTier===null&&actor().Description===''&&actor().Tier===2,'Delete left stale row fields');
  check(element('count').textContent==='0 of 3636 actors','Deleted actor stayed in Overrides only filter');
  await click(true);check(!file().FormKeyOverrides[key],'Repeated deletion must be safe');
  element('overrides').value='';vm.runInContext('render()',context);
  await click(false,'6','Named');check(actor().HasOverride&&file().FormKeyOverrides[key].PowerTier===6,'Creating an override after deletion failed');
  await click(false,'6','Named','17','false');check(file().FormKeyOverrides[key].FixedLevel===17&&file().FormKeyOverrides[key].Delevel===false&&actor().FixedLevel===17,'Fixed level and exemption did not save');
  const unchanged=fs.readFileSync(config,'utf8');await click(false,'11','Named');
  check(fs.readFileSync(config,'utf8')===unchanged&&element('message').textContent.includes('whole number'),'Invalid input must not change the configuration');
  const token=vm.runInContext('token',context);
  const invalid=await nativeFetch(url+'/api/actor',{method:'POST',headers:{'Content-Type':'application/json',Origin:url,'X-Actor-Editor-Token':token},body:JSON.stringify({formKey:key,tier:11,handling:null})});
  check(invalid.status===400&&fs.readFileSync(config,'utf8')===unchanged,'Server must reject invalid tiers without changing the file');
  await click(true);
  const groupRow={querySelector:selector=>selector==='.group-tier'?{value:'4'}:{checked:true}};
  await element('group-rules').listeners.click({target:{classList:{contains:kind=>kind==='save-group'},dataset:{index:'0'},closest:()=>groupRow}});
  check(actor().Tier===4&&file().Groups.test[0].Values.PowerTier===4,'Group save did not update the browser or file');
  context.fetch=async()=>({ok:false,status:403,json:async()=>{throw new Error('Empty response')}});
  await click(false,'5','Named');check(element('message').textContent.includes('session expired'),'Expired-session errors must tell the user to refresh');
  check(file().Groups.test[0].Values.PowerTier===4&&!file().FormKeyOverrides[key],'Failed request changed the file');
  console.log('Live editor checks passed: save, refresh persistence, clear, tier zero, delete, filters, recreation, validation, group updates, notes and expired sessions.');
 }finally{
  server.kill();if(server.exitCode===null)await new Promise(resolve=>server.once('exit',resolve));
  fs.unlinkSync(config);fs.rmdirSync(directory);
 }
})().catch(error=>{console.error(error);process.exitCode=1});
