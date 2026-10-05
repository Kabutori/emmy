const assert=require('assert/strict'),fs=require('fs'),path=require('path');
const {JSDOM,VirtualConsole}=require(process.env.EMMY_JSDOM_MODULE||'jsdom');
(async()=>{
 const root=path.resolve(__dirname,'../src/Emmy.Host/wwwroot'),token=fs.readFileSync(process.env.EMMY_TEST_TOKEN_FILE,'utf8').trim();
 const errors=[],virtualConsole=new VirtualConsole();virtualConsole.on('jsdomError',e=>errors.push(e.message));
 const dom=new JSDOM(fs.readFileSync(root+'/index.html','utf8'),{url:'http://127.0.0.1:17840/#'+token,runScripts:'outside-only',pretendToBeVisual:true,virtualConsole});
 dom.window.fetch=(url,options)=>fetch(new URL(url,'http://127.0.0.1:17840'),options);
 dom.window.eval(fs.readFileSync(root+'/app.js','utf8'));
 const document=dom.window.document,wait=()=>new Promise(r=>setTimeout(r,100));await wait();
 assert.match(document.getElementById('connection').textContent,/Spielclient/);
 assert.equal(document.getElementById('cards').children.length,3);
 document.querySelector('[data-page="persona"]').click();assert.equal(document.getElementById('persona').hidden,false);
 document.querySelector('[data-page="memory"]').click();assert.equal(document.getElementById('memory').hidden,false);assert.equal(document.getElementById('persona').hidden,true);
 const style=fs.readFileSync(root+'/style.css','utf8');assert.ok(style.includes('@media(max-width:550px)'));
 const key=document.getElementById('key-status').textContent;assert.ok(key.includes('API-Schlüssel'));
 document.getElementById('test').click();await wait();assert.match(document.getElementById('test-result').textContent,/Kein API/);
 document.getElementById('stop').click();await wait();assert.deepEqual(errors,[]);
 dom.window.close();console.log('DOM checks passed: render, navigation, settings fields, provider failure and stop controls. Visual layout remains unverified here.');
})().catch(e=>{console.error(e);process.exit(1)});
