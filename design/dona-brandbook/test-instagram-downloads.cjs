// Download contracts: explicit extension, native save, fallback and cancellation.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const vm=require('node:vm');
const source=fs.readFileSync(__dirname+'/instagram.js','utf8').replace(
  'return {render,refresh:()=>{if(ready)renderGallery();},snapshot:', 'return {chooseFile,writeFile,saveFile,snapshot:');
const link={click(){this.clicks=(this.clicks||0)+1;this.onclick?.({isTrusted:false});}};
const urls=[],revoked=[];
const context={window:{},document:{getElementById:()=>({querySelector:()=>link})},
  structuredClone,Blob,File,console,URL:{createObjectURL:file=>{urls.push(file);return 'blob:test/'+urls.length;},revokeObjectURL:url=>revoked.push(url)}};
vm.runInNewContext(source,context);
const api=context.window.createDonaInstagram({});
(async()=>{
  const bytes=Buffer.from([137,80,78,71,13,10,26,10]);
  const png=new Blob([bytes],{type:'image/png'});
  assert.equal(await api.chooseFile('DONA-avatar-kiss.png','png'),null);
  api.saveFile(png,'DONA-avatar-kiss.png');
  assert.equal(link.download,'DONA-avatar-kiss.png');
  assert.equal(urls[0].name,link.download);
  assert.equal(urls[0].type,'image/png');
  assert.deepEqual(Buffer.from(await urls[0].arrayBuffer()),bytes);
  assert.equal(link.clicks,1);
  let options,closed=false,written;
  const handle={createWritable:async()=>({write:async blob=>{written=blob;},close:async()=>{closed=true;}})};
  context.window.showSaveFilePicker=async value=>{options=value;return handle;};
  for(const [ext,mime] of [['png','image/png'],['jpg','image/jpeg'],['svg','image/svg+xml'],['txt','text/plain']]){
    assert.equal(await api.chooseFile('DONA-test.'+ext,ext),handle);
    assert.equal(options.suggestedName,'DONA-test.'+ext);
    assert.equal(options.types[0].accept[mime][0],'.'+ext);
  }
  await api.writeFile(handle,png);
  assert.equal(written,png);assert.ok(closed);
  api.saveFile(png,'DONA-post-welcome.png',false);
  assert.equal(link.clicks,1,'Native save must not trigger a second download');
  assert.equal(link.download,'DONA-post-welcome.png');assert.equal(revoked.length,1);
  let prevented=false;
  link.onclick({isTrusted:true,preventDefault(){prevented=true;}});
  await new Promise(resolve=>setImmediate(resolve));
  assert.ok(prevented);assert.equal(options.suggestedName,'DONA-post-welcome.png');
  for(const name of ['SecurityError','NotAllowedError']){
    context.window.showSaveFilePicker=async()=>{throw Object.assign(new Error(),{name});};
    assert.equal(await api.chooseFile('test.png','png'),null);
  }
  context.window.showSaveFilePicker=async()=>{throw Object.assign(new Error(),{name:'AbortError'});};
  await assert.rejects(api.chooseFile('test.png','png'),{name:'AbortError'});
  let aborted=false;
  await assert.rejects(api.writeFile({createWritable:async()=>({write:async()=>{throw new Error('disk full');},abort:async()=>{aborted=true;}})},png),/disk full/);
  assert.ok(aborted);
  console.log('Instagram downloads passed: filenames, MIME, native save, repeat, fallback, cancellation and write failure.');
})().catch(error=>{console.error(error);process.exitCode=1;});
