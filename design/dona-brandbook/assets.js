// Lightweight previews are the only images requested while browsing.
const mediaIds=['dona-kiss-signature','kiss','bow','cherry','tulip','swan','packaging','garment','apparel','store-day','store-night','campaign','direction-dona','direction-muse','direction-belle','direction-belledona','direction-dona-cherry-swan','direction-muse-cherry-swan','direction-belle-cherry-swan','direction-belledona-cherry-swan','donamour-bow-v1','donamour-bow-v2','donamour-cherry-v1','donamour-cherry-v2','donamour-kiss-v1','donamour-kiss-v2','donamour-tulip-v1','donamour-tulip-v2','donamour-swan-v1','donamour-swan-v2'];
const licenseIds=['cormorantgaramond','prata','manrope','marckscript','greatvibes'];
const assetUrl=path=>`${path}?v=${window.DONA_RESOURCE_VERSIONS?.[path]||window.DONA_BUILD||'dev'}`;
window.DONA_MEDIA=Object.fromEntries(mediaIds.map(id=>[id,assetUrl(`previews/${id}.webp`)]));
window.DONA_LICENSES={};
const originals=new Map();
let licenses;
// Limit large original downloads to three concurrent requests; failures remain retryable.
let active=0;
const queue=[];
async function fetchAsset(path){
  if(active>=3)await new Promise(resolve=>queue.push(resolve));
  else active++;
  const controller=new AbortController(),timer=setTimeout(()=>controller.abort(),120000);
  try{
    const response=await fetch(assetUrl(path),{signal:controller.signal});
    if(!response.ok)throw new Error(`${path}: ${response.status}`);
    return await response.blob();
  }finally{clearTimeout(timer);const next=queue.shift();if(next)next();else active--;}
}
window.DONA_ORIGINAL=async id=>{
  if(!mediaIds.includes(id))throw new Error('Unknown media: '+id);
  if(!originals.has(id))originals.set(id,(async()=>{
    const blob=await fetchAsset(`assets/${id}.png`);
    return await new Promise((resolve,reject)=>{const reader=new FileReader();reader.onload=()=>resolve(reader.result);reader.onerror=reject;reader.readAsDataURL(blob);});
  })().catch(error=>{originals.delete(id);throw error;}));
  return originals.get(id);
};
window.DONA_LOAD_LICENSES=()=>licenses||(licenses=Promise.all(licenseIds.map(async id=>{
  window.DONA_LICENSES[id]=await (await fetchAsset(`licenses/${id}-OFL.txt`)).text();
})).catch(error=>{licenses=undefined;throw error;}));
window.DONA_ORIGINALS=async()=>Object.fromEntries(await Promise.all(mediaIds.map(async id=>[id,await window.DONA_ORIGINAL(id)])));
