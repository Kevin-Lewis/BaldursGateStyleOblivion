const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const {spawn}=require('node:child_process');
const net=require('node:net');
const check=(value,message)=>{if(!value)throw Error(message)};
(async()=>{
 const directory=fs.mkdtempSync(path.resolve('artifacts/list-editor-test-'));
 const actorConfig=path.join(directory,'actor-classification.json');
 const config=path.join(directory,'creature-lists.json');
 fs.writeFileSync(actorConfig,JSON.stringify({FormKeyOverrides:{}}));
 fs.copyFileSync('BaldursGateStyleOblivion/creature-lists.json',config);
 const before=JSON.parse(fs.readFileSync(config));
 before.FormKeyOverrides={};fs.writeFileSync(config,JSON.stringify(before));
 const probe=net.createServer();await new Promise(resolve=>probe.listen(0,'127.0.0.1',resolve));
 const port=probe.address().port;await new Promise(resolve=>probe.close(resolve));
 const url=`http://127.0.0.1:${port}`;
 const server=spawn('dotnet',['tools/ActorResearch/bin/Debug/net10.0/ActorResearch.dll','edit','--config',actorConfig,'--list-config',config,'--port',String(port),'--no-open'],{stdio:['ignore','pipe','pipe']});
 let log='';server.stdout.on('data',value=>log+=value);server.stderr.on('data',value=>log+=value);
 try{
  let html;
  for(let attempt=0;attempt<100&&!html;attempt++){
   try{const response=await fetch(url+'/lists');if(response.ok)html=await response.text()}catch{}
   if(!html)await new Promise(resolve=>setTimeout(resolve,100));
  }
  check(html,'List editor failed to start: '+log);
  const script=html.match(/<script>([\s\S]*?)<\/script>/)[1];
  const token=script.match(/const token="([A-F0-9]+)"/)[1];
  const elements=new Map();
  const element=id=>{
   if(!elements.has(id))elements.set(id,{value:id==='size'?'50':id==='sort'?'name':'',innerHTML:'',textContent:'',listeners:{},querySelectorAll:()=>[],insertAdjacentHTML(where,value){this.innerHTML+=value},addEventListener(type,fn){this.listeners[type]=fn}});
   return elements.get(id);
  };
  const context=vm.createContext({document:{getElementById:element},location:{reload(){}},fetch:(route,options)=>fetch(url+route,{...options,headers:{...options.headers,Origin:url}})});
  vm.runInContext(script,context);
  check((element('rows').innerHTML.match(/<tr data-key=/g)||[]).length===50,'Default page must show 50 lists');
  vm.runInContext('page=1;render()',context);check(element('page-info').textContent.startsWith('Page 2'),'Paging must move to the next page');
  element('search').value='LL1WildernessForest';vm.runInContext('page=0;render()',context);
  check(element('rows').innerHTML.includes('LL1WildernessForest'),'Name search must find the correct pool');
  const initial=await (await fetch(url+'/api/lists')).json();check(initial.Lists.length===701,'All original lists must be available');
  const list=initial.Lists.find(list=>list.EditorID==='LL1WildernessForest');
  const request=async(route,body,validToken=token)=>fetch(url+route,{method:'POST',headers:{'Content-Type':'application/json',Origin:url,'X-Actor-Editor-Token':validToken},body:JSON.stringify(body)});
  const entry=list.Entries[0];
  const definition={Name:list.EditorID,Policy:'CuratedPool',Reason:'Manual test notes',AllowSpecial:false,Entries:[{Reference:entry.Reference,Weight:3,Count:2}]};
  let response=await request('/api/list',{FormKey:list.FormKey,Definition:definition});check(response.ok,'Save failed: '+await response.clone().text());
  let saved=JSON.parse(fs.readFileSync(config));check(saved.FormKeyOverrides[list.FormKey].Entries[0].Weight===3&&saved.FormKeyOverrides[list.FormKey].Entries[0].Count===2,'Weight/count must persist');
  check(JSON.stringify(saved.Groups)===JSON.stringify(before.Groups),'Saving one pool must preserve group decisions');
  check(JSON.stringify(saved.ReviewedScripts)===JSON.stringify(before.ReviewedScripts),'Saving a pool must preserve reviewed script fingerprints');
  const after=await response.json();check(after.Lists.find(row=>row.FormKey===list.FormKey).HasOverride,'Save response must show an individual override');
  element('search').value='';element('overrides').value='only';
  context.result=after;vm.runInContext('data=result;page=0;render()',context);
  check((element('rows').innerHTML.match(/<tr data-key=/g)||[]).length===1,'Override-only filter must update immediately');
  response=await request('/api/list',{FormKey:list.FormKey,Definition:{...definition,Policy:'Preserve',Entries:[]}});check(response.ok,'Policy change must save');
  check(JSON.parse(fs.readFileSync(config)).FormKeyOverrides[list.FormKey].Policy==='Preserve','Preserve policy must persist');
  const previous=fs.readFileSync(config,'utf8');
  response=await request('/api/list',{FormKey:list.FormKey,Definition:{...definition,Entries:[{Reference:'FFFFFF:Missing.esp',Weight:1,Count:1}]}});check(response.status===400,'Unknown entries must be rejected');
  response=await request('/api/list',{FormKey:list.FormKey,Definition:{...definition,Entries:[{Reference:entry.Reference,Weight:256,Count:1}]}});check(response.status===400,'Over-capacity pools must be rejected');
  check(fs.readFileSync(config,'utf8')===previous,'Rejected saves must not alter the configuration');
  response=await request('/api/list/weights',{CommonMaxTier:3,StrongMaxTier:5,Common:60,Strong:30,Rare:10});check(response.ok,'Automatic weights must save');
  check(JSON.parse(fs.readFileSync(config)).Weights.Rare===10,'Global weights must persist');
  response=await request('/api/list/weights',{CommonMaxTier:6,StrongMaxTier:3,Common:60,Strong:30,Rare:10});check(response.status===400,'Invalid tier bands must be rejected');
  response=await request('/api/list/delete',{FormKey:list.FormKey},'expired');check(response.status===403,'Expired token must not authorize edits');
  response=await request('/api/list/delete',{FormKey:list.FormKey});check(response.ok,'Delete override must succeed');
  saved=JSON.parse(fs.readFileSync(config));check(!saved.FormKeyOverrides[list.FormKey],'Deleted override must be removed from disk');
  check((await response.json()).Lists.find(row=>row.FormKey===list.FormKey).Policy==='WeightedPool','Deleting must restore the group policy');
  console.log('Creature list editor checks passed: page rendering, paging, search, override filters, policy/weight saves, persisted reload, delete, validation, and token protection.');
 }finally{server.kill()}
})().catch(error=>{console.error(error);process.exitCode=1});
