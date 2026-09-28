// Convert same-origin assets to embedded URLs for portable SVG/PNG exports.
window.DONA_MEDIA = {};
window.DONA_MEDIA_READY = Promise.all(['kiss','bow','cherry','tulip','swan','packaging','garment','apparel','store-day','store-night','campaign','direction-dona','direction-muse','direction-belle','direction-belledona','direction-dona-cherry-swan','direction-muse-cherry-swan','direction-belle-cherry-swan','direction-belledona-cherry-swan'].map(async id => {
  const response=await fetch(`assets/${id}.png`);
  if(!response.ok)throw new Error(`Image ${id}: ${response.status}`);
  const blob=await response.blob();
  window.DONA_MEDIA[id]=await new Promise((resolve,reject)=>{const reader=new FileReader();reader.onload=()=>resolve(reader.result);reader.onerror=reject;reader.readAsDataURL(blob);});
}));

window.DONA_LICENSES={};
window.DONA_MEDIA_READY=Promise.all([window.DONA_MEDIA_READY,...['cormorantgaramond','prata','manrope','marckscript','greatvibes'].map(async family=>{const r=await fetch(`licenses/${family}-OFL.txt`);if(!r.ok)throw new Error('Font license unavailable');window.DONA_LICENSES[family]=await r.text();})]);
