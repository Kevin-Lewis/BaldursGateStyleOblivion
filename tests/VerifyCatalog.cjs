const fs=require('node:fs');const vm=require('node:vm');
const html=fs.readFileSync(process.argv[2]||'artifacts/actor-catalog.html','utf8');
const elements=new Map();
const element=id=>{if(!elements.has(id))elements.set(id,{value:id==='sort'?'group':id==='page-size'?'50':'',checked:false,textContent:'',innerHTML:'',children:[],listeners:{},append(child){this.children.push(child)},addEventListener(type,handler){this.listeners[type]=handler}});return elements.get(id)};
const document={getElementById:element,createElement(){return {value:'',textContent:''}}};
const context=vm.createContext({document,URL,setTimeout,clearTimeout});
vm.runInContext(html.match(/<script>([\s\S]*?)<\/script>/)[1],context);
const check=(condition,message)=>{if(!condition)throw new Error(message)};
check(element('count').textContent==='3636 of 3636 actors','Catalog coverage mismatch');
check(element('rows').innerHTML.includes('Edit:'),'Editable file path missing');
element('search').value='016487:Oblivion.esm';vm.runInContext('render()',context);
check(element('count').textContent==='1 of 3636 actors','FormKey filter failed');
check(element('rows').innerHTML.includes('Mannimarco'),'Actor identity missing');
element('search').value='';element('plugin').value='Knights.esp';vm.runInContext('render()',context);
check(!element('rows').innerHTML.includes('Winner: Oblivion.esm'),'Plugin filter failed');
check(!html.includes('AI proposal:')&&!html.includes('review statuses'),'Approval controls remain');
element('plugin').value='';
for(const sort of ['tier','tier-asc']){
 element('sort').value=sort;vm.runInContext('render()',context);
 const valid=vm.runInContext(`(()=>{const sorted=[...actors].sort((a,b)=>compare(a,b,'${sort}'));let unknown=false;for(let i=0;i<sorted.length;i++){if(sorted[i].Tier==null)unknown=true;else if(unknown)return false;if(i&&compare(sorted[i-1],sorted[i],'${sort}')>0)return false;}return true})()`,context);
 check(valid,'Tier sorting or unassigned placement failed');
}
element('tier-sort').listeners.click();check(element('sort').value==='tier','Tier header toggle failed');
check(vm.runInContext("compare({Tier:0,Name:'Zero',FormKey:'a'},{Tier:null,Name:'Unknown',FormKey:'b'},'tier-asc')<0",context),'Tier zero must precede unassigned');
check(!html.includes('placeholder="Group"')&&!html.includes('Use group rule'),'Confusing individual controls remain');
console.log('Catalog checks passed: coverage, file paths, FormKey/plugin filters, and script execution.');

element('search').value='';element('plugin').value='';element('sort').value='name';vm.runInContext('page=0;render()',context);
const visible=()=> (element('rows').innerHTML.match(/<tr><td><strong>/g)||[]).length;
check(visible()===50,'Default page must render only 50 actors');
const firstPage=element('rows').innerHTML;
element('next').listeners.click();check(visible()===50&&element('rows').innerHTML!==firstPage,'Next page failed');
check(element('page-info').textContent==='51-100 of 3636 matching actors','Second page range failed');
element('previous-bottom').listeners.click();check(element('rows').innerHTML===firstPage,'Bottom previous control failed');
element('page').value='72';element('page').listeners.input();check(visible()===36&&element('next').disabled,'Last page clamp failed');
element('search').value='016487:Oblivion.esm';element('group').listeners.input();check(visible()===1&&element('previous').disabled,'Filtering must reset page and search the full dataset');
element('search').value='nonexistent-actor-xyz';element('group').listeners.input();check(visible()===0&&element('next').disabled&&element('previous').disabled,'Empty results navigation failed');
element('search').value='';element('page-size').value='25';element('page-size').listeners.input();check(visible()===25,'Page size change failed');
console.log('Pagination passed: page sizes, navigation, last page, full-list search, filter reset and empty results.');
