// Convert same-origin assets to embedded URLs for portable SVG/PNG exports.
window.DONA_MEDIA = {};
const assetVersion=window.DONA_BUILD||'dev';
const assetUrl=path=>`${path}?v=${assetVersion}`;
let loadedAssets=0,loadingFailed=false;
function reportLoading(){const el=document.querySelector('#loading-state');if(el&&!loadingFailed)el.textContent=`Подготавливаем файлы для скачивания: ${loadedAssets}/24. Брендбук уже можно просматривать.`;}
async function fetchAsset(path){
  const controller=new AbortController(),timer=setTimeout(()=>controller.abort(),45000);
  try{const r=await fetch(assetUrl(path),{signal:controller.signal});if(!r.ok)throw new Error(`${path}: ${r.status}`);const blob=await r.blob();return blob;}finally{clearTimeout(timer);}
}
window.DONA_MEDIA_READY = Promise.all(['kiss','bow','cherry','tulip','swan','packaging','garment','apparel','store-day','store-night','campaign','direction-dona','direction-muse','direction-belle','direction-belledona','direction-dona-cherry-swan','direction-muse-cherry-swan','direction-belle-cherry-swan','direction-belledona-cherry-swan'].map(async id => {
  window.DONA_MEDIA[id]=assetUrl(`assets/${id}.png`);
  const blob=await fetchAsset(`assets/${id}.png`);
  window.DONA_MEDIA[id]=await new Promise((resolve,reject)=>{const reader=new FileReader();reader.onload=()=>resolve(reader.result);reader.onerror=reject;reader.readAsDataURL(blob);});
  loadedAssets++;reportLoading();
}));

window.DONA_LICENSES={};
window.DONA_MEDIA_READY=Promise.all([window.DONA_MEDIA_READY,...['cormorantgaramond','prata','manrope','marckscript','greatvibes'].map(async family=>{const blob=await fetchAsset(`licenses/${family}-OFL.txt`);window.DONA_LICENSES[family]=await blob.text();loadedAssets++;reportLoading();})]);

// Attach an early rejection handler; the application displays a retry message.
window.DONA_MEDIA_READY.catch(()=>{loadingFailed=true;});
