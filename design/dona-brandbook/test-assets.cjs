// Regression checks: no eager originals, bounded concurrency, deduplication, retry.
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
let calls=[],active=0,peak=0,fail=false;
const context={window:{DONA_RESOURCE_VERSIONS:{'previews/bow.webp':'bow-hash'}},AbortController,setTimeout,clearTimeout,
 fetch:async url=>{calls.push(url);active++;peak=Math.max(peak,active);
   await new Promise(r=>setTimeout(r,2));active--;
   if(fail){fail=false;throw new Error('temporary network failure');}
   return {ok:true,blob:async()=>({text:async()=> 'license',url})};},
 FileReader:class{readAsDataURL(blob){this.result='data:image/png;base64,'+Buffer.from(blob.url).toString('base64');this.onload();}}
};
vm.runInNewContext(fs.readFileSync(__dirname+'/assets.js','utf8'),context);
(async()=>{
 assert.equal(calls.length,0,'Browsing must not fetch originals or licenses');
 assert.equal(context.window.DONA_MEDIA.bow,'previews/bow.webp?v=bow-hash');
 const [a,b]=await Promise.all([context.window.DONA_ORIGINAL('bow'),context.window.DONA_ORIGINAL('bow')]);
 assert.equal(a,b);assert.equal(calls.length,1,'Duplicate requests must coalesce');
 assert.ok(a.startsWith('data:image/png;base64,'));
 fail=true;
 await assert.rejects(context.window.DONA_ORIGINAL('cherry'));
 await context.window.DONA_ORIGINAL('cherry');
 const all=await context.window.DONA_ORIGINALS();
 assert.equal(Object.keys(all).length,20);assert.ok(peak<=3,'Original downloads must be bounded');
 await context.window.DONA_LOAD_LICENSES();assert.equal(Object.keys(context.window.DONA_LICENSES).length,5);
 assert.ok(context.window.DONA_MEDIA.bow.startsWith('previews/'),'Export must not replace previews');
 console.log('Asset loader checks passed: lazy originals, full-quality embedding, concurrency, retry.');
})().catch(error=>{console.error(error);process.exitCode=1;});
